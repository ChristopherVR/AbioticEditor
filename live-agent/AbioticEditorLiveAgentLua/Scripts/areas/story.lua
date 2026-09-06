-- Live main-quest / story-progression indicator.
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
-- NO WRITE PATH: no `SetCurrentQuest`, no settable `OnRep_CurrentQuest` (it is a client
-- notification, not an input), and no native "set story progression" function anywhere in the
-- PDB. The story chapter is therefore READ-ONLY live - `story.set` always errors, and the C#
-- side's LiveStorySession.CanSetStoryChapter is false so the shared WorldStoryTab hides the SET
-- controls instead of offering something that cannot work. The QUEST FLAGS tab remains the real
-- live way to earn a chapter's trigger flags (flags.set), which is what actually advances quests
-- in the running game.
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

    ctx.handlers["story.set"] = function(_, respond)
        ctx.runOnGameThread(function()
            error("the story chapter cannot be set from outside the game - set its trigger flags on the quest flags tab instead")
        end, respond)
    end
end
