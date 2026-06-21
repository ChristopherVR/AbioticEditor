# Research: backpack capacity / special slots + missing trait descriptions

Date: 2026-06-12. Probe file: `tests/AbioticEditor.Tests/BackpackTraitProbeTests.cs` (11 facts, all passing
against the live install + Cascade fixtures + the Chrissie save under `Saved/SaveGames/.../Worlds/Chrissie`).

## TASK 1 - Backpack capacity and special slots

### Where capacity lives

`ItemTable_Global` row -> `EquipmentData_100_...` struct -> **`ContainerCapacity_20_DC743D864AC6607638E3D2B6D044F42D`**
(IntProperty). Companion fields in the same struct: `IsContainer_13_...` (True for every backpack),
`ContainerWeightReduction_37_...` (the pack's carry-weight discount, 0–0.42), and
`EquipSlot_5_...` = `E_InventorySlotType::NewEnumerator7` (= `EquipmentSlot_Backpack` per the enum's
DisplayNameMap). There is **no** `NumberOfExtraSlots`-style column; capacity is the single number.

`E_InventorySlotType` DisplayNameMap (from `Blueprints/Data/E_InventorySlotType`):
`0=Hotbar, 1=InventoryBackpack, 2=EquipmentSlots_All, 14(=3)=EquipmentSlot_Torso, 5(=4)=Head, 6(=5)=Legs,
7(=6)=Backpack, 12(=7)=Arms, 13(=8)=Suit, 15(=9)=Headlamp, 16(=10)=Trinket, 17(=11)=Wristwatch,
18(=12)=Hacker, 19(=13)=Shield, 20(=14)=Trinket2, 21(=15)=Companion` (NewEnumeratorN = display order ≠ value).

### Per-backpack table (all 20 `backpack_*` rows in ItemTable_Global)

| Row id | Display name | Capacity (total slots) | Weight red. | Special slots (0-based Inventory_ index) |
|---|---|---|---|---|
| backpack_makeshift | Makeshift Backpack | 15 | 10% | - |
| backpack_small | Basic Backpack | 18 | 15% | - |
| backpack_votv_small | Dunkeltaler Backpack | 18 | 15% | - |
| backpack_radio | Radio Pack | 21 | 12% | - |
| backpack_security | Security Pack | 21 | 14% | - |
| backpack_medium | Military Pack | 24 | 20% | - |
| backpack_chieftain | Chieftain Pack | 24 | 20% | - |
| backpack_heavy | Giant's Pack | 30 | 30% | - |
| backpack_hikingpack | Hiking Pack | 30 | 42% | - |
| backpack_snow | Alpine Pack | 30 | 33% | - |
| backpack_rat | Rat Pack | 30 | 0% | - |
| backpack_jetpack | Jetpack | 30 | 28% | - |
| backpack_longjumppack | Long Jump Pack | 30 | 20% | - |
| backpack_large | Research Pack | 30 | 22% | 0=Shielded(rad), 1=Refrigerated |
| backpack_research_U1a | Field Sample Pack | 30 | 22% | 0,2=Shielded(rad), 1=Warmer(heated) |
| backpack_research_U1b | Cold Storage Pack | 30 | 22% | 0,2=Refrigerated, 1=Freezer |
| backpack_huge | **Voidpack** | 36 | 25% | - (description is just "huge storage capacity") |
| backpack_voidpack_U1a | Crystalline Pack | 36 | 27% | - (recharges battery/laser items - a behavior, not slots) |
| backpack_voidpack_U1b | Glitch Pack | 36 | 27% | 1=Freezer, 6=Shielded, 8=Refrigerated, 13=Warmer |
| backpack_dimension | Pocket Dimension | 42 | 30% | - |

The "Voidpack" the user remembers is row id **`backpack_huge`** (display name "Voidpack"); there is no
`backpack_void` row. The cold/warm/rad packs are the Research-pack family
(`backpack_large`, `backpack_research_U1a/b`) plus the Glitch Pack.

### Where special slots live (NOT in ItemTable_Global)

Chain: **`DT_ItemCosmetics`** (`Blueprints/DataTables/DT_ItemCosmetics`, 298 rows, row name = item id) ->
column **`DataAsset_51_083C190445D5CD182B8E95B43BD31D1F`** (ObjectProperty) -> an
**`InventoryData_Parent_C`** DataAsset under `Blueprints/DataAssets/Inventory/`:

| DataAsset | Used by | RefrigeratedSlots | FreezerSlots | ShieldedSlots | WarmerSlots |
|---|---|---|---|---|---|
| BackpackData_Research | backpack_large | [1] | - | [0] | - |
| BackpackData_ColdStorage | backpack_research_U1b | [0,2] | [1] | - | - |
| BackpackData_FieldSample | backpack_research_U1a | - | - | [0,2] | [1] |
| BackpackData_Glitch | backpack_voidpack_U1b | [8] | [1] | [6] | [13] |

Each asset also carries `SlotAppearanceOverrides` (map: slot index -> `E_InventorySlotAppearance`:
NewEnumerator5=freezer, 6=refrigerated, 7=shielded, 9=warmer skin). These are the only four such assets in
the game; every other backpack has `DataAsset = null` in its cosmetics row.

So special slots are **specific 0-based indices into the player's `Inventory_` array** - at the FRONT
(0/1/2) for the research packs, scattered for the Glitch Pack - never "the last N slots".
(`W_Inventory_PlayerBackpack` reads `SlotAppearanceOverrides`/`SlotTypeToIndex` from this asset at runtime.)

### Save-side behaviour (4 Cascade fixture players + Chrissie)

`Inventory_` length **tracks the equipped backpack exactly** - it is not a fixed array with spare tail:

| Save | Equip[3] (backpack) | Inventory_ length | Capacity from table |
|---|---|---|---|
| Cascade ...479 | backpack_longjumppack | 30 | 30 |
| Cascade ...890 | backpack_huge | 36 | 36 |
| Cascade ...108 | backpack_medium | 24 | 24 |
| Cascade ...042 | backpack_research_U1b | 30 | 30 |
| Chrissie ...479 | backpack_large | 30 | 30 |

`HotbarInventory_` was 8 and `EquipmentInventory_` 13 in every save (Fanny Pack trait claims +2 hotbar;
no sampled save lacks it, so the no-trait base is unverified). Unused slots hold the `"Empty"` sentinel
(some patched saves also show `None`). No save had a backpack-less character, so the length with
`Equip[3] = Empty` is unverified (expect the game to shrink/resize on (un)equip).

### What the editor should do

- **Usable slots** = look up `Equipment[3].ItemId` in ItemCatalog -> `EquipmentData_` -> `ContainerCapacity_`
  (prefix-match both names). The save's `Inventory_` length should already equal it.
- When the editor **changes the equipped backpack**, resize `Inventory_` to the new capacity (pad with
  `"Empty"` slots / truncate trailing slots) so the array matches what the game writes.
- **Special-slot badges**: hardcode (or load from the 4 BackpackData assets via DT_ItemCosmetics) the table
  above; annotate inventory cells whose index appears in the relevant array when the matching pack is equipped.
- `ItemCatalogEntry` currently doesn't surface `ContainerCapacity` - add it (read from the
  `EquipmentData_` sub-struct in `ItemCatalog.BuildEntry`).

