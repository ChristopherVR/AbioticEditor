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
-- anywhere - see round 76 comment) and removal (no despawn/respawn round trip has any precedent
-- for a living NPC, unlike dropped items' InitDespawn/OnItemDespawn pairing) - pets.set never
-- accepts npcClass and there is no pets.remove; LivePetsSession reports
-- SupportsSpeciesChange/SupportsRemoval as false so the shared tab hides those controls live.
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

    local function writeLimbHealth(npc, limbs)
        for limb, propName in pairs(LIMB_PROPERTY) do
            if limbs[limb] ~= nil then
                pcall(function() npc[propName] = limbs[limb] end)
            end
        end
        -- Same call vitals.set already makes after writing these exact fields, confirmed live.
        pcall(function() npc:OnRep_CurrentHealth() end)
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
                supportsRemoval = false,
                reason = "Only Pest- and Skink-family pets can be matched to a save record live " ..
                    "right now (the game gives them a stable id); Peccary and Lamogi pets can " ..
                    "still be renamed, healed and levelled up in the save file.",
            }
        end, respond)
    end

    ctx.handlers["pets.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can edit pets") end
            local npc = payload.id and findPestByGuid(payload.id)
            if not npc then error("pet not found (it may have been unloaded, or isn't a Pest/Skink-family pet)") end

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
            if payload.limbHealth ~= nil then writeLimbHealth(npc, payload.limbHealth) end
            if payload.xp ~= nil then setDynamicInt(npc, "XP", math.floor(payload.xp)) end
            return nil
        end, respond)
    end
end
