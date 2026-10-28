-- Community Update 5 multi-quest: quests.get / quests.track (see areas/quests.lua).
return function(H)
    H.hostSession()
    local controller = H.playerController
    controller.TrackedQuestLine = { RowName = H.fname("QuestLine_Primary") }
    local rpcCalls = 0
    rawget(controller, "__methods").Request_ChangeTrackedQuestLine = function(self, handle)
        rpcCalls = rpcCalls + 1
        self.TrackedQuestLine = { RowName = handle.RowName }
    end

    -- Without the world flag subsystem the active-objective list is unknown, not empty.
    local state = H.ok(H.dispatch("quests.get"), "quests.get")
    H.eq(state.trackedLine, "QuestLine_Primary", "tracked line read off the controller")
    H.eq(state.currentQuestLine, "QuestLine_Primary", "world quest line read off the game state")
    H.eq(state.activeQuestsKnown, false, "active objectives unknown without the subsystem")

    H.world.add(H.object("WorldFlagSubsystem", {}, {
        FindAllActiveQuests = function(_, filter, out, ignoreSub, onlyLatest)
            H.check(filter ~= nil and ignoreSub == false and onlyLatest == false, "native signature: filter, out array, two flags")
            out[1] = H.outParam({ RowName = H.fname("quest_a") })
            out[2] = H.outParam({ RowName = H.fname("quest_b") })
            return true
        end,
    }))
    state = H.ok(H.dispatch("quests.get"), "quests.get with the subsystem")
    H.eq(state.activeQuestsKnown, true, "active objectives known")
    H.eq(#state.activeQuests, 2, "both simultaneous objectives reported")
    H.eq(state.activeQuests[2], "quest_b", "objective names read")

    local tracked = H.ok(H.dispatch("quests.track", { line = "QuestLine_Kylie" }), "quests.track")
    H.eq(rpcCalls, 1, "the game's own tracking RPC is called once")
    H.eq(tracked.trackedLine, "QuestLine_Kylie", "the read-back tracked line is returned")

    tracked = H.ok(H.dispatch("quests.track", { line = "" }), "clearing tracking")
    H.eq(rpcCalls, 2, "clearing goes through the same RPC")
end
