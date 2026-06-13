# Abiotic Factor Player Save Schema

Save class: `/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C`
(`Player_<steamid64>.sav`). Everything lives under the single `CharacterSaveData`
struct property. All field names carry blueprint hash suffixes - match by prefix.

## Fields we model

| Property prefix          | Type | Notes |
|--------------------------|------|-------|
| `CurrentSurvivalStats_`  | Struct | Hunger/Thirst/Sanity/Fatigue/Continence doubles, 0–100. |
| `CurrentMoney_`          | Int | Vending-machine money; counter caps at 1000 in-game. |
| `EquipmentInventory_`    | Array<Abiotic_InventoryItemSlotStruct> | 13 slots, paper-doll. |
| `HotbarInventory_`       | Array<...> | 8 slots (10 with Fanny Pack). |
| `Inventory_`             | Array<...> | Main backpack. Empty slots use RowName sentinel `Empty`. |
| `Skills_`                | Array<Abiotic_CharacterSkill_Struct> | **Positional** - see below. |
| `Traits_`                | Array<Name> | Internal ids, e.g. `Trait_LeadBelly`. See `TraitCatalog`. |
| `PhD_`                   | Name | Background/job id, e.g. `PhD_HumanBio`. See `TraitCatalog.Backgrounds`. |
| `CharacterHealth_`       | Struct (BodyLimbHealth_Struct) | Head/Torso/L+R Arm/L+R Leg doubles. |

Also modeled: `RecipesUnlock_`, `EmailsRead_`, `JournalEntries_`,
`Compendium_EmailSections_`/`Compendium_NarrativeSections_`/`Compendium_ExplorationSections_`
(read as a union, view-only).

Not yet modeled: `RecipesRequiringResearch_`, `FavoritedSlots_`,
`TransmogDisabledArray_` (13 bool flags), `LastControlRotation_`.

## Transmog and respawn

Researched in `docs/research-customization.md`; modeled on `PlayerSaveData`:

| Property prefix          | Type | Model |
|--------------------------|------|-------|
| `TransmogInventory_`     | Array<Abiotic_InventoryItemSlotStruct> | `TransmogSlots` - 6 slots, the cosmetic item shown over each armor slot. Only the slot's `ItemId` (RowName) drives appearance. Writable via `PlayerSaveWriter.ApplyTransmogSlots` (in-place, index-matched; absent array -> silent skip). |
| `TransmogVisibility_`    | Array<Bool> | `TransmogVisibility` - 12 per-slot "show armor piece" flags. Writable via `PlayerSaveWriter.ApplyTransmogVisibility` (in-place, never resized). |
| `LastSafeWorldLocation_` | Struct (Vector) | `RespawnX`/`RespawnY`/`RespawnZ` - respawn/load-in position (read-only). |
| `LastSafeWorldGUID_`     | Str | `RespawnLevelGuid` - matches a world save's top-level `LevelGUID` (read-only). |
| `TerminalRespawnID_`     | Name | `TerminalRespawnId` - opaque GUID of the registered respawn terminal, a static level actor; not resolvable from save data (read-only). |

The world-side half of a respawn point is the bed claim: a bed deployable's
`CustomTextDisplay_` carries `<steamid64>}|!|{<name>` - see `WorldDeployable.IsBed`,
`OwnerSteamId`, `OwnerName`. Character appearance is *not* in this file at all; it lives
in the per-account `ScientistCustomization_<slot>.sav` (see `CustomizationSaveFile`).

## Narrative content tables

- `DT_Emails` (197 rows): `SubjectLineText_`, `EmailSections_` (array of
  `{EmailSenderName_, EmailText_}`), `Attachments_` (can carry `RecipeToUnlock_`),
  `JournalEntriesToUnlock_`. Save's `EmailsRead_` stores row names.
- `DT_JournalEntries` (138 rows): `Title_`, `Note_`, `Category_`.
- `DT_Compendium` (192 rows): `Title`, `Subtitle`, `Tags`, `Sections[].SectionText`.
- `DT_StoryProgression` rows carry `WorldFlag_` (`{RowName}`) - the per-chapter trigger
  flag (e.g. `Office` -> `Office_NewGameStarted`, `EndGame` -> `EndBossDefeated`); see
  `StoryProgressionCatalog` for the full mapping. The flags themselves live in the
  per-region `WorldSave_*.sav` `WorldFlags` arrays.

## Skills are positional

Every `Skills_` entry carries `SkillName`/`SkillTooltip` TextProperties - but they are
**inert blueprint defaults** (`skill_sprinting` on every entry, verified in the raw
binary). Identity is the array index. The order matches `DT_Skills`
(`AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills`) row order with
the two `DONOTUSE` rows (Engineering, Resilience) skipped:

```
0 Sprinting   1 Accuracy   2 Reloading   3 Sneaking    4 SharpMelee
5 BluntMelee  6 Fishing    7 Crafting    8 Construction 9 FirstAid
10 Agriculture 11 Cooking  12 Fortitude  13 Strength   14 Throwing
```

Editable per entry: `CurrentSkillXP_` (float, cumulative) and
`CurrentXPMultiplier_` (float, job/trait XP bonus).

Level ⇄ XP table (cumulative; wiki v1.0, confirmed against capped end-game save values
that all sit just above the L20 threshold):

```
L1 200      L2 500      L3 940      L4 1572     L5 2464
L6 3699     L7 5379     L8 7587     L9 10417    L10 13950
L11 18242   L12 23307   L13 29101   L14 35776   L15 43310
L16 51631   L17 60608   L18 70354   L19 80755   L20 91655
```

Skill icons: `/Game/Textures/GUI/SkillIcons/skillicon_*` (Fishing uses
`/Game/Textures/GUI/Icons/icon_fishing`).

## World metadata save (`WorldSave_MetaData.sav`)

Class `Abiotic_WorldMetadataSave_C`. Key fields:

- `StoryProgressionRow` (Name) - current main-quest chapter; one of the 37 ordered rows
  of `DT_StoryProgression` (see `StoryProgressionCatalog`): Office -> ... -> EndGame.
- `MinutesPassed` (Int) - playtime.
- `GlobalUnlocks` (Struct) - world-wide recipe/email/journal/compendium arrays.
- `LeyakContainmentIDs`, `ServerEntitlements` (Maps).
