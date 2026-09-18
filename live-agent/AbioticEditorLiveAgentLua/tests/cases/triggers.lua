-- Scripted world triggers (areas/triggers.lua, round 102). Field/function names off the real
-- class+bytecode probe (tests/AbioticEditor.Probes/LiveGapProbe.cs): UniqueTriggerID,
-- TimesTriggered, HasBeenTriggeredOnce, TriggerLimit (all direct, unsuffixed instance properties
-- on Abiotic_TriggerVolume_ParentBP_C), and ResetTriggerState()/SaveTriggerData() (both real,
-- actor-level, no-argument BlueprintCallable functions). Rows are identified by UniqueTriggerID,
-- NOT GetFullName() - unlike every other live area in this mod, since that is what the save
-- file's own TriggerMap key actually is.
return function(H)
    H.hostSession()

    local function trigger(class, uniqueId, timesTriggered, hasBeenTriggeredOnce, triggerLimit, extraMethods)
        local resetCalls = 0
        local methods = {
            ResetTriggerState = function(self)
                local fields = rawget(self, "__fields")
                fields.TimesTriggered = 0
                fields.HasBeenTriggeredOnce = false
                resetCalls = resetCalls + 1
            end,
            SaveTriggerData = function() end,
            K2_GetActorLocation = function() return H.vector(4, 4, 4) end,
        }
        if extraMethods then for k, v in pairs(extraMethods) do methods[k] = v end end
        return H.world.add(H.object(class, {
            __bases = { "Abiotic_TriggerVolume_ParentBP_C" },
            UniqueTriggerID = H.fname(uniqueId),
            TimesTriggered = timesTriggered,
            HasBeenTriggeredOnce = hasBeenTriggeredOnce,
            TriggerLimit = triggerLimit,
        }, methods))
    end

    local newGame = trigger("Trigger_CompendiumExploration_C", "WF_NewGameStarted", 1, true, 1)
    local punchCard = trigger("CA_PunchCard_TutorialPanelTrigger_C", "CA_PunchCard_TutorialPanelTrigger", 0, false, -1)

    local list = H.ok(H.dispatch("triggers.list"), "triggers.list")
    H.eq(#list.triggers, 2, "both trigger volumes listed")

    local function rowFor(rows, id)
        for _, row in ipairs(rows) do if row.id == id then return row end end
        return nil
    end
    local newGameRow = rowFor(list.triggers, "WF_NewGameStarted")
    local punchCardRow = rowFor(list.triggers, "CA_PunchCard_TutorialPanelTrigger")
    H.check(newGameRow ~= nil and punchCardRow ~= nil, "both rows found by their UniqueTriggerID, not an actor path")

    H.eq(newGameRow.timesTriggered, 1, "timesTriggered read directly off the actor")
    H.eq(newGameRow.hasBeenTriggeredOnce, true, "hasBeenTriggeredOnce read directly off the actor")
    H.eq(newGameRow.triggerLimit, 1, "triggerLimit read directly off the actor")
    H.eq(newGameRow.label, "Trigger_CompendiumExploration_C", "label is the actor's real class name")
    H.eq(punchCardRow.timesTriggered, 0, "never-fired trigger reads 0")

    -- triggers.set: an arbitrary timesTriggered write persists via SaveTriggerData().
    H.ok(H.dispatch("triggers.set", { triggers = { { id = "CA_PunchCard_TutorialPanelTrigger", timesTriggered = 5 } } }),
        "set the punch-card trigger's fire count directly")
    H.eq(H.field(punchCard, "TimesTriggered"), 5, "TimesTriggered written directly")
    H.eq(H.calls(punchCard, "SaveTriggerData"), 1, "SaveTriggerData called to persist the direct write")
    H.eq(H.calls(punchCard, "ResetTriggerState"), 0, "ResetTriggerState not called for a plain count write")

    -- triggers.set: reset uses the game's own ResetTriggerState(), which also clears
    -- hasBeenTriggeredOnce - a strictly bigger reset than a bare timesTriggered=0 write.
    H.ok(H.dispatch("triggers.set", { triggers = { { id = "WF_NewGameStarted", reset = true } } }),
        "reset the new-game trigger")
    H.eq(H.calls(newGame, "ResetTriggerState"), 1, "ResetTriggerState called once")
    H.eq(H.field(newGame, "TimesTriggered"), 0, "timesTriggered cleared by the reset")
    H.eq(H.field(newGame, "HasBeenTriggeredOnce"), false, "hasBeenTriggeredOnce also cleared by the reset")

    -- reset alongside a timesTriggered value on the same row: reset wins, the value is ignored -
    -- matching the module's own documented behavior (ResetTriggerState always writes 0 for both
    -- fields regardless of what else was requested).
    H.ok(H.dispatch("triggers.set", {
        triggers = { { id = "CA_PunchCard_TutorialPanelTrigger", timesTriggered = 99, reset = true } },
    }), "reset takes priority over a timesTriggered value on the same row")
    H.eq(H.field(punchCard, "TimesTriggered"), 0, "reset wins over the simultaneous timesTriggered=99 request")

    -- A brand-new trigger-shaped class this module has never heard of by name, reached only
    -- through the hierarchy sweep on the one confirmed root - still listed and settable.
    local futureType = trigger("Trigger_FutureDLC_C", "DLC_FutureTrigger", 0, false, -1)
    local futureList = H.ok(H.dispatch("triggers.list"), "triggers.list including an unknown future trigger class")
    local futureRow = rowFor(futureList.triggers, "DLC_FutureTrigger")
    H.check(futureRow ~= nil, "unknown trigger subclass found through the hierarchy sweep")
    H.eq(futureRow.label, "Trigger_FutureDLC_C", "the unknown subclass's real class name is reported as its label")
    H.ok(H.dispatch("triggers.set", { triggers = { { id = "DLC_FutureTrigger", reset = true } } }),
        "the unknown subclass is still settable")
    H.eq(H.calls(futureType, "ResetTriggerState"), 1, "reset works on the unknown subclass exactly like a known one")

    -- A trigger-shaped fake that lacks UniqueTriggerID entirely does not list at all (there is no
    -- key to report it under - honest omission, not a crash).
    H.world.add(H.object("Trigger_Unresolvable_C", { __bases = { "Abiotic_TriggerVolume_ParentBP_C" } }, {
        K2_GetActorLocation = function() return H.vector(0, 0, 0) end,
    }))
    local unresolvableList = H.ok(H.dispatch("triggers.list"), "triggers.list including a trigger with no UniqueTriggerID")
    H.eq(#unresolvableList.triggers, 3, "the id-less trigger is omitted, not listed with a blank id (3: newGame, punchCard, futureType)")

    -- Missing trigger id: player-safe failure, not a Lua error - any resolvable row in the same
    -- call still applies first.
    local missingReply = H.dispatch("triggers.set", { triggers = {
        { id = "WF_NewGameStarted", timesTriggered = 2 },
        { id = "no-such-trigger", reset = true },
    } })
    H.fails(missingReply, "not found", "unknown trigger id fails cleanly")
    H.eq(H.field(newGame, "TimesTriggered"), 2, "the resolvable row in the same batch still applied")

    -- Non-host refusal.
    H.clientSession()
    local clientTrigger = trigger("Trigger_CompendiumExploration_C", "Client_Trigger", 0, false, -1)
    local clientList = H.ok(H.dispatch("triggers.list"), "triggers.list as client")
    H.fails(H.dispatch("triggers.set", { triggers = { { id = clientList.triggers[1].id, reset = true } } }),
        "only the host", "client cannot edit world triggers")
end
