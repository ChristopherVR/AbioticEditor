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
-- "Wrecked" (the save's Destroyed flag) has NO evidenced live property: the closest hit in the
-- whole probe dump is a LOCAL VARIABLE named Destroyed inside the vehicle's own UpdateWorldSave
-- function (computed from something at save time, not a class member), so there is nothing this
-- module can read or write for it - vehicles.lua does not report or accept it, and the shared
-- tab hides the "Wrecked" checkbox for a live session instead of guessing.
--
-- Move / reset-to-spawn is grounded in K2_TeleportTo(Location, Rotation), used verbatim in
-- CheatConsoleCommands' AFUtils/BaseUtils/BaseUtils.lua (TeleportActorToActor) with the actor's
-- own K2_GetActorRotation() kept unchanged, exactly as done here.
return function(ctx)
    local function vehicleRows()
        local result = { __forceArray = true }
        for _, obj in ipairs(ctx.findAll("ABF_Vehicle_ParentBP_C")) do
            if obj:IsValid() then
                local name = ctx.fullName(obj)
                if name then
                    local x, y, z = ctx.actorLocation(obj)
                    local okId, vehicleId = pcall(function() return obj.VehicleID end)
                    local okDrive, driveable = pcall(function() return obj.VehicleDriveable == true end)
                    table.insert(result, {
                        id = name,
                        vehicleId = (okId and vehicleId ~= "") and vehicleId or nil,
                        vehicleClass = ctx.classLabel(name),
                        driveable = okDrive and driveable or false,
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["vehicles.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { vehicles = vehicleRows(), isHost = ctx.isHost(), supportsWreckedState = false }
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
            if payload.x ~= nil and payload.y ~= nil and payload.z ~= nil then
                local okRot, rotation = pcall(function() return obj:K2_GetActorRotation() end)
                local target = FVector(payload.x, payload.y, payload.z)
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
