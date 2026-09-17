-- Ground items (main.lua, Abiotic_Item_Dropped_C): a removed or picked-up item is destroyed
-- with K2_DestroyActor, and a destroyed actor stays in FindAllOf as a still-:IsValid() object
-- until the engine's next garbage collection. dropped.list must leave those out, and
-- dropped.remove must report an item that was asked to despawn but did not actually go.
-- core.lua covers the plain list/remove round trip; this case is about the in-between state.
return function(H)
    H.hostSession()

    -- A stub whose OnItemDespawn behaves like the real blueprint (SaveItem + K2_DestroyActor):
    -- after it runs, the actor answers IsActorBeingDestroyed() with true.
    local function droppedItem(rowName, destroys)
        local state = { destroyed = false }
        local item = H.object("Abiotic_Item_Dropped_C", {
            HasBeenPickedUp = false,
            ItemDataRow = { RowName = H.fname(rowName) },
            ChangeableData = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1 },
        }, {
            K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
            InitDespawn = function() end,
            OnItemDespawn = function() if destroys then state.destroyed = true end end,
            IsActorBeingDestroyed = function() return state.destroyed end,
        })
        return H.world.add(item), state
    end

    local alive = droppedItem("scrap_metal", true)
    local ghost, ghostState = droppedItem("scrap_cloth", true)
    ghostState.destroyed = true -- already destroyed in-game, waiting on the garbage collector
    local stubborn = droppedItem("duct_tape", false)
    -- An older build where the function is missing entirely: listed, never hidden.
    local legacy = H.world.add(H.object("Abiotic_Item_Dropped_C", {
        HasBeenPickedUp = false, ItemDataRow = { RowName = H.fname("battery") },
        ChangeableData = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1 },
    }, { K2_GetActorLocation = function() return H.vector(4, 5, 6) end, InitDespawn = function() end, OnItemDespawn = function() end }))

    local listed = H.ok(H.dispatch("dropped.list"), "dropped.list").items
    local ids = {}
    for _, row in ipairs(listed) do ids[row.itemId] = row.id end
    H.eq(#listed, 3, "the actor already being destroyed is not listed")
    H.check(ids.scrap_metal ~= nil and ids.duct_tape ~= nil and ids.battery ~= nil, "every live actor is listed")
    H.check(ids.scrap_cloth == nil, "the destroyed-but-not-yet-collected actor is left out")

    -- Removing the live one destroys it, and the very next list no longer shows it even though
    -- the stub world still holds the object (no garbage collection ever runs here).
    local result = H.ok(H.dispatch("dropped.remove", { ids = { ids.scrap_metal } }), "dropped.remove")
    H.eq(result.removed, 1, "one item confirmed removed")
    H.eq(result.stuck, 0, "nothing stuck")
    H.eq(H.calls(alive, "OnItemDespawn"), 1, "despawn ran on the live actor")
    listed = H.ok(H.dispatch("dropped.list")).items
    H.eq(#listed, 2, "a just-removed item drops off the list immediately")

    -- Asking to remove the ghost again is "not found", not a second despawn.
    result = H.ok(H.dispatch("dropped.remove", { ids = { rawget(ghost, "__fullName") } }))
    H.eq(result.removed, 0, "an actor already being destroyed is reported as already gone")
    H.eq(H.calls(ghost, "OnItemDespawn"), 0, "and is not despawned a second time")

    -- One whose despawn runs but leaves the actor standing is reported as stuck, not removed.
    result = H.ok(H.dispatch("dropped.remove", { ids = { ids.duct_tape } }))
    H.eq(result.removed, 0, "a despawn that did not destroy the actor is not counted as removed")
    H.eq(result.stuck, 1, "it is reported as stuck instead")
    H.eq(H.calls(stubborn, "OnItemDespawn"), 1, "the despawn was still attempted")

    -- Without IsActorBeingDestroyed at all the despawn is trusted, as it always was.
    result = H.ok(H.dispatch("dropped.remove", { ids = { ids.battery } }))
    H.eq(result.removed, 1, "an actor that cannot be asked is still counted as removed")
    H.eq(H.calls(legacy, "OnItemDespawn"), 1, "legacy despawn ran")
end
