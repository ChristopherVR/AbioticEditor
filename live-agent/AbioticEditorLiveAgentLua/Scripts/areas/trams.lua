-- Live editing area: trams (TramMap, round 103; Facility only). The live twin of
-- Core/WorldSaves/Services/WorldMapFeatures/TramMapFeature.cs (the save's TramMap: editable
-- "lastStation" choice, read-only on-board container count).
--
-- Read straight from the coordinator's own CUE4Parse class dump plus the full blueprint bytecode
-- (ScriptBytecode JSON) of Tram_ParentBP_C's own station-arrival logic - not guessed. See
-- docs/PROGRESS.md's Round-103 entry (and its follow-up) and docs/reference/live-editing-protocol.md
-- for the full citation trail.
--
-- CLASS DISCOVERY: Tram_ParentBP_C (super=Actor) is the one class every placed tram derives from,
-- confirmed from each concrete tram asset's own `super=` in the dump (Tram_Default_C,
-- Tram_ContainmentLift_C). FindAllOf is hierarchy-inclusive (same idiom every other area here
-- relies on), so a single FindAllOf("Tram_ParentBP_C") already returns every subclass instance and
-- any future one the game adds, with no class name hardcoded anywhere below.
-- TramSystem_RecallStation_C (see below) has no parent of its own (super=Actor, a leaf class), so
-- its own sweep is a plain FindAllOf with no hierarchy/fallback table needed.
--
-- STATION IDENTITY: "last parked station" is the live `PreviousStation` ObjectProperty (replicated,
-- RepNotify OnRep_PreviousStation) - confirmed, not guessed, by tracing the actual arrival sequence
-- in Tram_ParentBP_C's own ubergraph bytecode: on reaching a stop, the graph sets
-- `PreviousStation = TargetStation` (a plain instance-to-instance property copy), calls
-- MarkPropertyDirtyFromRepIndex + OnRep_PreviousStation(), and the very next statement calls
-- `GameMode:UpdateActorToWorldSave(Self, false, 14)` - the actual "persist this tram now" call,
-- run immediately after PreviousStation is updated. This is exactly the save-time mapping
-- TramMapFeature's own doc comment describes for LastStation_, traced end to end rather than
-- inferred by symmetry (unlike the elevator area's load-time-only mapping). `TargetStation` (the
-- stop the tram is currently heading to or already sitting at) is read as a bonus read-only field.
-- Separately confirmed this round: `TramSystem_Station_C:TramReachedLocation`'s own bytecode is a
-- short, unbranching function whose only meaningful statement sets its `ContinueMoving` out-param
-- to `false` unconditionally (one `EX_LetBool ... = EX_False` right before the only `EX_Return`,
-- no `EX_Jump`/`EX_JumpIfNot` anywhere in the function) - every station stop is a real, full stop;
-- a tram never sails through an intermediate station toward a farther target on its own.
--
-- RECALL WRITE PATH (round-103 follow-up): the coordinator dumped Button_TramRecall.json,
-- Button_Tram.json and TramSystem_Station.json plus layouts.txt entries (properties/function
-- signatures only, no bytecode) for TramSystem_RecallStation_C and TramSystem_Rail_C.
-- `Button_TramRecall_C` (super=Button_Tram_C, itself super=Button_Generic_C with no properties/
-- functions of its own) overrides only `GetInteractText` - confirmed from its own bytecode, which
-- calls `TramReference:FindNextStation(Positive)` (TramReference is an FObjectProperty typed
-- `Tram_ParentBP_C`) purely to build display text ("next stop: X"), and touches nothing else. The
-- button itself has NO other logic - Tram_ParentBP_C's own bytecode (checked directly) never
-- references TramRecall/RecallButton/RecallStation at all, so the actual recall trigger cannot live
-- on either of those two classes.
--
-- The real trigger is `TramSystem_RecallStation_C` (props LinkedTram, LinkedStation, RecallButton,
-- TramRecallStatus with OnRep_TramRecallStatus; funcs TramRecallPressed, UpdateRecallStatus,
-- TramReachedStation, TramTargetStationUpdated, WarnLastInteractedPlayer). Its own
-- ExecuteUbergraph_TramSystem_RecallStation (properties-only view; layouts.txt, not bytecode) casts
-- something to `Button_Tram_Recall` and to `Button_Generic` and creates delegates
-- (K2Node_DynamicCast_AsButton_Tram_Recall / K2Node_CreateDelegate_OutputDelegate, times 3) right
-- next to its own `RecallButton` property - the same "cast the button, bind its Activated delegate"
-- shape Tram_ParentBP_C itself uses for its own Positive/NegativeButton (confirmed, not guessed:
-- Tram_ParentBP_C's ubergraph does the identical dynamic-cast + CreateDelegate pairing for those
-- two, and this project has never found that shape used for anything other than binding a button's
-- Activated event). `TramRecallPressed(Activated: bool)`'s own local-variable list (layouts.txt) -
-- DirectionCheck, StationCheck, PositivePath, FoundPath, RailToCheck, StationDistanceCount, plus
-- CallFunc_FindNextStation_*, CallFunc_GetDirectionFromStation_Positive, CallFunc_GetNextStopPoint_*,
-- CallFunc_IsStationLocked_Locked - proves it performs real multi-hop pathfinding (a counted loop
-- through TramSystem_Rail:GetNextStopPoint, checking each station along the way with
-- IsStationLocked) to compute which direction (`PositivePath`) reaches `LinkedStation`, and whether
-- a path exists at all (`FoundPath`). Combined with the confirmed "every station is a full stop"
-- fact above, this reads as an asynchronous, multi-step journey the RecallStation actor drives over
-- real time (its own `TramReachedStation`/`TramTargetStationUpdated` functions read as the
-- re-trigger-on-arrival hooks that keep a multi-hop recall going after each intermediate stop) -
-- NOT a single atomic teleport.
--
-- **What is NOT independently confirmed**: `TramSystem_RecallStation_C.json`'s own ScriptBytecode
-- was not part of this round's dump (properties/function signatures only), so the exact call
-- `TramRecallPressed` makes on `LinkedTram` (almost certainly `SetNextStopPoint`, the one function
-- on `Tram_ParentBP_C` already confirmed this round to set `TargetStation` from
-- `Rail:GetNextStopPoint` and call `SetMoving` - see the `SetNextStopPoint` note below - but not
-- literally seen called from here), whether `TramRecallPressed` gates on `IsServer()`/host itself,
-- and the exact mechanics of how an intermediate stop resumes the journey are all inferred from the
-- evidence above, not read off `TramRecallPressed`'s own bytecode. `TramSystem_Rail_C`'s own
-- `GetNextStopPoint`/`GetDirectionFromStation` bytecode is similarly not available. Per this mod's
-- own rule against guessing a function's behavior from a hunch, `trams.set` below still gates on
-- the observable outcome only (matches `elevators.set`'s own "a press with no confirmed effect is
-- an error, not a false success" discipline) rather than asserting anything about
-- `TramRecallPressed`'s internals - the coordinator can dump `TramSystem_RecallStation.json` (and
-- `TramSystem_Rail.json`) in a future round to close this last gap with full certainty.
--
-- `SetNextStopPoint(Positive: bool, CurrentPoint: TramSystem_StopPoint)` - fully traced this round
-- (Tram_ParentBP_C's own bytecode): calls `Rail:GetNextStopPoint(...)`, writes the result into
-- `TargetStation`, broadcasts the `TargetStationUpdated` delegate, calls `TriggerStationFlag`, and
-- calls `SetMoving(true)` (twice, once per branch of an internal check not itself traced). This is
-- a genuine "start heading to the immediate next stop in a direction" call, confirmed to actually
-- move the tram - but only one hop, never an arbitrary distant station by itself.
--
-- `trams.set` therefore implements `{id, targetStation}` by finding a `TramSystem_RecallStation_C`
-- instance whose `LinkedTram` is the requested tram and whose `LinkedStation` (by its friendly
-- label) is the requested station, then calling that instance's own `TramRecallPressed(true)` -
-- the game's own function, not a reimplementation of its pathfinding. Refuses up front if the tram
-- is already moving (matching the owner's own instruction and elevators.set's "currently moving"
-- refusal) or if no recall station links this exact tram/station pair (the offline feature can name
-- any station the save has ever referenced; live, only station pairs an actual placed recall
-- station links are reachable - an honest, narrower, but real write). After calling
-- `TramRecallPressed(true)`, re-reads `Moving`/`PreviousStation` and accepts either the tram now
-- moving (a hop toward the target started, matching the async multi-hop model above - the journey
-- may still be in progress) or the tram already sitting at the requested station as success;
-- anything else (no confirmed effect at all) is an honest error, not a false success - the same
-- discipline `elevators.set` already established for this mod.
return function(ctx)
    local PARENT_CLASS = "Tram_ParentBP_C"
    local RECALL_STATION_CLASS = "TramSystem_RecallStation_C"

    local function allTrams()
        local result, seen = {}, {}
        for _, actor in ipairs(ctx.findAll(PARENT_CLASS)) do
            local name = ctx.fullName(actor)
            if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
        end
        return result
    end

    local function findTram(id)
        for _, actor in ipairs(allTrams()) do
            if actor:IsValid() and ctx.fullName(actor) == id then return actor end
        end
        return nil
    end

    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    -- "PersistentLevel.TramSystem_Station_C_9" out of a station actor's own GetFullName() (which
    -- may or may not carry a leading "TramSystem_Station_C " class-name token depending on the
    -- caller) - matches TramMapFeature's own FriendlyStation output shape exactly (it strips the
    -- same "PersistentLevel." qualifier from the save's SubPathString) so a station shown here
    -- reads the same as the offline WORLD > Trams screen.
    local function stationLabel(stationActor)
        if not stationActor then return nil end
        local okValid, valid = pcall(function() return stationActor:IsValid() end)
        if not okValid or not valid then return nil end
        local name = ctx.fullName(stationActor)
        if not name then return nil end
        local tail = name:match("PersistentLevel%.(.+)$")
        return tail or name
    end

    -- Every TramSystem_RecallStation_C instance whose LinkedTram matches tramFullName, as
    -- {station = <friendly LinkedStation label>, actor = <the recall station itself>}. A recall
    -- station missing either link (feature-detected, not assumed) is skipped, not erroring the
    -- whole sweep.
    local function recallLinksFor(tramFullName)
        local links = {}
        for _, rs in ipairs(ctx.findAll(RECALL_STATION_CLASS)) do
            if rs:IsValid() then
                local okTram, linkedTram = pcall(function() return rs.LinkedTram end)
                local okStation, linkedStation = pcall(function() return rs.LinkedStation end)
                if okTram and linkedTram and okStation and linkedStation then
                    local tramName = ctx.fullName(linkedTram)
                    if tramName == tramFullName then
                        local label = stationLabel(linkedStation)
                        if label then table.insert(links, { station = label, actor = rs }) end
                    end
                end
            end
        end
        return links
    end

    local function recallStationLabelsFor(tramFullName)
        local labels, seen = { __forceArray = true }, {}
        for _, link in ipairs(recallLinksFor(tramFullName)) do
            if not seen[link.station] then seen[link.station] = true; table.insert(labels, link.station) end
        end
        return labels
    end

    local function findRecallStationFor(tramFullName, targetLabel)
        for _, link in ipairs(recallLinksFor(tramFullName)) do
            if link.station == targetLabel then return link.actor end
        end
        return nil
    end

    local function tramRows()
        local result = { __forceArray = true }
        for _, tram in ipairs(allTrams()) do
            if tram:IsValid() then
                local name = ctx.fullName(tram)
                if name then
                    local x, y, z = ctx.actorLocation(tram)
                    local okPrev, previous = pcall(function() return tram.PreviousStation end)
                    local okTarget, target = pcall(function() return tram.TargetStation end)
                    local okMoving, moving = pcall(function() return tram.Moving end)
                    local okDirection, direction = pcall(function() return tram.PositiveDirection end)
                    local okAtStation, atStation = pcall(function() return tram.IsAtStation end)
                    local okPassengers, passengers = pcall(function() return tram.HasPassengers end)
                    local okContainers, containers = pcall(function() return tram:GetTramContainers() end)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name),
                        previousStation = okPrev and stationLabel(previous) or nil,
                        targetStation = okTarget and stationLabel(target) or nil,
                        moving = boolOrNil(okMoving, moving),
                        positiveDirection = boolOrNil(okDirection, direction),
                        isAtStation = boolOrNil(okAtStation, atStation),
                        hasPassengers = boolOrNil(okPassengers, passengers),
                        containers = (okContainers and containers) and #containers or 0,
                        recallStations = recallStationLabelsFor(name),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["trams.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { trams = tramRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Returns nil on success (already there, or a hop toward targetLabel was confirmed to start -
    -- see the header note on the async multi-hop model), or a player-safe reason string. Never
    -- presses a recall button for a tram already confirmed moving (the owner's own instruction,
    -- matching elevators.set's "currently moving" refusal), and never invents a recall path this
    -- module cannot find a real linked TramSystem_RecallStation_C for.
    local function recallTramToStation(tram, tramFullName, targetLabel)
        -- boolOrNil, not the pcall "ok" flag alone: a fake or an unfamiliar real actor with no
        -- Moving property at all still returns `nil` without erroring (the same trap buttons.lua's
        -- own header comment documents), so only a genuine boolean counts as "read".
        local okMoving, movingRaw = pcall(function() return tram.Moving end)
        local moving = boolOrNil(okMoving, movingRaw)
        if moving == nil then return "cannot confirm this tram's moving state right now" end
        local okPrev, previous = pcall(function() return tram.PreviousStation end)
        local currentLabel = okPrev and stationLabel(previous) or nil
        if not moving and currentLabel == targetLabel then
            return nil -- already parked at the requested station
        end
        if moving then return "tram is currently moving" end

        local recallStation = findRecallStationFor(tramFullName, targetLabel)
        if not recallStation then
            return "no recall station links this tram to that station"
        end

        pcall(function() recallStation:TramRecallPressed(true) end)

        local okAfterMoving, afterMovingRaw = pcall(function() return tram.Moving end)
        local afterMoving = boolOrNil(okAfterMoving, afterMovingRaw)
        local okAfterPrev, afterPrev = pcall(function() return tram.PreviousStation end)
        local afterLabel = okAfterPrev and stationLabel(afterPrev) or nil
        local startedMoving = afterMoving == true
        local alreadyThereNow = (not startedMoving) and afterLabel == targetLabel
        if not (startedMoving or alreadyThereNow) then
            return "could not confirm the tram started moving toward that station"
        end
        return nil
    end

    -- Matches doors.set/elevators.set: every resolvable row applies first; only once every row has
    -- run does an unresolved id (or a refused recall) turn the whole reply into an error, naming
    -- the first row that failed and why.
    ctx.handlers["trams.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change trams") end
            local rows = payload.trams or {}
            local missingId, failedId, failedReason = nil, nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local tram = row.id and findTram(row.id)
                if tram then
                    if row.targetStation ~= nil then
                        local reason = recallTramToStation(tram, row.id, row.targetStation)
                        if reason then
                            failedId = failedId or row.id
                            failedReason = failedReason or reason
                        end
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("tram not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if failedId then error(tostring(failedReason) .. ": " .. tostring(failedId)) end
            return nil
        end, respond)
    end
end
