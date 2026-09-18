-- Power sockets (areas/powersockets.lua, round 103). Field/function names off the real class+
-- bytecode probe: GetPowerSocketID() (BlueprintPure, MakeSavedObjectPath+BreakSoftObjectPath),
-- LatestSaveData.HasTimer_9_3EE13A6A4C191C0D16A106BF214E9F81 /
-- LatestSaveData.TimerMode_8_B4F1798A4364D14CC93168AD9E666B81 (struct-nested, read-only - the only
-- write site, Update_SaveData, unconditionally resets both to false/0 in both of its branches),
-- IsPowered(). Discovery is a hierarchy sweep (FindAllOf("PowerSocket_ParentBP_C")), so every fake
-- socket needs __bases = { "PowerSocket_ParentBP_C" }, matching buttons.lua/elevators.lua.
local HAS_TIMER_LEAF = "HasTimer_9_3EE13A6A4C191C0D16A106BF214E9F81"
local TIMER_MODE_LEAF = "TimerMode_8_B4F1798A4364D14CC93168AD9E666B81"

return function(H)
    H.hostSession()

    local function socket(className, socketId, hasTimer, timerMode, plugged, extraFields)
        local fields = {
            __bases = { "PowerSocket_ParentBP_C" },
            LatestSaveData = { [HAS_TIMER_LEAF] = hasTimer, [TIMER_MODE_LEAF] = timerMode },
            PluggedInDevice = plugged,
        }
        if extraFields then for k, v in pairs(extraFields) do fields[k] = v end end
        return H.world.add(H.object(className, fields, {
            GetPowerSocketID = function() return socketId end,
            IsPowered = function() return true end,
            K2_GetActorLocation = function() return H.vector(2, 2, 2) end,
        }))
    end

    -- A plugged-in device: a plain fake actor with its own GetFullName(), read through the
    -- normal ctx.fullName/classLabel pair, never through the save's asset-id resolver (that is
    -- offline-only machinery this live area does not need).
    local device = H.world.add(H.object("Deployed_ChemistryBench_C", {}, {}))

    -- reactor-adjacent socket: has a device plugged in, no timer armed (the only value this
    -- module can ever observe per the header's evidence).
    local plugged = socket("PowerSocket_MgtCore_C", "/Game/Maps/Facility.Facility:PersistentLevel.PowerSocket_MgtCore_C_1",
        false, 0, device)
    -- empty socket: nothing plugged in.
    local empty = socket("PowerSocket_ParentBP_C", "/Game/Maps/Facility.Facility:PersistentLevel.PowerSocket_ParentBP_C_2",
        false, 0, nil)

    local function rowFor(list, classFragment)
        for _, row in ipairs(list.sockets) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local list = H.ok(H.dispatch("powersockets.list"), "powersockets.list")
    H.eq(#list.sockets, 2, "both sockets listed, including a subclass found only through the parent sweep")
    local plugRow = rowFor(list, "PowerSocket_MgtCore_C")
    local emptyRow = rowFor(list, "PowerSocket_ParentBP_C")
    H.check(plugRow ~= nil and emptyRow ~= nil, "both rows found")
    H.eq(plugRow.socketId, "/Game/Maps/Facility.Facility:PersistentLevel.PowerSocket_MgtCore_C_1",
        "socketId reads GetPowerSocketID() verbatim")
    H.eq(plugRow.pluggedInDevice, "Deployed_ChemistryBench_C", "plugged device reports its own real class name")
    H.eq(emptyRow.pluggedInDevice, "nothing plugged in", "an empty socket reports nothing plugged in")
    H.eq(plugRow.hasTimer, false, "hasTimer reads back the only value this module can ever observe")
    H.eq(plugRow.timerMode, 0, "timerMode reads back the raw byte (0 = NewEnumerator0)")
    H.eq(plugRow.powered, true, "powered reads IsPowered()")

    -- powersockets.set: hasTimer is refused outright, by name, never silently ignored.
    local setReply = H.dispatch("powersockets.set", { sockets = { { id = plugRow.id, hasTimer = true } } })
    H.fails(setReply, "cannot be armed live", "arming a socket's timer live is refused with the evidenced reason")

    -- Missing socket id: player-safe failure, not a Lua error.
    local missingReply = H.dispatch("powersockets.set", { sockets = { { id = "no-such-socket", hasTimer = true } } })
    H.fails(missingReply, "not found", "unknown socket id fails cleanly")

    -- A brand-new socket subclass this module has never heard of is still found through the
    -- parent sweep alone (no name for it appears anywhere in powersockets.lua) and lists with
    -- whatever it actually has.
    local futureDlc = socket("PowerSocket_FutureDLC_C", "/Game/Maps/Facility.Facility:PersistentLevel.PowerSocket_FutureDLC_C_3",
        false, 0, nil)
    local futureList = H.ok(H.dispatch("powersockets.list"), "powersockets.list after adding an unknown subclass")
    local futureRow = rowFor(futureList, "PowerSocket_FutureDLC_C")
    H.eq(futureRow.label, "PowerSocket_FutureDLC_C", "the unknown subclass's real class name is reported as its label")
    H.eq(futureRow.hasTimer, false, "the unknown subclass's hasTimer still reads (feature-detected, not assumed)")

    -- A socket matched by the parent sweep but missing every expected capability (no
    -- GetPowerSocketID, no LatestSaveData, no IsPowered) still lists - not dropped, not an error -
    -- with every state field absent instead of a guessed value.
    local unfamiliar = H.world.add(H.object("PowerSocket_UnfamiliarTest_C", { __bases = { "PowerSocket_ParentBP_C" } }, {
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
    }))
    local unfamiliarList = H.ok(H.dispatch("powersockets.list"), "powersockets.list after adding an unfamiliar socket")
    local unfamiliarRow = rowFor(unfamiliarList, "PowerSocket_UnfamiliarTest_C")
    H.check(unfamiliarRow ~= nil, "unfamiliar socket still listed")
    H.eq(unfamiliarRow.socketId, nil, "no GetPowerSocketID on this instance, so socketId is absent")
    H.eq(unfamiliarRow.hasTimer, nil, "no LatestSaveData on this instance, so hasTimer is absent")
    H.eq(unfamiliarRow.powered, nil, "no IsPowered on this instance, so powered is absent")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(plugged)
    local clientList = H.ok(H.dispatch("powersockets.list"), "powersockets.list as client")
    H.fails(H.dispatch("powersockets.set", { sockets = { { id = clientList.sockets[1].id, hasTimer = true } } }),
        "only the host", "client cannot edit power sockets")
end
