-- Live "bulk unlocks" editing (round 76): ITEMS SEEN and MAPS discover on demand; ITEMS CRAFTED
-- is read-only. Grounded in the game's own class layout, NOT a working mod: LiveClassPropsProbe's
-- dump of Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent.uasset carries:
--   prop ItemsPickedUpArray : FArrayProperty   -- matches the file format's ItemsPickedUp concept
--   prop CurrentMaps : FArrayProperty           -- matches the file format's MapsUnlocked concept
--   prop CraftedItems : FArrayProperty          -- read-only, see below
--   func Server_CheckNewItemPickedUp(ItemRowName: FName)
--   func Server_AddMapToJournal(MapRow: FName)
-- Neither write function is called by any installed reference mod, but both are named/shaped
-- exactly like Request_UnlockCompendiumSection - a function on this SAME component class
-- confirmed real by CheatConsoleCommands/scripts/Features.lua:900. See
-- docs/reference/live-editing-protocol.md "general.get / general.set" for the wire shape.
--
-- CraftedItems is read-only: the component updates it automatically from actually crafting
-- something (Local_CheckForNewlyCraftedItems / OnRep_CraftedItems), but exposes no single-item
-- "mark as crafted" function anywhere in its ~200 exported functions, unlike items-seen and maps.
-- The account/owner-id change has no live path at all - renaming which save file a character
-- belongs to is a file-system operation with nothing to call live, so it is not part of this area
-- at all (the desktop app hides that whole section when connected live).
--
-- Round 77: BACKGROUND and TRAITS, re-checked against the game's own class layouts (round 76 had
-- not looked at either).
--
-- Background/PhD IS writable: LiveClassPropsProbe's dump of
-- Content/Blueprints/Meta/Abiotic_PlayerState.uasset (fragment "Abiotic_PlayerState.") carries a
-- plain, no-hash-suffix `prop PhD : FNameProperty` directly on Abiotic_PlayerState_C - the same
-- row-name concept the file format's PhD_ tag stores (see PlayerSaveReader.ReadStats). No
-- OnRep_PhD exists, so this is a direct field write on the server's own authoritative PlayerState
-- object, the same "low blast-radius direct write" category vitals.set/inventory.set already use
-- for fields with no RepNotify - a replicated UPROPERTY changed on the server object replicates to
-- owning clients on the next network update with no RPC call needed.
--
-- Traits stays read-only. CharacterProgressionComponent.Traits is read the same way the reference
-- mod's own "traits" console command does (CommandsManager.lua "Show Traits":
-- `progressionComponen.Traits[i]:ToString()`), so the read is solid precedent. The ONLY functions
-- touching that array are SetTraits/GetTraits/InitializeTraits - no Server_/Request_ prefix, i.e.
-- not RPCs, and used only by the one-time character-creation flow
-- (Abiotic_PlayerController.Server_SetupInitialTraits -> Client_DoTraitSelectionSequence ->
-- GoToTraitsSelection); calling SetTraits/InitializeTraits mid-game would re-run that creation
-- flow (re-rolls AmnesiaThreshold, resets FirstTimeTraitsRunning), not swap one trait. The native
-- PDB's only trait-adjacent RPCs are UCharacterBuffComponent::Server_AddTraitBuff/
-- Server_RemoveTraitBuff(FBuffDebuffRowHandle) - a DIFFERENT system (a temporary gameplay buff
-- keyed by a buff/debuff row handle) that does not touch CharacterProgressionComponent.Traits or
-- the save's Traits_ array at all, so calling it would not actually "give a trait" the way this
-- list means. No targeted single-trait write path exists; this area only reads the list so a live
-- session (which has no CHARACTER tab) can show what a character actually has.
return function(ctx)
    ---@return userdata? progressionComponent
    local function getProgressionComponent(payload)
        local targetPlayer = ctx.resolvePlayer(payload)
        if not targetPlayer then return nil end
        local component = targetPlayer.CharacterProgressionComponent
        if not component or not component:IsValid() then return nil end
        return component
    end

    ---Finds the connected player's PlayerState (APawn.PlayerState is a base-engine property
    ---present on every pawn; main.lua's own resolvePlayer/localPlayerId already reads the SAME
    ---property off the CONTROLLER (`controller.PlayerState`) for a different purpose, so reading
    ---it off the PAWN here follows the identical, already-proven access pattern).
    ---@return userdata? playerState
    local function getPlayerState(payload)
        if payload and payload.playerId then
            local states = ctx.allPlayerStates()
            for index, state in ipairs(states) do
                if state:IsValid() and ctx.playerId(state, index - 1) == payload.playerId then return state end
            end
            return nil
        end
        local player = ctx.getMyPlayer()
        if not player then return nil end
        local ok, state = pcall(function() return player.PlayerState end)
        if ok and state and state:IsValid() then return state end
        return nil
    end

    -- Same indexed iteration + :ToString() the reference mod's "traits" console command uses on
    -- progressionComponen.Traits, applied here to DIFFERENT array properties with no precedent of
    -- their own, hence the pcall.
    local function readNameArray(getArray)
        local result = { __forceArray = true }
        local ok, arr = pcall(getArray)
        if not ok or not arr then return result end
        for i = 1, #arr do
            local okName, name = pcall(function() return arr[i]:ToString() end)
            if okName and name and name ~= "" then table.insert(result, name) end
        end
        return result
    end

    ctx.handlers["general.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            local background = nil
            local state = getPlayerState(payload)
            if state then
                local ok, phd = pcall(function() return state.PhD:ToString() end)
                if ok and phd and phd ~= "" and phd ~= "None" then background = phd end
            end
            return {
                itemsSeen = readNameArray(function() return component.ItemsPickedUpArray end),
                itemsCrafted = readNameArray(function() return component.CraftedItems end),
                maps = readNameArray(function() return component.CurrentMaps end),
                -- Read-only here (see the file header comment) - progressionComponen.Traits is
                -- the same array the reference mod's "traits" console command reads.
                traits = readNameArray(function() return component.Traits end),
                background = background,
            }
        end, respond)
    end

    local function callEach(component, ids, call)
        for i = 1, #ids do
            if ids[i] and ids[i] ~= "" then
                pcall(function() call(component, FName(ids[i], EFindName.FNAME_Find)) end)
            end
        end
    end

    ctx.handlers["general.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            callEach(component, payload.itemsSeen or {}, function(c, name) c:Server_CheckNewItemPickedUp(name) end)
            callEach(component, payload.maps or {}, function(c, name) c:Server_AddMapToJournal(name) end)
            -- payload.itemsCrafted is deliberately not accepted - see the file header comment.
            if payload.background and payload.background ~= "" then
                local state = getPlayerState(payload)
                if state then
                    -- Direct field write, no OnRep to call (see the file header comment).
                    pcall(function() state.PhD = FName(payload.background, EFindName.FNAME_Find) end)
                end
            end
            -- payload.traits is deliberately not accepted - see the file header comment.
            return nil
        end, respond)
    end
end
