-- Live WORLD-LEVEL unlocks (round 77): the counterpart to the file editor's world-recipes browser
-- (WorldStoryTab's "WORLD RECIPES" section, `WorldSaveWriter`'s `GlobalUnlocks` struct) plus the
-- other world-wide (not per-player) progress lists the game tracks.
--
-- GROUNDING: extending LiveClassPropsProbe's dump to `Abiotic_Survival_GameState.uasset` (the
-- SAME package `areas/story.lua` already reads `CurrentQuest` from) found:
--   prop GlobalRecipesUnlocked : FSetProperty
--   prop GlobalRecipesResearched : FSetProperty
--   prop GlobalItemsPickedUp : FArrayProperty
--   prop GlobalEmailsRead : FArrayProperty
--   prop GlobalJournalEntries : FArrayProperty
--   prop GlobalCompendiumEmail : FArrayProperty
--   prop GlobalCompendiumNarrative : FArrayProperty
--   prop GlobalCompendiumExploration : FArrayProperty
-- These are the world-wide analogues of the per-player arrays `areas/codex.lua` and
-- `areas/recipes.lua` already read (matches `WorldSaveSession.GlobalRecipes`/`GlobalUnlocks` on
-- the file side, `Core/Serialization/World/WorldSaveWriter`).
--
-- READ: the six FArrayProperty fields (everything except the two FSetProperty recipe fields) use
-- the exact same indexed `for i = 1, #arr do arr[i]:ToString() end` technique already confirmed
-- working for EmailsRead/JournalEntries/FishCaughtArray in `areas/codex.lua` - real precedent for
-- the TECHNIQUE, not these specific names, hence still wrapped in pcall. `recipesUnlocked`/
-- `recipesResearched` read the two FSetProperty fields the SAME optimistic-pcall way
-- `Local_AllCompendiumEntries` used to (see codex.lua's round-76 history): if UE4SS's Lua binding
-- does not expose a TSet with #/[i] indexing, these two come back as an empty list (a visible
-- "0 unlocked" rather than a crash) instead of failing the whole command.
--
-- NO WRITE PATH FOR ANY OF THESE - grounded absence, not a guess. Two independent checks came back
-- empty:
--   1. Neither `Abiotic_Survival_GameState_C` nor `Abiotic_Survival_GameMode_C`'s exported function
--      list (LiveClassPropsProbe) contains ANY function referencing "Recipe"/"Global"/"Unlock" by
--      name that touches these fields - the GameMode's many `ApplyWorldSaveData|*`/`Update*ToWorldSave`
--      functions are the file load/save round trip for per-ACTOR world state (doors, NPCs, pets,
--      vehicles, ...), and none of them is paired to a "GlobalRecipes"/"GlobalCompendium" world-save
--      slice - the two local variables that DO reference `SaveData_GlobalUnlocks_Struct`
--      (`SetTimeOfDayOnWorldSave`) and `LocalGlobalUnlocks` (`UpdateActiveLeyakContainmentID`) are
--      both inside the disk save/load routines themselves, not a callable unlock RPC.
--   2. No installed reference mod anywhere touches a `TSet`/`TArray` property directly (no `:Add(`,
--      no `:Remove(`, no direct element assignment) - every real write precedent in this whole
--      project is either a UFunction call (`Request_UnlockNewFish`, `SetWorldFlag`, `K2_TeleportTo`)
--      or a scalar/struct field assignment (`DoorState = 1`, `VehicleDriveable = true`). Appending to
--      `GlobalRecipesUnlocked` (a replicated `TSet<FName>`) the way `flags.set` appends to
--      `WorldFlags` would require inventing a technique with no working precedent anywhere in this
--      project or any installed mod - guessing it risks corrupting replicated state other players
--      are actively reading. `worldunlocks.set` therefore always returns `ok:false`, exactly like
--      `story.set` does for the same reason (see `areas/story.lua`).
return function(ctx)
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
                -- FSetProperty: best-effort, same optimistic-pcall caveat as codex.lua's old
                -- Local_AllCompendiumEntries read (see header comment).
                recipesUnlocked = readArray(function() return gameState.GlobalRecipesUnlocked end),
                recipesResearched = readArray(function() return gameState.GlobalRecipesResearched end),
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

    ctx.handlers["worldunlocks.set"] = function(_, respond)
        ctx.runOnGameThread(function()
            error("world-wide unlocks cannot be changed from outside the game - no unlock function exists for them, " ..
                "and writing directly into the game's own unlock lists has no safe, confirmed technique yet")
        end, respond)
    end
end
