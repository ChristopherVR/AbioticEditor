-- ===== Tamed pets (PetNPC in the save; live actors are ordinary NPC_Base_ParentBP_C) =====
-- Round 76 found NO general live path (tame/name/health data is exposed wildly inconsistently
-- between creature families, with no safe way to match a live actor back to a world save's
-- PetNPC GUID for most of them). Round 77 re-checked the game's own class layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragments "NPC_Monster_Pest.",
-- "NPC_Skink_Basic", "NPC_Monster_Peccary.", "NPC_Peccary_Sow", "NPC_Monster_WinterSprite", plus
-- the native usmap dump of AbioticCharacter) and found a real, PARTIAL path instead of guessing a
-- universal one:
--
--   - NPC_Monster_Pest_C (and its subclass NPC_Skink_Basic_C, since FindAllOf is
--     hierarchy-inclusive - confirmed already by bases.lua/containers.list scanning this same
--     way) directly exposes, with NO hash suffix:
--       PetName : FTextProperty, with a real OnRep_PetName
--       Guid : FStrProperty                    -- a stable id, matching the save's own PetNPC key
--       DynamicProperties : FArrayProperty      -- {Key: EDynamicProperty, Value: int} structs,
--         the exact same shape companions.lua's carried-pet XP already reads/writes (just without
--         a hash suffix here, since it sits directly on the NPC, not inside ChangeableData).
--     A live actor of this family CAN be safely matched to a save's PetNPC record: by Guid.
--   - NPC_Monster_Peccary_C / NPC_Peccary_Sow_C and NPC_Monster_WinterSprite_C were re-checked
--     and confirmed to still carry NONE of Guid/PetName/DynamicProperties as their own
--     properties - there is still no stable id for them, so pets.lua still does not list them
--     (listing them under their engine object name instead of a stable id would silently point
--     at a different animal after any respawn/reload, which is exactly the kind of guess this
--     project got burned by once already, GetMyPlayerController). This is the one real
--     limitation left: Peccary and Lamogi family pets stay file-only.
--   - Per-limb health is UNIVERSAL, not pet-specific: AbioticCharacter (the native base class of
--     EVERY player AND every NPC, confirmed from the native usmap dump) carries
--     CurrentHealth_Head/Torso/LeftArm/RightArm/LeftLeg/RightLeg as plain unsuffixed floats with
--     one shared OnRep_CurrentHealth - the EXACT fields main.lua's vitals.set already writes for
--     the local player, CONFIRMED LIVE (round 74/75, HUD head-injury indicator visibly cleared).
--     So healing/downing a pet here is the same write already proven live, just aimed at the
--     pet's own actor instead of the player's.
--   - IsDead / OnRep_IsDead are the same fields npcs.set already writes for any world NPC
--     (shipped and working - see main.lua's npcs.list/npcs.set).
--
-- Still NOT supported, honestly: changing species (SpawnPet is a GameMode function that would
-- mean despawning and respawning the actor with an FTransform this probe never saw constructed
-- anywhere - see round 76 comment) - pets.set never accepts npcClass, and LivePetsSession reports
-- SupportsSpeciesChange as false so the shared tab hides that control live.
--
-- Round-78 bug fix #1 (reported live: "pet health and level editing doesn't seem to work"): the
-- ROOT CAUSE was not that the writes themselves failed - it was that WorldPetsTab's Apply() always
-- sends every field (isDead/customName/limbHealth/xp) in ONE combined pets.set call, and the old
-- code raised a hard Lua error() the moment ANY one field looked unwritable, most commonly the xp
-- field: a pet still at its untouched default has NO "XP" entry in DynamicProperties at all yet
-- (the exact same delta-omission the save file itself uses; WorldSaveWriter.ApplyDynamicInt's own
-- comment on the file-format equivalent of this gap notes it used to be "silently lost" there too),
-- and setDynamicInt only ever patches an EXISTING entry - it never fabricates one (constructing a
-- brand-new EDynamicProperty-keyed struct element over UE4SS Lua reflection has no working
-- precedent anywhere in this project, and per areas/worldunlocks.lua's own header comment guessing
-- a TArray-append technique is refused project-wide, not just here - that part of the gap is real
-- and stays unsupported). Because Lua's error() aborts the WHOLE handler, that one unrelated xp
-- mismatch threw away a health/name edit that had ALREADY been written into the live NPC's memory
-- moments earlier in the same function (Lua runs top-to-bottom) - the C# side saw the whole call as
-- failed, never called RefreshAsync(), and the tab kept showing stale values: exactly "editing
-- doesn't seem to work" for BOTH health and level, even on requests that never touched level at
-- all. Fixed by applying every field independently and collecting per-field WARNINGS instead of
-- aborting: a request that changes health only ever fails because of a genuine health-write
-- problem now, never because of an unrelated, unrequested XP echo-back; a request that genuinely
-- tries to raise a never-earned pet's level still can't fabricate the entry, but says so as a
-- warning alongside whatever else in the same call DID apply, instead of masking it as total
-- failure. Per-limb health failures (a write pcall genuinely erroring) are reported the same way.
return function(ctx)
    local PET_FAMILY_CLASS = "NPC_Monster_Pest_C" -- hierarchy-inclusive: also finds NPC_Skink_Basic_C.
    local LIMB_PROPERTY = {
        Head = "CurrentHealth_Head", Torso = "CurrentHealth_Torso",
        LeftArm = "CurrentHealth_LeftArm", RightArm = "CurrentHealth_RightArm",
        LeftLeg = "CurrentHealth_LeftLeg", RightLeg = "CurrentHealth_RightLeg",
    }

    -- Same {Key, Value} enum-tail matching companions.lua already uses for carried-pet XP -
    -- reused here against DynamicProperties directly on the NPC actor (no hash suffix, unlike
    -- the inventory-slot ChangeableData version companions.lua reads).
    local function dynamicInt(npc, keySuffix)
        local ok, array = pcall(function() return npc.DynamicProperties end)
        if not ok or not array then return 0 end
        for i = 1, #array do
            local okEntry, key, value = pcall(function()
                local entry = array[i]
                local keyValue = entry.Key
                return (keyValue.ToString and keyValue:ToString() or tostring(keyValue)), entry.Value
            end)
            if okEntry and key and tostring(key):match(keySuffix .. "$") then return value or 0 end
        end
        return 0
    end

    local function setDynamicInt(npc, keySuffix, value)
        local ok, array = pcall(function() return npc.DynamicProperties end)
        if not ok or not array then return false end
        for i = 1, #array do
            local okEntry, matched = pcall(function()
                local entry = array[i]
                local keyValue = entry.Key
                local keyString = keyValue.ToString and keyValue:ToString() or tostring(keyValue)
                if tostring(keyString):match(keySuffix .. "$") then entry.Value = value return true end
                return false
            end)
            if okEntry and matched then return true end
        end
        return false
    end

    local function readLimbHealth(npc)
        local limbs = {}
        for limb, propName in pairs(LIMB_PROPERTY) do
            local ok, value = pcall(function() return npc[propName] end)
            limbs[limb] = (ok and value) or 0
        end
        return limbs
    end

    -- Bug fix (reported live: "pet health editing doesn't seem to work"): every per-limb write
    -- used to be wrapped in its own throwaway pcall with the result discarded, so a write that
    -- genuinely failed (wrong actor state, a field rejected by the engine, anything) looked
    -- identical to one that succeeded - pets.set would still report ok, and the UI would refresh
    -- to find nothing changed with no clue why. Returns the limb names that failed to write so the
    -- caller can turn that into an honest error instead of a silent no-op.
    local function writeLimbHealth(npc, limbs)
        local failed = {}
        for limb, propName in pairs(LIMB_PROPERTY) do
            if limbs[limb] ~= nil then
                local ok = pcall(function() npc[propName] = limbs[limb] end)
                if not ok then table.insert(failed, limb) end
            end
        end
        -- Same call vitals.set already makes after writing these exact fields, confirmed live.
        pcall(function() npc:OnRep_CurrentHealth() end)
        return failed
    end

    local function findPestByGuid(id)
        for _, candidate in ipairs(ctx.findAll(PET_FAMILY_CLASS)) do
            if candidate:IsValid() then
                local ok, guid = pcall(function() return candidate.Guid:ToString() end)
                if ok and guid == id then return candidate end
            end
        end
        return nil
    end

    local function petRows()
        local result = { __forceArray = true }
        for _, npc in ipairs(ctx.findAll(PET_FAMILY_CLASS)) do
            if npc:IsValid() then
                local okGuid, guid = pcall(function() return npc.Guid:ToString() end)
                if okGuid and guid and guid ~= "" then
                    local x, y, z = ctx.actorLocation(npc)
                    local okName, name = pcall(function() return npc.PetName:ToString() end)
                    local fullName = ctx.fullName(npc)
                    table.insert(result, {
                        id = guid,
                        npcClass = ctx.classLabel(fullName),
                        isDead = npc.IsDead == true,
                        customName = (okName and name ~= "" and name) or nil,
                        x = x, y = y, z = z,
                        limbHealth = readLimbHealth(npc),
                        xp = dynamicInt(npc, "XP"),
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["pets.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return {
                pets = petRows(),
                isHost = ctx.isHost(),
                available = true,
                supportsSpeciesChange = false,
                -- Round 78: real removal, evidenced by the reference CheatConsoleCommands mod's
                -- own "deleteobject" command destroying an arbitrary world actor with
                -- `actor:K2_DestroyActor()` (CommandsManager.lua) - see pets.remove below. There
                -- is no undo once this runs, unlike the file session's staged delete.
                supportsRemoval = true,
                reason = "Only Pest- and Skink-family pets can be matched to a save record live " ..
                    "right now (the game gives them a stable id); Peccary and Lamogi pets can " ..
                    "still be renamed, healed and levelled up in the save file. Removing a pet " ..
                    "here despawns it immediately - there is no undo.",
            }
        end, respond)
    end

    -- Round 78: applies every field independently and collects non-fatal WARNINGS instead of
    -- aborting the whole call on the first field that can't be written - see this file's own
    -- header comment for the bug this fixes (a combined health+level request used to fail
    -- entirely, and look stale in the UI, because of an unrelated/unrequested XP mismatch).
    ctx.handlers["pets.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can edit pets") end
            local npc = payload.id and findPestByGuid(payload.id)
            if not npc then error("pet not found (it may have been unloaded, or isn't a Pest/Skink-family pet)") end

            local warnings = { __forceArray = true }

            if payload.isDead ~= nil and npc.IsDead ~= payload.isDead then
                npc.IsDead = payload.isDead
                pcall(function() npc:OnRep_IsDead() end)
            end
            if payload.customName ~= nil then
                -- No precedent for writing an FText property besides bases.lua's own rename this
                -- round - same try-FText-then-plain-string fallback.
                local ok = pcall(function() npc.PetName = FText(payload.customName) end)
                if not ok then ok = pcall(function() npc.PetName = payload.customName end) end
                if ok then pcall(function() npc:OnRep_PetName() end) end
            end
            if payload.limbHealth ~= nil then
                local failed = writeLimbHealth(npc, payload.limbHealth)
                if #failed > 0 then
                    table.insert(warnings, "couldn't write health for: " .. table.concat(failed, ", "))
                end
            end
            -- setDynamicInt only ever patches an EXISTING DynamicProperties entry - same
            -- no-fabrication limit companions.lua's own carried-pet version documents, and for the
            -- same reason (no verified way to construct a brand-new EDynamicProperty-keyed struct
            -- element over UE4SS Lua reflection - see areas/worldunlocks.lua's own header comment
            -- on why guessing a TArray-append technique is refused project-wide). A pet that has
            -- never earned real XP has no "XP" entry at all yet (the same delta-omission the save
            -- file itself uses - see WorldSaveWriter.ApplyDynamicInt's own comment on this exact
            -- gap once being "silently lost"). Only compare-and-warn when a real change was
            -- actually requested, so unrelated edits (health/name/dead) on the very same pet never
            -- get flagged because of this, and - the round-78 fix - never get thrown away either:
            -- this is a WARNING now, not an error, so it never aborts the fields already applied
            -- above.
            if payload.xp ~= nil then
                local target = math.floor(payload.xp)
                if dynamicInt(npc, "XP") ~= target then
                    local applied = setDynamicInt(npc, "XP", target)
                    if not applied then
                        table.insert(warnings, "this pet has never earned XP yet, so its level can't be " ..
                            "raised live (edit the save file instead, or let it gain real XP first)")
                    end
                end
            end
            return { warnings = warnings }
        end, respond)
    end

    -- Round 78: real live removal. No blueprint function cleanly "releases" a tamed world pet
    -- (checked CreatePetItem/ReleaseFromAIDirector/IsFollower on NPC_Base_ParentBP_C - none of
    -- them detach-and-vanish an already-world-placed NPC), so this destroys the actor outright,
    -- the same technique the reference CheatConsoleCommands mod's own "deleteobject" console
    -- command uses on an arbitrary world actor (CommandsManager.lua: `actor:K2_DestroyActor()`) -
    -- a standard AActor function, not a guess specific to pets. No undo once this runs.
    ctx.handlers["pets.remove"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can remove pets") end
            local npc = payload.id and findPestByGuid(payload.id)
            if not npc then error("pet not found (it may already be gone)") end
            local ok = pcall(function() npc:K2_DestroyActor() end)
            if not ok then error("couldn't remove this pet from the world") end
            return nil
        end, respond)
    end
end
