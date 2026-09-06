-- Fixed world teleporters (areas/portals.lua). Field/function names off the class dump:
-- IsTeleporterActive (bool), MyTeleporterID/DestinationTeleporterID (FName), OnRep_IsTeleporterActive,
-- SavePortalState(ForceWorldSave).
return function(H)
    H.hostSession()

    local function teleporter(myId, destId, active)
        return H.world.add(H.object("BP_Teleporter_ParentBP_C", {
            IsTeleporterActive = active,
            MyTeleporterID = H.fname(myId),
            DestinationTeleporterID = H.fname(destId),
        }, {
            OnRep_IsTeleporterActive = function() end,
            SavePortalState = function() end,
            K2_GetActorLocation = function() return H.vector(1, 1, 1) end,
        }))
    end
    local padA = teleporter("PadA", "PadB", true)
    local padB = teleporter("PadB", "PadA", false)

    local list = H.ok(H.dispatch("portals.list"), "portals.list")
    H.eq(#list.portals, 2, "two portals")
    H.eq(list.portals[1].active, true, "pad A active")
    H.eq(list.portals[1].teleporterId, "PadA", "pad A's own id converted from FName")
    H.eq(list.portals[1].destinationId, "PadB", "pad A's destination id converted from FName")
    H.eq(list.portals[2].active, false, "pad B inactive")

    -- portals.set: flip pad B on, leave pad A untouched.
    H.ok(H.dispatch("portals.set", { portals = { { id = list.portals[2].id, active = true } } }), "activate pad B")
    H.eq(H.field(padB, "IsTeleporterActive"), true, "pad B activated")
    H.eq(H.calls(padB, "OnRep_IsTeleporterActive"), 1, "OnRep pushed once")
    H.eq(H.calls(padB, "SavePortalState"), 1, "SavePortalState called once")
    H.eq(H.field(padA, "IsTeleporterActive"), true, "pad A left alone")

    -- Missing teleporter id: player-safe failure, not a Lua error - and any resolvable rows in
    -- the same call still apply first.
    H.ok(H.dispatch("portals.set", { portals = { { id = list.portals[1].id, active = false } } }), "deactivate pad A")
    local reply = H.dispatch("portals.set", { portals = {
        { id = list.portals[1].id, active = true },
        { id = "no-such-teleporter", active = true },
    } })
    H.fails(reply, "not found", "unknown teleporter id fails cleanly")
    H.eq(H.field(padA, "IsTeleporterActive"), true, "the resolvable row in the same batch still applied")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(padA)
    H.fails(H.dispatch("portals.set", { portals = { { id = list.portals[1].id, active = false } } }), "only the host", "client cannot edit teleporters")
end
