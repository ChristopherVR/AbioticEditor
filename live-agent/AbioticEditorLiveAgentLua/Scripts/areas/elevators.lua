-- Live editing area: fixed elevator platforms (ElevatorMap, round 79; corrected round 96 after
-- the coordinator ran a real CUE4Parse class+bytecode probe against the installed game -
-- tests/AbioticEditor.Probes/ElevatorButtonProbe.cs; discovery made subclass-generic round 97).
--
-- Elevator_ParentBP_C (super=Actor) has NO TopOpen property and no OnRep_TopOpen - the round-79
-- guess was wrong. The real live state is a replicated byte enum, ElevatorCurrentMode
-- (E_ElevatorMovementTypes: 0 StoppedAtBottom, 1 StoppedAtTop, 2 MovingToTop,
-- 3 MovingToBottom - confirmed from the enum asset's own DisplayNameMap/Names tables), with
-- OnRep_ElevatorCurrentMode as its replication notify.
--
-- Confirmed from the blueprint's own bytecode (not guessed):
-- - OnLoadedFromSave(Top: bool) sets ElevatorCurrentMode = Top ? 1 (StoppedAtTop) : 0
--   (StoppedAtBottom) via a plain Select node - the save's own load-time mapping, so the saved
--   TopOpen_ leaf means exactly "ElevatorCurrentMode == StoppedAtTop". The reverse, save-time
--   direction runs through SaveElevatorStateToWorldSave -> the game mode's own
--   UpdateActorToWorldSave, which lives outside this class and was not itself traced; this
--   module relies on the load-time mapping by symmetry, not on an independently confirmed save
--   path.
-- - TryPressTopButton(Activated: bool) / TryPressBottomButton(Activated: bool) only act when
--   Activated is true (false pops out immediately, a no-op) and NEITHER checks IsServer() nor
--   IsPowered() internally - the switch-on-ElevatorCurrentMode logic runs unconditionally once
--   entered. TryPressTopButton: mode 0 (StoppedAtBottom) -> 2 (start moving up), mode 3
--   (MovingToBottom) -> 2 (redirect up), mode 2 (MovingToTop) -> unchanged (prints "Elevator is
--   already on its way up"), mode 1 (StoppedAtTop) -> 3 (a real toggle-AWAY quirk: pressing the
--   top button while already parked at the top sends it back down, not a bug in this module).
--   TryPressBottomButton is the exact mirror (0 -> 2 toggle-away, 1 -> 3, 2 -> 3 redirect, 3
--   unchanged/prints "already on its way down"). Both set ElevatorCurrentMode then call
--   OnRep_ElevatorCurrentMode(), the same OnRep pattern doors/portals already use elsewhere.
-- - IsElevatorMoving() = (ElevatorCurrentMode == 2) OR (ElevatorCurrentMode == 3); IsPowered()
--   returns a plain PowerOn bool. Both confirmed present and used here as the safety gates the
--   press functions themselves do not apply.
--
-- Because pressing the button for the side the elevator already occupies is a toggle-AWAY quirk,
-- elevators.set never presses a button that would immediately move the platform off the state
-- the caller asked for - see setTopOpen below. Because moving the platform is asynchronous (real
-- travel time), a press that starts or continues the correct direction of travel is accepted as
-- success; only a press with no confirmed effect is reported as an error.
--
-- Discovery is subclass-generic, not a hardcoded class list (the game already ships at least one
-- confirmed subclass, Elevator_Office_BP_C, super=Elevator_ParentBP_C, plus Elevator_ORD_BP and
-- Elevator_VWinter in the same asset folder with the same naming pattern - not independently
-- confirmed as subclasses but never special-cased below either way, and a DLC could add more).
-- FindAllOf on a parent class already returns every subclass instance too - the same idiom
-- main.lua's npcs.list relies on for NPC_Base_ParentBP_C (wildlife/monsters/robots, every
-- concrete creature class at once) - so ctx.findAll("Elevator_ParentBP_C") alone is the primary
-- and normally only sweep needed; nothing here names Office/ORD/VWinter. (BucketElevator_Spline_BP
-- is deliberately never swept: a spline-based "bucket" actor reads as a different actor family,
-- more like a conveyor, with no evidence it is part of the ElevatorMap/TopOpen system at all.)
-- A short, explicitly-labelled fallback class list only runs if the parent sweep itself comes
-- back empty (a UE4SS build where subclass matching does not work, or the parent class is
-- renamed) - it is data fed through the exact same capability-detected path below, not special
-- logic. Every instance, known or not, is read through pcall feature-detection
-- (does it have ElevatorCurrentMode? does pressing a button change it?) so an elevator type this
-- module has never heard of still lists - with its real class name as its label, matching how
-- every other fixed-actor feature in this mod already has no friendly name - and reports
-- "not controllable" instead of erroring or being silently dropped.
return function(ctx)
    local PARENT_CLASS = "Elevator_ParentBP_C"
    -- Fallback only (see header comment) - never consulted while the parent sweep finds anything.
    local FALLBACK_CLASSES = { "Elevator_Office_BP_C", "Elevator_ORD_BP_C", "Elevator_VWinter_C" }
    local STOPPED_AT_BOTTOM, STOPPED_AT_TOP, MOVING_TO_TOP, MOVING_TO_BOTTOM = 0, 1, 2, 3

    local function findElevators()
        local all = ctx.findAll(PARENT_CLASS)
        if #all > 0 then return all end
        local fallback = {}
        for _, class in ipairs(FALLBACK_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                table.insert(fallback, actor)
            end
        end
        return fallback
    end

    local function findElevatorByFullName(id)
        for _, actor in ipairs(findElevators()) do
            if actor:IsValid() and ctx.fullName(actor) == id then return actor end
        end
        return nil
    end

    -- Returns the raw ElevatorCurrentMode byte (0-3, see the constants above), or nil when this
    -- instance does not expose it (an unfamiliar elevator type - reported as "not controllable"
    -- rather than treated as an error).
    local function readMode(elevator)
        local ok, mode = pcall(function() return elevator.ElevatorCurrentMode end)
        if not ok then return nil end
        return tonumber(mode)
    end

    -- Round 125: exposed so the player can see why a move might be refused (setTopOpen below
    -- already gates a press on this same IsPowered() call) before clicking, not only from the
    -- refusal reason afterward. nil (not false) when this instance has no readable IsPowered()
    -- at all - a different, honest "not available live" case from a real false.
    local function readPowered(elevator)
        local ok, powered = pcall(function() return elevator:IsPowered() end)
        if ok and type(powered) == "boolean" then return powered end
        return nil
    end

    local function elevatorRows()
        local result = { __forceArray = true }
        for _, elevator in ipairs(findElevators()) do
            if elevator:IsValid() then
                local name = ctx.fullName(elevator)
                if name then
                    local x, y, z = ctx.actorLocation(elevator)
                    local mode = readMode(elevator)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name), -- the actor's real class name, always
                        controllable = mode ~= nil,
                        topOpen = mode == STOPPED_AT_TOP,
                        moving = mode == MOVING_TO_TOP or mode == MOVING_TO_BOTTOM,
                        powered = readPowered(elevator),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    -- Calls one elevator toward `wanted` (true = top, false = bottom) using the game's own
    -- TryPressTopButton/TryPressBottomButton - never pressing the button for the side the
    -- elevator is already parked at (the toggle-AWAY quirk documented above). Returns nil on
    -- success (already there, or the platform is now moving/continuing the right way), or a
    -- player-safe reason string. An elevator type without a readable ElevatorCurrentMode (see
    -- readMode) fails here with a named reason instead of ever attempting a press.
    local function setTopOpen(elevator, wanted)
        local mode = readMode(elevator)
        if mode == nil then return "this elevator type has no known live state (not controllable)" end
        if mode == MOVING_TO_TOP or mode == MOVING_TO_BOTTOM then
            return "elevator is currently moving"
        end
        if (wanted and mode == STOPPED_AT_TOP) or ((not wanted) and mode == STOPPED_AT_BOTTOM) then
            return nil -- already parked where it was asked to go
        end
        local okPower, powered = pcall(function() return elevator:IsPowered() == true end)
        if not (okPower and powered) then return "elevator is not powered" end
        if wanted then
            pcall(function() elevator:TryPressTopButton(true) end)
        else
            pcall(function() elevator:TryPressBottomButton(true) end)
        end
        local after = readMode(elevator)
        local accepted = (wanted and (after == STOPPED_AT_TOP or after == MOVING_TO_TOP))
            or ((not wanted) and (after == STOPPED_AT_BOTTOM or after == MOVING_TO_BOTTOM))
        if not accepted then return "could not confirm the elevator started moving" end
        return nil
    end

    ctx.handlers["elevators.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { elevators = elevatorRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Matches doors.set/portals.set: a row whose id does not resolve to a live elevator is
    -- collected and only turned into an error once every resolvable row has run, naming the
    -- first id that failed to resolve; a resolvable row whose setTopOpen call fails is reported
    -- the same way, naming the first such row and its reason.
    ctx.handlers["elevators.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change elevators") end
            local rows = payload.elevators or {}
            local missingId, failedId, failedReason = nil, nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local elevator = row.id and findElevatorByFullName(row.id)
                if elevator then
                    if row.topOpen ~= nil then
                        local reason = setTopOpen(elevator, row.topOpen)
                        if reason then
                            failedId = failedId or row.id
                            failedReason = failedReason or reason
                        end
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("elevator not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if failedId then error(tostring(failedReason) .. ": " .. tostring(failedId)) end
            return nil
        end, respond)
    end
end
