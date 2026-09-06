-- World bases / deployables (areas/bases.lua): AlternativeObjectName (FText) rename, and
-- container-backed deployables reporting hasInventory/storedItemCount via the same slot helpers
-- containers.list already uses.
return function(H)
    H.hostSession()

    -- A plain deployable (no inventory) - bases.list must still report it. __bases lets the
    -- fake world's FindAllOf("AbioticDeployed_ParentBP_C") match it, matching how every deployed
    -- object in the real game derives from that one blueprint class.
    local bench = H.world.add(H.object("Deployed_Bench_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" },
        AlternativeObjectName = H.fstring(""),
    }, {
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))

    -- A deployable WITH a container inventory (a storage-capable base piece), one slot filled.
    local containerInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("scrap_metal") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 5,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
    } }, { OnRep_CurrentInventory = function() end })
    local locker = H.world.add(H.object("Deployed_Locker_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" },
        AlternativeObjectName = H.fstring("Loot Locker"),
        ContainerInventory = containerInv,
    }, { K2_GetActorLocation = function() return H.vector(4, 5, 6) end }))

    local list = H.ok(H.dispatch("bases.list"), "bases.list")
    H.eq(#list.deployables, 2, "both deployables found through the shared parent class")
    H.eq(list.supportsBenchUpgrades, false, "bench upgrades honestly unsupported live")
    local benchId, lockerId = bench:GetFullName(), locker:GetFullName()
    local benchRow, lockerRow
    for _, d in ipairs(list.deployables) do
        if d.id == benchId then benchRow = d end
        if d.id == lockerId then lockerRow = d end
    end
    H.check(benchRow ~= nil and lockerRow ~= nil, "both rows matched by full name")
    H.eq(benchRow.customName, nil, "an empty custom name reads as nil, not an empty string")
    H.eq(benchRow.hasInventory, false, "the bench has no container inventory")
    H.eq(lockerRow.customName, "Loot Locker", "the locker's custom name converted from FText")
    H.eq(lockerRow.hasInventory, true, "the locker has a container inventory")
    H.eq(lockerRow.storedItemCount, 1, "one non-empty slot counted")

    -- bases.set: rename via FText(text) (a real UE4SS global, unlike FVector()/FRotator()).
    H.ok(H.dispatch("bases.set", { id = lockerId, customName = "Renamed Locker" }), "rename the locker")
    H.eq(H.field(locker, "AlternativeObjectName"):ToString(), "Renamed Locker", "the new name was actually written and reads back through FText")

    -- Missing deployable id: player-safe failure, not a Lua error.
    H.fails(H.dispatch("bases.set", { id = "no-such-deployable", customName = "X" }), "not found", "unknown deployable id fails cleanly")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(locker)
    H.fails(H.dispatch("bases.set", { id = lockerId, customName = "Y" }), "only the host", "client cannot rename deployables")
end
