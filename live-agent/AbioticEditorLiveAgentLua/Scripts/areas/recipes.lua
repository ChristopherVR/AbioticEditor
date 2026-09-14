-- Recipe names are stored in CharacterProgressionComponent.RecipesUnlockedArray.
-- Hosts unlock and relock by replacing the reflected
-- FName array and invoking the exported OnRep_RecipesUnlockedArray. Array assignment
-- is grounded in UE4SS LuaUObject.cpp push_arrayproperty, not an invented relock RPC.
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

    -- Reads a live TArray<FName> property as a plain Lua string array: the same indexed
    -- iteration + :ToString() the reference mod's own "traits" console command uses on
    -- progressionComponen.Traits (CommandsManager.lua, "Show Traits"), applied here to a
    -- DIFFERENT array property with no precedent of its own, hence the pcall.
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

    ctx.handlers["recipes.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            return { unlockedIds = readNameArray(function() return component.RecipesUnlockedArray end), canLock = ctx.isHost() and replication.available() }
        end, respond)
    end

    ctx.handlers["recipes.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            if ctx.isHost() and replication.available() then
                local helper = replication.requireHelper()
                local replacement = names.prepare(component.RecipesUnlockedArray, payload.unlockIds, payload.lockIds)
                component.RecipesUnlockedArray = replacement
                replication.mark(helper, component, "RecipesUnlockedArray")
                component:OnRep_RecipesUnlockedArray()
                return nil
            end
            if payload.lockIds and #payload.lockIds > 0 then error("relocking recipes requires host authority and replication notification support") end
            local ids = payload.unlockIds or {}
            for i = 1, #ids do
                if ids[i] and ids[i] ~= "" then
                    -- Same FName-from-string pattern main.lua's writeSlot() uses for item ids:
                    -- FName(str, EFindName.FNAME_Find).
                    component:Request_UnlockNewRecipe(FName(ids[i], EFindName.FNAME_Find))
                end
            end
            return nil
        end, respond)
    end
end
