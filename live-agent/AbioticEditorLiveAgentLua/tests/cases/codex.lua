-- codex.get / codex.set: EMAIL/NOTES/FISH mark-known, plus the round-77 COMPENDIUM unlock (round
-- 76 shipped this area read-only for compendium; round 77 grounded the enum - see areas/codex.lua).
return function(H)
    local pawn = H.hostSession()
    local progression = H.field(pawn, "CharacterProgressionComponent")

    -- codex.get reads the per-player arrays, including the three Compendium_*Sections lists
    -- merged (deduplicated) into one "compendium" field.
    local codex = H.ok(H.dispatch("codex.get"), "codex.get")
    H.eq(#codex.emails, 1, "one email known"); H.eq(codex.emails[1], "Email_Crossbow", "email row")
    H.eq(#codex.journals, 0, "no journals known")
    H.eq(#codex.fish, 0, "no fish known")
    H.eq(#codex.compendium, 1, "one compendium row known (from Compendium_ExplorationSections)")
    H.eq(codex.compendium[1], "Compendium_Office", "compendium row read from the per-category array")

    -- codex.set: email/journal/fish unlock exactly like round 76.
    H.ok(H.dispatch("codex.set", { emails = { "Email_Radio" } }), "codex.set emails")
    H.eq(H.calls(progression, "Server_AddEmailToReadList"), 1, "email RPC called once")
    H.ok(H.dispatch("codex.set", { journals = { "Journal_Sink" } }), "codex.set journals")
    H.eq(H.calls(progression, "Server_AddNoteToJournal"), 1, "journal RPC called once")
    H.ok(H.dispatch("codex.set", { fish = { "Fish_Anchovy" } }), "codex.set fish")
    H.eq(H.calls(progression, "Request_UnlockNewFish"), 1, "fish RPC called once")

    -- codex.set: compendium now accepts {row, sectionType} pairs, grounded via the usmap's own
    -- ECompendiumUnlockType enum (Exploration=0, Email=1, NarrativeNPC=2).
    H.ok(H.dispatch("codex.set", { compendium = {
        { row = "Compendium_Radio", sectionType = "Exploration" },
        { row = "Compendium_Carson", sectionType = "NarrativeNPC" },
    } }), "codex.set compendium")
    H.eq(H.calls(progression, "Request_UnlockCompendiumSection"), 2, "compendium RPC called twice")

    -- A string sectionType the enum table doesn't know, or a KilLRequirement/MAX index, is
    -- silently skipped rather than sending garbage to the game.
    H.ok(H.dispatch("codex.set", { compendium = {
        { row = "Compendium_Bad", sectionType = "NotARealType" },
        { row = "Compendium_Kill", sectionType = 3 },
    } }), "codex.set ignores unmapped section types")
    H.eq(H.calls(progression, "Request_UnlockCompendiumSection"), 2, "no additional RPC calls for unmapped types")

    -- A joined client can still read (codex data is per-player, read is never host-gated), but
    -- cannot be tested for "cannot write" here since codex.set has no host check (see area
    -- comment: it targets whichever player the request names, gated by resolvePlayer honoring the
    -- game's own authority through the RPCs themselves, same as recipes.set/general.set).
    H.clientSession()
    H.ok(H.dispatch("codex.get"), "codex.get still readable from a client")
end
