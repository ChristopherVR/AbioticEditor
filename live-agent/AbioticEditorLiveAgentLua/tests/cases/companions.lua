-- Carried pets (areas/companions.lua): pet rows live in the SAME backpack/equip/hotbar slot
-- structs inventory.list already reads, plus a DynamicProperties_ array of {Key, Value} entries
-- whose Key is an enum that stringifies as "EDynamicProperty::XP" etc (matches
-- PlayerSaveReader.ReadSlotDynamicInt's own "EndsWith("::"+suffix)" match in the file format).
return function(H)
    local pawn = H.hostSession()

    -- A stand-in for the live enum key: exposes :ToString() the same way FName/FString do, since
    -- companions.lua's own dynamicInt()/setDynamicInt() only ever call :ToString() on it.
    local function enumKey(suffix)
        return setmetatable({}, { __index = { ToString = function() return "EDynamicProperty::" .. suffix end } })
    end

    local function dynamicProps(xp, mutationProgress, petMutation)
        return {
            { Key = enumKey("XP"), Value = xp },
            { Key = enumKey("MutationProgress"), Value = mutationProgress },
            { Key = enumKey("PetMutation"), Value = petMutation },
        }
    end

    -- Put a pet item in equip slot 0 and a plain, non-pet item in backpack slot 0 - companions
    -- should report the occupied slot in equip and skip nothing itself (filtering which rows are
    -- actually pets happens on the .NET side, per the module's own header comment), but it must
    -- still skip genuinely EMPTY slots.
    local equip = pawn.CharacterEquipSlotInventory
    local petSlot = equip.CurrentInventory[1]
    petSlot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName = H.fname("pet_skink")
    petSlot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313.PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9 = H.fstring("Sir Scales")
    petSlot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 40
    petSlot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 100
    petSlot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313.DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = dynamicProps(120, 5, 0)

    local list = H.ok(H.dispatch("companions.list"), "companions.list")
    H.eq(#list.pets, 1, "only the occupied slot is listed (empty backpack/hotbar slots skipped)")
    local row = list.pets[1]
    H.eq(row.kind, "equip", "kind"); H.eq(row.slotIndex, 0, "slot index")
    H.eq(row.itemId, "pet_skink", "item id"); H.eq(row.name, "Sir Scales", "custom name converted from FString")
    H.eq(row.health, 40, "health"); H.eq(row.maxHealth, 100, "max health")
    H.eq(row.xp, 120, "xp read via the enum-tail match"); H.eq(row.mutationProgress, 5, "mutation progress read")
    H.eq(row.petMutation, 0, "pet mutation read")

    -- companions.set: health/xp/mutation are plain numbers and round-trip through the fake the
    -- same way the real struct does.
    H.ok(H.dispatch("companions.set", { kind = "equip", slotIndex = 0, health = 55, xp = 200, mutationProgress = 8 }), "companions.set numbers")
    local afterNumbers = H.ok(H.dispatch("companions.list")).pets[1]
    H.eq(afterNumbers.health, 55, "health written"); H.eq(afterNumbers.xp, 200, "xp written"); H.eq(afterNumbers.mutationProgress, 8, "mutation progress written")

    -- companions.set name: a plain Lua string assigned to an FStrProperty struct field - this is
    -- how UE4SS Lua bindings accept a native FString write (confirmed live in round 76: "a
    -- pet-slot name set and cleared"), so the MODULE writes the raw string directly rather than
    -- guessing at some FString(...) wrapper. The stub's ChangeableData is a plain Lua table (not
    -- a real reflected struct proxy), so it cannot fully model the real engine's own read-back
    -- auto-conversion the way it does for FName (FName(str, EFindName.FNAME_Find) always returns
    -- a proper fake FName) - so this checks the WRITE landed on the raw field directly, rather
    -- than requiring a full round trip back through companions.list for this one field.
    H.ok(H.dispatch("companions.set", { kind = "equip", slotIndex = 0, name = "Lord Scales" }), "companions.set name")
    H.eq(petSlot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313.PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9, "Lord Scales", "the new name was actually written")

    -- companions.set clear: back to the "Empty" sentinel (confirmed live in round 74, not "None").
    H.ok(H.dispatch("companions.set", { kind = "equip", slotIndex = 0, clear = true }), "companions.set clear")
    H.eq(#H.ok(H.dispatch("companions.list")).pets, 0, "cleared slot no longer listed")

    -- Missing slot: player-safe failure, not a Lua error.
    H.fails(H.dispatch("companions.set", { kind = "equip", slotIndex = 999, health = 1 }), "slot not found", "an out-of-range slot fails cleanly")
    H.fails(H.dispatch("companions.set", { kind = "nope", slotIndex = 0, health = 1 }), "slot not found", "an unknown inventory kind fails cleanly")
end
