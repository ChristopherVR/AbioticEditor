-- GlobalRecipesUnlocked/GlobalRecipesResearched are TSet<FName> on the game state.
-- Use UE4SS TSet.ForEach/Add/Remove with host gating and runtime capability checks.
-- GlobalItemsPickedUp, emails, journals and compendium lists are TArray<FName>.
-- Fields are grounded in the exported Abiotic_Survival_GameState layout.
return function(ctx)
    local replication = require("replication")
    local function canEditRecipes(state)
        local ok, supported = pcall(function()
            return state.GlobalRecipesUnlocked.Add ~= nil and state.GlobalRecipesUnlocked.Remove ~= nil
                and state.GlobalRecipesUnlocked.ForEach ~= nil
                and state.GlobalRecipesResearched.ForEach ~= nil
                and state.GlobalRecipesResearched.Add ~= nil and state.GlobalRecipesResearched.Remove ~= nil
        end)
        return ok and supported
    end
    local function readSet(set)
        local result = { __forceArray = true }
        set:ForEach(function(element) result[#result + 1] = element:get():ToString() end)
        table.sort(result)
        return result
    end
    local function currentGameState()
        local ok, gameState = pcall(function() return ctx.UEHelpers.GetGameStateBase() end)
        if ok and gameState and gameState:IsValid() then return gameState end
        return nil
    end

    local function readArray(getArray)
        local result = { __forceArray = true }
        local ok, arr = pcall(getArray)
        if not ok or not arr then return result end
        for i = 1, #arr do
            local okName, name = pcall(function() return arr[i]:ToString() end)
            if okName and name and name ~= "" then table.insert(result, name) end
        end
        return result
    end

    ctx.handlers["worldunlocks.get"] = function(_, respond)
        ctx.runOnGameThread(function()
            local gameState = currentGameState()
            if not gameState then error("the world is not loaded (are you in a world?)") end
            return {
                isHost = ctx.isHost(),
                canEditRecipes = ctx.isHost() and replication.available() and canEditRecipes(gameState),
                -- FSetProperty: best-effort, same optimistic-pcall caveat as codex.lua's old
                -- Local_AllCompendiumEntries read (see header comment).
                recipesUnlocked = canEditRecipes(gameState) and readSet(gameState.GlobalRecipesUnlocked)
                    or readArray(function() return gameState.GlobalRecipesUnlocked end),
                recipesResearched = canEditRecipes(gameState) and readSet(gameState.GlobalRecipesResearched)
                    or readArray(function() return gameState.GlobalRecipesResearched end),
                -- FArrayProperty: same confirmed technique as codex.lua's EmailsRead/JournalEntries.
                itemsPickedUp = readArray(function() return gameState.GlobalItemsPickedUp end),
                emailsRead = readArray(function() return gameState.GlobalEmailsRead end),
                journalEntries = readArray(function() return gameState.GlobalJournalEntries end),
                compendiumEmail = readArray(function() return gameState.GlobalCompendiumEmail end),
                compendiumNarrative = readArray(function() return gameState.GlobalCompendiumNarrative end),
                compendiumExploration = readArray(function() return gameState.GlobalCompendiumExploration end),
            }
        end, respond)
    end

    ctx.handlers["worldunlocks.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("world-wide unlocks require host authority") end
            local state = currentGameState()
            if not state or not canEditRecipes(state) then error("world-wide unlocks require UE4SS TSet support") end
            local helper = replication.requireHelper()
            local prepared = {}
            for _, edit in ipairs(payload.recipes or {}) do
                if type(edit.id) ~= "string" or edit.id == "" or type(edit.unlocked) ~= "boolean" then error("invalid recipe edit") end
                local name = FName(edit.id, EFindName.FNAME_Find)
                if name:ToString() == "None" then error("unknown recipe name: " .. edit.id) end
                prepared[#prepared + 1] = { name = name, unlocked = edit.unlocked }
            end
            for _, edit in ipairs(prepared) do
                if edit.unlocked then
                    state.GlobalRecipesUnlocked:Add(edit.name)
                    state.GlobalRecipesResearched:Add(edit.name)
                else
                    state.GlobalRecipesUnlocked:Remove(edit.name)
                    state.GlobalRecipesResearched:Remove(edit.name)
                end
            end
            if #prepared > 0 then
                replication.mark(helper, state, "GlobalRecipesUnlocked")
                replication.mark(helper, state, "GlobalRecipesResearched")
            end
            return nil
        end, respond)
    end
end
