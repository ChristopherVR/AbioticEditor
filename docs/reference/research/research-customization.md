# Research: customization, transmog, respawn point, teleport location

Probed with `tests/AbioticEditor.Tests/CustomizationProbeTests.cs` (Probe1–Probe9)
against the `Cascade` fixture saves and the real game install/paks (CUE4Parse + usmap).
All property names below are exact, hash suffixes as observed in the fixtures
(match by prefix - the numeric/hash suffix can differ between game versions).

---

## 1. Character customization (head/face, hair, clothing, colors)

**Not stored in `PlayerData/Player_<steamid64>.sav` and not in any `WorldSave_*.sav`.**
Evidence: a raw byte scan of all four fixture player saves finds no property named
`Customiz*`, `Hair*`, `Voice*`, `Face*` etc. - the only "Customization" hit is the
`/Game/Blueprints/DataTables/Customization/DT_TextureVariants` path used by item
`TextureVariantRow_` row handles.

Customization lives **per Steam account on the local machine**, outside the world folder:

```
%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid64>\ScientistCustomization_<slot>.sav
```

(`<slot>` is the character slot number, e.g. `ScientistCustomization_1.sav`.)
The file is a plain GVAS save (save class `Abiotic_CustomizationSave_C`,
path `/Game/Blueprints/Saves/Abiotic_CustomizationSave`). Its top-level properties are
**flat, un-hashed NameProperty values** - each is a row name into a customization DataTable:

| Property (exact, observed)      | Type         | Sample value         | DataTable (rows enumerated via Probe5) |
|---------------------------------|--------------|----------------------|----------------------------------------|
| `Customization_Head`            | NameProperty | `Head_M01a`          | `DT_Customization_Head` (18 rows: Head_M01a, Head_F01a, ... Head_skeleton, Head_F02chemist) |
| `Customization_HeadAccessory`   | NameProperty | `Glasses_None`       | `DT_Customization_HeadAccessory` (21 rows) |
| `Customization_Wristwatch`      | NameProperty | `Watch_Black`        | `DT_Customization_Watch` (9 rows) |
| `Customization_Tie`             | NameProperty | `Tie_Launch`         | `DT_Customization_Tie` (49 rows) |
| `Customization_UpperBody`       | NameProperty | `UpperBody_Rolled`   | `DT_Customization_UpperBody` (51 rows) |
| `Customization_LowerBody`       | NameProperty | `Pants_Torii`        | `DT_Customization_LowerBody` (32 rows) |
| `Customization_HairStyle`       | NameProperty | `Hair_Professor`     | `DT_Customization_HairStyle` (18 rows) |
| `Customization_HairColor`       | NameProperty | `HairColor_White`    | `DT_Customization_HairColor` (15 rows) |
| `Customization_ShirtColor`      | NameProperty | `ShirtColor_Blue`    | `DT_Customization_ShirtColor` (17 rows) |
| `Customization_Shoes`           | NameProperty | `Shoes_Torii`        | `DT_Customization_Shoes` (17 rows) |
| `Customization_Belt`            | NameProperty | `Belt_Black`         | `DT_Customization_Belt` (14 rows) |
| `customization_beard`           | NameProperty | `Beard_Professor2_A` | `DT_Customization_Beards` (42 rows) - note lowercase `c` in the property name |
| `Customization_IDCard`          | NameProperty | `id_science`         | `DT_Customization_IDCard` (22 rows) |

All tables live under `AbioticFactor/Content/Blueprints/DataTables/Customization/`.
Two additional tables exist that have no save property in this file version:
`DT_Customization_Labcoats` (9 rows) and `DT_Customization_FannyPacks` (1 row) and
`DT_Customization_Makeup` (4 rows).

**Voice:** there is no voice property in any save file (player save, world save,
customization save, `UserSettings.sav`, `PlayerStatsSave.sav`, `Unlocks.sav` - all raw-scanned).
The paks contain exactly two player voice data assets
(`DataAssets/PlayerCharacterVoices/PlayerCharacterVoice_MaleA` / `_FemaleA`), so the voice is
derived from the chosen head's gender, not stored.

