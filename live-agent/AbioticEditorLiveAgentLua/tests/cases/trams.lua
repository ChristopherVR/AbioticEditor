-- Trams (areas/trams.lua, round 103; recall write path added round-103 follow-up). Field names off
-- the real class dump and bytecode: PreviousStation/TargetStation (ObjectProperty,
-- TramSystem_Station_C), Moving, PositiveDirection, IsAtStation, HasPassengers,
-- GetTramContainers(). Discovery is a hierarchy sweep (FindAllOf("Tram_ParentBP_C")), so every fake
-- tram needs __bases = { "Tram_ParentBP_C" }. TramSystem_RecallStation_C is a leaf class (no
-- hierarchy sweep needed), discovered via LinkedTram/LinkedStation matching; its own
-- TramRecallPressed(Activated) is the real game function trams.set calls - see the module's own
-- header comment for exactly what is and is not independently bytecode-confirmed about it.
return function(H)
    H.hostSession()

    local function station()
        return H.world.add(H.object("TramSystem_Station_C", {}, {}))
    end

    -- The friendly label trams.lua's stationLabel() derives from a station's own GetFullName() -
    -- strips the "PersistentLevel." qualifier, matching TramMapFeature's own FriendlyStation.
    -- Computed from the fake's own auto-generated __fullName rather than hardcoded, since the
    -- harness's object counter is not reset per test case and the exact numeric suffix is not
    -- something this test should depend on.
    local function friendlyName(stationObj)
        local full = stationObj.__fullName
        return full:match("PersistentLevel%.(.+)$") or full
    end

    local platformA = station()
    local platformB = station()
    local platformC = station()
    local platformALabel = friendlyName(platformA)
    local platformBLabel = friendlyName(platformB)
    local platformCLabel = friendlyName(platformC)

    local function tram(className, previous, target, moving, positive, atStation, passengers, containers, extraFields)
        local fields = {
            __bases = { "Tram_ParentBP_C" },
            PreviousStation = previous,
            TargetStation = target,
            Moving = moving,
            PositiveDirection = positive,
            IsAtStation = atStation,
            HasPassengers = passengers,
        }
        if extraFields then for k, v in pairs(extraFields) do fields[k] = v end end
        return H.world.add(H.object(className, fields, {
            GetTramContainers = function() return containers or {} end,
            K2_GetActorLocation = function() return H.vector(4, 4, 4) end,
        }))
    end

    -- parked-at-A tram, not moving, two on-board containers.
    local atA = tram("Tram_Default_C", platformA, platformA, false, true, true, false, { {}, {} })
    -- en-route-to-B tram, moving, one passenger.
    local enRoute = tram("Tram_ContainmentLift_C", platformA, platformB, true, true, false, true, {})

    -- A recall station linking atA to platform B: pressing it simulates a real hop starting
    -- (Moving becomes true), matching what SetNextStopPoint is confirmed to do.
    local recallAtoB = H.world.add(H.object("TramSystem_RecallStation_C", {
        LinkedTram = atA,
        LinkedStation = platformB,
    }, {
        TramRecallPressed = function(self, activated)
            if not activated then return end
            rawget(atA, "__fields").Moving = true
        end,
    }))
    -- A second recall station linking atA to itself (platform A) - never exercised (already-there
    -- short-circuits before any recall station lookup), proves discovery finds the right one by
    -- LinkedTram+LinkedStation, not just the first one found.
    local recallAtoA = H.world.add(H.object("TramSystem_RecallStation_C", {
        LinkedTram = atA,
        LinkedStation = platformA,
    }, {
        TramRecallPressed = function() error("must never be pressed - already at the requested station") end,
    }))
    -- A recall station for a DIFFERENT tram (enRoute), to platform C - proves LinkedTram matching
    -- excludes it from atA's own recallStations list and from atA's own recall lookup.
    local recallEnRouteToC = H.world.add(H.object("TramSystem_RecallStation_C", {
        LinkedTram = enRoute,
        LinkedStation = platformC,
    }, {
        TramRecallPressed = function() end,
    }))

    local function rowFor(list, classFragment)
        for _, row in ipairs(list.trams) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local function hasLabel(list, label)
        for _, l in ipairs(list) do
            if l == label then return true end
        end
        return false
    end

    local list = H.ok(H.dispatch("trams.list"), "trams.list")
    H.eq(#list.trams, 2, "both trams listed, including a subclass found only through the parent sweep")
    local atARow = rowFor(list, "Tram_Default_C")
    local enRouteRow = rowFor(list, "Tram_ContainmentLift_C")
    H.check(atARow ~= nil and enRouteRow ~= nil, "both rows found")
    H.eq(atARow.previousStation, platformALabel, "previousStation strips the PersistentLevel. qualifier, matching offline's FriendlyStation")
    H.eq(atARow.targetStation, platformALabel, "targetStation reads the same way")
    H.eq(atARow.moving, false, "parked tram is not moving")
    H.eq(atARow.isAtStation, true, "parked tram reports at-station")
    H.eq(atARow.containers, 2, "on-board container count from GetTramContainers()")
    H.check(#atARow.recallStations == 2 and hasLabel(atARow.recallStations, platformALabel) and hasLabel(atARow.recallStations, platformBLabel),
        "atA's recall stations list exactly the stations linked to atA (A and B), not C (linked to the other tram)")
    H.eq(enRouteRow.previousStation, platformALabel, "en-route tram's last-parked station is still A")
    H.eq(enRouteRow.targetStation, platformBLabel, "en-route tram's target is B")
    H.eq(enRouteRow.moving, true, "en-route tram reports moving")
    H.eq(enRouteRow.hasPassengers, true, "en-route tram reports passengers")
    H.eq(enRouteRow.containers, 0, "en-route tram has no on-board containers")
    H.check(#enRouteRow.recallStations == 1 and hasLabel(enRouteRow.recallStations, platformCLabel),
        "en-route tram's own recall stations list only its own linked station (C)")

    -- trams.set success: recall atA to B through the real linked recall station.
    H.ok(H.dispatch("trams.set", { trams = { { id = atARow.id, targetStation = platformBLabel } } }),
        "recall atA to its linked station B")
    H.eq(H.calls(recallAtoB, "TramRecallPressed"), 1, "the linked A->B recall station's TramRecallPressed was pressed once")
    H.eq(H.calls(recallAtoA, "TramRecallPressed"), 0, "the unrelated A->A recall station was never touched")
    H.eq(H.field(atA, "Moving"), true, "the tram now reports moving, confirming the hop was accepted")

    -- Already-there short-circuit: reset atA to parked-and-not-moving, request its own current
    -- station - no recall station is pressed at all.
    rawget(atA, "__fields").Moving = false
    H.ok(H.dispatch("trams.set", { trams = { { id = atARow.id, targetStation = platformALabel } } }),
        "requesting the station already parked at is a no-op success")
    H.eq(H.calls(recallAtoA, "TramRecallPressed"), 0, "no recall station is pressed when already at the requested station")

    -- Currently-moving refusal: no recall attempted at all, even though no recall link exists for
    -- enRoute -> A (proves the moving check runs before any recall-station lookup).
    local movingReply = H.dispatch("trams.set", { trams = { { id = enRouteRow.id, targetStation = platformALabel } } })
    H.fails(movingReply, "currently moving", "a moving tram refuses a new recall request")

    -- No recall station links this exact tram/station pair.
    local noLinkReply = H.dispatch("trams.set", { trams = { { id = atARow.id, targetStation = platformCLabel } } })
    H.fails(noLinkReply, "no recall station links", "an unlinked tram/station pair is refused by name")

    -- A press with no confirmed effect (the honesty branch a wrong assumption about
    -- TramRecallPressed's internals would hit for real) is an error, not a false success.
    local stubbornRecall = H.world.add(H.object("TramSystem_RecallStation_C", {
        LinkedTram = atA,
        LinkedStation = platformC,
    }, {
        TramRecallPressed = function() end,
    }))
    local stubbornReply = H.dispatch("trams.set", { trams = { { id = atARow.id, targetStation = platformCLabel } } })
    H.fails(stubbornReply, "could not confirm", "a recall press with no observable effect is an honest error")
    H.eq(H.calls(stubbornRecall, "TramRecallPressed"), 1, "the stubborn recall station was still pressed once (the honesty check runs after, not instead of, the press)")

    -- Missing tram id: player-safe failure, not a Lua error - and any resolvable rows in the same
    -- batch still apply first (matching doors.set/elevators.set).
    local missingReply = H.dispatch("trams.set", { trams = { { id = "no-such-tram", targetStation = platformALabel } } })
    H.fails(missingReply, "not found", "unknown tram id fails cleanly")

    -- A tram matched by the parent sweep but missing every expected capability (no PreviousStation,
    -- no GetTramContainers) still lists - not dropped, not an error - with every state field absent.
    local unfamiliar = H.world.add(H.object("Tram_UnfamiliarTest_C", { __bases = { "Tram_ParentBP_C" } }, {
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
    }))
    local unfamiliarList = H.ok(H.dispatch("trams.list"), "trams.list after adding an unfamiliar tram")
    local unfamiliarRow = rowFor(unfamiliarList, "Tram_UnfamiliarTest_C")
    H.check(unfamiliarRow ~= nil, "unfamiliar tram still listed")
    H.eq(unfamiliarRow.previousStation, nil, "no PreviousStation on this instance, so previousStation is absent")
    H.eq(unfamiliarRow.moving, nil, "no Moving on this instance, so moving is absent")
    H.eq(unfamiliarRow.containers, 0, "GetTramContainers missing entirely reads back as zero, not an error")
    H.check(#unfamiliarRow.recallStations == 0, "an unfamiliar tram with no matching recall station links has an empty list, not an error")

    -- Cannot confirm moving state: a tram matched by the sweep but with no readable Moving field
    -- refuses a recall request outright rather than risking a redirect on an unknown state.
    local noMovingReadRecall = H.world.add(H.object("TramSystem_RecallStation_C", {
        LinkedTram = unfamiliar,
        LinkedStation = platformA,
    }, {
        TramRecallPressed = function() end,
    }))
    local unknownMovingReply = H.dispatch("trams.set", { trams = { { id = unfamiliarRow.id, targetStation = platformALabel } } })
    H.fails(unknownMovingReply, "cannot confirm this tram's moving state", "an unreadable Moving state refuses rather than guesses")
    H.eq(H.calls(noMovingReadRecall, "TramRecallPressed"), 0, "no recall attempted when the moving state itself could not be confirmed")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(atA)
    local clientList = H.ok(H.dispatch("trams.list"), "trams.list as client")
    H.fails(H.dispatch("trams.set", { trams = { { id = clientList.trams[1].id, targetStation = platformALabel } } }),
        "only the host", "client cannot edit trams")
end
