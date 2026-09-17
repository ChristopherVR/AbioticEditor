return function(H)
    local pawn = H.hostSession()
    local module = require("item_metadata")
    local D = "DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7"
    local T = "GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1"
    local C = "ChangeableData_12_2B90E1F74F648135579D39A49F5A2313"
    local I = "ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B"
    local names = { [7] = "EDynamicProperty::XP", [12] = "EDynamicProperty::WeaponCoating", [18] = "EDynamicProperty::CoatingDurability" }
    H.world.static("/Script/AbioticFactor.EDynamicProperty", H.object("UEnum", {}, {
        GetNameByValue = function(_, value) return H.fname(names[value]) end,
        ForEachName = function(_, callback) for value, name in pairs(names) do callback(H.fname(name), value) end end,
    }))
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("NetPushModelHelpers", {}, { MarkPropertyDirty = function() end }))
    local slots = pawn.CharacterInventory.CurrentInventory
    local original = slots[1][C]
    original[D] = { { Key = 7, Value = 321 }, { Key = 12, Value = 2 }, { Key = 18, Value = 44 } }
    original[T] = { GameplayTags = { { TagName = H.fname("Item.Special") } }, ParentTags = { { TagName = H.fname("Item") } } }
    local empty = slots[2][C]
    empty[D] = {}
    empty[T] = { GameplayTags = {}, ParentTags = {} }
    local metadata = module.read(original, slots[1])
    H.eq(metadata.dynamicProperties[1].key, names[7], "dynamic enum name read from numeric value")
    H.eq(metadata.dynamicProperties[1].value, 321, "pet XP captured")
    H.eq(metadata.gameplayTags[1], "Item.Special", "gameplay tag captured")
    H.eq(metadata.parentGameplayTags[1], "Item", "parent gameplay tag captured")
    local prepared = module.prepare(empty, metadata)
    module.clear(original)
    module.apply(empty, prepared)
    H.eq(empty[D][1].Value, 321, "detached metadata survives source clear")
    H.eq(empty[D][2].Key, 12, "coating uses actual enum value")
    H.eq(empty[T].GameplayTags[1].TagName:ToString(), "Item.Special", "tag copied to destination")
    H.eq(#original[D], 0, "clear removes dynamic item properties")
    H.eq(#original[T].GameplayTags, 0, "clear removes stale tags")
    metadata.dynamicProperties[3].value = -1
    local ok, err = pcall(module.prepare, original, metadata)
    H.eq(ok, false, "negative coating durability rejected")
    H.eq(tostring(err):find("invalid coating durability", 1, true) ~= nil, true, "coating failure is explicit")
    metadata.dynamicProperties[3].value = 44
    local overridden = H.itemTable("/Game/Mods/Items.Items", { test_item = true })
    local normal = H.itemTable("/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global", { test_item = true })
    slots[1][I].DataTable = normal
    slots[1][I].RowName = H.fname("test_item")
    metadata.itemDataTable = "/Game/Mods/Items.Items"
    H.ok(H.dispatch("inventory.setcomplete", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "test_item", dataTable = metadata.itemDataTable,
            details = { instanceMetadata = metadata } },
    } }), "same-id move accepts explicit source table override")
    H.eq(slots[1][I].DataTable, overridden, "mod item table follows moved instance")
    local processingInventory = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { [I] = { RowName = H.fname("Empty") }, [C] = { [D] = {}, [T] = { GameplayTags = {}, ParentTags = {} } } }
    } }, { OnRep_CurrentInventory = function() end })
    H.world.add(H.object("Deployed_ProcessingBench_ParentBP_C", { BenchInventory = processingInventory },
        { K2_GetActorLocation = function() return H.vector(1, 2, 3) end }))
    local containers = H.ok(H.dispatch("containers.list"))
    H.eq(#containers.containers, 1, "processing bench inventory is listed")
    local id = containers.containers[1].id
    H.ok(H.dispatch("inventory.transfer", { first = { kind = "backpack", slotIndex = 0 },
        second = { containerId = id, slotIndex = 0 } }), "player to bench transfer")
    H.eq(slots[1][I].RowName:ToString(), "Empty", "source emptied after transfer")
    H.eq(processingInventory.CurrentInventory[1][I].RowName:ToString(), "test_item", "destination receives item")
    H.eq(processingInventory.CurrentInventory[1][C][D][1].Value, 321, "dynamic metadata transferred")

    -- containers.list deliberately omits instanceMetadata (round 79 perf fix, see main.lua's
    -- readItemDetails skipMetadata comment) - confirm the metadata is still really there on the
    -- live object (checked above) even though this listing does not report it, and that the
    -- ordinary fields a container browser needs are still present.
    local afterTransfer = H.ok(H.dispatch("containers.list")).containers[1].slots[1]
    H.eq(afterTransfer.itemId, "test_item", "containers.list still reports the item id")
    H.eq(afterTransfer.details.instanceMetadata, nil, "containers.list omits instance metadata for speed")

    H.fails(H.dispatch("inventory.transfer", { first = { containerId = id, slotIndex = 0 },
        second = { kind = "backpack", slotIndex = 9999 } }), "unavailable", "invalid destination rejects transfer")
    H.eq(processingInventory.CurrentInventory[1][I].RowName:ToString(), "test_item", "failed transfer preserves source")

    -- Round 85: a shared/Void-Chest-shaped destination does not get forced through a synchronous
    -- OnRep_CurrentInventory() refresh the way an ordinary container does - a live report showed
    -- exactly this (dragging an item into a Void Chest) freezing the game for a moment. See
    -- containers.set's matching remarks in main.lua for the full reasoning; ctx.containerInventory
    -- reports which endpoints are shared, and this handler only skips the manual OnRep call for
    -- those, the network mark still applies either way.
    local transferSourceInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { [I] = { DataTable = normal, RowName = H.fname("test_item") }, [C] = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1, [D] = {}, [T] = { GameplayTags = {}, ParentTags = {} } } },
    } }, { OnRep_CurrentInventory = function() end })
    H.world.add(H.object("Deployed_Container_ParentBP_C", { ContainerInventory = transferSourceInv },
        { K2_GetActorLocation = function() return H.vector(20, 20, 20) end }))
    local sharedDestInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { [I] = { RowName = H.fname("Empty") }, [C] = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0, [D] = {}, [T] = { GameplayTags = {}, ParentTags = {} } } },
    } }, { OnRep_CurrentInventory = function() end })
    local decoyDestInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    H.world.add(H.object("Deployed_Container_ParentBP_C", { ContainerInventory = decoyDestInv },
        { GetContainerInventory = function() return sharedDestInv end, K2_GetActorLocation = function() return H.vector(21, 21, 21) end }))
    local transferListing = H.ok(H.dispatch("containers.list")).containers
    local sourceId, sharedDestId
    for _, entry in ipairs(transferListing) do
        if entry.x == 20 then sourceId = entry.id end
        if entry.x == 21 then sharedDestId = entry.id end
    end
    H.check(sourceId ~= nil and sharedDestId ~= nil, "both transfer endpoints are listed")
    H.ok(H.dispatch("inventory.transfer", { first = { containerId = sourceId, slotIndex = 0 },
        second = { containerId = sharedDestId, slotIndex = 0 } }), "container to shared-container transfer")
    H.eq(sharedDestInv.CurrentInventory[1][I].RowName:ToString(), "test_item", "item really moved into the shared inventory")
    H.eq(H.calls(transferSourceInv, "OnRep_CurrentInventory"), 1, "the ordinary source still gets its synchronous refresh")
    H.eq(H.calls(sharedDestInv, "OnRep_CurrentInventory"), 0, "the shared destination does not")

    -- Weapon coating round trip: a coating index/durability written through
    -- inventory.setcomplete must read back through inventory.list, and clearing (index -1,
    -- durability 0) must zero both values rather than leave the old coating behind - see
    -- InventoryItemSlot.CoatingIndex/CoatingDurability and LiveItemDetails.FromSlot on the
    -- editor side, which merge exactly this shape into an existing instance's metadata.
    slots[1][I].DataTable = normal
    slots[1][I].RowName = H.fname("test_item")
    slots[1][C].CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1
    slots[1][C][D] = {}
    slots[1][C][T] = { GameplayTags = {}, ParentTags = {} }
    local function dynamicValue(instanceMetadata, key)
        for _, prop in ipairs(instanceMetadata.dynamicProperties) do if prop.key == key then return prop.value end end
        return nil
    end
    local function backpackSlotZero(list)
        for _, entry in ipairs(list) do if entry.kind == "backpack" and entry.slotIndex == 0 then return entry end end
    end

    H.ok(H.dispatch("inventory.setcomplete", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "test_item", details = { instanceMetadata = {
            dynamicProperties = { { key = "EDynamicProperty::WeaponCoating", value = 3 },
                { key = "EDynamicProperty::CoatingDurability", value = 60 } },
            gameplayTags = {}, parentGameplayTags = {} } } },
    } }), "coating write accepted")
    local coatedSlot = backpackSlotZero(H.ok(H.dispatch("inventory.list")))
    H.eq(dynamicValue(coatedSlot.details.instanceMetadata, "EDynamicProperty::WeaponCoating"), 3, "coating index reads back")
    H.eq(dynamicValue(coatedSlot.details.instanceMetadata, "EDynamicProperty::CoatingDurability"), 60, "coating durability reads back")

    H.ok(H.dispatch("inventory.setcomplete", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "test_item", details = { instanceMetadata = {
            dynamicProperties = { { key = "EDynamicProperty::WeaponCoating", value = -1 },
                { key = "EDynamicProperty::CoatingDurability", value = 0 } },
            gameplayTags = {}, parentGameplayTags = {} } } },
    } }), "coating clear accepted")
    local clearedSlot = backpackSlotZero(H.ok(H.dispatch("inventory.list")))
    H.eq(dynamicValue(clearedSlot.details.instanceMetadata, "EDynamicProperty::WeaponCoating"), -1, "clearing zeroes the coating index instead of leaving the old value")
    H.eq(dynamicValue(clearedSlot.details.instanceMetadata, "EDynamicProperty::CoatingDurability"), 0, "clearing zeroes the coating durability instead of leaving the old value")
end