**Colors:** "colors" are not free RGB values; they are row choices
(`Customization_HairColor`, `Customization_ShirtColor`, shoe/belt/watch color variants are
separate rows). Each head row carries fixed `ColorA/ColorB/ColorC` FLinearColor columns and a
`SkinTone` enum (`E_SkinTones`) in the DataTable - not editable per player.

Unlocked-but-not-default cosmetics are tracked in the sibling file `Unlocks.sav`
(save class `Abiotic_CustomizationUnlocks_Save_C`).

### Recommended C# model

```csharp
public sealed record CharacterCustomization(
    string Head,            // row in DT_Customization_Head
    string HeadAccessory,   // DT_Customization_HeadAccessory
    string Wristwatch,      // DT_Customization_Watch
    string Tie,             // DT_Customization_Tie
    string UpperBody,       // DT_Customization_UpperBody
    string LowerBody,       // DT_Customization_LowerBody
    string HairStyle,       // DT_Customization_HairStyle
    string HairColor,       // DT_Customization_HairColor
    string ShirtColor,      // DT_Customization_ShirtColor
    string Shoes,           // DT_Customization_Shoes
    string Belt,            // DT_Customization_Belt
    string Beard,           // DT_Customization_Beards (property: "customization_beard")
    string IdCard);         // DT_Customization_IDCard
```

Reader/writer: load the GVAS file, read each top-level `NameProperty` by exact name
(case-insensitive recommended because of `customization_beard`). Populate editor dropdowns
from the corresponding DataTable row keys (`UnlockedByDefault`, `DLCRequirement`,
`AchievementRequired` columns can be used for gating/labels; `DisplayName` column gives the
in-game label, e.g. `Head_M01a` = "Hubert").

---

## 2. Armor transmog state

**Stored per player in `PlayerData/Player_<steamid64>.sav`** inside the
`CharacterSaveData` root struct. Three sibling properties:

| Property (exact, observed) | Type | Shape | Meaning |
|---|---|---|---|
| `TransmogInventory_106_927A0B6647350D453ED2F5B7E007ADDB` | ArrayProperty of `Struct` | 6 elements | The cosmetic item applied over each armor slot. Each element is a full inventory-slot struct: `ItemDataTable_18_...` (RowHandle: `DataTable` + `RowName`) + `ChangeableData_12_2B90E1F74F648135579D39A49F5A2313` (same layout as every other inventory slot: `AssetID_25_...`, `CurrentItemDurability_4_...`, `MaxItemDurability_6_...`, `CurrentStack_9_...`, `CurrentAmmoInMagazine_12_...`, `LiquidLevel_46_...`, `CurrentLiquid_19_...`, `TextureVariantRow_28_...`, `DynamicState_39_...`, `PlayerMadeString_42_...`, `GameplayTags_45_...`, `DynamicProperties_50_...`). |
| `TransmogVisibility_109_1A641CB4456B8440A96AE58F027AD93C` | ArrayProperty of Bool | 12 elements | Visibility toggle per equipment slot (the in-game "hide armor piece" checkboxes). Fixture players: all `True`. |
| `TransmogDisabledArray_145_2BA8A3F74C6661475F021A9999C06090` | ArrayProperty of Bool | 13 elements | Per-slot "transmog disabled" flags. Sample (player ...1479): `[False, True, False, True, False, False, True, True, True, True, True, True, True]`. |

Sample occupied transmog slot (player `76561198128277890`, `TransmogInventory[1]`):
`RowName = armor_helmet_cowl` (row of the item DataTable), `AssetID = C3A909D846289A61DB5AFB8685B2DA37`,
durability 50/25, stack 1. Empty slots use the usual sentinels: `RowName = None` or `Empty`,
`AssetID = "-1"`, stack 0, `LiquidLevel = -1` for the `Empty` form.

The 6 `TransmogInventory` entries line up with the 6 armor-capable equipment slots; only the
`ItemDataTable_...RowName` matters for appearance (the rest is the standard slot payload).
Transmog is therefore **fully editable from the player save** - it is not world/machine state.

### Recommended C# model

