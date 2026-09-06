-- Round 77: story.get / story.set (the story chapter is settable live via its trigger flags -
-- see areas/story.lua's header comment and docs/PROGRESS.md).
return function(H)
    H.hostSession()

    -- story.get reads the replicated CurrentQuest row the fixture seeds (H.hostSession's
    -- gameState.CurrentQuest.RowName = "quest_RES_EndInterlude").
    local state = H.ok(H.dispatch("story.get"), "story.get")
    H.eq(state.currentQuestRow, "quest_RES_EndInterlude", "current quest row read")
    H.eq(state.isHost, true, "host authority reported")

    -- The flag subsystem + row-handle library fixture, same shape core.lua's flags block uses,
    -- with handles for both a flag story.set will SET and one it will CLEAR.
    local subsystem = H.world.add(H.object("WorldFlagSubsystem", {}, {
        GetWorldFlags = function(_, out) out[1] = H.outParam(H.fname("Office_PowerOn")) return true end,
        SetWorldFlag = function() end,
    }))
    H.world.static("/Script/AbioticFactor.Default__WorldFlagHandleFunctionLibrary", H.object("WorldFlagHandleFunctionLibrary", {}, {
        GetAllWorldFlagRowNames = function(_, out) out[1] = H.outParam(H.fname("Office_PowerOn")) end,
        GetAllWorldFlagRowHandles = function(_, out)
            out[1] = H.outParam({ RowName = H.fname("Office_InformationFound"), DataTablePath = "DT_WorldFlags" })
            out[2] = H.outParam({ RowName = H.fname("MapReveal_Security"), DataTablePath = "DT_WorldFlags" })
        end,
    }))
    -- H.world.add(subsystem) above adds the subsystem; the gameState added by hostSession() is
    -- still registered too (H.world.reset() has not run since), so FindFirstOf("WorldFlagSubsystem")
    -- and UEHelpers.GetGameStateBase() both still resolve.

    -- story.set: moves the chapter forward one flag (flagsToSet) and clears one flag that would
    -- be a step past it (flagsToClear) in the same request, then nudges CurrentQuest.
    H.ok(H.dispatch("story.set", {
        currentQuestRow = "Office2",
        flagsToSet = { "Office_InformationFound" },
        flagsToClear = { "MapReveal_Security" },
    }), "story.set")
    H.eq(H.calls(subsystem, "SetWorldFlag"), 2, "SetWorldFlag called once per flag (set + clear)")
    H.eq(H.field(H.gameState, "CurrentQuest").RowName:ToString(), "Office2", "CurrentQuest.RowName nudged")
    H.eq(H.calls(H.gameState, "OnRep_CurrentQuest"), 1, "OnRep_CurrentQuest pushed once")

    -- An unknown flag name in either list is rejected the same way flags.set rejects one.
    H.fails(H.dispatch("story.set", { currentQuestRow = "Office2", flagsToSet = { "Nope" } }),
        "unknown quest flag", "unknown flag in flagsToSet rejected")

    -- A joined client cannot move the story (host is checked before anything else is touched, so
    -- this still fails correctly even though H.clientSession() resets the world and drops the
    -- flag subsystem/library fixtures registered above).
    H.clientSession()
    H.fails(H.dispatch("story.set", { currentQuestRow = "Office2", flagsToSet = {} }),
        "only the host", "client cannot set the story chapter")
    -- story.get itself stays readable for a non-host client.
    H.eq(H.ok(H.dispatch("story.get")).isHost, false, "client story.get reports no authority")
end
