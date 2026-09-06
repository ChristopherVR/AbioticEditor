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
return function(ctx)
    ---@return userdata? progressionComponent
    local function getProgressionComponent(payload)
        local targetPlayer = ctx.resolvePlayer(payload)
        if not targetPlayer then return nil end
        local component = targetPlayer.CharacterProgressionComponent
        if not component or not component:IsValid() then return nil end
        return component
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
            return {
                itemsSeen = readNameArray(function() return component.ItemsPickedUpArray end),
                itemsCrafted = readNameArray(function() return component.CraftedItems end),
                maps = readNameArray(function() return component.CurrentMaps end),
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
            return nil
        end, respond)
    end
end
