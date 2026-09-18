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
--     properties - there is still no stable id for them. Round 105 (below) stopped omitting them
--     entirely: they are now listed with a per-session actor-path id instead of a save-matching
--     one (never silently reused as if it were the save's own key - every row says outright
--     whether it is save-matched, see `matched` below), with only the universal health/alive-state
--     fields editable. Renaming and levelling them still only works in the save file.
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
-- Round-105 status (superseded by round 109 below): changing species stayed refused through
-- round 105 (SpawnPet is a GameMode function that would mean despawning and respawning the actor
-- with an FTransform this probe never saw constructed anywhere - see round 76 comment).
--
-- Round-105 (closing the Peccary/Lamogi gap "as far as the game allows", not just re-confirming
-- it): re-grounded two things against the coordinator's own class dumps
-- (tests/AbioticEditor.Probes/LiveGapProbe.cs output, "NPC_Monster_Peccary" and
-- "NPC_Monster_WinterSprite" fragments, with function bytecode) rather than re-asserting round
-- 77/79's conclusion unchanged:
--
--   1. A REAL, GENERIC tamed marker exists and is NOT Pest/Skink-specific: NPC_Monster_WinterSprite_C's
--      own compiled graph calls a static library function, `AbioticFunctionLibrary::IsTamedPet(Self)`
--      (bool, one parameter - the actor to check), from three of its own overridden functions
--      (IsInvincible, TargetBlockedAttack, UpdateHealthTextureIndex). This is a general-purpose
--      actor query, not a Pest/Skink-only helper, so PETS.LIST now sweeps the SAME parent class
--      npcs.lua/npcs.list already sweeps for the whole CREATURES tab (NPC_Base_ParentBP_C -
--      hierarchy-inclusive, so this needs no Peccary/Lamogi/whatever-comes-next class list at
--      all) and feature-detects per instance: skip anything with its own Guid (already covered by
--      the Pest/Skink path above), keep anything IsTamedPet(npc) reports true for. This is how
--      Peccary/Lamogi pets get listed now, plus automatically any future tamed family that also
--      never grows a Guid. Calling this on a NON-WinterSprite actor for the first time is new and
--      genuinely unverified until tested live (same honesty caveat every other first-use call in
--      this project already carries), which is why it stays pcall-guarded per actor exactly like
--      the Guid check above.
--   2. These rows still cannot be matched to a save's PetNPC record (still no Guid/PetName/
--      DynamicProperties/FollowingOwner - re-confirmed against the full dump, not assumed), so
--      their `id` is the live actor's own GetFullName() (matching npcs.lua's own id scheme for the
--      CREATURES tab) - stable for that actor's current lifetime only, never a save key. They come
--      back with `matched = false`. Per-limb health and IsDead ARE real, universal
--      AbioticCharacter fields on these actors too (the same fields main.lua's vitals.set/pets.set
--      already prove live for the player and for Pest/Skink pets), so those two stay editable;
--      customName/xp are refused with a warning instead of an error, since the class genuinely has
--      no such field to write, not because of a policy choice.
--   3. Re-checked whether the tamed marker unlocks a safe live species change too: the game's own
--      Abiotic_Survival_GameMode_C DOES have a real SpawnPet(Class, SpawnTransform, Guid, Name,
--      Owner, DynamicProperties, Tamed) function with exactly the shape a "respawn as a different
--      class" edit would need - but SpawnTransform is an FTransform, a nested struct
--      (rotation/translation/scale) this project has NO working construction precedent for
--      anywhere over UE4SS Lua reflection, unlike the flat FVector/FRotator tables round 76 proved
--      out for spawn.set. Guessing an unverified struct shape for a native-bridged call is exactly
--      what caused the BASES tab's fatal (non-catchable) crash in round 79 - so this stayed refused
--      project-wide through round 105. SEE THE ROUND-109 SECTION BELOW: this was re-examined with
--      a different technique (an engine-returned transform passed through unchanged, never a
--      fabricated table) rather than re-asserted unchanged - it is no longer a blanket refusal.
--
-- Round-109: species change, for MATCHED (Pest/Skink-family) pets only, no longer refused.
-- Re-examined against the coordinator's fresh pak dump (Abiotic_Survival_GameMode.json,
-- layouts.txt ~line 5470) rather than re-asserting the round-76/105 conclusion unchanged:
--
--   1. SpawnPet's real signature, read straight from the dump (not guessed):
--      SpawnPet(Class: class, SpawnTransform: FTransform, Guid: string, Name: text,
--      Owner: object, DynamicProperties: array&, Tamed: bool) -> NPC_Base_ParentBP_C actor.
--      FunctionFlags is "FUNC_Public | FUNC_HasOutParms | FUNC_HasDefaults |
--      FUNC_BlueprintCallable | FUNC_BlueprintEvent" - the exact same flag combination as every
--      other function this project already calls live (TeleportPlayer, TrySpawnNPCNew,
--      SetSpawnOnCooldown, ...), not some special/unreachable kind of function. SpawnTransform's
--      own struct type is confirmed `Class'Transform'` from `/Script/CoreUObject` (ElementSize 96
--      bytes, matching the engine's real double-precision FTransform), not a guess.
--   2. The SpawnTransform blocker, re-examined: round 76/105 refused it because this project had
--      no working CONSTRUCTION precedent for a hand-built FTransform table - and building an
--      unverified struct table for a native call is exactly what caused the BASES tab's fatal,
--      non-catchable crash in round 79 (see that round's own writeup: a struct mismatch in a
--      native UFunction call does not raise a Lua error pcall can catch - it can be memory
--      corruption on the C++ side). This round does NOT build a table at all: it reads the OLD
--      pet actor's OWN current transform via `npc:K2_GetActorTransform()` - a standard,
--      zero-argument, BlueprintPure AActor function, the exact same category of call as
--      K2_GetActorLocation/K2_GetActorRotation, which this project has ALREADY proven live
--      (spawn.lua's TeleportPlayer path, vehicles.lua's K2_TeleportTo path: an engine-returned
--      FVector/FRotator struct handed straight back into another native call's matching struct
--      parameter, sometimes with individual leaf fields overwritten first). K2_GetActorTransform
--      is the same shape of call at the AActor level, just for FTransform instead of FVector/
--      FRotator, and its result is passed to SpawnPet completely UNCHANGED - no field on it is
--      ever read, guessed, or written, which is a strictly SMALLER risk than the already-proven
--      vector/rotator case (that one also mutates individual leaf fields before passing it back).
--   3. Every other SpawnPet argument is likewise never fabricated: Guid/Tamed are plain scalars of
--      types this module already writes; Name is the OLD pet's own `PetName` FText userdata,
--      passed straight through (never re-encoded via a Lua string, unlike the customName write
--      below); Owner is the OLD pet's own `FollowingOwner` object reference (confirmed real and
--      pcall-readable on this exact family by companions.lua, round 78/79); DynamicProperties is
--      the OLD pet's own live DynamicProperties array, passed by reference unchanged (this is WHY
--      XP/mutation progress survive a species change even though nothing here touches them
--      directly). Nothing here is a hand-fabricated struct or a guessed field name.
--   4. Health/limb state is NOT part of SpawnPet's signature, so a species change does NOT carry
--      the old pet's current health over - the new actor gets its class's normal spawn health.
--      Re-healing afterward (if needed) is a separate, already-proven pets.set health write.
--   5. Safety ordering: SpawnPet is called FIRST; the returned actor is required to be valid AND
--      to report back the SAME Guid that was passed in before the OLD actor is destroyed. Any
--      failure at any step (unresolved class, unreadable transform, the call itself erroring, an
--      invalid or mismatched-identity result) leaves the OLD pet completely untouched and reports
--      a warning - never a thrown error, and never a destroy of the original actor without a
--      confirmed replacement. See `trySpeciesChange` below. A spawn that "worked" but came back
--      with the wrong identity is the one case that can leave a stray extra actor behind (the new
--      one, un-linked) even though the edit is reported as failed - see that function's remarks.
--   6. HONEST CAVEAT, unlike almost everything else in this file: a wrong-shaped argument to a
--      native UFunction call is the ONE class of failure in this whole project that `pcall` cannot
--      be trusted to catch (round 79's BASES crash). Every reasoning step above argues why THIS
--      call should not hit that failure mode (no fabricated struct anywhere in it), but
--      `K2_GetActorTransform`/`SpawnPet` have never actually run against the real game - this is
--      exercised only by the Lua stub harness so far. Treat the first real-game use of
--      pets.set{npcClass=...} as a genuine test, ideally on a low-stakes/replaceable pet in a
--      singleplayer or otherwise easily-restartable session, before trusting it broadly.
--
-- Species change is refused for UNMATCHED pets (Peccary/Lamogi and anything else with no Guid):
-- there is no Guid to preserve identity with, so there is nothing to verify a "same pet" result
-- against - see the `not matched and payload.npcClass ~= nil` branch below.
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
    -- Round 105: the generic tamed sweep - every creature, not a Peccary/Lamogi-specific list (see
    -- this file's own header comment). NPC_Base_ParentBP_C is hierarchy-inclusive, the exact same
    -- parent class main.lua's npcs.list already sweeps for the whole CREATURES tab.
    local ALL_NPC_CLASS = "NPC_Base_ParentBP_C"
    local FUNCTION_LIBRARY_PATH = "/Script/AbioticFactor.Default__AbioticFunctionLibrary"
    local LIMB_PROPERTY = {
        Head = "CurrentHealth_Head", Torso = "CurrentHealth_Torso",
        LeftArm = "CurrentHealth_LeftArm", RightArm = "CurrentHealth_RightArm",
        LeftLeg = "CurrentHealth_LeftLeg", RightLeg = "CurrentHealth_RightLeg",
    }
    -- Round 109: same folder PetCatalog.cs's own ClassPathFor uses on the .NET side
    -- (Core/Catalogs/World/PetCatalog.cs) - only used as a fallback when a caller sends a bare
    -- short class name instead of the full path WorldPetsTab.razor's own VariantOptions() always
    -- sends (variant.ClassPath).
    local NPC_CLASS_FOLDER = "/Game/Blueprints/Characters/NPCs/"

    -- Round 109: same world->game-mode lookup main.lua's own isHost() and containment.lua's own
    -- gameMode() already use and this project has already proven works live - reused here rather
    -- than introducing a second, untested access path to the same object.
    local function gameMode()
        local ok, world = pcall(function() return ctx.UEHelpers.GetWorld() end)
        if not ok or not world or not world:IsValid() then return nil end
        local ok2, gm = pcall(function() return world.AuthorityGameMode end)
        if ok2 and gm and gm:IsValid() then return gm end
        return nil
    end

    -- Resolves a pet species target to a real UClass. Accepts either the full soft-class path the
    -- offline PetCatalog/WorldPetsTab already sends (e.g. ".../NPC_Peccary_Sow.NPC_Peccary_Sow_C")
    -- or a bare short class name, and falls back to LoadAsset the same way main.lua's own data
    -- table lookup already does for a not-yet-loaded asset (main.lua:944-948, a documented UE4SS
    -- global) - never a new pattern. No hardcoded leaf class list: whatever path is given is what
    -- gets resolved.
    local function resolveClass(path)
        if not path or path == "" then return nil end
        local full = path
        if not full:find("%.") then
            local short = full:match("([^/]+)$") or full
            full = NPC_CLASS_FOLDER .. short .. "." .. short
        end
        local okFind, class = pcall(function() return StaticFindObject(full) end)
        if okFind and class and class:IsValid() then return class end
        if type(LoadAsset) == "function" then
            pcall(function() LoadAsset(full) end)
            local okRetry, retried = pcall(function() return StaticFindObject(full) end)
            if okRetry and retried and retried:IsValid() then return retried end
        end
        return nil
    end

    -- The trailing class-name token of a class path or a GetFullName()-style "Class Path" string,
    -- used only to compare "is this actually a different species than the pet already is" -
    -- payload.npcClass round-trips as the CURRENT class on every non-species-change pets.set call
    -- (WorldPetsTab.razor's Apply() always resends pet.NpcClass unless the caller explicitly picked
    -- a new one), so this comparison is what keeps a plain health/name edit from ever attempting a
    -- species change at all.
    local function shortClassTag(value)
        if not value then return nil end
        local tail = value:match("([^./%s]+)$")
        return tail and tail:upper() or nil
    end

    -- Round 109: live species change for a MATCHED (Pest/Skink-family) pet - see this file's own
    -- header comment for the full reasoning on why the one previously-blocking piece
    -- (SpawnTransform, an FTransform) is safe to attempt now: the OLD actor's own transform is read
    -- fresh and passed straight through, never fabricated. Every other argument is likewise read
    -- straight off the OLD actor, never guessed. pcall-guarded at every step; the new actor's Guid
    -- is verified to match before the old one is destroyed - a mismatch (or any failure before
    -- that point) leaves the old pet completely untouched. Returns true, nil on success, or
    -- false, "reason" on failure (never throws - this is applied the same non-fatal way the round-
    -- 78 XP/name fields already are, see pets.set below).
    local function trySpeciesChange(npc, guid, npcClassPath)
        local gm = gameMode()
        if not gm then return false, "no authoritative game mode (are you the host, and is the world loaded?)" end

        local targetClass = resolveClass(npcClassPath)
        if not targetClass then return false, "unknown or not-yet-loaded creature type" end

        -- The one genuinely new call this round adds: a standard, zero-argument, BlueprintPure
        -- AActor function (same category as the already-proven K2_GetActorLocation/
        -- K2_GetActorRotation) read fresh off the OLD pet and passed to SpawnPet UNCHANGED - no
        -- field on the returned struct is ever touched. See this file's header comment, point 2.
        local okTransform, transform = pcall(function() return npc:K2_GetActorTransform() end)
        if not okTransform or not transform then
            return false, "couldn't read this pet's current position/rotation"
        end

        -- Name/Owner/DynamicProperties: read straight off the OLD actor, never reconstructed.
        local okName, name = pcall(function() return npc.PetName end)
        if not okName then name = nil end
        local okOwner, owner = pcall(function() return npc.FollowingOwner end)
        if not okOwner then owner = nil end
        local okDyn, dynamic = pcall(function() return npc.DynamicProperties end)
        if not okDyn then dynamic = nil end

        local okSpawn, newPet = pcall(function()
            return gm:SpawnPet(targetClass, transform, guid, name, owner, dynamic, true)
        end)
        if not okSpawn or not newPet or not newPet:IsValid() then
            return false, "the game would not spawn the new creature type"
        end

        local okNewGuid, newGuid = pcall(function() return newPet.Guid:ToString() end)
        if not okNewGuid or newGuid ~= guid then
            -- The spawn "worked" but didn't come back as the same pet identity - do NOT destroy
            -- the old actor over an edit that didn't actually land as requested. This can leave
            -- the new actor behind as an extra, unmatched creature in the world (a later
            -- pets.list sweep will list it via the round-105 tamed-marker path) - a real,
            -- acknowledged side effect of a spawn that partially succeeded, never a silent loss
            -- of the original pet.
            return false, "the new creature didn't come back with the same identity, so the original pet was kept"
        end

        local okDestroy = pcall(function() npc:K2_DestroyActor() end)
        if not okDestroy then
            return false, "the new creature was created, but the old one couldn't be removed - both may now exist"
        end
        return true, nil
    end

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

    local function guidOf(npc)
        local ok, guid = pcall(function() return npc.Guid:ToString() end)
        if ok and guid and guid ~= "" then return guid end
        return nil
    end

    local function findPestByGuid(id)
        for _, candidate in ipairs(ctx.findAll(PET_FAMILY_CLASS)) do
            if candidate:IsValid() and guidOf(candidate) == id then return candidate end
        end
        return nil
    end

    -- Round 105: the fallback for a row that came back matched=false - the same npcFullName-based
    -- re-match main.lua's npcs.set already relies on (a fresh scan every call, since a world's NPC
    -- roster changes constantly). Deliberately does NOT re-check IsTamedPet here: a caller editing
    -- a row it already saw from pets.list should never lose that edit just because the tamed state
    -- flickered between calls, the same way findPestByGuid never re-checks anything about the pet
    -- besides its identity.
    local function findUnmatchedByFullName(id)
        for _, candidate in ipairs(ctx.findAll(ALL_NPC_CLASS)) do
            if candidate:IsValid() and guidOf(candidate) == nil and ctx.fullName(candidate) == id then
                return candidate
            end
        end
        return nil
    end

    -- Returns the pet actor and whether it is `matched` (a save-linkable Pest/Skink-family Guid)
    -- or not (round 105's generic tamed-marker fallback, see this file's header comment).
    local function findPet(id)
        if not id then return nil, false end
        local byGuid = findPestByGuid(id)
        if byGuid then return byGuid, true end
        return findUnmatchedByFullName(id), false
    end

    local function functionLibrary()
        local ok, lib = pcall(function() return StaticFindObject(FUNCTION_LIBRARY_PATH) end)
        if ok and lib and lib:IsValid() then return lib end
        return nil
    end

    -- AbioticFunctionLibrary::IsTamedPet(Actor) - a real, general-purpose static function found in
    -- NPC_Monster_WinterSprite_C's own compiled graph (IsInvincible/TargetBlockedAttack/
    -- UpdateHealthTextureIndex all call it on Self). Calling it on a non-WinterSprite actor is new
    -- and unverified against the real game until tested - see this file's header comment - hence
    -- the pcall guard here, same as every other first-use call in this project.
    local function isTamedPet(npc, lib)
        if not lib then return false end
        local ok, tamed = pcall(function() return lib:IsTamedPet(npc) end)
        return ok and tamed == true
    end

    local function petRows()
        local result = { __forceArray = true }
        for _, npc in ipairs(ctx.findAll(PET_FAMILY_CLASS)) do
            -- pcall per pet actor (matching main.lua containers.list's own per-container guard):
            -- isDead below is a direct, unguarded property read, and this walks every Pest/Skink
            -- actor loaded in the world - one actor in an unusual state should skip only itself,
            -- not fail the whole PETS tab listing with an uncaught Lua error.
            pcall(function()
                if not npc:IsValid() then return end
                local guid = guidOf(npc)
                if not guid then return end
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
                    matched = true,
                })
            end)
        end
        return result
    end

    -- Round 105: every OTHER tamed creature (Peccary/Lamogi today, and any future family that
    -- never grows a Guid) - found generically via IsTamedPet, not a hardcoded class list. Skips
    -- anything with its own Guid (already covered by petRows() above) so a pet is never listed
    -- twice. id is the live actor's own full path (matching npcs.lua's id scheme) since there is
    -- still no stable id these rows could share with a save's PetNPC record.
    local function unmatchedTamedRows(lib)
        local result = { __forceArray = true }
        for _, npc in ipairs(ctx.findAll(ALL_NPC_CLASS)) do
            pcall(function()
                if not npc:IsValid() then return end
                if guidOf(npc) then return end
                if not isTamedPet(npc, lib) then return end
                local fullName = ctx.fullName(npc)
                if not fullName then return end
                local x, y, z = ctx.actorLocation(npc)
                table.insert(result, {
                    id = fullName,
                    npcClass = ctx.classLabel(fullName),
                    isDead = npc.IsDead == true,
                    customName = nil,
                    x = x, y = y, z = z,
                    limbHealth = readLimbHealth(npc),
                    xp = 0,
                    matched = false,
                })
            end)
        end
        return result
    end

    ctx.handlers["pets.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            local lib = functionLibrary()
            local pets = petRows()
            for _, row in ipairs(unmatchedTamedRows(lib)) do table.insert(pets, row) end
            return {
                pets = pets,
                isHost = ctx.isHost(),
                available = true,
                -- Round 109: this build of the live agent knows how to attempt a species change
                -- (see trySpeciesChange/pets.set below) - an older agent build simply omits this
                -- field, which LivePetsSession reads as false (see that class's remarks), so the
                -- app's creature-type control stays hidden against an older agent automatically.
                -- Only MATCHED (Pest/Skink-family) rows can actually use it - see pets.set.
                supportsSpeciesChange = true,
                -- Round 78: real removal, evidenced by the reference CheatConsoleCommands mod's
                -- own "deleteobject" command destroying an arbitrary world actor with
                -- `actor:K2_DestroyActor()` (CommandsManager.lua) - see pets.remove below. There
                -- is no undo once this runs, unlike the file session's staged delete.
                supportsRemoval = true,
                reason = "Pest- and Skink-family pets can be matched to a save record live (the " ..
                    "game gives them a stable id) - their creature type can also be changed live " ..
                    "now (round 109), though this is a first-use, largely unverified call: try it " ..
                    "on a low-stakes pet first. Peccary and Lamogi pets are listed too (found by " ..
                    "the game's own tamed-creature check) but can't be matched to a save record, " ..
                    "so only their health and alive/dead status can be changed here - rename, " ..
                    "level, and change the species of those in the save file instead. Removing a " ..
                    "pet here despawns it immediately - there is no undo.",
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
            local npc, matched = findPet(payload.id)
            if not npc then error("pet not found (it may have been unloaded, or is no longer a tamed pet)") end

            local warnings = { __forceArray = true }

            if payload.isDead ~= nil and npc.IsDead ~= payload.isDead then
                npc.IsDead = payload.isDead
                pcall(function() npc:OnRep_IsDead() end)
            end
            -- Round 105: an unmatched row's class has no PetName/DynamicProperties at all (see
            -- this file's header comment) - never even attempted, reported as a warning instead of
            -- a silently-ignored no-op, the same non-fatal shape the round-78 XP fix already uses.
            if not matched and payload.customName ~= nil then
                table.insert(warnings, "this pet's creature type has no name field to write live " ..
                    "(edit the save file instead)")
            end
            if matched and payload.customName ~= nil then
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
            if not matched and payload.xp ~= nil then
                table.insert(warnings, "this pet's creature type has no XP/level field to write " ..
                    "live (edit the save file instead)")
            end
            if matched and payload.xp ~= nil then
                local target = math.floor(payload.xp)
                if dynamicInt(npc, "XP") ~= target then
                    local applied = setDynamicInt(npc, "XP", target)
                    if not applied then
                        table.insert(warnings, "this pet has never earned XP yet, so its level can't be " ..
                            "raised live (edit the save file instead, or let it gain real XP first)")
                    end
                end
            end
            -- Round 109: species change - see this file's own header comment and
            -- trySpeciesChange's remarks for the full reasoning. Compared by trailing class-name
            -- token, not equality of the whole string, because WorldPetsTab.razor's Apply()
            -- resends the pet's OWN current npcClass on every non-species-change call (a plain
            -- health/name/xp edit must never attempt a species change just because the field was
            -- present in the payload). Applied LAST and only once: a species change spawns a
            -- brand-new actor, so anything above already wrote to the OLD actor's fields (a
            -- harmless no-op in the normal case, since those fields already matched what was
            -- re-sent) - see the header comment's point 4 for what does and doesn't carry over.
            if payload.npcClass ~= nil then
                local currentTag = shortClassTag(ctx.classLabel(ctx.fullName(npc)))
                local requestedTag = shortClassTag(payload.npcClass)
                if requestedTag and requestedTag ~= currentTag then
                    if not matched then
                        table.insert(warnings, "this pet can't be matched to a save record, so " ..
                            "its species can't be changed live (edit the save file instead)")
                    else
                        local ok, failure = trySpeciesChange(npc, payload.id, payload.npcClass)
                        if not ok then
                            table.insert(warnings, "couldn't change this pet's creature type live: " ..
                                tostring(failure))
                        end
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
            local npc = payload.id and (findPet(payload.id))
            if not npc then error("pet not found (it may already be gone)") end
            local ok = pcall(function() npc:K2_DestroyActor() end)
            if not ok then error("couldn't remove this pet from the world") end
            return nil
        end, respond)
    end
end
