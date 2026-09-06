-- Multiple connected players (main.lua): players.list's isLocal flag, and playerId targeting a
-- SECOND connected player for vitals/inventory (skills already share the same resolvePlayer()
-- path and is covered indirectly). None of these are host-gated - a player's own vitals/inventory
-- are player-owned data, the same category the file editor already treats without a host check.
return function(H)
    local localPawn = H.hostSession()

    local guestInventory = { CurrentInventory = {} }
    for i = 1, 5 do
        guestInventory.CurrentInventory[i] = {
            ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
            ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = {
                CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0,
                CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0,
                MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0,
            },
        }
    end
    local guestPawn = H.object("Abiotic_PlayerCharacter_C", {
        CurrentHunger = 10, CurrentThirst = 20, CurrentSanity = 30, CurrentFatigue = 40, CurrentContinence = 50,
        CurrentMoney = 0, CurrentHealth_Head = 100, CurrentHealth_Torso = 100, CurrentHealth_LeftArm = 100,
        CurrentHealth_RightArm = 100, CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
        CharacterEquipSlotInventory = H.object("Abiotic_InventoryComponent_C", guestInventory, { OnRep_CurrentInventory = function() end }),
    }, {
        HasAuthority = function() return false end, -- a joined guest's own pawn is not authoritative on THIS process.
        OnRep_CurrentHealth = function() end,
        Request_ModifyMoney = function() end,
    })
    local guestState = H.object("Abiotic_PlayerState_C", {
        PawnPrivate = guestPawn, PlayerNamePrivate = H.fstring("Guest"), UniquePlayerID = H.fstring("11111111111111111"),
    })
    table.insert(H.playerStates, guestState)
    H.world.add(guestPawn)

    -- players.list: both connected, only the actual local one flagged.
    local list = H.ok(H.dispatch("players.list"), "players.list")
    H.eq(#list.players, 2, "two connected players")
    local localRow, guestRow
    for _, p in ipairs(list.players) do
        if p.name == "Tribbes" then localRow = p end
        if p.name == "Guest" then guestRow = p end
    end
    H.check(localRow ~= nil and guestRow ~= nil, "both players named correctly")
    H.eq(localRow.isLocal, true, "the local player is flagged")
    H.eq(guestRow.isLocal, false, "the guest is not flagged as local")
    H.eq(list.isHost, true, "this process still reports its own authority correctly")

    -- vitals.get/set targeting the guest by playerId: reads/writes THAT pawn, not the local one.
    local guestVitals = H.ok(H.dispatch("vitals.get", { playerId = guestRow.id }), "vitals.get for the guest")
    H.eq(guestVitals.hunger, 10, "the guest's own hunger read, not the local player's")
    H.ok(H.dispatch("vitals.set", { playerId = guestRow.id, hunger = 99 }), "vitals.set for the guest")
    H.eq(H.field(guestPawn, "CurrentHunger"), 99, "the guest's hunger written")
    H.eq(H.field(localPawn, "CurrentHunger"), 50, "the local player's own hunger is untouched")

    -- inventory.list/set targeting the guest by playerId.
    local guestSlots = H.ok(H.dispatch("inventory.list", { playerId = guestRow.id }), "inventory.list for the guest")
    H.eq(#guestSlots, 5, "only the guest's own 5 equip slots, not the local player's 30+13+8+6")
    H.ok(H.dispatch("inventory.set", { playerId = guestRow.id, edits = { { kind = "equip", slotIndex = 0, itemId = "bandage", stack = 1 } } }), "inventory.set for the guest")
    H.eq(H.ok(H.dispatch("inventory.list", { playerId = guestRow.id }))[1].itemId, "bandage", "the guest's own slot written")
    H.eq(H.ok(H.dispatch("inventory.list"))[1].isEmpty, true, "the local player's backpack slot 0 is still empty")

    -- Unknown playerId: player-safe failure, not a Lua error.
    H.fails(H.dispatch("vitals.get", { playerId = "no-such-player" }), "player not found", "unknown playerId fails cleanly on vitals.get")
    H.fails(H.dispatch("inventory.list", { playerId = "no-such-player" }), "player not found", "unknown playerId fails cleanly on inventory.list")
end
