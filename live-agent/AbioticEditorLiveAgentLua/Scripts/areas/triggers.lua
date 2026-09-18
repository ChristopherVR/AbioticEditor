-- Live editing area: scripted world triggers (TriggerMap, round 102).
--
-- Confirmed against the coordinator's own CUE4Parse class+bytecode dump (tests/AbioticEditor.
-- Probes/LiveGapProbe.cs; see docs/PROGRESS.md's Round-102 entry for the exact citations). Every
-- property/function name below is copied verbatim from that dump, not guessed.
--
-- NOT keyed by actor path, unlike every other area in this mod - the save's TriggerMap key is the
-- trigger's own game-authored id string, not GetFullName(). Confirmed straight off the placed
-- actor: Abiotic_TriggerVolume_ParentBP_C carries its own `UniqueTriggerID` (FNameProperty,
-- Edit|BlueprintVisible|ExposeOnSpawn - set once per placed instance in the level, not derived
-- from anything else) and this module uses UniqueTriggerID:ToString() as `id`, matching the file
-- format's own map key exactly (e.g. "WF_NewGameStarted", "CA_PunchCard_TutorialPanelTrigger").
-- The task brief's original guess - a global TriggerMap living on the game mode/game state - was
-- checked and is wrong: Abiotic_WorldSave_C itself carries a `TriggerMap` (Str ->
-- SaveData_Trigger_Struct), confirmed in the dump, but each trigger actor keeps and persists its
-- OWN entry directly (see SaveTriggerData below) rather than the game mode reading/writing a
-- shared collection - there is no separate "the map" object to go through live.
--
-- CLASS DISCOVERY: Abiotic_TriggerVolume_ParentBP_C (super=Actor) is the one confirmed root - the
-- dump's own example subclass, Trigger_CompendiumExploration_C, declares
-- `super=Abiotic_TriggerVolume_ParentBP_C` directly. The probe's package set did not happen to
-- include the other ~16 Trigger_* blueprints the offline feature's own fixture data names, but
-- every one of them follows the identical "Trigger_<Something>" naming/placement pattern under the
-- same content folder as the one confirmed example, and FindAllOf is hierarchy-inclusive (the same
-- idiom buttons.lua/elevators.lua/pets.lua already rely on), so a single sweep of the one confirmed
-- root is expected to cover all of them without any hardcoded leaf list. ADDITIONAL_ROOT_CLASSES
-- exists (data, not logic) for the day a trigger-shaped class turns up that does NOT chain to this
-- root - empty today, no counter-example found in the dump.
--
-- FIELD MAPPING, read straight off Abiotic_TriggerVolume_ParentBP_C's own declared properties and
-- its ResetTriggerState()/SaveTriggerData() bytecode:
--   timesTriggered      <- trigger.TimesTriggered (IntProperty, Edit|BlueprintVisible|
--                           DisableEditOnInstance - a direct, unsuffixed instance property)
--   hasBeenTriggeredOnce <- trigger.HasBeenTriggeredOnce (BoolProperty, same flags)
--   triggerLimit         <- trigger.TriggerLimit (IntProperty, ExposeOnSpawn - a per-instance
--                           design value, informational only, never written by this module)
--
-- WRITES:
--   timesTriggered (arbitrary count) - a direct property write, `trigger.TimesTriggered = value`,
--     followed by calling the trigger's own `SaveTriggerData()` - a real, actor-level
--     FUNC_Public|FUNC_BlueprintCallable|FUNC_BlueprintEvent function confirmed in the dump, the
--     same function ResetTriggerState() (below) itself calls to persist - this is the game's own
--     "persist this trigger's state now" entry point.
--   reset (momentary action) - calls the trigger's own `ResetTriggerState()`, a real, actor-level
--     FUNC_Public|FUNC_BlueprintCallable|FUNC_BlueprintEvent function with NO parameters, confirmed
--     from its own bytecode to do exactly: TimesTriggered = 0, HasBeenTriggeredOnce = false,
--     re-enable the trigger volume's collision (SetCollisionEnabled), re-allow overlap on its
--     linked OtherTriggersToTrigger/OtherTriggersToAllowOverlaps arrays, THEN call SaveTriggerData()
--     itself - a strictly more complete reset than a bare timesTriggered=0 write (it also clears
--     HasBeenTriggeredOnce and re-arms the volume itself), so this module prefers it as the "reset
--     to 0" action; a `reset` request on the same row as a `timesTriggered` value ignores the
--     latter, since ResetTriggerState always writes 0 for both fields anyway.
return function(ctx)
    local TRIGGER_ROOT_CLASS = "Abiotic_TriggerVolume_ParentBP_C"
    -- Data, not logic - see the header comment. Empty today: no trigger-shaped class was found in
    -- the dump that does NOT chain to TRIGGER_ROOT_CLASS.
    local ADDITIONAL_ROOT_CLASSES = {}
    local ROOT_CLASSES = { TRIGGER_ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local function allTriggerActors()
        local result = {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do table.insert(result, actor) end
        end
        return result
    end

    -- Reads UniqueTriggerID off a live actor, or nil when this instance does not expose one at all
    -- (an unfamiliar trigger-shaped class reached only through ADDITIONAL_ROOT_CLASSES).
    local function triggerId(actor)
        local ok, uid = pcall(function() return actor.UniqueTriggerID:ToString() end)
        if ok and uid and uid ~= "" and uid ~= "None" then return uid end
        return nil
    end

    -- Matched by UniqueTriggerID, NOT GetFullName() - see the header comment for why. Several
    -- placed volumes CAN legitimately share one id (round 122: a live report crashed the whole
    -- editor page - "More than one sibling of element 'button' has the same key value" - once two
    -- volumes sharing 'CA_PunchCard_TutorialPanelTrigger' were both loaded at once), so this
    -- returns every match, not just the first. triggerRows() below folds them into one row (the
    -- save's own TriggerMap only ever has one entry per id, so that is the correct shape to show
    -- the player); triggers.set applies an edit to every matched volume so they stay in lockstep
    -- with that merged row.
    local function findTriggersById(id)
        local result = {}
        for _, actor in ipairs(allTriggerActors()) do
            if actor:IsValid() and triggerId(actor) == id then table.insert(result, actor) end
        end
        return result
    end

    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end
    local function intOrNil(ok, value)
        if ok and type(value) == "number" then return value end
        return nil
    end

    local function triggerRow(actor)
        local id = triggerId(actor)
        if not id then return nil end
        local fullName = ctx.fullName(actor)
        local x, y, z = ctx.actorLocation(actor)
        local okTimes, times = pcall(function() return actor.TimesTriggered end)
        local okOnce, once = pcall(function() return actor.HasBeenTriggeredOnce end)
        local okLimit, limit = pcall(function() return actor.TriggerLimit end)
        return {
            id = id,
            label = fullName and ctx.classLabel(fullName) or "?",
            timesTriggered = intOrNil(okTimes, times),
            hasBeenTriggeredOnce = boolOrNil(okOnce, once),
            triggerLimit = intOrNil(okLimit, limit),
            x = x, y = y, z = z,
        }
    end

    -- One row per id, not one row per placed volume (round 122). The save's own TriggerMap only
    -- ever has one entry per UniqueTriggerID, so a save round-trip already treats every volume
    -- sharing an id as the same logical trigger; showing them as separate rows here would both
    -- misrepresent that and produce duplicate row ids, which crashes the editor's list (a
    -- duplicate render key is an uncatchable renderer error, not a recoverable one - see
    -- RenderKeys.cs on the editor side for the matching defense there). When merging:
    --   timesTriggered      <- the HIGHEST count seen across the volumes sharing this id, since a
    --                          lower reading is never a more complete picture of "how many times
    --                          has this fired" than one already observed.
    --   hasBeenTriggeredOnce <- true if ANY volume sharing the id reports true.
    --   label/x/y/z/triggerLimit <- taken from whichever volume was found first; these are
    --                          informational display fields only, never written back.
    local function triggerRows()
        local byId = {}
        local order = {}
        for _, actor in ipairs(allTriggerActors()) do
            if actor:IsValid() then
                local row = triggerRow(actor)
                if row then
                    local existing = byId[row.id]
                    if not existing then
                        byId[row.id] = row
                        table.insert(order, row.id)
                    else
                        if row.timesTriggered and (not existing.timesTriggered or row.timesTriggered > existing.timesTriggered) then
                            existing.timesTriggered = row.timesTriggered
                        end
                        if row.hasBeenTriggeredOnce then
                            existing.hasBeenTriggeredOnce = true
                        end
                    end
                end
            end
        end
        local result = { __forceArray = true }
        for _, id in ipairs(order) do table.insert(result, byId[id]) end
        return result
    end

    ctx.handlers["triggers.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { triggers = triggerRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Matches doors.set/buttons.set/elevators.set: every resolvable row applies first; only once
    -- every row has run does an unresolved id or a failed write turn the whole reply into an
    -- error, naming the first one. Unlike those areas, one row's id here can back MORE THAN ONE
    -- placed volume (see triggerRows() above), so an edit is applied to every matched volume, not
    -- just the first, to keep them all in lockstep with the merged row the player sees.
    ctx.handlers["triggers.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change world triggers") end
            local rows = payload.triggers or {}
            local missingId, failedId, failedReason = nil, nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local matches = row.id and findTriggersById(row.id) or {}
                if #matches > 0 then
                    for _, trigger in ipairs(matches) do
                        if row.reset then
                            local ok = pcall(function() trigger:ResetTriggerState() end)
                            if not ok then
                                failedId = failedId or row.id
                                failedReason = failedReason or "this trigger type has no known live reset control"
                            end
                        elseif row.timesTriggered ~= nil then
                            local okWrite = pcall(function() trigger.TimesTriggered = row.timesTriggered end)
                            if okWrite then
                                pcall(function() trigger:SaveTriggerData() end)
                            else
                                failedId = failedId or row.id
                                failedReason = failedReason or "this trigger type has no known live fire-count control"
                            end
                        end
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("trigger not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if failedId then error(tostring(failedReason) .. ": " .. tostring(failedId)) end
            return nil
        end, respond)
    end
end
