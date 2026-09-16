-- worldunlocks.get / worldunlocks.set (round 77): world-wide (not per-player) unlock lists on
-- Abiotic_Survival_GameState_C. Read is always real; the write path (and the read via
-- TSet.ForEach rather than the best-effort array fallback) needs a UE4SS build that exposes
-- TSet.Add/Remove/ForEach on the recipe sets - see areas/worldunlocks.lua's header comment.
-- globalRecipeEditsUnavailableReason names exactly why edits are unavailable ("not-host",
-- "no-replication", or "runtime-unsupported") so the app can show a specific, localized
-- explanation instead of just disabling the control with no context.

-- A minimal fake TSet<FName>: Add/Remove/ForEach, matching what readSet/worldunlocks.set expect
-- from a UE4SS build new enough to expose them (see H.object's own header comment on why fakes
-- here only answer what they explicitly declare).
local function makeTSet(names)
    local elements = {}
    for _, n in ipairs(names) do table.insert(elements, n) end
    local set = {}
    function set:Add(name)
        for _, e in ipairs(elements) do if e:ToString() == name:ToString() then return end end
        table.insert(elements, name)
    end
    function set:Remove(name)
        for i, e in ipairs(elements) do
            if e:ToString() == name:ToString() then table.remove(elements, i) return end
        end
    end
    function set:ForEach(callback)
        for _, e in ipairs(elements) do callback({ get = function() return e end }) end
    end
    return set
end

return function(H)
    -- ---------- older UE4SS build: TSet.Add/Remove/ForEach unavailable ----------
    H.hostSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))

    local unlocks = H.ok(H.dispatch("worldunlocks.get"), "worldunlocks.get")
    H.eq(unlocks.isHost, true, "host authority reported")
    H.eq(unlocks.canEditRecipes, false, "recipe edits unsupported without TSet.Add/Remove/ForEach")
    H.eq(unlocks.globalRecipeEditsUnavailableReason, "runtime-unsupported", "reason names the old-UE4SS case")
    H.eq(#unlocks.recipesUnlocked, 1, "one world recipe unlocked"); H.eq(unlocks.recipesUnlocked[1], "recipe_bandage", "recipe row")
    H.eq(#unlocks.recipesResearched, 0, "no world recipes researched")
    H.eq(#unlocks.itemsPickedUp, 1, "one world item picked up"); H.eq(unlocks.itemsPickedUp[1], "scrap_metal", "item row")
    H.eq(#unlocks.emailsRead, 1, "one world email read")
    H.eq(#unlocks.journalEntries, 0, "no world journal entries")
    H.eq(#unlocks.compendiumEmail, 0, "no world compendium email entries")
    H.eq(#unlocks.compendiumNarrative, 0, "no world compendium narrative entries")
    H.eq(#unlocks.compendiumExploration, 1, "one world compendium exploration entry")

    H.fails(H.dispatch("worldunlocks.set", {}), "require UE4SS TSet support", "older runtimes cannot edit sets")

    -- Reading still requires a loaded world's game state.
    H.gameState = nil
    H.fails(H.dispatch("worldunlocks.get"), "the world is not loaded", "worldunlocks.get refuses with no game state")

    -- ---------- newer UE4SS build: TSet.Add/Remove/ForEach available, real write path ----------
    H.hostSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))
    H.gameState.GlobalRecipesUnlocked = makeTSet({ H.fname("recipe_bandage") })
    H.gameState.GlobalRecipesResearched = makeTSet({})

    local supported = H.ok(H.dispatch("worldunlocks.get"), "worldunlocks.get (supported runtime)")
    H.eq(supported.canEditRecipes, true, "recipe edits supported once TSet methods exist")
    H.eq(supported.globalRecipeEditsUnavailableReason, nil, "no reason once edits are supported")
    H.eq(#supported.recipesUnlocked, 1, "still reads the one unlocked recipe, now via TSet.ForEach")
    H.eq(supported.recipesUnlocked[1], "recipe_bandage", "recipe row via TSet.ForEach")

    H.ok(H.dispatch("worldunlocks.set", {
        recipes = { { id = "recipe_bandage", unlocked = false }, { id = "scrap_metal", unlocked = true } },
    }), "worldunlocks.set (supported runtime)")
    local after = H.ok(H.dispatch("worldunlocks.get"), "worldunlocks.get (after set)")
    H.eq(#after.recipesUnlocked, 1, "bandage relocked, scrap_metal newly unlocked (one net change)")
    H.eq(after.recipesUnlocked[1], "scrap_metal", "scrap_metal is now the only unlocked world recipe")
    H.eq(#after.recipesResearched, 1, "scrap_metal researched alongside unlocked")
    H.eq(after.recipesResearched[1], "scrap_metal", "researched mirrors unlocked")

    -- Even with a supporting runtime, a non-host client cannot edit world recipes.
    H.clientSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))
    H.gameState.GlobalRecipesUnlocked = makeTSet({})
    H.gameState.GlobalRecipesResearched = makeTSet({})
    local notHost = H.ok(H.dispatch("worldunlocks.get"), "worldunlocks.get (client)")
    H.eq(notHost.canEditRecipes, false, "non-host cannot edit even with TSet support")
    H.eq(notHost.globalRecipeEditsUnavailableReason, "not-host", "reason names the authority case")
    H.fails(H.dispatch("worldunlocks.set", {}), "require host authority", "non-host cannot even attempt a write")
end