```csharp
public sealed record TransmogState(
    IReadOnlyList<InventoryItemSlot> Transmogs,   // 6 slots; reuse existing slot model, RowName = cosmetic item id
    IReadOnlyList<bool> Visibility,               // 12 flags
    IReadOnlyList<bool> Disabled);                // 13 flags
```

Reuse `PlayerSaveReader.ReadInventoryArray` for `TransmogInventory_`; the two bool arrays are
plain `ArrayProperty` of `BoolProperty`.

---

## 3. Home bed / respawn point

Two cooperating pieces:

### a) Player side - `CharacterSaveData` in `Player_<steamid64>.sav`

| Property (exact, observed) | Type | Sample | Meaning |
|---|---|---|---|
| `LastSafeWorldLocation_30_486FB66C419E431BD6B754B0C04C9F5B` | StructProperty (Vector) | `-15752.96, 11353.07, 108.15` | World position used for respawn (and for "where do I load in"). All four fixture players have values within metres of their claimed beds. |
| `LastSafeWorldGUID_98_22FA31304F1D3CE0D223A99ED23D936E` | StrProperty | `EB422B4245ACC9F546C26989FC936F5C` | The level the location belongs to. Matches the `LevelGUID` top-level StrProperty of a `WorldSave_*.sav`; in the fixture all four players match `WorldSave_Facility_Office1.sav` (`LevelGUID=EB422B4245ACC9F546C26989FC936F5C`). |
| `LastControlRotation_69_33E2359F425EBFDFB5CE2D84DCE6AD1B` | StructProperty (Vector) | `347.56, 33.02, 0` | Facing on respawn/load. |
| `TerminalRespawnID_122_768A1D184AC865DC562E39A2765F34BE` | NameProperty | `95CAED254C17360B69B3738E468CD49C` | The respawn terminal the player registered at. Three fixture players share `95CAED25...`; the fourth has `35DCF84F4AC366B8DCBB61A93D9C83C0`. These GUIDs do **not** appear anywhere in any `WorldSave_*.sav` (raw scan, Probe7) - they are editor-assigned GUIDs of static respawn-terminal actors baked into the cooked levels, so treat the value as an opaque FName. |

### b) World side - bed claim in `DeployedObjectMap`

Beds are normal deployables in the owning `WorldSave_*.sav` (fixture: `WorldSave_Facility.sav`),
classes `Deployed_Furniture_CraftedBed_C`, `Deployed_Furniture_CraftedBed_T2_C`
(plus `Deployed_PetBed_Small_C` for pets). The **owner claim is encoded in
`CustomTextDisplay_152_B59A50C74001B5D2234D9E9B0D7CAB7F`** (StrProperty) as:

```
<steamid64>}|!|{<playerName>
```

Samples from the fixture (`WorldSave_Facility.sav`):

| Deployable key (AssetID) | Class | CustomTextDisplay |
|---|---|---|
| `E10C814A41AE7459E478AD9AA96BE8E3` | Deployed_Furniture_CraftedBed_C | `}\|!\|{` (unclaimed) |
| `CF4A2D7141497FFE1CE77C8C02B8C342` | Deployed_Furniture_CraftedBed_T2_C | `76561198128277890}\|!\|{J0K3R` |
| `6C32CBA24F897E3B31DFF0B788D40410` | Deployed_Furniture_CraftedBed_T2_C | `76561197993781479}\|!\|{Tribbes` |
| `F4A53C2940A04A5DE7350FBFA6085DA2` | Deployed_Furniture_CraftedBed_T2_C | `76561198179787042}\|!\|{Mantis` |

Bed position is `Transform_50_85E8B13D40141C9B1308F4BB943BD753 -> Translation` (e.g. Tribbes'
bed at `-15530.87, 11150.30, 11.0`, matching his `LastSafeWorldLocation` `-15752.96, 11353.07, 108.15`).

So: claiming a bed writes the owner string onto the bed deployable, and sleeping/saving updates
the player's `LastSafeWorldLocation`/`LastSafeWorldGUID`. An editor that wants to "move a
player's spawn" should update both sides (or at minimum the player-side triple).

### Recommended C# model

