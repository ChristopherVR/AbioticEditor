-- ItemsPickedUpArray/CurrentMaps use the existing discovery RPCs. Hosts can
-- append to CraftedItems through UE4SS FName-array assignment and OnRep_CraftedItems.
-- Trait edits update only the persistent trait buff and replicated trait-name array.
-- Never replay InitializeTraits: it also grants starting items, skill XP and random rewards.
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

    local function traitRuntime(payload)
        if not ctx.isHost() then error("trait editing requires host authority") end
        local helper = replication.requireHelper()
        local library = StaticFindObject("/Script/AbioticFactor.Default__BuffDebuffHandleFunctionLibrary")
        local class = StaticFindObject("/Script/AbioticFactor.CharacterBuffComponent")
        if not library or not library:IsValid() or not class or not class:IsValid() then
            error("trait buff support is unavailable")
        end
        local player = ctx.resolvePlayer(payload)
        local buffs = player and player:GetComponentByClass(class)
        if not buffs or not buffs:IsValid() then error("no character buff component found") end
        return helper, library, buffs
    end

    ctx.handlers["general.trait.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if type(payload.id) ~= "string" or payload.id == "" or type(payload.enabled) ~= "boolean" then
                error("trait id and enabled state are required")
            end
            if type(payload.buffRowName) ~= "string" then error("installed trait buff row is required") end
            local helper, library, buffs = traitRuntime(payload)
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            local targetName = FName(payload.id, EFindName.FNAME_Find)
            if targetName:ToString() == "None" then error("unknown trait row: " .. payload.id) end
            local targetId = targetName:ToString():lower()
            local current, replacement, present = {}, {}, false
            for index = 1, #component.Traits do
                local name = FName(component.Traits[index]:ToString(), EFindName.FNAME_Find)
                current[#current + 1] = name
                local matches = name:ToString():lower() == targetId
                if matches then present = true end
                if payload.enabled or not matches then replacement[#replacement + 1] = name end
            end
            if present == payload.enabled then return nil end
            if payload.enabled then replacement[#replacement + 1] = targetName end
            local handle
            if payload.buffRowName ~= "" and payload.buffRowName ~= "None" then
                local buffName = FName(payload.buffRowName, EFindName.FNAME_Find)
                if buffName:ToString() == "None" then error("unknown trait buff row: " .. payload.buffRowName) end
                handle = library:MakeBuffDebuffRowHandle(buffName)
                local path = handle.DataTablePath:ToString()
                path = path:match("'([^']+)'$") or path
                local dataTable = StaticFindObject(path)
                local tables = StaticFindObject("/Script/Engine.Default__DataTableFunctionLibrary")
                if not dataTable or not dataTable:IsValid() or not tables or not tables:IsValid() then
                    error("trait buff table is unavailable")
                end
                if not tables:DoesDataTableRowExist(dataTable, handle.RowName) then
                    error("unknown trait buff row: " .. payload.buffRowName)
                end
            end
            local function applyBuff(enabled)
                if not handle then return end
                if enabled then buffs:Server_AddTraitBuff(handle)
                else buffs:Server_RemoveTraitBuff(handle) end
            end
            local ok, err = pcall(function()
                applyBuff(payload.enabled)
                component.Traits = replacement
                replication.mark(helper, component, "Traits")
            end)
            if not ok then
                pcall(function() component.Traits = current; replication.mark(helper, component, "Traits") end)
                pcall(function() applyBuff(not payload.enabled) end)
                error(err)
            end
            return nil
        end, respond)
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
                traits = readNameArray(function() return component.Traits end),
                background = background,
                canDiscoverCrafted = ctx.isHost() and replication.available(),
                canEditTraits = pcall(function() traitRuntime(payload) end),
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
            -- Trait edits use their dedicated command with an installed catalog buff row.
            return nil
        end, respond)
    end
end
