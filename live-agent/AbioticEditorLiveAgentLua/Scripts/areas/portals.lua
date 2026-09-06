-- Live editing area: fixed world teleporters ("World Teleporters" / PortalMap, round 76).
--
-- Class layout confirmed from the game's own blueprint (tests/AbioticEditor.Probes/
-- LiveClassPropsProbe.cs, "BP_Teleporter_ParentBP" fragment): BP_Teleporter_ParentBP_C carries
-- IsTeleporterActive (FBoolProperty - the live twin of the save's PortalActive_ leaf, see
-- Core/WorldSaves/Features/PortalMapFeature.cs), MyTeleporterID/DestinationTeleporterID (FName),
-- OnRep_IsTeleporterActive(), and SavePortalState(ForceWorldSave) - its own "persist this now"
-- function. No installed mod references this actor class or any of these members (checked:
-- CheatConsoleCommands has no "teleporter"/"portal" command beyond player-to-player/location
-- teleport, which is a completely different system), so every write here is pcall-guarded and
-- this is the first live exercise of this exact path.
return function(ctx)
    local TELEPORTER_CLASS = "BP_Teleporter_ParentBP_C"

    local function teleporterRows()
        local result = { __forceArray = true }
        for _, teleporter in ipairs(ctx.findAll(TELEPORTER_CLASS)) do
            if teleporter:IsValid() then
                local name = ctx.fullName(teleporter)
                if name then
                    local x, y, z = ctx.actorLocation(teleporter)
                    local okActive, active = pcall(function() return teleporter.IsTeleporterActive == true end)
                    local okMy, myId = pcall(function() return teleporter.MyTeleporterID:ToString() end)
                    local okDest, destId = pcall(function() return teleporter.DestinationTeleporterID:ToString() end)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name),
                        active = okActive and active or false,
                        teleporterId = okMy and myId or "",
                        destinationId = okDest and destId or "",
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["portals.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { portals = teleporterRows(), isHost = ctx.isHost() }
        end, respond)
    end

    ctx.handlers["portals.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change world teleporters") end
            local rows = payload.portals or {}
            for i = 1, #rows do
                local row = rows[i]
                local teleporter = row.id and ctx.findByFullName(TELEPORTER_CLASS, row.id)
                if teleporter and row.active ~= nil then
                    teleporter.IsTeleporterActive = row.active
                    -- No mod precedent for either call on this actor (see file header); direct
                    -- write + OnRep is the same shape doors/NPCs already use elsewhere in this
                    -- mod, and SavePortalState is the blueprint's own "persist this now" function.
                    pcall(function() teleporter:OnRep_IsTeleporterActive() end)
                    pcall(function() teleporter:SavePortalState(true) end)
                end
            end
            return nil
        end, respond)
    end
end