## TASK 2 - Missing trait descriptions in CDT_AllTraits

`TraitCatalog.LoadDetailsFrom` is **not** buggy - no hash-suffix/prefix mismatch this time. The exact
columns are `TraitName_7_E2887506467F3946582B49A4AD6F3CE7`, `TraitDescription_8_53B3D8DA4A2E41A6FF24038C38E5F06D`
(both TextProperty/FText), `PointCost_27_1F822A4E48D40334AD58858A1AB4F9F3` (Int) and they're read correctly.

The empty descriptions are **genuinely empty in the game data**: the FText has `HistoryType=None` (no
string at all). Exactly 9 of the 50 rows are affected, and they are precisely the **cut/disabled traits** -
all have `AvailableOnStart_29_... = False` *and* placeholder `TraitIcon = icon_missing`:

> Trait_Agoraphobic, Trait_Fumbler, Trait_Claustrophobic, Trait_Clumsy (icon ok), Trait_Hemophobic (icon ok),
> Trait_Dyslexia, Trait_RestlessSleeper, Trait_Smoker, Trait_Unlucky

These never appear in the in-game character creator. Their buff rows in
`Blueprints/DataTables/BuffsDebuffs/DT_BuffsDebuffs` (e.g. `Debuff_Trait_Dyslexia`) were checked as a
fallback description source - **also empty** (DisplayDescription/TooltipText_Apply blank, icon_missing);
trait buffs are hidden HUD-less buffs, even for implemented traits. There is no alternate text anywhere.

'Forbidden Diet' (Trait_Cannibal) genuinely has `PointCost = 0` - it's a world-acquired trait (eating human
flesh), not a creation pick, so "no points value" is correct data; same for Trait_SunDisk (0) and the
special rows PhD_Iron / Trait_FormerGuard / Trait_EasilyStartled (`AvailableOnStart=False` but fully
described - they're granted by game events/modes, not creator picks).

### Exact fix

1. In `TraitCatalog.LoadDetailsFrom`, also read the **`AvailableOnStart_`** prefix (BoolProperty) into
   `TraitDetail` (e.g. `bool AvailableOnStart`).
2. UI: for traits with an empty/null description, show a stock line like
   *"No in-game description (unused/disabled trait)"* when `AvailableOnStart` is false - or filter such
   rows out of any "pick a trait" list entirely (keep them visible read-only if a save already has one).
3. Show `0 PTS` (or hide the badge) for 0-cost world-obtained traits instead of treating 0 as missing.
4. Optionally treat empty-string descriptions as null: `FText` with `HistoryType=None` stringifies to ""
   via `GenericValue.ToString()`, so coerce `description = string.IsNullOrWhiteSpace(s) ? null : s`.

## Probe inventory (BackpackTraitProbeTests)

- `Dump_BackpackRows_ItemTableGlobal` - all "pack" row ids + full column dump of backpack rows.
- `Dump_SlotRelatedColumns_AcrossItemTable` - deduped flattened column-name census of ItemTable_Global.
- `Dump_PlayerInventoryLengths_VsEquippedBackpack` - the save-side table above.
- `Dump_BackpackSpecialSlotSources` - asset census + Gear_Backpack BP exports (BPs hold no slot data).
- `Dump_BackpackDataAssets_AndSlotTypeEnum` - the 4 BackpackData assets + E_InventorySlotType DisplayNameMap.
- `Scan_WhoReferencesBackpackData` - raw byte scan of all 3227 Blueprints packages -> only DT_ItemCosmetics.
- `Dump_ItemCosmetics_BackpackRows` - the `DataAsset_51_...` wiring rows.
- `Scan_GearBackpackHuge_ForSlotNames` - confirms slot arrays live in the data assets, widget reads them.
- `Dump_AllTraitsRows_FullColumns` / `Dump_ProblemTraitRows_DescriptionColumns` - all 50 trait rows with
  FText internals (HistoryType) proving the data is empty.
- `Dump_BuffRows_ForEmptyDescriptionTraits` - DT_BuffsDebuffs fallback check (negative).
