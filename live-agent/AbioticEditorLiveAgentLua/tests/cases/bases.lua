-- World bases / deployables (areas/bases.lua): PlayerMadeString (round 121) rename with an
-- AlternativeObjectName read-only fallback, container-backed deployables reporting
-- hasInventory/storedItemCount via the same slot helpers containers.list already uses, and
-- PaintedColor (plain EPaintColor property + OnRep) reads/writes.
return function(H)
    H.hostSession()

    -- netHelper is set up first (round 121): the primary rename path now needs it too (the same
    -- push-model MarkPropertyDirty notification containers.rename already demonstrates), not just
    -- the paint write further down.
    local netHelper = H.object("NetPushModelHelpers", {}, { MarkPropertyDirty = function() end })
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", netHelper)

    -- A plain deployable (no inventory) - bases.list must still report it. __bases lets the
    -- fake world's FindAllOf("AbioticDeployed_ParentBP_C") match it, matching how every deployed
    -- object in the real game derives from that one blueprint class.
    local bench = H.world.add(H.object("Deployed_Bench_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" },
        PaintedColor = 12, -- EPaintColor::None
    }, {
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))

    -- A deployable WITH a container inventory (a storage-capable base piece), one slot filled.
    -- Its name is read through PlayerMadeString (the real, networked field - see the header
    -- comment in bases.lua) as FString-like userdata, exercising textValue's :ToString() branch.
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
        PlayerMadeString = H.fstring("Loot Locker"),
        ContainerInventory = containerInv,
        PaintedColor = 5, -- EPaintColor::Purple
    }, {
        K2_GetActorLocation = function() return H.vector(4, 5, 6) end,
        NewPlayerMadeString = function() end,
    }))

    -- Round 121: a class with no PlayerMadeString at all (not Furniture-derived) still reports
    -- whatever AlternativeObjectName carries, read-only - see deployableCustomName's own remarks.
    local altNameOnly = H.world.add(H.object("Deployed_Light_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" },
        AlternativeObjectName = H.fstring("Porch Light"),
    }, {
        K2_GetActorLocation = function() return H.vector(2, 2, 2) end,
    }))

    local list = H.ok(H.dispatch("bases.list"), "bases.list")
    H.eq(#list.deployables, 3, "all three deployables found through the shared parent class")
    H.eq(list.supportsBenchUpgrades, false, "unsafe bench upgrades are not advertised")
    local benchId, lockerId, altNameOnlyId = bench:GetFullName(), locker:GetFullName(), altNameOnly:GetFullName()
    local benchRow, lockerRow, altNameOnlyRow
    for _, d in ipairs(list.deployables) do
        if d.id == benchId then benchRow = d end
        if d.id == lockerId then lockerRow = d end
        if d.id == altNameOnlyId then altNameOnlyRow = d end
    end
    H.check(benchRow ~= nil and lockerRow ~= nil and altNameOnlyRow ~= nil, "all three rows matched by full name")
    H.eq(benchRow.customName, nil, "no name set at all reads as nil, not an empty string")
    H.eq(benchRow.hasInventory, false, "the bench has no container inventory")
    -- Distance data (round 121): x/y/z travel through bases.list unconditionally - the desktop
    -- app computes distance/sub-level from these client-side (WorldDeployable.DistanceTo/SubLevel),
    -- rather than this module reporting either one itself.
    H.eq(benchRow.x, 1, "actor position x reported for client-side distance math")
    H.eq(benchRow.y, 2, "actor position y reported for client-side distance math")
    H.eq(benchRow.z, 3, "actor position z reported for client-side distance math")
    H.eq(lockerRow.customName, "Loot Locker", "the locker's custom name read from PlayerMadeString, converted from FString-like userdata")
    H.eq(lockerRow.hasInventory, true, "the locker has a container inventory")
    H.eq(lockerRow.storedItemCount, 1, "one non-empty slot counted")
    H.eq(altNameOnlyRow.customName, "Porch Light", "a class with no PlayerMadeString still reports AlternativeObjectName, read-only")
    H.eq(benchRow.paintColor, nil, "EPaintColor::None reads as unpainted (nil), not 12")
    H.eq(lockerRow.paintColor, 5, "a real paint colour value reads through as a number")

    -- bases.set: rename via PlayerMadeString (round 121) - the same real, networked field
    -- containers.rename already uses, with the identical mark-dirty + NewPlayerMadeString refresh.
    local marksBeforeRename = H.calls(netHelper, "MarkPropertyDirty")
    H.ok(H.dispatch("bases.set", { id = lockerId, customName = "Renamed Locker" }), "rename the locker")
    H.eq(H.field(locker, "PlayerMadeString"), "Renamed Locker",
        "the new name was actually written as a plain string, the write handler's first attempt")
    H.eq(H.calls(locker, "NewPlayerMadeString"), 1, "the host's own view is refreshed immediately, mirroring OnRep")
    H.eq(H.calls(netHelper, "MarkPropertyDirty"), marksBeforeRename + 1,
        "push-model replication notified, so other already-connected players see the new name too")
    local afterRename = H.ok(H.dispatch("bases.list"))
    for _, row in ipairs(afterRename.deployables) do
        if row.id == lockerId then H.eq(row.customName, "Renamed Locker", "the new name is reported back through bases.list") end
    end

    local upgradedBench = H.world.add(H.object("AbioticDeployed_CraftingBench_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" }, SupportsUpgrades = true,
        UpgradeTagContainer = { GameplayTags = { {TagName=H.fname("BenchUpgrade.TougherBench")}, {TagName=H.fname("Other.Tag")} } },
    }, { K2_GetActorLocation = function() return H.vector(0,0,0) end }))
    local upgraded = H.ok(H.dispatch("bases.list"))
    for _, row in ipairs(upgraded.deployables) do
        if row.id == upgradedBench:GetFullName() then H.eq(row.installedUpgrades[1], "TougherBench", "installed tags read without native handles") end
    end

    local tagData = { GameplayTags = {{TagName=H.fname("Other.Tag")}}, ParentTags = {{TagName=H.fname("Other")}} }
    local writable = H.world.add(H.object("AbioticDeployed_CraftingBench_ParentBP_C", {
        __bases={"AbioticDeployed_ParentBP_C"}, SupportsUpgrades=true,
        UpgradeTagContainer={GameplayTags={},ParentTags={}},
        ChangeableData={GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1=tagData},
    }, {
        K2_GetActorLocation=function() return H.vector(0,0,0) end,
        FlushNetDormancy=function() end, OnRep_UpgradeTagContainer=function() end, SaveDeployable=function() end,
    }))
    H.ok(H.dispatch("bases.set",{id=writable:GetFullName(),upgradeRow="TougherBench",upgradeInstalled=true}), "install through replicated and saved tags")
    H.eq(writable.UpgradeTagContainer.GameplayTags[1].TagName:ToString(), "BenchUpgrade.TougherBench", "replicated tag installed")
    H.eq(tagData.GameplayTags[1].TagName:ToString(), "Other.Tag", "unrelated saved tag retained")
    H.eq(tagData.GameplayTags[2].TagName:ToString(), "BenchUpgrade.TougherBench", "saved tag installed")
    H.eq(H.calls(writable,"SaveDeployable"),1,"bench save requested")
    H.ok(H.dispatch("bases.set",{id=writable:GetFullName(),upgradeRow="TougherBench",upgradeInstalled=false}), "remove tag and refresh components")
    H.eq(#writable.UpgradeTagContainer.GameplayTags,0,"replicated tag removed")
    H.eq(#writable.UpgradeTagContainer.ParentTags,0,"stale parent tag removed")
    H.eq(#tagData.GameplayTags,1,"only selected saved tag removed")
    H.eq(H.calls(writable,"OnRep_UpgradeTagContainer"),2,"upgrade components refresh on install and removal")

    -- bases.set: paint colour - a plain property write + OnRep replay + replication notify,
    -- never the Blueprint SetPaintColor function (see the header comment).
    -- The fake EDynamicProperty enum: PaintColor is value 6 in the real game (usmap); the
    -- other entry stands in for an unrelated property that must survive the repaint untouched.
    H.world.static("/Script/AbioticFactor.EDynamicProperty", H.object("UEnum", {}, {
        ForEachName = function(_, fn)
            fn("EDynamicProperty::XP", 7)
            fn("EDynamicProperty::PaintColor", 6)
        end,
    }))
    local savedDynamic = { { Key = 7, Value = 250 }, { Key = 6, Value = 12 } }
    local paintable = H.world.add(H.object("Deployed_CraftingBench_Default_C", {
        __bases = { "AbioticDeployed_ParentBP_C" }, PaintedColor = 12,
        ChangeableData = { DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = savedDynamic },
    }, {
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
        OnRep_PaintedColor = function() end,
        SaveDeployable = function() end,
    }))
    local marksBefore = H.calls(netHelper, "MarkPropertyDirty")
    H.ok(H.dispatch("bases.set", { id = paintable:GetFullName(), paintColor = 8 }), "paint the crafting bench")
    H.eq(paintable.PaintedColor, 8, "paint colour written to the plain replicated property")
    H.eq(H.calls(paintable, "OnRep_PaintedColor"), 1, "OnRep replayed after the direct write")
    H.eq(H.calls(netHelper, "MarkPropertyDirty"), marksBefore + 2, "push-model replication notified for the live property and the saved data")
    local saved = paintable.ChangeableData.DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7
    H.eq(#saved, 2, "the saved dynamic-property array keeps its other entry")
    H.eq(saved[1].Value, 250, "the unrelated saved entry is untouched")
    H.eq(saved[2].Key, 6, "the saved paint entry keeps the PaintColor key")
    H.eq(saved[2].Value, 8, "the saved paint entry carries the new colour")
    H.eq(H.calls(paintable, "SaveDeployable"), 1, "the deployable is asked to save after the repaint")

    -- A deployable without a saved dynamic-property array still repaints live (no error).
    local plainPaintable = H.world.add(H.object("Deployed_Rug_Default_C", {
        __bases = { "AbioticDeployed_ParentBP_C" }, PaintedColor = 12,
    }, {
        K2_GetActorLocation = function() return H.vector(8, 8, 8) end,
        OnRep_PaintedColor = function() end,
    }))
    H.ok(H.dispatch("bases.set", { id = plainPaintable:GetFullName(), paintColor = 2 }), "repaint an object with no saved array")
    H.eq(plainPaintable.PaintedColor, 2, "live property still written without a saved array")
    local repainted = H.ok(H.dispatch("bases.list"))
    for _, row in ipairs(repainted.deployables) do
        if row.id == paintable:GetFullName() then H.eq(row.paintColor, 8, "the new colour reads back through bases.list") end
    end

    -- Missing deployable id: player-safe failure, not a Lua error.
    H.fails(H.dispatch("bases.set", { id = "no-such-deployable", customName = "X" }), "not found", "unknown deployable id fails cleanly")

    -- Round 88: renaming a Void-Chest-shaped deployable through THIS screen must refuse the same
    -- way containers.rename already does (see world_gaps.lua's own shared-identity rename test) -
    -- a live report showed the cross-instance rename bleed persisting even after that block
    -- landed, because bases.lua reaches the same actors through its own, wider
    -- AbioticDeployed_ParentBP_C sweep with a completely separate, unprotected customName write.
    local sharedBaseInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    local sharedBaseDecoy = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    local sharedBase = H.world.add(H.object("Deployed_Container_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" },
        ContainerInventory = sharedBaseDecoy, PlayerMadeString = H.fstring(""),
    }, {
        GetContainerInventory = function() return sharedBaseInv end,
        K2_GetActorLocation = function() return H.vector(13, 13, 13) end,
        NewPlayerMadeString = function() end,
    }))
    -- Round 90: renaming is per-actor and allowed again (see containers.rename's remarks).
    H.ok(H.dispatch("bases.set", { id = sharedBase:GetFullName(), customName = "Mine Only" }),
        "a shared-inventory deployable still takes its own name from the BASES screen")

    -- Round 121: SCOPE - bases.lua sweeps every currently-loaded deployable with no per-actor
    -- map-path filter, the same as every other region-scoped live area (see bases.lua's own header
    -- comment for why, and doors.list/destructibles.lua/triggers.lua alongside it). This locks
    -- that design in: a deployable whose actor path names a totally different sub-level still
    -- appears in bases.list - the desktop app's mitigation is showing that sub-level per row
    -- (WorldDeployable.SubLevel, covered by the C# LiveBasesSessionTests) and a distance-based
    -- "Nearest first" sort, not hiding it here with an unproven filter.
    local otherRegion = H.world.add(H.object("Deployed_Bench_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" }, PaintedColor = 12,
    }, {
        K2_GetActorLocation = function() return H.vector(500, 500, 0) end,
    }))
    rawset(otherRegion, "__fullName",
        "Deployed_Bench_ParentBP_C /Game/Maps/OtherRegion.OtherRegion:PersistentLevel.Deployed_Bench_ParentBP_C_999")
    local crossRegion = H.ok(H.dispatch("bases.list"))
    local otherRegionRow
    for _, row in ipairs(crossRegion.deployables) do
        if row.id == otherRegion:GetFullName() then otherRegionRow = row end
    end
    H.check(otherRegionRow ~= nil,
        "a deployable from a different sub-level still appears - this module does not filter by region")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(locker)
    H.fails(H.dispatch("bases.set", { id = lockerId, customName = "Y" }), "only the host", "client cannot rename deployables")
end
