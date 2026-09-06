-- Doors (main.lua, SimpleDoor_ParentBP_C / SecurityDoor_C): both door kinds, a one-way door, a
-- disabled door, and the security door's separate IsDoorOpen/OnRep_IsDoorOpen shape. core.lua
-- already covers one plain hinged door end to end; this case goes deeper into the fields that
-- only show up on doors that are not in their default state.
return function(H)
    H.hostSession()

    -- A one-way door that has already been unlocked from the far side, and is also disabled
    -- (e.g. sealed by the story) - both flags read independently of DoorState.
    local oneWay = H.world.add(H.object("SimpleDoor_ParentBP_C", {
        DoorState = 2, OneWayDoor_HasBeenUnlocked = true, DoorDisabled = true,
    }, {
        K2_GetActorLocation = function() return H.vector(1, 1, 1) end,
        OnRep_DoorState = function() end, DoorUpdateState = function() end,
    }))
    -- A plain closed hinged door, for the "open it" write path.
    local hinged = H.world.add(H.object("SimpleDoor_ParentBP_C", {
        DoorState = 0, OneWayDoor_HasBeenUnlocked = false, DoorDisabled = false,
    }, {
        K2_GetActorLocation = function() return H.vector(2, 2, 2) end,
        OnRep_DoorState = function() end, DoorUpdateState = function() end,
    }))
    -- A security door: a completely separate class with IsDoorOpen/OnRep_IsDoorOpen, no
    -- DoorState/OneWayDoor_HasBeenUnlocked/DoorDisabled at all.
    local security = H.world.add(H.object("SecurityDoor_C", { IsDoorOpen = false }, {
        K2_GetActorLocation = function() return H.vector(3, 3, 3) end,
        OnRep_IsDoorOpen = function() end,
    }))

    local list = H.ok(H.dispatch("doors.list"), "doors.list")
    H.eq(#list.doors, 3, "all three doors found")
    local oneWayRow, hingedRow, securityRow
    for _, d in ipairs(list.doors) do
        if d.id == oneWay:GetFullName() then oneWayRow = d end
        if d.id == hinged:GetFullName() then hingedRow = d end
        if d.id == security:GetFullName() then securityRow = d end
    end
    H.eq(oneWayRow.kind, "simple", "one-way door reports as a hinged door")
    H.eq(oneWayRow.oneWayUnlocked, true, "one-way-unlocked flag read")
    H.eq(oneWayRow.disabled, true, "disabled flag read")
    H.eq(oneWayRow.state, 2, "locked state read")
    H.eq(hingedRow.state, 0, "hinged door starts closed")
    H.eq(hingedRow.isOpen, false, "isOpen derived from state 0")
    H.eq(securityRow.kind, "security", "security door reports its own kind")
    H.eq(securityRow.isOpen, false, "security door starts closed")

    -- Open the hinged door: direct DoorState write + OnRep_DoorState + DoorUpdateState (see
    -- main.lua's comment on why TryOpenOrUnlockDoor/MarkOneWayDoorAsUnlocked were not adopted).
    H.ok(H.dispatch("doors.set", { doors = { { id = hingedRow.id, kind = "simple", state = 1 } } }), "open the hinged door")
    H.eq(H.field(hinged, "DoorState"), 1, "state written")
    H.eq(H.calls(hinged, "OnRep_DoorState"), 1, "OnRep_DoorState called once")
    H.eq(H.calls(hinged, "DoorUpdateState"), 1, "DoorUpdateState called once")

    -- Re-lock the one-way door and re-enable it.
    H.ok(H.dispatch("doors.set", { doors = { { id = oneWayRow.id, kind = "simple", oneWayUnlocked = false, disabled = false } } }), "relock the one-way door")
    H.eq(H.field(oneWay, "OneWayDoor_HasBeenUnlocked"), false, "one-way-unlocked cleared")
    H.eq(H.field(oneWay, "DoorDisabled"), false, "disabled cleared")

    -- Security door: a completely different write shape (IsDoorOpen + OnRep_IsDoorOpen, no
    -- DoorState at all).
    H.ok(H.dispatch("doors.set", { doors = { { id = securityRow.id, kind = "security", isOpen = true } } }), "open the security door")
    H.eq(H.field(security, "IsDoorOpen"), true, "security door opened")
    H.eq(H.calls(security, "OnRep_IsDoorOpen"), 1, "security door's own OnRep called")
    H.eq(H.calls(security, "OnRep_DoorState") or 0, 0, "the hinged-door OnRep was never called on a security door")

    -- Missing door id: player-safe failure, not a Lua error - and any resolvable row in the same
    -- batch still applies first.
    H.ok(H.dispatch("doors.set", { doors = { { id = hingedRow.id, kind = "simple", state = 0 } } }), "close the hinged door")
    local reply = H.dispatch("doors.set", { doors = {
        { id = hingedRow.id, kind = "simple", state = 1 },
        { id = "no-such-door", kind = "simple", state = 1 },
    } })
    H.fails(reply, "not found", "unknown door id fails cleanly")
    H.eq(H.field(hinged, "DoorState"), 1, "the resolvable row in the same batch still applied")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(hinged)
    H.fails(H.dispatch("doors.set", { doors = { { id = hingedRow.id, kind = "simple", state = 0 } } }), "only the host", "client cannot edit doors")
end
