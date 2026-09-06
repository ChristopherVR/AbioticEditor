-- worldunlocks.get / worldunlocks.set (round 77): world-wide (not per-player) unlock lists on
-- Abiotic_Survival_GameState_C. Read is real; set always fails - see areas/worldunlocks.lua's
-- header comment for exactly why there is no write path.
return function(H)
    H.hostSession()

    local unlocks = H.ok(H.dispatch("worldunlocks.get"), "worldunlocks.get")
    H.eq(unlocks.isHost, true, "host authority reported")
    H.eq(#unlocks.recipesUnlocked, 1, "one world recipe unlocked"); H.eq(unlocks.recipesUnlocked[1], "recipe_bandage", "recipe row")
    H.eq(#unlocks.recipesResearched, 0, "no world recipes researched")
    H.eq(#unlocks.itemsPickedUp, 1, "one world item picked up"); H.eq(unlocks.itemsPickedUp[1], "scrap_metal", "item row")
    H.eq(#unlocks.emailsRead, 1, "one world email read")
    H.eq(#unlocks.journalEntries, 0, "no world journal entries")
    H.eq(#unlocks.compendiumEmail, 0, "no world compendium email entries")
    H.eq(#unlocks.compendiumNarrative, 0, "no world compendium narrative entries")
    H.eq(#unlocks.compendiumExploration, 1, "one world compendium exploration entry")

    -- No grounded write path exists at all - always ok:false with a player-safe explanation,
    -- exactly like story.set.
    H.fails(H.dispatch("worldunlocks.set", {}), "cannot be changed from outside the game", "worldunlocks.set always refused")

    -- Reading still works without a loaded world's game state being reachable... except it isn't:
    -- worldunlocks.get requires GetGameStateBase() to return something valid. Simulate "no world
    -- loaded" the same way story.lua's own tests would (gameState missing).
    H.gameState = nil
    H.fails(H.dispatch("worldunlocks.get"), "the world is not loaded", "worldunlocks.get refuses with no game state")
end
