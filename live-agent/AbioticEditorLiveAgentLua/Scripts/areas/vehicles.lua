-- ===== Vehicles (ABF_Vehicle_ParentBP_C, super=AbioticWheeledVehiclePawn) =====
-- Round 76. Confirmed from the game's own pak layout (LiveClassPropsProbe, fragment
-- "ABF_Vehicle"):
--   VehicleDriveable : FBoolProperty, with OnRep_VehicleDriveable() -- direct write + OnRep,
--     the exact shape every other confirmed live area already uses (vitals, doors, NPCs).
--   VehicleID : FStrProperty, GetVehicleID() -- matches the save's VehicleID_ field.
--   GetVehicleContainers(), GetDriver()/GetLocalDriver()/GetCurrentDriver() -- not used here.
-- No installed mod touches vehicles at all (nothing to cross-check against), so
-- OnRep_VehicleDriveable is called via pcall, same treatment flags.set gives a call with no
-- mod precedent.
--
-- "Wrecked" (round 77, closing the round-76 gap): UpdateWorldSave's own local "Destroyed"
-- variable is fed by a REAL class property, confirmed this round from the same class dump -
-- ABF_Vehicle_ParentBP_C carries:
--   PendingDestroy : FBoolProperty   -- no hash suffix, a genuine class member (not a local)
--   func ReceiveDestroyed(Destroyed: bool)  -- the engine's own AActor::Destroyed() event
--     override; a callback the engine calls automatically when this actor is torn down, not
--     something this module should invoke itself as a setter (calling it out of turn could run
--     destruction-side gameplay logic no mod has ever exercised).
-- PendingDestroy is written directly here, the same "no confirmed OnRep, write the field, done"
-- pattern bases.lua's AlternativeObjectName rename already uses (there is no
-- "OnRep_PendingDestroy"/"OnRep_Destroyed" anywhere in this class's function list either). This
-- is grounded in the game's own layout, but genuinely UNVERIFIED against the running game: no
-- mod anywhere reads or writes PendingDestroy, and whether flipping it alone updates the vehicle's
-- wreck VISUALS (mesh/FX) live, or only the value UpdateWorldSave later persists, is unknown
-- without launching the game. vehicles.list reports it back read-only-safe (a straight property
-- read) so a caller can at least confirm the write stuck.
--
-- Move / reset-to-spawn is grounded in K2_TeleportTo(Location, Rotation), used verbatim in
-- CheatConsoleCommands' AFUtils/BaseUtils/BaseUtils.lua (TeleportActorToActor) with the actor's
-- own K2_GetActorRotation() kept unchanged, exactly as done here.
--
-- On-board storage (coordinator round, closing the "vehicle storage not editable live" gap): the
-- game's own class layout was re-probed (fragment "ABF_Vehicle_ParentBP") and this is NOT a
-- guess. ABF_Vehicle_ParentBP_C carries `StorageContainer : FObjectProperty` (a plain
-- ChildActorComponent, unsuffixed, confirmed from the pak class dump) and a BlueprintPure
-- GetVehicleContainers() whose own bytecode does exactly this: dynamic-cast
-- StorageContainer.ChildActor to Deployed_Container_ParentBP_C and read its ContainerInventory.
-- The forklift's own placed child-actor default is Deployed_Container_ForkliftCargo_C (confirmed
-- from the BP export's ChildActorTemplate), which chains
-- Deployed_Container_ForkliftCargo_C -> Deployed_Container_Cargo_C -> Deployed_Container_ParentBP_C
-- - the exact class containers.lua's own CONTAINER_CLASSES sweep already scans for. That means a
-- vehicle's cargo hold is a genuine, independently-loaded Deployed_Container_ParentBP_C actor, not
-- a bespoke vehicle-only structure: it is already listed, read and written by the existing
-- containers.list/containers.get/containers.set handlers with no changes there at all. This module
-- only needs to report which container id belongs to which vehicle, so vehicles.list's
-- hasInventory/inventoryItemCount/containerId are real instead of hardcoded, and the app can jump
-- straight to that id in the CONTAINERS tab (one slot-edit code path, not a second one here).
-- ctx.containerInventory is the same GetContainerInventory()-preferring helper containers.lua uses
-- for every other container (handles a class override the same way, though none is known for
-- vehicle cargo); ctx.slotRowName is the same "" / "Empty" / "None" sentinel check slotRow uses.
-- Feature-detected per vehicle via pcall: a vehicle type with no StorageContainer child actor
-- resolved (cast fails, e.g. no cargo model wired up) reports hasInventory=false/containerId=nil
-- rather than erroring the whole list.
return function(ctx)
    local function vehicleContainer(obj)
        local okComp, comp = pcall(function() return obj.StorageContainer end)
        if not okComp or not comp or not comp:IsValid() then return nil end
        local okChild, child = pcall(function() return comp.ChildActor end)
        if not okChild or not child or not child:IsValid() then return nil end
        return child
    end

    local function vehicleStorage(obj)
        local ok, containerId, hasInventory, itemCount = pcall(function()
            local container = vehicleContainer(obj)
            if not container then return nil, false, 0 end
            local cid = ctx.fullName(container)
            if not cid then return nil, false, 0 end
            local count = 0
            local inv = ctx.containerInventory(container)
            if inv and inv.CurrentInventory then
                for i = 1, #inv.CurrentInventory do
                    local okRow, rowName = pcall(ctx.slotRowName, inv.CurrentInventory[i])
                    if okRow and rowName ~= "" and rowName ~= "Empty" and rowName ~= "None" then
                        count = count + 1
                    end
                end
            end
            return cid, true, count
        end)
        if not ok then return nil, false, 0 end
        return containerId, hasInventory, itemCount
    end

    local function vehicleRows()
        local result = { __forceArray = true }
        for _, obj in ipairs(ctx.findAll("ABF_Vehicle_ParentBP_C")) do
            if obj:IsValid() then
                local name = ctx.fullName(obj)
                if name then
                    local x, y, z = ctx.actorLocation(obj)
                    -- VehicleID is an FString userdata - it must be converted, or json.encode
                    -- rejects the whole reply (found live, round 76).
                    local okId, vehicleId = pcall(function() return obj.VehicleID:ToString() end)
                    local okDrive, driveable = pcall(function() return obj.VehicleDriveable == true end)
                    local okWrecked, wrecked = pcall(function() return obj.PendingDestroy == true end)
                    local containerId, hasInventory, inventoryItemCount = vehicleStorage(obj)
                    table.insert(result, {
                        id = name,
                        vehicleId = (okId and vehicleId ~= "") and vehicleId or nil,
                        vehicleClass = ctx.classLabel(name),
                        driveable = okDrive and driveable or false,
                        wrecked = okWrecked and wrecked or false,
                        x = x, y = y, z = z,
                        containerId = containerId,
                        hasInventory = hasInventory,
                        inventoryItemCount = inventoryItemCount,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["vehicles.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { vehicles = vehicleRows(), isHost = ctx.isHost(), supportsWreckedState = true }
        end, respond)
    end

    -- Host-only, matching doors.set/containers.set: a vehicle is shared world state, not
    -- something owned by whichever client happens to be sitting in the editor.
    ctx.handlers["vehicles.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change vehicles") end
            local obj = payload.id and ctx.findByFullName("ABF_Vehicle_ParentBP_C", payload.id)
            if not obj then error("vehicle not found (it may have been unloaded or destroyed)") end
            if payload.driveable ~= nil then
                obj.VehicleDriveable = payload.driveable
                pcall(function() obj:OnRep_VehicleDriveable() end)
            end
            if payload.wrecked ~= nil then
                -- No confirmed OnRep for this field (see header comment) - direct write only.
                local ok = pcall(function() obj.PendingDestroy = payload.wrecked end)
                if not ok then error("could not set this vehicle's wrecked state on this game build") end
            end
            if payload.x ~= nil and payload.y ~= nil and payload.z ~= nil then
                local okRot, rotation = pcall(function() return obj:K2_GetActorRotation() end)
                -- FVector is not a UE4SS Lua global (found live, round 76); a plain X/Y/Z table
                -- is what UE4SS accepts for a struct parameter.
                local target = { X = payload.x, Y = payload.y, Z = payload.z }
                if okRot then
                    obj:K2_TeleportTo(target, rotation)
                else
                    -- Falls back to a zero rotator only if the actor's own rotation could not be
                    -- read at all; keeps the vehicle where it is otherwise.
                    obj:K2_TeleportTo(target, { Pitch = 0, Yaw = 0, Roll = 0 })
                end
            end
            return nil
        end, respond)
    end
end
