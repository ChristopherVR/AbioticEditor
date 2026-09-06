-- Player spawn / position (areas/spawn.lua). Teleport goes through the native K2_TeleportTo (a
-- plain X/Y/Z table, not an FVector() constructor - there is no such UE4SS global); the
-- respawn-terminal claim writes the LOCAL controller's TerminalRespawnID (FName) regardless of
-- which player a request names, since UEHelpers has no "get that OTHER player's controller" form.
return function(H)
    local pawn = H.hostSession()

    -- spawn.get: position plus the local controller's level/terminal.
    local got = H.ok(H.dispatch("spawn.get"), "spawn.get")
    H.eq(got.x, 100, "x read"); H.eq(got.y, 200, "y read"); H.eq(got.z, 300, "z read")
    H.eq(got.levelName, "Facility", "level name read")
    H.eq(got.terminalGuid, "E57CB02C4853F46D2BB7CA80303EB6A3", "respawn terminal guid read")
    H.eq(got.isHost, true, "host authority reported")

    -- spawn.set teleport: K2_TeleportTo with plain tables, not FVector()/FRotator() (neither
    -- exists as a UE4SS global - see harness.lua's own comment on this).
    H.ok(H.dispatch("spawn.set", { teleport = { x = 111, y = 222, z = 333 } }), "teleport")
    local afterTeleport = H.ok(H.dispatch("spawn.get"))
    H.eq(afterTeleport.x, 111, "x moved"); H.eq(afterTeleport.y, 222, "y moved"); H.eq(afterTeleport.z, 333, "z moved")

    -- spawn.set terminal claim: writes TerminalRespawnID via FName(str, EFindName.FNAME_Find).
    H.ok(H.dispatch("spawn.set", { terminalGuid = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }), "claim terminal")
    H.eq(H.field(H.playerController, "TerminalRespawnID"):ToString(), "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "terminal id written")

    -- A second connected player: spawn.get/set with playerId reads and moves THAT pawn, but a
    -- terminal claim always lands on the LOCAL controller regardless of which playerId was named
    -- (there is no live path to claim a terminal for someone else's controller).
    local otherPawn = H.object("Abiotic_PlayerCharacter_C", {}, {
        K2_GetActorLocation = function() return H.vector(9, 8, 7) end,
        K2_GetActorRotation = function() return H.rotator(0, 0, 0) end,
        K2_TeleportTo = function(self, location) rawget(self, "__methods").K2_GetActorLocation = function() return { X = location.X, Y = location.Y, Z = location.Z } end return true end,
    })
    local otherState = H.object("Abiotic_PlayerState_C", { PawnPrivate = otherPawn, PlayerNamePrivate = H.fstring("Guest"), UniquePlayerID = H.fstring("999") })
    table.insert(H.playerStates, otherState)
    H.world.add(otherPawn)

    local otherGet = H.ok(H.dispatch("spawn.get", { playerId = "999" }), "spawn.get for the other player")
    H.eq(otherGet.x, 9, "other player's own position read, not the local one")
    H.ok(H.dispatch("spawn.set", { playerId = "999", teleport = { x = 1, y = 2, z = 3 } }), "teleport the other player")
    H.eq(H.ok(H.dispatch("spawn.get", { playerId = "999" })).x, 1, "other player actually moved")
    H.eq(H.ok(H.dispatch("spawn.get")).x, 111, "the local player's own position is untouched")

    H.ok(H.dispatch("spawn.set", { playerId = "999", terminalGuid = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" }), "claim terminal while targeting another player")
    H.eq(H.field(H.playerController, "TerminalRespawnID"):ToString(), "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
        "the terminal claim still landed on the LOCAL controller, not the named player")

    -- Unknown player id: player-safe failure, not a Lua error.
    H.fails(H.dispatch("spawn.get", { playerId = "no-such-player" }), "player not found", "unknown playerId fails cleanly on spawn.get")
    H.fails(H.dispatch("spawn.set", { playerId = "no-such-player", teleport = { x = 0, y = 0, z = 0 } }), "player not found", "unknown playerId fails cleanly on spawn.set")
end
