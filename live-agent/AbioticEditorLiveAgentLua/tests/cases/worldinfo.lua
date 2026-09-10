-- world.info (round 78): which region of the world is currently loaded, reusing spawn.get's own
-- evidenced ActiveLevelName read (see main.lua's handler comment for why this is not a new,
-- unverified GetWorld():GetMapName() call).
return function(H)
    H.hostSession()

    local info = H.ok(H.dispatch("world.info"), "world.info")
    H.eq(info.levelToken, "Facility", "current level token read from the local controller")
    H.eq(info.isHost, true, "host authority reported")

    -- Moving to a different streaming level (a region change) is just a different
    -- ActiveLevelName on the same controller - nothing else about world.info depends on it.
    H.playerController.ActiveLevelName = H.fname("Facility_MFWest")
    local moved = H.ok(H.dispatch("world.info"), "world.info after moving region")
    H.eq(moved.levelToken, "Facility_MFWest", "level token follows the controller")

    -- No local controller (main menu / not yet in a world): a nil token, not an error - the
    -- desktop app's sidebar falls back to its static region list in that case.
    H.playerController = nil
    local noController = H.ok(H.dispatch("world.info"), "world.info with no controller")
    H.eq(noController.levelToken, nil, "no level token without a controller")

    -- A joined client can still read this - unlike a world WRITE, reporting where the game is
    -- does not require authority.
    H.hostSession()
    H.clientSession()
    local clientInfo = H.ok(H.dispatch("world.info"), "world.info as a joined client")
    H.eq(clientInfo.levelToken, "Facility", "client still reads its own level token")
    H.eq(clientInfo.isHost, false, "client authority correctly reported false")
end
