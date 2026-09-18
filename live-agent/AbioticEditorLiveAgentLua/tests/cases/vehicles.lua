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
    H.eq(#list.vehicles, 1, "one vehicle"); H.eq(list.supportsWreckedState, true, "wrecked state supported live (round 77 PendingDestroy path)")
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

    -- On-board storage (coordinator round): no StorageContainer child actor resolved (the
    -- forklift fake above never set one) degrades to no-storage instead of erroring.
    local noStorage = H.ok(H.dispatch("vehicles.list")).vehicles[1]
    H.eq(noStorage.hasInventory, false, "no StorageContainer resolved -> hasInventory false")
    H.eq(noStorage.inventoryItemCount, 0, "no StorageContainer resolved -> item count 0")
    H.check(noStorage.containerId == nil, "no StorageContainer resolved -> no container id")

    -- On-board storage, positive path: StorageContainer.ChildActor is a real
    -- Deployed_Container_ParentBP_C instance (the forklift's own placed default is
    -- Deployed_Container_ForkliftCargo_C -> Deployed_Container_Cargo_C ->
    -- Deployed_Container_ParentBP_C, confirmed from the game's own BP export's ChildActorTemplate)
    -- - the exact class containers.lua's own sweep already scans for, so this proves vehicle cargo
    -- needs no dedicated slot-edit path of its own: one write handler (containers.set) covers it.
    local netHelper = H.object("NetPushModelHelpers", {}, { MarkPropertyDirty = function() end })
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", netHelper)
    local cargoInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("scrap_metal") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 3,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
    } }, { OnRep_CurrentInventory = function() end })
    local cargoActor = H.world.add(H.object("Deployed_Container_ForkliftCargo_C",
        { __bases = { "Deployed_Container_ParentBP_C" }, ContainerInventory = cargoInv },
        { K2_GetActorLocation = function() return H.vector(52, 60, 70) end }))
    forklift.StorageContainer = H.object("ChildActorComponent", { ChildActor = cargoActor }, {})

    local withStorage = H.ok(H.dispatch("vehicles.list")).vehicles[1]
    H.eq(withStorage.hasInventory, true, "StorageContainer.ChildActor resolved -> hasInventory true")
    H.eq(withStorage.inventoryItemCount, 1, "one non-empty cargo slot counted, the empty one skipped")
    H.check(withStorage.containerId ~= nil, "cargo container id reported")

    local containerList = H.ok(H.dispatch("containers.list"), "containers.list already sees vehicle cargo")
    local sawCargo = false
    for _, entry in ipairs(containerList.containers) do
        if entry.id == withStorage.containerId then sawCargo = true end
    end
    H.check(sawCargo, "vehicle cargo container is discoverable through the generic container sweep, unchanged")
    H.ok(H.dispatch("containers.set", { id = withStorage.containerId,
        edits = { { slotIndex = 1, itemId = "bandage", stack = 2 } } }),
        "containers.set writes into vehicle cargo through the ordinary handler")
    H.eq(H.calls(cargoInv, "OnRep_CurrentInventory"), 1, "container OnRep pushed after the write")
    local afterWrite = H.ok(H.dispatch("vehicles.list")).vehicles[1]
    H.eq(afterWrite.inventoryItemCount, 2, "vehicles.list reflects the container write with no vehicle-specific code")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(forklift)
    H.fails(H.dispatch("vehicles.set", { id = id, driveable = true }), "only the host", "client cannot edit vehicles")
end
