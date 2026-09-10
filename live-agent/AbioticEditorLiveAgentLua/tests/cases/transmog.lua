-- Live transmog visibility (areas/transmog.lua): the six visual-slot "hide this armor piece"
-- toggles, plus the round-78 bug fix (see that file's own header comment) that repaints the
-- game's own equipment UI immediately instead of waiting for the player to press the button.
return function(H)
    local pawn = H.hostSession()
    local tmog = pawn.TmogInventory

    local get = H.ok(H.dispatch("transmog.get"), "transmog.get")
    H.eq(#get.visibility, 6, "only the six visual slots are exposed")
    for i = 1, 6 do H.eq(get.visibility[i].isVisible, true, "slot " .. (i - 1) .. " starts visible (CDO default)") end

    H.ok(H.dispatch("transmog.set", { visibility = { { index = 0, isVisible = false }, { index = 5, isVisible = false } } }),
        "transmog.set hides chest and suit")
    H.eq(H.calls(tmog, "Request_ChangeTransmogVisibilityFlag"), 2, "the real RPC was called once per edited slot")

    local after = H.ok(H.dispatch("transmog.get")).visibility
    H.eq(after[1].isVisible, false, "chest (index 0) now hidden")
    H.eq(after[6].isVisible, false, "suit (index 5) now hidden")
    H.eq(after[2].isVisible, true, "head (index 1) untouched")

    -- Bug fix: a server never receives its own OnRep for a property it just wrote itself, so the
    -- in-game equipment UI stayed stale until the player pressed the toggle button by hand.
    -- transmog.set now forces that same repaint itself.
    H.eq(H.calls(tmog, "OnRep_TransmogVisibility"), 1, "the visibility OnRep is forced after writing, so the open equipment UI repaints without the player toggling it")

    -- Out-of-range indices (past the six visual slots) are ignored, not written.
    H.ok(H.dispatch("transmog.set", { visibility = { { index = 6, isVisible = false } } }), "an out-of-range index is silently ignored")
    H.eq(#H.ok(H.dispatch("transmog.get")).visibility, 6, "still only six slots reported")
end
