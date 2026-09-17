-- Round 77: the five WORLD live-editing gaps closed this round - pets (Pest/Skink family only),
-- vehicle wrecked state, bench upgrades (install only), dropped-item spawn + container sort, and
-- narrative NPCs. Each write is also checked for non-host gating.
return function(H)
    -- ---------- pets: Pest/Skink family (has Guid), Peccary stays unmatched ----------
    local pawn = H.hostSession()
    local pest = H.world.add(H.object("NPC_Monster_Pest_C", {
        Guid = H.fstring("11111111-1111-1111-1111-111111111111"),
        PetName = H.fstring("Sparky"),
        IsDead = false,
        CurrentHealth_Head = 50, CurrentHealth_Torso = 100, CurrentHealth_LeftArm = 100,
        CurrentHealth_RightArm = 100, CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
        DynamicProperties = { { Key = H.fname("EDynamicProperty::XP"), Value = 40 } },
    }, {
        OnRep_PetName = function() end, OnRep_IsDead = function() end, OnRep_CurrentHealth = function() end,
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))
    -- A subclass (Skink) proves FindAllOf("NPC_Monster_Pest_C") stays hierarchy-inclusive.
    H.world.add(H.object("NPC_Skink_Basic_C", {
        __bases = { "NPC_Monster_Pest_C" },
        Guid = H.fstring("22222222-2222-2222-2222-222222222222"),
        PetName = H.fstring(""), IsDead = false,
        CurrentHealth_Head = 30, CurrentHealth_Torso = 30, CurrentHealth_LeftArm = 0,
        CurrentHealth_RightArm = 0, CurrentHealth_LeftLeg = 0, CurrentHealth_RightLeg = 0,
        DynamicProperties = {},
    }, { K2_GetActorLocation = function() return H.vector(0, 0, 0) end }))
    -- No Guid/PetName/DynamicProperties at all - must stay off the list (round 76/77 finding).
    H.world.add(H.object("NPC_Monster_Peccary_C", { IsDead = false },
        { K2_GetActorLocation = function() return H.vector(4, 5, 6) end }))

    local petDir = H.ok(H.dispatch("pets.list"), "pets.list")
    H.eq(petDir.available, true, "pets available (partial)")
    H.eq(petDir.supportsSpeciesChange, false, "no live species change")
    H.eq(petDir.supportsRemoval, true, "live removal supported (round 78: K2_DestroyActor)")
    H.eq(#petDir.pets, 2, "only Pest/Skink family listed (Peccary excluded)")
    local sparky
    for _, p in ipairs(petDir.pets) do if p.id == "11111111-1111-1111-1111-111111111111" then sparky = p end end
    H.check(sparky ~= nil, "Sparky matched by Guid")
    H.eq(sparky.customName, "Sparky", "pet custom name read")
    H.eq(sparky.limbHealth.Head, 50, "pet limb health read")
    H.eq(sparky.xp, 40, "pet XP read from DynamicProperties")

    H.ok(H.dispatch("pets.set", {
        id = "11111111-1111-1111-1111-111111111111", isDead = true, customName = "Buddy",
        limbHealth = { Head = 100 }, xp = 999,
    }), "pets.set")
    H.eq(H.field(pest, "IsDead"), true, "pet killed")
    H.eq(H.calls(pest, "OnRep_IsDead"), 1, "pet OnRep_IsDead pushed")
    H.eq(H.field(pest, "PetName"):ToString(), "Buddy", "pet renamed through FText")
    H.eq(H.calls(pest, "OnRep_PetName"), 1, "pet OnRep_PetName pushed")
    H.eq(H.field(pest, "CurrentHealth_Head"), 100, "pet limb healed")
    H.eq(H.calls(pest, "OnRep_CurrentHealth"), 1, "pet OnRep_CurrentHealth pushed")
    H.eq(H.field(pest, "DynamicProperties")[1].Value, 999, "pet XP written")

    -- ---------- vehicles: wrecked state ----------
    local vehicle = H.world.add(H.object("ABF_Vehicle_ParentBP_C", {
        VehicleID = H.fstring("V1"), VehicleDriveable = false, PendingDestroy = false,
    }, {
        OnRep_VehicleDriveable = function() end,
        K2_GetActorRotation = function() return H.rotator(0, 0, 0) end,
        K2_GetActorLocation = function() return H.vector(0, 0, 0) end,
        K2_TeleportTo = function() end,
    }))
    local vehicleDir = H.ok(H.dispatch("vehicles.list"), "vehicles.list")
    H.eq(vehicleDir.supportsWreckedState, true, "wrecked state now supported")
    H.eq(vehicleDir.vehicles[1].wrecked, false, "vehicle not wrecked initially")
    H.ok(H.dispatch("vehicles.set", { id = vehicleDir.vehicles[1].id, wrecked = true }), "vehicles.set wrecked")
    H.eq(H.field(vehicle, "PendingDestroy"), true, "vehicle marked wrecked")
    H.eq(H.ok(H.dispatch("vehicles.list")).vehicles[1].wrecked, true, "wrecked read back")

    -- ---------- bench upgrades: install AND the "Has Upgrade" probe are both disabled (round 79)
    -- ---------- - see areas/bases.lua's header comment: the fabricated row-handle struct these
    -- ---------- two calls shared crashed the real game with a fatal error, reproducibly, every
    -- ---------- time the Bases tab opened (bases.list used to call "Has Upgrade" automatically
    -- ---------- for every bench). Neither AddUpgrade nor "Has Upgrade" is exercised by this test
    -- ---------- any more; SupportsUpgrades (a plain property) still reports correctly.
    local installed = {}
    local bench = H.world.add(H.object("AbioticDeployed_CraftingBench_ParentBP_C", {
        __bases = { "AbioticDeployed_ParentBP_C" }, SupportsUpgrades = true,
    }, {
        AddUpgrade = function(_, handle) installed[handle.RowName:ToString()] = true end,
        ["Has Upgrade"] = function(_, handle) return installed[handle.RowName:ToString()] == true end,
        OnRep_UpgradeTagContainer = function() end,
        K2_GetActorLocation = function() return H.vector(1, 1, 1) end,
    }))
    local basesDir = H.ok(H.dispatch("bases.list"), "bases.list")
    H.eq(basesDir.supportsBenchUpgrades, false, "unsafe bench upgrades are not advertised")
    H.eq(basesDir.supportsBenchUpgradeRemoval, false, "bench upgrade removal still unsupported")
    local benchRow = basesDir.deployables[1]
    H.eq(benchRow.supportsUpgrades, true, "bench reports SupportsUpgrades")
    H.eq(#benchRow.installedUpgrades, 0, "installedUpgrades always empty now - the probe that filled it is disabled")

    H.fails(H.dispatch("bases.set", { id = benchRow.id, upgradeRow = "TougherBench" }),
        "isn't supported", "installing an upgrade is refused, not attempted with the unverified handle")
    H.eq(H.calls(bench, "AddUpgrade"), 0, "AddUpgrade never called - the crash-causing call is disabled")
    local afterInstall = H.ok(H.dispatch("bases.list")).deployables[1]
    H.eq(#afterInstall.installedUpgrades, 0, "still empty - the refused install did not change anything")

    H.fails(H.dispatch("bases.set", { id = benchRow.id, upgradeRow = "TougherBench", upgradeInstalled = false }),
        "isn't supported", "removing an upgrade is refused, not guessed")
    H.fails(H.dispatch("bases.set", { id = benchRow.id, upgradeRow = "NotARealUpgrade" }),
        "unknown bench upgrade row", "unknown upgrade row rejected")

    -- ---------- dropped items: add (spawn via a scratch inventory slot + drop RPC) ----------
    local droppedInv, droppedIndex
    rawget(pawn, "__methods").Request_DropInventorySlot = function(_, inv, index)
        droppedInv, droppedIndex = inv, index
        return true
    end
    H.ok(H.dispatch("dropped.add", { itemId = "scrap_metal", stack = 5 }), "dropped.add")
    local hotbarInv = H.field(pawn, "CharacterHotbarInventory")
    H.check(droppedInv == hotbarInv, "drop routed through the hotbar (first free slot)")
    H.eq(droppedIndex, 0, "drop used slot 0")
    local afterDrop = H.ok(H.dispatch("inventory.list"))
    local hotbarSlot0
    for _, slot in ipairs(afterDrop) do
        if slot.kind == "hotbar" and slot.slotIndex == 0 then hotbarSlot0 = slot end
    end
    H.eq(hotbarSlot0.itemId, "scrap_metal", "scratch slot carried the item before the drop RPC")
    H.eq(hotbarSlot0.stack, 5, "scratch slot carried the stack")
    H.fails(H.dispatch("dropped.add", {}), "itemId is required", "dropped.add needs an itemId")

    -- ---------- inventory: drop an EXISTING slot (round 79, PlayerInventoryTab's DROP ITEM) ----------
    -- Field name is "slotIndex" throughout, matching the real wire shape LiveInventoryChannel.
    -- DropSlotAsync's DropWire record sends (camelCased by System.Text.Json) - NOT "index". An
    -- earlier version of both this handler AND this test used "index" on both sides, so the
    -- mismatch against the REAL .NET-side field name never showed up here; every live DROP ITEM
    -- click failed with "index is required" regardless.
    droppedInv, droppedIndex = nil, nil
    H.ok(H.dispatch("inventory.set", { edits = {
        { kind = "backpack", slotIndex = 0, itemId = "scrap_metal", stack = 3 },
    } }), "seed backpack slot 0 for the drop test")
    H.ok(H.dispatch("inventory.drop", { kind = "backpack", slotIndex = 0 }), "inventory.drop")
    local backpackInv = H.field(pawn, "CharacterInventory")
    H.check(droppedInv == backpackInv, "existing-slot drop targets the named inventory directly")
    H.eq(droppedIndex, 0, "existing-slot drop used the exact slot named, no scratch-slot routing")
    H.fails(H.dispatch("inventory.drop", { kind = "backpack", slotIndex = 1 }), "already empty", "dropping an empty slot is rejected")
    H.fails(H.dispatch("inventory.drop", { slotIndex = 0 }), "kind is required", "inventory.drop needs a kind")
    H.fails(H.dispatch("inventory.drop", { kind = "backpack" }), "slotIndex is required", "inventory.drop needs a slotIndex")
    H.fails(H.dispatch("inventory.drop", { kind = "backpack", index = 0 }), "slotIndex is required",
        "the old wrong field name (\"index\") is not silently accepted")

    -- ---------- containers: sort ----------
    local sortableInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("scrap_metal") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } } },
    }, { OnRep_CurrentInventory = function() end, SortInventory = function() end })
    H.world.add(H.object("Deployed_Container_ParentBP_C",
        { ContainerInventory = sortableInv }, { K2_GetActorLocation = function() return H.vector(2, 2, 2) end }))
    local sortContainerId = H.ok(H.dispatch("containers.list")).containers[1].id
    H.ok(H.dispatch("containers.set", { id = sortContainerId, edits = {}, sort = true }), "containers.set sort")
    H.eq(H.calls(sortableInv, "SortInventory"), 1, "SortInventory called")
    H.eq(H.calls(sortableInv, "OnRep_CurrentInventory"), 1, "container OnRep pushed after sort")

    -- ---------- containers: one corrupt slot does not fail the whole list (round 79) ----------
    -- slotRow (and readItemDetails underneath it) index several ItemDataTable/ChangeableData
    -- fields directly, unlike slotRowName/containerInventory/actorLocation next to them which are
    -- all pcall-guarded - one slot in a bad state (a container mid-destruction, a modded/DLC
    -- deployable with a different shape) used to raise an uncaught error that failed the ENTIRE
    -- containers.list request, which the editor could only show as a generic "not available",
    -- indistinguishable from no world being loaded. See the per-slot pcall guard added around
    -- slotRow in this handler.
    local corruptSlot = setmetatable({}, { __index = function() error("simulated corrupt slot") end })
    local mixedInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        corruptSlot,
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("bandage") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
    } }, { OnRep_CurrentInventory = function() end })
    H.world.add(H.object("Deployed_Container_ParentBP_C",
        { ContainerInventory = mixedInv }, { K2_GetActorLocation = function() return H.vector(3, 3, 3) end }))
    local afterCorrupt = H.ok(H.dispatch("containers.list"), "containers.list survives a corrupt slot instead of failing outright")
    local sawGoodSlot = false
    for _, entry in ipairs(afterCorrupt.containers) do
        for _, slot in ipairs(entry.slots) do
            if slot.itemId == "bandage" then sawGoodSlot = true end
        end
    end
    H.check(sawGoodSlot, "the sibling well-formed slot next to the corrupt one still comes back")

    -- ---------- containers: a Void Chest reads through GetContainerInventory(), not its own
    -- ContainerInventory property (round 83). Verified against the game's real Blueprint exports:
    -- Deployed_StorageCrate_Void_C overrides GetContainerInventory() to return an inventory owned
    -- by the world's GameState (every placed Void Chest shares one storage pool), while its own
    -- ContainerInventory property stays a genuinely empty, unused decoy - reading that property
    -- directly (the old behavior) always found nothing, no matter what the shared pool held.
    local decoyInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    local sharedInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("rotten_food") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 1,
          CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } },
    } })
    H.world.add(H.object("Deployed_Container_ParentBP_C",
        { ContainerInventory = decoyInv },
        { GetContainerInventory = function() return sharedInv end, K2_GetActorLocation = function() return H.vector(4, 4, 4) end }))
    local voidChest = H.ok(H.dispatch("containers.list"), "containers.list with a Void-Chest-shaped container")
    local sawSharedItem = false
    for _, entry in ipairs(voidChest.containers) do
        for _, slot in ipairs(entry.slots) do
            if slot.itemId == "rotten_food" then sawSharedItem = true end
        end
    end
    H.check(sawSharedItem, "the shared inventory GetContainerInventory() returns wins over the decoy ContainerInventory property")

    -- ---------- containers: health/durability, when the game tracks it (round 83) ----------
    local healthyInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    H.world.add(H.object("Deployed_Container_ParentBP_C",
        { ContainerInventory = healthyInv, CurrentDurability = 40, MaxDurability = 80 },
        { K2_GetActorLocation = function() return H.vector(5, 5, 5) end }))
    local withHealth = H.ok(H.dispatch("containers.list"), "containers.list reports health when the game tracks it")
    local healthyEntry
    for _, entry in ipairs(withHealth.containers) do if entry.health == 40 then healthyEntry = entry end end
    H.check(healthyEntry ~= nil, "a container with real durability reports it")
    H.eq(healthyEntry.maxHealth, 80, "max health reported alongside current")

    local noDurabilityInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {} })
    H.world.add(H.object("Deployed_Container_ParentBP_C",
        { ContainerInventory = noDurabilityInv, MaxDurability = 0 },
        { K2_GetActorLocation = function() return H.vector(6, 6, 6) end }))
    local withoutHealth = H.ok(H.dispatch("containers.list"), "containers.list survives a container with no durability tracking")
    local noHealthEntry
    for _, entry in ipairs(withoutHealth.containers) do
        if entry.x == 6 then noHealthEntry = entry end
    end
    H.check(noHealthEntry ~= nil, "the no-durability container still appears")
    H.eq(noHealthEntry.health, nil, "health omitted (not reported as 0) when MaxDurability is 0")

    -- ---------- narrative NPCs ----------
    local narrative = H.world.add(H.object("NarrativeNPC_ParentBP_C", { IsCorpse = false, NarrativeState = 1 }, {
        SetNewNarrativeState = function(self, value) self.NarrativeState = value end,
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
    }))
    local narrativeDir = H.ok(H.dispatch("narrativenpcs.list"), "narrativenpcs.list")
    H.eq(#narrativeDir.npcs, 1, "one narrative NPC")
    H.eq(narrativeDir.npcs[1].isCorpse, false, "narrative NPC alive")
    H.eq(narrativeDir.npcs[1].narrativeState, 1, "narrative state read")
    H.ok(H.dispatch("narrativenpcs.set", {
        npcs = { { id = narrativeDir.npcs[1].id, isCorpse = true, narrativeState = 2 } },
    }), "narrativenpcs.set")
    H.eq(H.field(narrative, "IsCorpse"), true, "narrative NPC marked a corpse")
    H.eq(H.calls(narrative, "SetNewNarrativeState"), 1, "SetNewNarrativeState called")
    H.eq(H.field(narrative, "NarrativeState"), 2, "narrative state written")

    -- ---------- a joined client cannot write any of this new world state ----------
    local clientPawn = H.clientSession()
    H.fails(H.dispatch("pets.set", { id = "x", isDead = true }), "only the host", "client cannot edit pets")
    H.fails(H.dispatch("vehicles.set", { id = "x", wrecked = true }), "only the host", "client cannot wreck vehicles")
    H.fails(H.dispatch("bases.set", { id = "x", upgradeRow = "TougherBench" }), "only the host", "client cannot install upgrades")
    H.fails(H.dispatch("dropped.add", { itemId = "scrap_metal" }), "only the host", "client cannot spawn dropped items")
    -- Not host-gated (matching inventory.set - see main.lua's own comment on that handler): a
    -- client editing their own connected pawn's own inventory has authority over it.
    rawget(clientPawn, "__methods").Request_DropInventorySlot = function() return true end
    H.ok(H.dispatch("inventory.set", { edits = { { kind = "backpack", slotIndex = 0, itemId = "scrap_metal", stack = 1 } } }),
        "seed the client's own backpack slot 0")
    H.ok(H.dispatch("inventory.drop", { kind = "backpack", slotIndex = 0 }), "a client can still drop from their own inventory")
    H.fails(H.dispatch("containers.set", { id = "x", edits = {}, sort = true }), "only the host", "client cannot sort containers")
    H.fails(H.dispatch("narrativenpcs.set", { npcs = {} }), "only the host", "client cannot edit narrative NPCs")
end
