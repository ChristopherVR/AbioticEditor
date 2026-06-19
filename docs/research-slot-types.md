# Research: per-item equipment slot types, bench names, steamid in player saves

Probes: `tests/AbioticEditor.Tests/SlotTypeProbeTests.cs` (all guarded - they
skip without a game install / the dedicated-server tree). Run 2026-06-12 against
ItemTable_Global (game build matching `AbioticFactor-5.4.4-1030002` mappings), the
checked-in Cascade fixture, and the user's live server tree
(`b31789b5-.../SaveGames/Server/Worlds/Cascade`).

## Q1 - Which ItemTable column encodes the equipment slot

**Column:** `EquipmentData_100_576D05464F36104AFE501B878255E318`
-> `EquipSlot_5_7DAF59D54ADD37B8594D91A65C47292E` (byte enum, `E_InventorySlotType`).

It is the FIRST column of the EquipmentData struct, alongside (full column list,
verified via Probe1): `CanAutoEquip_7_8C31C786...`, `ArmorBonus_18_84B6DFE7...`,
`HeatResist_52_E754E645...`, `ColdResist_53_B79B2238...`,
`DamageMitigationType_26_A9588042...` (map, usually empty),
`IsContainer_13_34FE7C95...`, `ContainerCapacity_20_DC743D86...`,
`ContainerWeightReduction_37_EDAD1C7B...`, `InventoryPlaceSound_34_091D6ABA...`,
`SetBonus_47_CB28322D...` (RowHandle, e.g. `set_makeshift`).

### E_InventorySlotType - complete DisplayNameMap

`Blueprints/Data/E_InventorySlotType.uasset` is a UserDefinedEnum; its
`DisplayNameMap` IS accessible (tagged property on the export). Full map:

| Enumerator | Display name | Meaning |
|---|---|---|
| NewEnumerator0 | Hotbar | not an equip slot |
| NewEnumerator1 | InventoryBackpack | not equippable (default for non-equipment) |
| NewEnumerator2 | EquipmentSlots_All | wildcard |
| NewEnumerator5 | EquipmentSlot_Head | helmet |
| NewEnumerator6 | EquipmentSlot_Legs | legs |
| NewEnumerator7 | EquipmentSlot_Backpack | backpack |
| NewEnumerator12 | EquipmentSlot_Arms | arms |
| NewEnumerator13 | EquipmentSlot_Suit | full-body suit |
| NewEnumerator14 | EquipmentSlot_Torso | chest |
| NewEnumerator15 | EquipmentSlot_Headlamp | headlamp |
| NewEnumerator16 | EquipmentSlot_Trinket | trinket (both trinket slots) |
| NewEnumerator17 | EquipmentSlot_Wristwatch | watch |
| NewEnumerator18 | EquipmentSlot_Hacker | keypad hacker |
| NewEnumerator19 | EquipmentSlot_Shield | off-hand shield |
| NewEnumerator20 | EquipmentSlot_Trinket2 | second trinket slot (slot id, see note) |
| NewEnumerator21 | EquipmentSlot_Companion | companion pet |

Declaration order in the enum asset: 0,1,2,14,5,6,7,12,13,15,16,17,18,19,20,21 -
i.e. positions 3.. map 1:1 onto save `EquipmentInventory_` indices 0..12
(chest, head, legs, backpack, arms, suit, headlamp, trinket, watch, hacker,
shield, trinket2, companion), matching `research-transmog-appearance.md`.

### Observed per-item EquipSlot values

