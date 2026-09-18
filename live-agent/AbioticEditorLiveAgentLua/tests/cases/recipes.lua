-- recipes.get / recipes.set: per-player recipe unlock, plus relock (round 106 grounding pass).
-- Unlock always works through Request_UnlockNewRecipe (any client, no host check - the RPC
-- targets the caller's own character). Relock replaces RecipesUnlockedArray directly and invokes
-- OnRep_RecipesUnlockedArray, so it needs host authority (only the server's own authoritative copy
-- of a replicated property is what actually persists/propagates) plus replication-notification
-- support - see areas/recipes.lua's header comment. Grounded against the round-106 pak dump
-- (pass2\Abiotic_CharacterProgressionComponent.json / layouts.txt): RecipesUnlockedArray is a
-- plain FArrayProperty, OnRep_RecipesUnlockedArray is a real exported function, and no dedicated
-- "forget"/"lock"/"remove recipe" RPC exists anywhere on this class - array-replace is the only
-- path the game exposes, so canLock is already as widely true as the game honestly allows.
return function(H)
    -- ---------- host, no replication support: unlock works, relock refused ----------
    local pawn = H.hostSession()
    local progression = H.field(pawn, "CharacterProgressionComponent")

    local recipes = H.ok(H.dispatch("recipes.get"), "recipes.get")
    H.eq(#recipes.unlockedIds, 1, "one recipe unlocked"); H.eq(recipes.unlockedIds[1], "recipe_bandage", "recipe row")
    H.eq(recipes.canLock, false, "no replication notification support yet")

    H.ok(H.dispatch("recipes.set", { unlockIds = { "recipe_hatchet" } }), "recipes.set unlock (no replication support)")
    H.eq(H.calls(progression, "Request_UnlockNewRecipe"), 1, "unlock RPC called once")

    H.fails(H.dispatch("recipes.set", { lockIds = { "recipe_bandage" } }),
        "relocking recipes requires host authority and replication notification support",
        "relock refused without replication support")

    -- ---------- host, with replication support: relock is a real array-replace + OnRep ----------
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))
    local withReplication = H.ok(H.dispatch("recipes.get"), "recipes.get (replication available)")
    H.eq(withReplication.canLock, true, "canLock true once host authority + replication both hold")

    H.ok(H.dispatch("recipes.set", { unlockIds = { "recipe_axe" }, lockIds = { "recipe_bandage" } }),
        "recipes.set unlock+lock in one request")
    H.eq(H.calls(progression, "OnRep_RecipesUnlockedArray"), 1, "OnRep_RecipesUnlockedArray pushed after the relock")
    local afterLock = H.ok(H.dispatch("recipes.get"), "recipes.get (after relock)")
    local ids = {}
    for _, id in ipairs(afterLock.unlockedIds) do ids[id] = true end
    H.check(ids.recipe_bandage == nil, "recipe_bandage relocked (removed)")
    H.check(ids.recipe_axe == true, "recipe_axe unlocked and present after the same write")

    -- An unresolvable lock id is rejected, not silently written (names.prepare's own validation).
    local oldFName = FName
    FName = function(text, mode) return oldFName(text == "not_a_real_recipe_at_all" and "None" or text, mode) end
    H.fails(H.dispatch("recipes.set", { lockIds = { "not_a_real_recipe_at_all" } }),
        "unknown row name", "an unresolvable recipe id is rejected")
    FName = oldFName

    -- ---------- non-host client: unlock still works (own-character RPC), relock refused ----------
    local clientPawn = H.clientSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))
    local clientProgression = H.field(clientPawn, "CharacterProgressionComponent")
    local clientRecipes = H.ok(H.dispatch("recipes.get"), "recipes.get (client)")
    H.eq(clientRecipes.canLock, false, "a non-host client can never relock, even with replication support")

    H.ok(H.dispatch("recipes.set", { unlockIds = { "recipe_torch" } }), "recipes.set unlock still works for a client")
    H.eq(H.calls(clientProgression, "Request_UnlockNewRecipe"), 1, "unlock RPC still reaches the client's own character")

    H.fails(H.dispatch("recipes.set", { lockIds = { "recipe_bandage" } }),
        "relocking recipes requires host authority and replication notification support",
        "a non-host client cannot relock even with replication support present")
end
