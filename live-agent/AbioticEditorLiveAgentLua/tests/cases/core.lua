-- The original areas from main.lua: dispatch, player lookup, vitals, inventory, flags, world.
return function(H)
    local pawn = H.hostSession()

    -- ping never touches the game.
    H.eq(H.ok(H.dispatch("ping")).pong, true, "ping")
    H.fails(H.dispatch("no.such"), "unknown command", "unknown command is rejected")

    -- players.list reads the engine PlayerArray; the local player is flagged.
    local players = H.ok(H.dispatch("players.list"), "players.list")
    H.eq(#players.players, 1, "one player")
    H.eq(players.players[1].name, "Tribbes", "player name converted from FString")
    H.eq(players.players[1].isLocal, true, "local player flagged")
    H.eq(players.isHost, true, "host authority")

    -- vitals round trip.
    local vitals = H.ok(H.dispatch("vitals.get"), "vitals.get")
    H.eq(vitals.hunger, 50, "hunger read")
    H.ok(H.dispatch("vitals.set", { hunger = 100, fatigue = 0 }), "vitals.set")
    H.eq(H.field(pawn, "CurrentHunger"), 100, "hunger written")
    H.eq(H.field(pawn, "CurrentFatigue"), 0, "fatigue written")
    H.eq(H.calls(pawn, "OnRep_CurrentHealth"), 1, "OnRep_CurrentHealth pushed once")

    -- inventory: all four kinds, empty sentinel, set + clear.
    local slots = H.ok(H.dispatch("inventory.list"), "inventory.list")
    local kinds = {}
    for _, s in ipairs(slots) do kinds[s.kind] = (kinds[s.kind] or 0) + 1 end
    H.eq(kinds.backpack, 30, "backpack slots"); H.eq(kinds.equip, 13, "equip slots")
    H.eq(kinds.hotbar, 8, "hotbar slots"); H.eq(kinds.transmog, 6, "transmog slots")
    H.eq(slots[1].isEmpty, true, "empty sentinel recognised")
    H.ok(H.dispatch("inventory.set", { edits = { { kind = "backpack", slotIndex = 0, itemId = "scrap_metal", stack = 3 } } }), "inventory.set")
    local after = H.ok(H.dispatch("inventory.list"))
    H.eq(after[1].itemId, "scrap_metal", "item written"); H.eq(after[1].stack, 3, "stack written")
    H.ok(H.dispatch("inventory.set", { edits = { { kind = "backpack", slotIndex = 0, clear = true } } }), "inventory clear")
    H.eq(H.ok(H.dispatch("inventory.list"))[1].isEmpty, true, "slot cleared")

    -- flags: the subsystem + row-handle library shape, host gating.
    local subsystem = H.world.add(H.object("WorldFlagSubsystem", {}, {
        GetWorldFlags = function(_, out) out[1] = H.outParam(H.fname("Office_PowerOn")) return true end,
        SetWorldFlag = function() end,
    }))
    H.world.static("/Script/AbioticFactor.Default__WorldFlagHandleFunctionLibrary", H.object("WorldFlagHandleFunctionLibrary", {}, {
        GetAllWorldFlagRowNames = function(_, out) out[1] = H.outParam(H.fname("Office_PowerOn")); out[2] = H.outParam(H.fname("MapReveal_Security")) end,
        GetAllWorldFlagRowHandles = function(_, out)
            out[1] = H.outParam({ RowName = H.fname("Office_PowerOn"), DataTablePath = "DT_WorldFlags" })
            out[2] = H.outParam({ RowName = H.fname("MapReveal_Security"), DataTablePath = "DT_WorldFlags" })
        end,
    }))
    local flags = H.ok(H.dispatch("flags.list"), "flags.list")
    H.eq(#flags.flags, 2, "two known flags"); H.eq(flags.flags[1].isSet, true, "set flag"); H.eq(flags.flags[2].isSet, false, "unset flag")
    H.ok(H.dispatch("flags.set", { flags = { { name = "MapReveal_Security", isSet = true } } }), "flags.set")
    H.eq(H.calls(subsystem, "SetWorldFlag"), 1, "SetWorldFlag called once")
    H.fails(H.dispatch("flags.set", { flags = { { name = "Nope", isSet = true } } }), "unknown quest flag", "unknown flag rejected")

    -- world clock/weather.
    local manager = H.world.add(H.object("DayNightManager_C", {
        CurrentDay = 22, CurrentTimeInSeconds = 41490, IsNight = false, DayNightManuallyPaused = false,
        CurrentWeatherEvent = H.fname("None"), RequiredDaysBetweenWeather = 3, Weather_RequestByPlayer = { RowName = H.fname("None") },
    }, {
        OnRep_CurrentTimeInSeconds = function() end, OnRep_IsNight = function() end, OnRep_CurrentDay = function() end,
        IsCurrentlyDaytime = function(self) return self.CurrentTimeInSeconds < 75000 end,
        TriggerWeatherEvent = function(self, row) self.CurrentWeatherEvent = row.RowName end,
    }))
    H.world.static("/Script/AbioticFactor.Default__WeatherEventHandleFunctionLibrary", H.object("WeatherEventHandleFunctionLibrary", {}, {
        GetAllWeatherEventRowNames = function(_, out) out[1] = H.outParam(H.fname("Fog")) end,
        GetAllWeatherEventRowHandles = function(_, out) out[1] = H.outParam({ RowName = H.fname("Fog"), DataTablePath = "DT_Weather" }) end,
    }))
    local world = H.ok(H.dispatch("world.get"), "world.get")
    H.eq(world.day, 22, "day"); H.eq(world.currentWeather, "None", "clear weather"); H.eq(world.weatherOptions[2], "Fog", "weather option")
    H.ok(H.dispatch("world.set", { timeSeconds = 80000, weather = "Fog" }), "world.set")
    H.eq(H.field(manager, "IsNight"), true, "night recomputed from IsCurrentlyDaytime")
    H.eq(H.ok(H.dispatch("world.get")).currentWeather, "Fog", "weather triggered")

    -- doors, containers, dropped items: one of each, host-gated writes.
    local door = H.world.add(H.object("SimpleDoor_ParentBP_C", { DoorState = 0, OneWayDoor_HasBeenUnlocked = false, DoorDisabled = false },
        { K2_GetActorLocation = function() return H.vector(1, 2, 3) end, OnRep_DoorState = function() end, DoorUpdateState = function() end }))
    local doors = H.ok(H.dispatch("doors.list"), "doors.list")
    H.eq(#doors.doors, 1, "one door"); H.eq(doors.doors[1].kind, "simple", "hinged door")
    H.ok(H.dispatch("doors.set", { doors = { { id = doors.doors[1].id, kind = "simple", state = 1 } } }), "doors.set")
    H.eq(H.field(door, "DoorState"), 1, "door opened"); H.eq(H.calls(door, "OnRep_DoorState"), 1, "door OnRep")

    local container = H.world.add(H.object("Deployed_Container_ParentBP_C", {
        ContainerInventory = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
            { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("None") },
              ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0,
              CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0, MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0 } } } },
            { OnRep_CurrentInventory = function() end }),
    }, { K2_GetActorLocation = function() return H.vector(4, 5, 6) end }))
    local containers = H.ok(H.dispatch("containers.list"), "containers.list")
    H.eq(containers.containers[1].slots[1].isEmpty, true, "None row counts as empty")
    H.ok(H.dispatch("containers.set", { id = containers.containers[1].id, edits = { { slotIndex = 0, itemId = "scrap_metal", stack = 2 } } }), "containers.set")
    H.eq(H.ok(H.dispatch("containers.list")).containers[1].slots[1].itemId, "scrap_metal", "container slot written")

    local item = H.world.add(H.object("Abiotic_Item_Dropped_C", { HasBeenPickedUp = false, ItemDataRow = { RowName = H.fname("scrap_cloth") },
        ChangeableData = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 3 } },
        { K2_GetActorLocation = function() return H.vector(7, 8, 9) end, InitDespawn = function() end, OnItemDespawn = function() end }))
    local dropped = H.ok(H.dispatch("dropped.list"), "dropped.list")
    H.eq(dropped.items[1].itemId, "scrap_cloth", "dropped row name converted")
    H.eq(H.ok(H.dispatch("dropped.remove", { ids = { dropped.items[1].id } })).removed, 1, "dropped.remove")
    H.eq(H.calls(item, "InitDespawn"), 1, "despawn started")

    -- NPCs.
    local npc = H.world.add(H.object("NPC_Base_ParentBP_C", { IsDead = false, IsDisabled = false, Invincible = false, Faction = 1 }, { OnRep_IsDead = function() end }))
    local npcs = H.ok(H.dispatch("npcs.list"), "npcs.list")
    H.eq(#npcs.npcs, 1, "one npc")
    H.ok(H.dispatch("npcs.set", { npcs = { { id = npcs.npcs[1].id, isDead = true } } }), "npcs.set")
    H.eq(H.field(npc, "IsDead"), true, "npc killed")

    -- A joined client cannot write world state.
    H.clientSession()
    H.world.add(door)
    H.fails(H.dispatch("doors.set", { doors = { { id = doors.doors[1].id, kind = "simple", state = 0 } } }), "only the host", "client cannot edit doors")
    H.fails(H.dispatch("world.set", { timeSeconds = 0 }), "only the host", "client cannot edit the clock")
    H.fails(H.dispatch("flags.set", { flags = {} }), "only the host", "client cannot edit flags")
    H.fails(H.dispatch("npcs.set", { npcs = {} }), "only the host", "client cannot edit npcs")
    H.fails(H.dispatch("dropped.remove", { ids = {} }), "only the host", "client cannot remove items")
    H.fails(H.dispatch("containers.set", { id = "x", edits = {} }), "only the host", "client cannot edit containers")
    H.eq(H.ok(H.dispatch("players.list")).isHost, false, "client reports no authority")
end