```csharp
public sealed record RespawnPoint(
    double X, double Y, double Z,            // LastSafeWorldLocation_
    string LevelGuid,                        // LastSafeWorldGUID_ (match WorldSave LevelGUID)
    double Pitch, double Yaw, double Roll,   // LastControlRotation_ (stored as a Vector)
    string TerminalRespawnId);               // TerminalRespawnID_ (opaque FName GUID)

public sealed record BedClaim(
    string DeployableId,                     // DeployedObjectMap key / AssetID
    string ClassName,                        // ...CraftedBed... class path
    double X, double Y, double Z,            // Transform translation
    ulong? OwnerSteamId,                     // parsed from CustomTextDisplay before "}|!|{"
    string? OwnerName);                      // parsed after "}|!|{"
```

---

## 4. Player-set teleport location (Personal Teleporter sync)

There is **no standalone "teleport location" property in any save**. The keyword scan over
player saves and world-save top levels (Probe2/Probe3 + raw byte scan) finds `Teleport` only in
item row names (`personalteleporter`, `recipe_personalteleporter`, `recipe_pest_teleporter`,
`recipe_moistureteleporter`).

The player-set teleport target is carried **on the Personal Teleporter item itself**, in its
inventory slot's `ChangeableData_12_2B90E1F74F648135579D39A49F5A2313`:

| Field | Type | Sample | Meaning |
|---|---|---|---|
| `PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9` | StrProperty | `572B5DD1440E8BBA76348C94FE7CF11A,` | **The sync target**: the GUID of the deployable it was synced at, with a trailing comma. In the fixture every target resolves to a `DeployedObjectMap` key of a crafting bench in `WorldSave_Facility.sav` (verified by the existing `TeleporterLinkTests`). |
| `AssetID_25_06DB7A12469849D19D5FC3BA6BEDEEAB` | StrProperty | `85BA63E5431AAA6F12219B97DCF294B7` | The item's own instance GUID - *not* the link. |
| `LiquidLevel_46_...` / `CurrentLiquid_19_...` | Int / Byte | `100` / `E_LiquidType::NewEnumerator8` | Charge: the teleporter stores its energy as "liquid" type 8, level 0–100. |

Three of the fixture teleporters point at the same bench (`572B5DD1440E8BBA76348C94FE7CF11A,`),
one at another bench (`948C8BE8470F20E130F26A9BF7A5676D,`). An unsynced teleporter has an empty
`PlayerMadeString`. The same encoding applies wherever the item sits (player inventory, world
container, dropped item).

### Recommended C# model

```csharp
public sealed record TeleporterLink(
    string ItemAssetId,        // AssetID_ of the teleporter item
    string? TargetDeployableId,// PlayerMadeString_ minus trailing ',' ; null = unsynced
    int ChargeLevel);          // LiquidLevel_ (0..100), CurrentLiquid_ enum value 8
```

Editing: write `"<deployableGuid>,"` into `PlayerMadeString_`; the target must exist as a
`DeployedObjectMap` key (crafting benches are the only observed/valid targets).

---

## File map summary

| Data | File | Where |
|---|---|---|
| Customization (head/hair/clothes/colors) | `%LOCALAPPDATA%\AbioticFactor\Saved\SaveGames\<steamid64>\ScientistCustomization_<n>.sav` | flat top-level NameProperties |
| Voice | nowhere (derived from head gender) | - |
| Transmog | `<world>\PlayerData\Player_<steamid64>.sav` | `CharacterSaveData -> TransmogInventory_106/TransmogVisibility_109/TransmogDisabledArray_145` |
| Respawn point (player side) | `<world>\PlayerData\Player_<steamid64>.sav` | `CharacterSaveData -> LastSafeWorldLocation_30 / LastSafeWorldGUID_98 / LastControlRotation_69 / TerminalRespawnID_122` |
| Bed claim (world side) | `<world>\WorldSave_<level>.sav` | `DeployedObjectMap[guid] -> CustomTextDisplay_152` = `steamid}\|!\|{name` |
| Teleport target | wherever the `personalteleporter` item slot is | `ChangeableData_12 -> PlayerMadeString_42` = `<benchGuid>,` |
