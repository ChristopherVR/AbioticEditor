-- Live recipe-unlock editing (round 76). Grounded in the game's own class layout, NOT a working
-- mod: tests/AbioticEditor.Probes/LiveClassPropsProbe.cs (fragment "CharacterProgressionComponent")
-- dumps Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent.uasset's exported
-- properties/functions from the installed game's paks. That dump carries:
--   prop RecipesUnlockedArray : FArrayProperty   -- the unlocked recipe row names, readable directly
--   func Request_UnlockNewRecipe(RecipeRow: FName)  -- the unlock RPC
-- Neither is called by any installed reference mod (CheatConsoleCommands has no recipe-unlocker
-- feature at all), but Request_UnlockNewRecipe is named and shaped exactly like
-- Request_UnlockCompendiumSection - a function on this SAME component class that IS confirmed
-- real by CheatConsoleCommands/scripts/Features.lua:900 (JournalEntryUnlocker), called the exact
-- same way (`component:Request_X(FName(...))`). See docs/reference/live-editing-protocol.md
-- "recipes.get / recipes.set" for the wire shape.
--
-- No lock/relock/remove-recipe function exists anywhere in the component's ~200 exported
-- functions - only "unlock" ones, matching how every "unlockallX" cheat in the reference mod is
-- one-directional too. So this area can only ever unlock a recipe, never lock one back up.
return function(ctx)
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
            return { unlockedIds = readNameArray(function() return component.RecipesUnlockedArray end) }
        end, respond)
    end

    ctx.handlers["recipes.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            local ids = payload.unlockIds or {}
            for i = 1, #ids do
                if ids[i] and ids[i] ~= "" then
                    -- Same FName-from-string pattern main.lua's writeSlot() uses for item ids:
                    -- FName(str, EFindName.FNAME_Find).
                    pcall(function()
                        component:Request_UnlockNewRecipe(FName(ids[i], EFindName.FNAME_Find))
                    end)
                end
            end
            return nil
        end, respond)
    end
end
