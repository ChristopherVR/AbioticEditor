-- Round 77 player-side live-editing gaps: transmog visibility (a real RPC pair found on the
-- SAME transmog inventory component inventory.list/set already touches) and general.get/set's
-- new BACKGROUND (a real direct-field write) and TRAITS (read-only, with evidence) fields.
-- See areas/transmog.lua and areas/general.lua's header comments for the pak/PDB/mod evidence.
return function(H)
    local pawn = H.hostSession()

    -- ---------- transmog.get / transmog.set ----------

    local visibility = H.ok(H.dispatch("transmog.get"), "transmog.get")
    H.eq(#visibility.visibility, 6, "six visible-slot flags")
    H.eq(visibility.visibility[1].isVisible, true, "chest starts visible")

    H.ok(H.dispatch("transmog.set", { visibility = { { index = 0, isVisible = false } } }), "transmog.set")
    H.eq(H.field(pawn.TmogInventory, "TransmogVisibility")[1], false, "visibility flag flipped in place")
    H.eq(H.calls(pawn.TmogInventory, "Request_ChangeTransmogVisibilityFlag"), 1, "Request_ChangeTransmogVisibilityFlag called once")

    local afterSet = H.ok(H.dispatch("transmog.get"), "transmog.get after set")
    H.eq(afterSet.visibility[1].isVisible, false, "transmog.get reflects the write")
    -- Only indices 0-5 are ever touched - index 6+ (the DisableTransmogArray-length tail) is
    -- rejected outright rather than silently written past the exposed range.
    H.ok(H.dispatch("transmog.set", { visibility = { { index = 11, isVisible = false } } }), "out-of-range index is ignored, not an error")
    H.eq(H.calls(pawn.TmogInventory, "Request_ChangeTransmogVisibilityFlag"), 1, "out-of-range index never reaches the RPC")

    H.fails(H.dispatch("transmog.get", { playerId = "no-such-player" }), "no transmog inventory component found",
        "wrong playerId is rejected for transmog.get")
    H.fails(H.dispatch("transmog.set", { playerId = "no-such-player", visibility = { { index = 0, isVisible = true } } }),
        "no transmog inventory component found", "wrong playerId is rejected for transmog.set")

    -- ---------- general.get / general.set: background (PhD) and traits ----------

    local general1 = H.ok(H.dispatch("general.get"), "general.get")
    H.eq(general1.background, "PhD_HumanBio", "background read from PlayerState.PhD")
    H.eq(#general1.traits, 1, "one trait")
    H.eq(general1.traits[1], "Trait_Chef", "trait name converted from FName")

    H.ok(H.dispatch("general.set", { background = "PhD_Medicine" }), "general.set background")
    H.eq(H.field(pawn.PlayerState, "PhD"):ToString(), "PhD_Medicine", "PhD written directly (no OnRep exists)")
    H.eq(H.ok(H.dispatch("general.get")).background, "PhD_Medicine", "general.get reflects the write")

    -- Traits is read-only: a general.set carrying traits must not raise AND must not touch the
    -- component's Traits array - see general.lua's header comment for why no write path exists.
    H.ok(H.dispatch("general.set", { traits = { "Trait_Strong" } }), "general.set ignores a traits payload")
    H.eq(#H.field(pawn.CharacterProgressionComponent, "Traits"), 1, "Traits array untouched")
    H.eq(H.field(pawn.CharacterProgressionComponent, "Traits")[1]:ToString(), "Trait_Chef", "still the original trait")

    H.fails(H.dispatch("general.get", { playerId = "no-such-player" }), "no CharacterProgressionComponent found",
        "wrong playerId is rejected for general.get")
    H.fails(H.dispatch("general.set", { playerId = "no-such-player", background = "PhD_Iron" }),
        "no CharacterProgressionComponent found", "wrong playerId is rejected for general.set")
end
