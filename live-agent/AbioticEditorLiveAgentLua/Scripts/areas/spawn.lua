-- Player spawn / position (round 76): shows the connected character's actual current position
-- and lets one explicit button move them there, plus lets a different explicit button claim a
-- different punch-card terminal as the respawn point. Nothing here ever moves anyone by itself -
-- editing spawn.get's numbers only happens on the .NET side (see LivePlayerSpawnSession); this
-- mod only ever acts on spawn.set, and only when it names a teleport or a terminal explicitly.
--
-- TeleportPlayer is the reference mod's own call, used verbatim: AFUtils.TeleportPlayerToPlayer
-- (AFUtils/AFUtils.lua ~line 641, `Player:TeleportPlayer(location, rotation, true, false)`) and
-- LocationsManager.LoadLocation (`myPlayer:TeleportPlayer(location.Location, FRotator(), true,
-- false)`) both call it this way. K2_GetActorLocation/K2_GetActorRotation are the same mod's own
-- position/rotation reads (LocationsManager.lua:81, AFUtils.lua:641-642).
--
-- TerminalRespawnID has NO reference-mod precedent: it was found directly in the game's own
-- class layout (Abiotic_PlayerController_C's own plain FName property, confirmed by
-- tests/AbioticEditor.Probes/LiveClassPropsProbe.cs - not hash-suffixed, a real native-style
-- blueprint variable, matching the exact GUID strings Core/Catalogs/Player/RespawnTerminalCatalog.cs
-- already keys respawn terminals by). Every access to it is pcall-guarded.
return function(ctx)
    local UEHelpers = ctx.UEHelpers

    -- Only the LOCAL player's controller is reachable this way (UEHelpers.GetPlayerController()
    -- has no "for a different connected player" form) - so unlike vitals/skills/inventory, a
    -- payload.playerId naming a DIFFERENT connected player can still read/move that player's
    -- pawn (ctx.resolvePlayer handles that), but can never claim a respawn terminal for them -
    -- only for whoever this mod is actually running as.
    local function myController()
        local ok, controller = pcall(function() return UEHelpers.GetPlayerController() end)
        if ok and controller and controller:IsValid() then return controller end
        return nil
    end

    ctx.handlers["spawn.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local player = ctx.resolvePlayer(payload)
            if not player then error("player not found") end
            local x, y, z = ctx.actorLocation(player)

            local levelName, terminalGuid
            local controller = myController()
            if controller then
                local okLevel, level = pcall(function() return controller.ActiveLevelName:ToString() end)
                if okLevel and level ~= "" then levelName = level end
                local okTerm, term = pcall(function() return controller.TerminalRespawnID:ToString() end)
                if okTerm and term ~= "" and term ~= "None" then terminalGuid = term end
            end

            return { x = x, y = y, z = z, levelName = levelName, terminalGuid = terminalGuid, isHost = ctx.isHost() }
        end, respond)
    end

    ctx.handlers["spawn.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local player = ctx.resolvePlayer(payload)
            if not player then error("player not found") end

            if payload.teleport then
                -- There is no FRotator constructor in UE4SS Lua (found live, round 76); keep the
                -- player's own rotation (K2_GetActorRotation, used the same way by the reference
                -- mod's TeleportPlayerToPlayer) and fall back to a plain table, which UE4SS
                -- accepts for a struct parameter.
                local rotation = { Pitch = 0, Yaw = 0, Roll = 0 }
                local okRotation, currentRotation = pcall(function() return player:K2_GetActorRotation() end)
                if okRotation and currentRotation then rotation = currentRotation end
                -- FVector is not a UE4SS global either. The reference mod's own teleport
                -- (AFUtils.TeleportPlayerToPlayer) takes the vector the game hands back from
                -- K2_GetActorLocation and overwrites its X/Y/Z fields - copy that exactly, since a
                -- plain X/Y/Z table was accepted by the native K2_TeleportTo (vehicles) but not by
                -- this blueprint function (found live, round 76).
                local okVector, target = pcall(function()
                    local vector = player:K2_GetActorLocation()
                    vector.X = payload.teleport.x
                    vector.Y = payload.teleport.y
                    vector.Z = payload.teleport.z
                    return vector
                end)
                if not okVector then target = { X = payload.teleport.x, Y = payload.teleport.y, Z = payload.teleport.z } end
                -- K2_TeleportTo is the native AActor teleport the reference mod uses for any actor
                -- (BaseUtils.TeleportActorToActor) and the one this protocol's vehicle move already
                -- proved live. The blueprint TeleportPlayer takes five parameters on the current
                -- game build (DestLocation, DestRotation, Force, SkipAdjustment, ExitChairs) and
                -- UE4SS refused the reference mod's four-argument form, so it is only the fallback.
                local okCall, success = pcall(function() return player:K2_TeleportTo(target, rotation) end)
                if not okCall or not success then
                    local okBp, bpSuccess = pcall(function() return player:TeleportPlayer(target, rotation, true, false, true) end)
                    if not okBp then
                        error("teleport failed: " .. tostring(success) .. " / " .. tostring(bpSuccess))
                    elseif not bpSuccess then
                        error("teleport failed (the destination may be blocked, or outside the loaded world)")
                    end
                end
            end

            if payload.terminalGuid then
                local controller = myController()
                if not controller then error("no local player controller (are you in a world?)") end
                local okSet = pcall(function()
                    controller.TerminalRespawnID = FName(payload.terminalGuid, EFindName.FNAME_Find)
                end)
                if not okSet then error("could not set the respawn terminal") end
            end

            return nil
        end, respond)
    end
end
