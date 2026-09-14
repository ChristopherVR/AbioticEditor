-- Reproduce a visible row name paired with an empty-slot or unrelated DataTable.
return function(H)
    local pawn = H.hostSession()
    local handleKey = "ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B"
    local dataKey = "ChangeableData_12_2B90E1F74F648135579D39A49F5A2313"
    local stackKey = "CurrentStack_9_D443B69044D640B0989FD8A629801A49"
    local gearPath = "/Game/Blueprints/Items/ItemTable_Gear.ItemTable_Gear"
    local gear = H.itemTable(gearPath, { backpack_test = true })
    local pickups = H.itemTable("/Game/Blueprints/Items/ItemTable_Pickups.ItemTable_Pickups", { Empty = true })
    local inventories = { backpack = pawn.CharacterInventory, equip = pawn.CharacterEquipSlotInventory,
        hotbar = pawn.CharacterHotbarInventory, transmog = pawn.TmogInventory }
    for kind, inv in pairs(inventories) do
        local slot = inv.CurrentInventory[1]
        slot[handleKey].DataTable = pickups
        H.ok(H.dispatch("inventory.set", { edits = {
            { kind = kind, slotIndex = 0, itemId = "backpack_test", dataTable = gearPath, stack = 1 },
        } }), kind .. " replaces the empty-slot table")
        H.eq(slot[handleKey].DataTable, gear, kind .. " holds the actual gear UObject")
        H.eq(slot[handleKey].RowName:ToString(), "backpack_test", kind .. " keeps the matching row")
        H.eq(H.calls(inv, "OnRep_CurrentInventory"), 1, kind .. " refreshes once")
    end

    local slot = pawn.CharacterInventory.CurrentInventory[1]
    local ammoKey = "CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D"
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, ammoInMagazine = 12 },
    } }), "ammo can be edited without replacing an item")
    H.eq(slot[dataKey][ammoKey], 12, "ammo reaches the real slot field")
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, stack = 1 },
    } }), "omitted ammo remains untouched")
    H.eq(slot[dataKey][ammoKey], 12, "quantity-only edit preserves ammo")
    H.fails(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, ammoInMagazine = 6 },
        { kind = "backpack", slotIndex = 1, ammoInMagazine = -1 },
    } }), "non-negative integer", "invalid ammo rejects a batch before mutation")
    H.eq(slot[dataKey][ammoKey], 12, "failed batch preserves first magazine")
    -- An older write can already have the right row name and still be invisible.
    slot[handleKey].DataTable = pickups
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "backpack_test", dataTable = gearPath, stack = 2 },
    } }), "repair a same-row wrong-table item")
    H.eq(slot[handleKey].DataTable, gear, "same-row repair updates DataTable")

    local modTable = H.itemTable("/Mod/Items.Items", { backpack_test = true })
    slot[handleKey].DataTable = modTable
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "backpack_test", dataTable = gearPath, stack = 3 },
    } }), "ordinary same-item edit preserves a valid override")
    H.eq(slot[handleKey].DataTable, modTable, "mod table preserved")

    H.fails(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "bandage", stack = 9 },
        { kind = "backpack", slotIndex = 1, itemId = "missing_row", dataTable = gearPath, stack = 1 },
    } }), "was not found", "bad row rejects the whole batch before writes")
    H.eq(slot[handleKey].RowName:ToString(), "backpack_test", "first slot was not partially replaced")
    H.eq(slot[dataKey][stackKey], 3, "first quantity was not partially changed")
    H.fails(H.dispatch("inventory.set", { edits = {
        { kind = "equip", slotIndex = 999, itemId = "bandage" },
    } }), "slot is unavailable", "invalid slots are not silently successful")

    local inv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = pawn.CharacterHotbarInventory.CurrentInventory },
        { OnRep_CurrentInventory = function() end })
    local container = H.world.add(H.object("Deployed_Container_ParentBP_C", { ContainerInventory = inv }, {}))
    inv.CurrentInventory[1][handleKey].DataTable = pickups
    H.ok(H.dispatch("containers.set", { id = container:GetFullName(), edits = {
        { slotIndex = 0, itemId = "backpack_test", dataTable = gearPath, stack = 1 },
    } }), "container uses the same table repair")
    H.eq(inv.CurrentInventory[1][handleKey].DataTable, gear, "container table repaired")
    H.fails(H.dispatch("containers.set", { id = container:GetFullName(), edits = {
        { slotIndex = 0, itemId = "bandage", stack = 7 },
        { slotIndex = 1, itemId = "backpack_test", dataTable = "/Missing/Table.Table", stack = 1 },
    } }), "DataTable is unavailable", "missing table rejects container batch")
    H.eq(inv.CurrentInventory[1][handleKey].RowName:ToString(), "backpack_test", "container first slot untouched")

    -- A loaded name may not exist until its table has been loaded into the game.
    local latePath = "/Game/Blueprints/Items/ItemTable_Late.ItemTable_Late"
    local oldLoad, oldFName = LoadAsset, FName
    local loaded = false
    LoadAsset = function(path)
        if path == latePath then loaded = true; H.itemTable(path, { late_item = true }) end
    end
    FName = function(text, mode) return oldFName(text == "late_item" and not loaded and "None" or text, mode) end
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 2, itemId = "late_item", dataTable = latePath, stack = 1 },
    } }), "load table before resolving its new FName")
    H.eq(pawn.CharacterInventory.CurrentInventory[3][handleKey].RowName:ToString(), "late_item", "late name resolved")
    LoadAsset, FName = oldLoad, oldFName

    H.ok(H.dispatch("companions.set", { kind = "equip", slotIndex = 1,
        itemId = "backpack_test", dataTable = gearPath }), "companion writes route the table")
    H.eq(pawn.CharacterEquipSlotInventory.CurrentInventory[2][handleKey].DataTable, gear,
        "companion replacement holds the supplied table")

    local dropCalls = 0
    rawget(pawn, "__methods").Request_DropInventorySlot = function() dropCalls = dropCalls + 1 end
    local dropSlot = pawn.CharacterHotbarInventory.CurrentInventory[2]
    dropSlot[handleKey].RowName = FName("None", EFindName.FNAME_Find)
    dropSlot[handleKey].DataTable = pickups
    H.ok(H.dispatch("dropped.add", { itemId = "backpack_test", dataTable = gearPath, stack = 1 }),
        "ground item uses a free None slot with the supplied table")
    H.eq(dropSlot[handleKey].DataTable, gear, "drop staging slot holds the correct table")
    H.eq(dropCalls, 1, "validated item reaches the drop request")

    -- Missing lookup support must fail without quietly falling back to row-only writes.
    H.world.statics["/Script/Engine.Default__DataTableFunctionLibrary"] = nil
    H.fails(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "bandage", stack = 1 },
    } }), "cannot validate", "missing validation fails closed")
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, clear = true },
    } }), "clearing an invisible item does not require its table")
    H.eq(slot[dataKey][ammoKey], 0, "cleared slot does not retain ammo")
end
