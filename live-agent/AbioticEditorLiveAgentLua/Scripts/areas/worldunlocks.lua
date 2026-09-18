-- GlobalRecipesUnlocked/GlobalRecipesResearched are TSet<FName> on the game state.
-- Use UE4SS TSet.ForEach/Add/Remove with host gating and runtime capability checks.
-- GlobalItemsPickedUp, emails, journals and compendium lists are TArray<FName>.
-- Fields are grounded in the exported Abiotic_Survival_GameState layout.
--
-- Round 106: the six FArrayProperty lists (GlobalItemsPickedUp/GlobalEmailsRead/
-- GlobalJournalEntries/GlobalCompendiumEmail/GlobalCompendiumNarrative/
-- GlobalCompendiumExploration) are now writable too, not just readable. Unlike the two recipe
-- TSets they sit beside, none of them carries "Net"/"RepNotify" in its PropertyFlags in the pak
-- dump (Abiotic_Survival_GameState.json - compare to GlobalRecipesUnlocked/Researched, which are
-- ALSO plain "Edit | BlueprintVisible | DisableEditOnInstance" with no Net flag there either, and
-- no OnRep_Global* function exists anywhere in the class for any of the eight Global* fields).
-- Writing them uses the exact same grounded technique codex.lua's "clear" path already uses for
-- the per-player arrays: replace the whole array (name_arrays.prepare, the same snapshot-then-
-- reassign helper recipes.lua's relock path uses) and mark it dirty for replication - best-effort
-- since these are not confirmed Net properties, but harmless (MarkPropertyDirty on a
-- non-replicated property is a no-op, not an error) and correct for what actually matters here:
-- the HOST's own authoritative GameState is what gets saved to WorldSave_MetaData.sav, so a host
-- write is real and durable regardless of whether other connected clients see it live. No OnRep
-- call is made (none exists to call), unlike the per-player compendium/email/journal arrays.
return function(ctx)
    local replication = require("replication")
    local names = require("name_arrays")
    -- The six FArrayProperty world-wide lists, wire field name -> game-state property name.
    local GLOBAL_LIST_FIELDS = {
        { wire = "itemsPickedUp", prop = "GlobalItemsPickedUp" },
        { wire = "emailsRead", prop = "GlobalEmailsRead" },
        { wire = "journalEntries", prop = "GlobalJournalEntries" },
        { wire = "compendiumEmail", prop = "GlobalCompendiumEmail" },
        { wire = "compendiumNarrative", prop = "GlobalCompendiumNarrative" },
        { wire = "compendiumExploration", prop = "GlobalCompendiumExploration" },
    }
    -- No TSet-style runtime capability check applies here (plain FArrayProperty assignment is the
    -- same base UE4SS property bridge recipes.lua's relock and codex.lua's clear already rely on),
    -- so this only needs host authority and replication-notification support.
    local function canEditGlobalLists() return ctx.isHost() and replication.available() end
    local function globalListEditsUnavailableReason()
        if not ctx.isHost() then return "not-host" end
        if not replication.available() then return "no-replication" end
        return nil
    end
    local function canEditRecipes(state)
        local ok, supported = pcall(function()
            return state.GlobalRecipesUnlocked.Add ~= nil and state.GlobalRecipesUnlocked.Remove ~= nil
                and state.GlobalRecipesUnlocked.ForEach ~= nil
                and state.GlobalRecipesResearched.ForEach ~= nil
                and state.GlobalRecipesResearched.Add ~= nil and state.GlobalRecipesResearched.Remove ~= nil
        end)
        return ok and supported
    end
    -- Short machine-readable reason the app can turn into a localized, player-facing
    -- explanation (WorldStoryTab.razor) instead of just disabling the control with no context.
    -- "runtime-unsupported" is the one this exists for: an older UE4SS build without
    -- TSet.Add/Remove/ForEach on the game state's recipe sets, fixed by updating UE4SS, not by
    -- anything the player can do in-game.
    local function globalRecipeEditsUnavailableReason(state)
        if not ctx.isHost() then return "not-host" end
        if not replication.available() then return "no-replication" end
        if not canEditRecipes(state) then return "runtime-unsupported" end
        return nil
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
                globalRecipeEditsUnavailableReason = globalRecipeEditsUnavailableReason(gameState),
                -- Round 106: the six FArrayProperty lists below - see the file header comment.
                canEditGlobalLists = canEditGlobalLists(),
                globalListEditsUnavailableReason = globalListEditsUnavailableReason(),
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
            if not state then error("the world is not loaded (are you in a world?)") end

            local recipeEdits = payload.recipes or {}
            if #recipeEdits > 0 then
                if not canEditRecipes(state) then error("world-wide unlocks require UE4SS TSet support") end
                local helper = replication.requireHelper()
                local prepared = {}
                for _, edit in ipairs(recipeEdits) do
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
                replication.mark(helper, state, "GlobalRecipesUnlocked")
                replication.mark(helper, state, "GlobalRecipesResearched")
            end

            -- Round 106: the six world-wide FArrayProperty lists - add/remove behind
            -- canEditGlobalLists, independent of the recipe TSets' extra runtime-support check.
            local hasListEdits = false
            for _, field in ipairs(GLOBAL_LIST_FIELDS) do
                if payload[field.wire] and #payload[field.wire] > 0 then hasListEdits = true end
            end
            if hasListEdits then
                if not replication.available() then error("world-wide list edits require replication notification support") end
                local helper = replication.requireHelper()
                for _, field in ipairs(GLOBAL_LIST_FIELDS) do
                    local edits = payload[field.wire]
                    if edits and #edits > 0 then
                        local additions, removals = {}, {}
                        for _, edit in ipairs(edits) do
                            if type(edit.id) ~= "string" or edit.id == "" or type(edit.present) ~= "boolean" then
                                error("invalid " .. field.wire .. " edit")
                            end
                            if edit.present then additions[#additions + 1] = edit.id else removals[#removals + 1] = edit.id end
                        end
                        state[field.prop] = names.prepare(state[field.prop], additions, removals)
                        replication.mark(helper, state, field.prop)
                    end
                end
            end
            return nil
        end, respond)
    end
end
