-- ItemsPickedUpArray/CurrentMaps use the existing discovery RPCs. Hosts can
-- append to CraftedItems through UE4SS FName-array assignment and OnRep_CraftedItems.
-- PhD is an FName on PlayerState. Traits remains a readout: its initialization has
-- gameplay side effects that need dedicated verification before enabling mid-game edits.
return function(ctx)
    local replication = require("replication")
    local names = require("name_arrays")
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
                canDiscoverCrafted = ctx.isHost() and replication.available(),
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
            if payload.itemsCrafted then
                if not ctx.isHost() then error("crafted-item discovery requires host authority") end
                local helper = replication.requireHelper()
                local replacement = names.prepare(component.CraftedItems, payload.itemsCrafted, {})
                component.CraftedItems = replacement
                replication.mark(helper, component, "CraftedItems")
                component:OnRep_CraftedItems()
            end
            callEach(component, payload.itemsSeen or {}, function(c, name) c:Server_CheckNewItemPickedUp(name) end)
            callEach(component, payload.maps or {}, function(c, name) c:Server_AddMapToJournal(name) end)
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
