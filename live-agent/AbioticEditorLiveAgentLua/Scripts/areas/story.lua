-- Live main-quest / story-progression indicator AND setter.
--
-- GROUNDING (round 76, see docs/PROGRESS.md and docs/reference/live-editing-protocol.md):
-- `tests/AbioticEditor.Probes/LiveClassPropsProbe.cs` dumps the blueprint
-- `AbioticFactor/Content/Blueprints/Meta/Abiotic_Survival_GameState.uasset` and shows
-- `Abiotic_Survival_GameState_C` (super=AbioticGameState) carries:
--   prop CurrentQuest : FStructProperty
--   func OnRep_CurrentQuest
-- i.e. a replicated row-handle-shaped struct every client already has, refreshed by the game the
-- same way `DayNightManager_C.CurrentWeatherEvent` is. The shipped PDB additionally confirms a
-- NATIVE function that computes it:
--   bool UWorldFlagSubsystem::FindCurrentQuest(FQuestRowHandle& Out)
-- and a whole `UQuestHandleFunctionLibrary` (BreakQuestRowHandle, MakeQuestRowHandle,
-- GetQuestRow(FQuestRowHandle, FQuestData&, ERowValid&), GetAllQuestRowNames/Handles,
-- DoesQuestRowExist) shaped exactly like the weather-event and world-flag row-handle libraries
-- this mod already drives. NEITHER of those two native calls is used here: both take a
-- BYREF STRUCT out-param (not the `TArray<FName>&` shape `outNames()` already has a working
-- precedent for), and no installed mod calls either one, so the calling convention would be a
-- guess. Reading the replicated `CurrentQuest` property directly needs no function call at all -
-- it is the same "read a struct property's .RowName field" pattern `world.get` already uses for
-- `manager.CurrentWeatherEvent:ToString()` - so that is the only read this module performs.
--
-- ROUND 77 (product owner's verdict: "the 'read-only' for story progression makes no sense" -
-- they are right): the story chapter IS a function of world flags. The editor's own
-- `Core/Catalogs/World/StoryProgressionCatalog.cs` maps every chapter to its `TriggerFlag`,
-- `Core/Services/World/FlagGate.cs` knows the linear-order prerequisite/dependent closure, and
-- `flags.set` above (`UWorldFlagSubsystem::SetWorldFlag`, verified against the real game in
-- round 75) IS the game's own mechanism for moving the story: every `Trigger_WorldFlag_C` in the
-- game advances the quest exactly this way, `UWorldFlagSubsystem::FindCurrentQuest` recomputes
-- `CurrentQuest` from the flag set, and `OnRep_CurrentQuest` pushes the change to clients. So
-- `story.set` no longer refuses: the .NET side (`LiveStorySession`) already has the catalogs, so
-- it computes the flag list once and sends it here as `flagsToSet`/`flagsToClear` - this module
-- does not reimplement the chapter/prerequisite math, it only applies flags the same way
-- `flags.set` does (via `ctx.applyWorldFlagRows`, factored out of `flags.set` in main.lua for
-- exactly this reuse) and then nudges the replicated `CurrentQuest` row directly as a
-- belt-and-braces extra - the flags are the real, game-native write; the direct struct-member
-- write has no mod precedent anywhere, so it is best-effort or in a pcall and never treated as
-- the source of truth (the game will recompute `CurrentQuest` from the flags on its own the next
-- time anything calls `FindCurrentQuest`/re-evaluates it, same as any other flag-driven state).
return function(ctx)
    local function currentGameState()
        local ok, gameState = pcall(function() return ctx.UEHelpers.GetGameStateBase() end)
        if ok and gameState and gameState:IsValid() then return gameState end
        return nil
    end

    ctx.handlers["story.get"] = function(_, respond)
        ctx.runOnGameThread(function()
            local gameState = currentGameState()
            if not gameState then error("the world is not loaded (are you in a world?)") end
            local row = "None"
            local okQuest, rowName = pcall(function() return gameState.CurrentQuest.RowName:ToString() end)
            if okQuest and rowName and rowName ~= "" then row = rowName end
            return {
                isHost = ctx.isHost(),
                currentQuestRow = row,
            }
        end, respond)
    end

    -- payload: { currentQuestRow = "<target chapter row>", flagsToSet = {...}, flagsToClear = {...} }
    -- (both flag lists are computed .NET-side by LiveStorySession from StoryProgressionCatalog +
    -- FlagGate - see docs/reference/live-editing-protocol.md "story.get / story.set").
    ctx.handlers["story.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change the story chapter") end
            local gameState = currentGameState()
            if not gameState then error("the world is not loaded (are you in a world?)") end

            local rows = {}
            for _, name in ipairs(payload.flagsToSet or {}) do
                table.insert(rows, { name = name, isSet = true })
            end
            for _, name in ipairs(payload.flagsToClear or {}) do
                table.insert(rows, { name = name, isSet = false })
            end
            if #rows > 0 then ctx.applyWorldFlagRows(rows) end

            -- Belt-and-braces nudge only: no installed mod writes a struct member directly, so
            -- this is wrapped in pcall and is not what makes the story move - the flags above are
            -- the game's own mechanism, and FindCurrentQuest will recompute this from them anyway.
            if payload.currentQuestRow and payload.currentQuestRow ~= "" then
                pcall(function()
                    gameState.CurrentQuest.RowName = FName(payload.currentQuestRow, EFindName.FNAME_Find)
                end)
                pcall(function() gameState:OnRep_CurrentQuest() end)
            end

            return nil
        end, respond)
    end
end
