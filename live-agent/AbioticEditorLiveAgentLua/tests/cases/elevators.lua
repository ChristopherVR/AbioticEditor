-- Fixed elevator platforms (areas/elevators.lua, corrected round 96, subclass-generic discovery
-- round 97). Field/function names off the real class+bytecode probe
-- (tests/AbioticEditor.Probes/ElevatorButtonProbe.cs): ElevatorCurrentMode (byte enum,
-- 0 StoppedAtBottom, 1 StoppedAtTop, 2 MovingToTop, 3 MovingToBottom), OnRep_ElevatorCurrentMode,
-- TryPressTopButton(Activated)/TryPressBottomButton(Activated), IsPowered. The fake TryPress*Button
-- methods below implement the same per-mode transition table the real bytecode does (case 0/3 ->
-- 2, case 1 -> 3 for the top button; the mirror for the bottom button; mode 2/3 in the "already
-- going that way" case left unchanged), so this exercises the module's read-back-tolerant success
-- path against realistic behavior, not just an always-succeeds stub.
return function(H)
    H.hostSession()

    local function pressTop(self, activated)
        if not activated then return end
        local mode = self.ElevatorCurrentMode
        if mode == 0 or mode == 3 then self.ElevatorCurrentMode = 2
        elseif mode == 1 then self.ElevatorCurrentMode = 3
        end
        -- mode == 2: already moving up, matches the real "already on its way up" no-op case.
    end
    local function pressBottom(self, activated)
        if not activated then return end
        local mode = self.ElevatorCurrentMode
        if mode == 1 or mode == 2 then self.ElevatorCurrentMode = 3
        elseif mode == 0 then self.ElevatorCurrentMode = 2
        end
        -- mode == 3: already moving down, matches the real "already on its way down" no-op case.
    end

    local function elevator(class, mode, extraMethods, extraFields)
        local methods = {
            OnRep_ElevatorCurrentMode = function() end,
            IsPowered = function() return true end,
            TryPressTopButton = pressTop,
            TryPressBottomButton = pressBottom,
            K2_GetActorLocation = function() return H.vector(1, 1, 1) end,
        }
        if extraMethods then for k, v in pairs(extraMethods) do methods[k] = v end end
        local fields = { ElevatorCurrentMode = mode }
        if extraFields then for k, v in pairs(extraFields) do fields[k] = v end end
        return H.world.add(H.object(class, fields, methods))
    end

    -- Discovery: findAll("Elevator_ParentBP_C") alone must also see a subclass instance
    -- (__bases, the same trick the harness itself uses for the confirmed Elevator_Office_BP_C
    -- relationship) - matching the NPC_Base_ParentBP_C idiom this module deliberately reuses, so
    -- no per-class name is hardcoded anywhere in elevators.lua.
    local atBottom = elevator("Elevator_ParentBP_C", 0)
    local atTop = elevator("Elevator_Office_BP_C", 1, nil, { __bases = { "Elevator_ParentBP_C" } })

    local list = H.ok(H.dispatch("elevators.list"), "elevators.list")
    H.eq(#list.elevators, 2, "two elevators across both classes")
    H.eq(list.elevators[1].topOpen, false, "first elevator parked at bottom")
    H.eq(list.elevators[1].moving, false, "first elevator not moving")
    H.eq(list.elevators[1].controllable, true, "first elevator is controllable")
    H.eq(list.elevators[2].topOpen, true, "second elevator (a subclass, found only through the parent sweep) parked at top")

    -- elevators.set: call the bottom one up. It is not there yet (real travel takes time), so
    -- topOpen only becomes true once it arrives - but the write itself is accepted because the
    -- platform started moving the right way.
    H.ok(H.dispatch("elevators.set", { elevators = { { id = list.elevators[1].id, topOpen = true } } }), "call elevator to top")
    H.eq(H.field(atBottom, "ElevatorCurrentMode"), 2, "elevator now moving to top, not yet arrived")
    H.eq(H.calls(atBottom, "TryPressTopButton"), 1, "TryPressTopButton pressed once")

    -- Toggle-away protection: pressing the top button on an elevator ALREADY at the top would
    -- send it back down (a real quirk in the game's own bytecode) - the module must never do
    -- this when the caller only asked to confirm it is already at the top.
    H.ok(H.dispatch("elevators.set", { elevators = { { id = list.elevators[2].id, topOpen = true } } }), "already at top is a no-op, not a toggle")
    H.eq(H.calls(atTop, "TryPressTopButton"), 0, "top button never pressed for an elevator already parked there")
    H.eq(H.field(atTop, "ElevatorCurrentMode"), 1, "elevator still parked at top")

    -- Currently-moving refusal: the bottom elevator (now mode 2, moving) refuses a new request
    -- with a named reason instead of redirecting it mid-transit.
    local movingReply = H.dispatch("elevators.set", { elevators = { { id = list.elevators[1].id, topOpen = false } } })
    H.fails(movingReply, "currently moving", "a moving elevator refuses a new call")
    H.eq(H.calls(atBottom, "TryPressBottomButton"), 0, "bottom button never pressed while already moving")

    -- Unpowered refusal.
    local unpowered = elevator("Elevator_ParentBP_C", 0, { IsPowered = function() return false end })
    local unpoweredList = H.ok(H.dispatch("elevators.list"), "elevators.list after adding the unpowered one")
    local unpoweredId = unpoweredList.elevators[3].id
    local unpoweredReply = H.dispatch("elevators.set", { elevators = { { id = unpoweredId, topOpen = true } } })
    H.fails(unpoweredReply, "not powered", "an unpowered elevator refuses to move")
    H.eq(H.calls(unpowered, "TryPressTopButton"), 0, "top button never pressed on an unpowered elevator")

    -- A press that has no confirmed effect (the honesty branch a wrongly-guessed property/
    -- function would hit for real) reports an error instead of a false success.
    local stubborn = elevator("Elevator_ParentBP_C", 0, { TryPressTopButton = function() end })
    local stubbornList = H.ok(H.dispatch("elevators.list"), "elevators.list after adding the stubborn one")
    local stubbornId = stubbornList.elevators[4].id
    local stubbornReply = H.dispatch("elevators.set", { elevators = { { id = stubbornId, topOpen = true } } })
    H.fails(stubbornReply, "could not confirm", "a press with no effect is an honest error, not a false success")

    -- A brand-new elevator subclass this module has never heard of (no name for it appears
    -- anywhere in elevators.lua) is still found through the parent sweep alone, and is fully
    -- listable and settable - proving a future DLC elevator variant needs no code change here.
    local futureDlc = elevator("Elevator_FutureDLC_BP_C", 0, nil, { __bases = { "Elevator_ParentBP_C" } })
    local futureList = H.ok(H.dispatch("elevators.list"), "elevators.list after adding an unknown subclass")
    local futureEntry = futureList.elevators[5]
    H.eq(futureEntry.label, "Elevator_FutureDLC_BP_C", "the unknown subclass's real class name is reported as its label")
    H.eq(futureEntry.controllable, true, "the unknown subclass is still controllable (it has the normal capabilities)")
    H.ok(H.dispatch("elevators.set", { elevators = { { id = futureEntry.id, topOpen = true } } }), "the unknown subclass is settable")
    H.eq(H.calls(futureDlc, "TryPressTopButton"), 1, "top button pressed on the unknown subclass exactly like a known one")

    -- An elevator matched by the parent sweep but missing the expected capabilities (no
    -- ElevatorCurrentMode, no TryPress*Button) still lists - not dropped, not an error - flagged
    -- not controllable, and a set attempt against it is refused with a named reason rather than
    -- silently doing nothing or throwing.
    local unfamiliar = H.world.add(H.object("Elevator_ParentBP_C", {}, {
        K2_GetActorLocation = function() return H.vector(3, 3, 3) end,
    }))
    local unfamiliarList = H.ok(H.dispatch("elevators.list"), "elevators.list after adding an unfamiliar elevator")
    local unfamiliarEntry = unfamiliarList.elevators[6]
    H.eq(unfamiliarEntry.controllable, false, "an elevator with no readable ElevatorCurrentMode lists as not controllable")
    H.eq(unfamiliarEntry.topOpen, false, "topOpen is reported false (meaningless) rather than erroring")
    local unfamiliarReply = H.dispatch("elevators.set", { elevators = { { id = unfamiliarEntry.id, topOpen = true } } })
    H.fails(unfamiliarReply, "not controllable", "a set attempt on an unfamiliar elevator is refused by name")

    -- Missing elevator id: player-safe failure, not a Lua error - and any resolvable rows in
    -- the same call still apply first.
    local reply = H.dispatch("elevators.set", { elevators = {
        { id = stubbornId, topOpen = false },
        { id = "no-such-elevator", topOpen = true },
    } })
    H.fails(reply, "not found", "unknown elevator id fails cleanly")

    -- Fallback discovery: if the parent-class sweep itself ever comes back empty (not exercised
    -- above, since every fixture there matches "Elevator_ParentBP_C" one way or another), the
    -- short known-class fallback list still finds a concrete subclass instance directly.
    H.world.reset()
    local fallbackOnly = elevator("Elevator_Office_BP_C", 0)
    local fallbackList = H.ok(H.dispatch("elevators.list"), "elevators.list via the fallback class list")
    H.eq(#fallbackList.elevators, 1, "fallback list finds the object the parent sweep alone would miss")
    H.eq(fallbackList.elevators[1].controllable, true, "fallback-found elevator is still fully controllable")

    -- Non-host refusal.
    H.clientSession()
    local clientElevator = elevator("Elevator_ParentBP_C", 0)
    local clientList = H.ok(H.dispatch("elevators.list"), "elevators.list as client")
    H.fails(H.dispatch("elevators.set", { elevators = { { id = clientList.elevators[1].id, topOpen = true } } }), "only the host", "client cannot edit elevators")
end
