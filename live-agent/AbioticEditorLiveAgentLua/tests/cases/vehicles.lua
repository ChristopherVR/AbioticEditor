-- Vehicles (areas/vehicles.lua): VehicleID (FStrProperty, must convert), VehicleDriveable + its
-- OnRep, and moving via the native K2_TeleportTo with a plain X/Y/Z table (no FVector() global).
return function(H)
    H.hostSession()

    local forklift = H.world.add(H.object("ABF_Vehicle_Forklift_C", {
        __bases = { "ABF_Vehicle_ParentBP_C" },
        VehicleID = H.fstring("Forklift_01"),
        VehicleDriveable = true,
    }, {
        OnRep_VehicleDriveable = function() end,
        K2_GetActorLocation = function() return H.vector(50, 60, 70) end,
        K2_GetActorRotation = function() return H.rotator(0, 45, 0) end,
        K2_TeleportTo = function(self, location) rawget(self, "__methods").K2_GetActorLocation = function() return { X = location.X, Y = location.Y, Z = location.Z } end return true end,
    }))

    local list = H.ok(H.dispatch("vehicles.list"), "vehicles.list")
    H.eq(#list.vehicles, 1, "one vehicle"); H.eq(list.supportsWreckedState, false, "wrecked state honestly unsupported live")
    H.eq(list.vehicles[1].vehicleId, "Forklift_01", "vehicle id converted from FString")
    H.eq(list.vehicles[1].driveable, true, "driveable read")
    local id = list.vehicles[1].id

    -- vehicles.set: flip driveable off, and push it a meter.
    H.ok(H.dispatch("vehicles.set", { id = id, driveable = false }), "make it undriveable")
    H.eq(H.field(forklift, "VehicleDriveable"), false, "driveable written")
    H.eq(H.calls(forklift, "OnRep_VehicleDriveable"), 1, "OnRep pushed once")

    H.ok(H.dispatch("vehicles.set", { id = id, x = 51, y = 60, z = 70 }), "move it")
    local afterMove = H.ok(H.dispatch("vehicles.list")).vehicles[1]
    H.eq(afterMove.x, 51, "x moved"); H.eq(afterMove.y, 60, "y unchanged"); H.eq(afterMove.z, 70, "z unchanged")

    -- Missing vehicle id: player-safe failure, not a Lua error.
    H.fails(H.dispatch("vehicles.set", { id = "no-such-vehicle", driveable = true }), "not found", "unknown vehicle id fails cleanly")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(forklift)
    H.fails(H.dispatch("vehicles.set", { id = id, driveable = true }), "only the host", "client cannot edit vehicles")
end