| Item id(s) | EquipSlot | Slot |
|---|---|---|
| armor_helmet_cqc, armor_helmet_mountaineer | NewEnumerator5 | Head |
| armor_chest_makeshift, armor_chest_forge (39 ids `armor_chest*`) | NewEnumerator14 | Torso/Chest |
| armor_legs_makeshift, armor_legs_forge (33 ids `armor_legs*`) | NewEnumerator6 | Legs |
| armor_arms_makeshift, armor_arms_forge (38 ids `armor_arms*`) | NewEnumerator12 | Arms |
| backpack_large | NewEnumerator7 | Backpack |
| suit_hazmat | NewEnumerator13 | Suit |
| **suit_hazmat_casual** | **NewEnumerator6** | **Legs(!)** - validate per-row, never by id prefix |
| trinket_kylie | NewEnumerator16 | Trinket |
| heatershield, heatershield_U1, heatershield_U2 | NewEnumerator19 | Shield |
| headlamp_default, headlamp_NVG (id family `headlamp_*`; exact "basic" id is `headlamp_default`) | NewEnumerator15 | Headlamp |
| **headlamp_NVG_t2_broken** | NewEnumerator1 | not equippable (broken variant) |
| pocketwatch, watch_stealth, watch_hauling, watch_decoy | NewEnumerator17 | Wristwatch |
| keypad_hacker, keypad_hacker_t2..t5 (exact base id: `keypad_hacker`) | NewEnumerator18 | Hacker |
| bandage (contrast; "bandage_basic" does not exist) | NewEnumerator1 | not equipment |

Notes for strict validation:

- Non-equipment items carry `EquipSlot = NewEnumerator1` (InventoryBackpack), so
  "equippable" = EquipSlot ∉ {0,1,2}.
- No item row was observed carrying `NewEnumerator20`; both trinket UI slots
  accept items whose EquipSlot is `NewEnumerator16`. Treat equipment index 11
  (Trinket2) as accepting slot-type 16 too.
- `armor_*_mountaineer` chest/legs/arms do NOT exist; only
  `armor_helmet_mountaineer` does.

## Q2 - Bench names in the user's real save: VERDICT = data, not a reader bug

- All 26 crafting benches of the live server world live in
  `Worlds/Cascade/WorldSave_Facility.sav` (no other region file has any). 9 carry a
  player-typed name in the deployable struct's top-level
  **`CustomTextDisplay_152_B59A50C74001B5D2234D9E9B0D7CAB7F`** (plain StrProperty,
  no escapes/control chars): `Starter base`, `Hydroplant`, `LodeStone`,
  `Voussoir`, `Mario weird`, `Reactor Underground`, `Reactor Portal World`,
  `Hot Stuff`, `Chaos`. Nothing name-like exists anywhere else in the bench
  struct (full leaf dump checked; `ChangableData_`/`PlayerMadeString` are not
  involved - those exist only on inventory item slots).
- `WorldSaveReader.ReadFromFile` on that exact file populates
  `WorldDeployable.CustomName` for all 9 (asserted equal in
  `Probe3_ServerBenches_CustomTextDisplay_And_ReaderRoundTrip`). The downstream
  chain (`DisplayName` -> `WorldBaseViewModel.Benches`/map labels) all key off
  `CustomName` correctly.
- **Root cause of "no names in our UI": the checked-in fixture
  (`tests/fixtures/SteamSaves/Legacy/Cascade/WorldSave_Facility.sav`) is an older snapshot of
  the same world in which the same bench GUIDs (e.g. `3CF2D146...` = "LodeStone"
  live) have an EMPTY `CustomTextDisplay_`.** The benches were named after the
  fixture snapshot was taken. If the app session showed no names, it was reading
  the stale fixture copy (or a Backups generation), not the live
  `SaveGames/Server/Worlds/Cascade` tree.

## Q3 - steamid64 inside player saves

Byte-scan + parsed-tree scan of `Player_76561197993781479.sav` (fixture AND live
server copy):

- **`SaveIdentifier` (top-level StrProperty) = `"76561197993781479"`** in both
  files - the steamid64 IS stored in content, as ASCII digits in a string
  property near the end of the file (right before `SaveVersion`). There is no
  binary uint64 / UTF-16 occurrence, and no separate UniqueID/PlayerID property.
- Incidental: the live server copy also has inventory slot 8's
  `ChangeableData_.../PlayerMadeString_42_CC0B72B2...` = the same steamid (a
  player-renamed/keycard-style item), so don't treat PlayerMadeString hits as
  identity.

So: player identity = file name AND `SaveIdentifier`. An editor "clone to other
player" feature must rewrite `SaveIdentifier`, not just rename the file.
