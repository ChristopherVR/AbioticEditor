-- Live multi-quest support (Community Update 5).
--
-- GROUNDING (2026-10-28, tests/AbioticEditor.Probes LiveClassPropsProbe dump of the 1.5 paks):
--   Abiotic_PlayerController_C  prop TrackedQuestLine : FStructProperty (a QuestLineRowHandle)
--                               func OnRep_TrackedQuestLine, Request_ChangeTrackedQuestLine(QuestLine)
--   Abiotic_Survival_GameState_C prop CurrentQuestline : FStructProperty, func OnRep_CurrentQuestline
--   Abiotic_PlayerState_C       prop ServerQuestLine : FStructProperty
--   The BP graph of Abiotic_PlayerState.SetQuestLineFromLoad calls two native functions on the
--   world flag subsystem: FindAllActiveQuests(TArray<FQuestRowHandle>&) -> bool and
--   FindQuestLineForQuest(FQuestRowHandle, FQuestLineRowHandle&) -> bool.
--
-- FindAllActiveQuests is read with the exact out-array pattern applyWorldFlagRows (main.lua)
-- already uses for GetAllWorldFlagRowHandles, so the game itself says which objectives are active
-- (several at once in 1.5). It is pcall-guarded: when it cannot be called the desktop app falls
-- back to deriving objective state from the world flags.
--
-- Tracking is PER PLAYER, so it only ever touches the local player's controller. The struct
-- argument is passed as a table holding RowName, the same shape SetWorldFlag takes. The result is
-- read back from TrackedQuestLine and returned so the app can tell "applied" from "refused" - the
-- calling convention has not been confirmed in a running game yet, so nothing is assumed.
return function(ctx)
    local function localController()
        local ok, controller = pcall(function() return ctx.UEHelpers.GetPlayerController() end)
        if ok and controller and controller:IsValid() then return controller end
        return nil
    end

    local function rowOf(getHandle)
        local ok, name = pcall(function() return getHandle().RowName:ToString() end)
        if ok and name and name ~= "" and name ~= "None" then return name end
        return nil
    end

    local function activeQuests()
        local subsystem = ctx.worldFlagSubsystem()
        if not subsystem then return nil end
        local out = {}
        local ok = pcall(function() return subsystem:FindAllActiveQuests(out) end)
        if not ok then return nil end
        local result = { __forceArray = true }
        for i = 1, #out do
            local okHandle, handle = pcall(function() return out[i]:get() end)
            if okHandle and handle then
                local okName, name = pcall(function() return handle.RowName:ToString() end)
                if okName and name and name ~= "" and name ~= "None" then table.insert(result, name) end
            end
        end
        return result
    end

    -- Tracking lives on the LOCAL player's controller, so a request aimed at another player
    -- (payload.playerId) is read-only: it reports the world, never that player's tracked line.
    local function targetsLocalPlayer(payload)
        if not payload or not payload.playerId or payload.playerId == "" then return true end
        local okTarget, target = pcall(function() return ctx.resolvePlayer(payload) end)
        local okMine, mine = pcall(function() return ctx.getMyPlayer() end)
        if not okTarget or not okMine or not target or not mine then return false end
        return ctx.fullName(target) == ctx.fullName(mine)
    end

    local function state(controller, isLocal)
        local result = {
            isHost = ctx.isHost(),
            trackedLine = isLocal and rowOf(function() return controller.TrackedQuestLine end) or "None",
            canTrack = isLocal,
        }
        local gameOk, gameState = pcall(function() return ctx.UEHelpers.GetGameStateBase() end)
        if gameOk and gameState and gameState:IsValid() then
            result.currentQuestLine = rowOf(function() return gameState.CurrentQuestline end) or "None"
        end
        local active = activeQuests()
        if active then
            result.activeQuestsKnown = true
            result.activeQuests = active
        else
            result.activeQuestsKnown = false
            result.activeQuests = { __forceArray = true }
        end
        return result
    end

    ctx.handlers["quests.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local controller = localController()
            if not controller then error("the world is not loaded (are you in a world?)") end
            return state(controller, targetsLocalPlayer(payload))
        end, respond)
    end

    -- payload: { line = "<QuestLine row>" } ("" or "None" clears the tracked line)
    ctx.handlers["quests.track"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local controller = localController()
            if not controller then error("the world is not loaded (are you in a world?)") end
            if not targetsLocalPlayer(payload) then error("quest tracking can only be changed for your own character") end
            local line = payload and payload.line or ""
            if line == "" then line = "None" end
            local name = FName(line, EFindName.FNAME_Find)
            if line ~= "None" and (not name or tostring(name:ToString()) == "None") then
                error("the game does not know a quest line called " .. line)
            end
            local okCall, err = pcall(function() controller:Request_ChangeTrackedQuestLine({ RowName = name }) end)
            if not okCall then error("the game refused the quest line change: " .. tostring(err)) end
            return state(controller, true)
        end, respond)
    end
end
