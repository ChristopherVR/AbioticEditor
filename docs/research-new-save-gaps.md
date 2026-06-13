# Research: gaps against current-version saves (`Saved/SaveGames` deep dive)

Probe: `tests/AbioticEditor.Tests/NewSaveDeepDiveTests.cs` (run 2026-06-11).
Subject: the game's own save tree at `D:\Development\uesave\Saved\SaveGames` - newer
than the checked-in `tests/fixtures/Cascade/` fixture. All 88 non-backup `.sav` files were parsed,
round-tripped, and schema-walked.

## 1. File tree

```
SaveGames/
└─ 76561197993781479/                      (steamid64 account folder)
   ├─ Admin.ini                            171 B   (non-GVAS, ini)
   ├─ PlayerStatsSave.sav                  2.8 KB  [/Script/AbioticFactor.PlayerStatsSave]   ← NATIVE class
   ├─ ScientistCustomization_1.sav         2.5 KB  [Abiotic_CustomizationSave_C]
   ├─ Unlocks.sav                          2.4 KB  [Abiotic_CustomizationUnlocks_Save_C]     ← not modeled
   ├─ UserSettings.sav                     3.0 KB  [Abiotic_SettingsSave_C]                  ← not modeled
   ├─ steam_autocloud.vdf
   ├─ Backups/Cascade/1..5/                306 .sav (rolling copies of Worlds/Cascade, same build)
   └─ Worlds/
      ├─ Cascade/                          62 .sav - engine 5.4.4 build -2146453646 (same build as fixture,
      │                                    newer data: 14.8 MB Facility, story=DarkLens, 40 127 min)
      │  ├─ PlayerData/Player_*.sav        ×4 [Abiotic_CharacterSave_C]
      │  ├─ WorldSave_MetaData.sav         [Abiotic_WorldMetadataSave_C]
      │  └─ WorldSave_*.sav                [Abiotic_WorldSave_C]
      └─ Chrissie/                         22 .sav - engine 5.4.4 build **-2146453647** (NEWEST build,
         │                                 fresh world: story=Labs, 176 min)
         ├─ PlayerData/Player_76561197993781479.sav
         └─ WorldSave_*.sav
```

Every `.sav` is GVAS v3, UE4 pkg 522 / UE5 pkg 1012, 74 custom format entries. The only
build difference is the `Worlds/Chrissie` tree (build id -2146453647 vs -2146453646).

## 2. Parse results (typed readers)

| Reader | Files | Result |
|---|---|---|
| `PlayerSaveReader` | 5 (4 Cascade + 1 Chrissie) | **All OK.** Skills=15, equip=13, hotbar=8 everywhere. |
| `WorldSaveReader` | 79 (incl. both MetaData) | **All OK.** Containers/flags/doors/dropped/NPCs/deployables all read. |
| `CustomizationSaveFile` | ScientistCustomization_1.sav | **OK** - 13/13 known fields, **zero unknown properties**. |
| raw `SaveGame.LoadFrom` | PlayerStatsSave / Unlocks / UserSettings | **All OK** (header-only classes, no custom header). |

No parse failures anywhere, including the newest-build Chrissie files.

## 3. Binary round-trip (corruption canary)

**All 88 non-backup saves round-trip byte-identical** through
`SaveGame.LoadFrom` -> `WriteTo` - including the newest-build Chrissie saves and the
native-class `PlayerStatsSave.sav`. Loading + saving with the typed editors will not
corrupt current-version saves.

## 4. JSON round-trip - **BROKEN (would corrupt on JSON import)**

`SaveGameSerializer` (used by `SaveJsonBridge` / the raw-JSON tab) is **not**
byte-identical for any character/world save, old or new:

```
DIFF [new] Chrissie/Player_*.sav            first diff 0x640
DIFF [new] Chrissie/WorldSave_*.sav         first diff 0x64D
DIFF [new] Chrissie/WorldSave_MetaData.sav  first diff 0x65D
DIFF [old-fixture control] same offsets     ← pre-existing, not a new-version regression
OK   ScientistCustomization_1.sav           (no custom header on this class)
```

Cause: `AbioticWorldSave`/`AbioticCharacterSave` carry a custom header
(`ABF_SAVE_VERSION` marker + `Version=3`, `Id=1` for world saves; `Version` int for
character saves), but **no JSON `ISaveClassSerializer` is registered** for these
classes, so `ConvertToJson` never emits a `CustomHeader` block and `ConvertFromJson`
re-creates the class with `Version=0`/`Id=0`. A save edited through the raw-JSON
import path is silently downgraded to save-version 0 - the game's load-time migration
logic then sees a "version 0" save. The byte length is unchanged, which makes the
corruption invisible until the game reads the version field.

**Fix:** add `SaveClassSerializerBase<AbioticWorldSave>` / `...<AbioticCharacterSave>`
implementations (in a JSON-aware assembly) that write/read `Version`/`Id` in a
`CustomHeader` JSON object. Until then the raw-JSON import feature is unsafe for
character and world saves.

## 5. Player save schema gaps (`CharacterSaveData`, newest build)

Chrissie player save: 32 properties, 24 consumed by `PlayerSaveReader`, 8 not:

| Unconsumed property | Type | Notes |
|---|---|---|
| `CompletedIntro_` | Bool | Listed in no docs; trivially modelable. |
| `LastControlRotation_` | Struct (Vector) | Documented as known-unmodeled. |
| `TransmogDisabledArray_` | Array\<Bool>[13] | Documented as known-unmodeled. |
| `RecipesRequiringResearch_` | Array\<Name>[120] | Documented as known-unmodeled; pairs with `RecipesUnlock_`. |
| `LastHotbarSelection_` | Int | **NEW in build -2146453647** (absent from every -646 save). Cosmetic. |
| `NewestRecipes_` | Array\<Name> | "NEW" badge list; should be cleared/extended when editing recipes. |
| `Compendium_Unread_` | Array\<Name>[48] | Unread-badge list; pairs with compendium edits. |
| `Journal_Unread_` | Array\<Name>[23] | Unread-badge list; pairs with `JournalEntries_`. |

Key-set diff vs old fixture player save (hash suffixes stripped):

- **Added:** `LastHotbarSelection` (only genuinely new field).
- **"Removed":** `FavoritedSlots`, `Compendium_Fish`, `Fish_Unread`, `ItemsDistilled` -
  **not removed by the game**; UE skips properties equal to blueprint defaults
  (delta-serialization). A fresh character simply never favorited/fished/distilled.
- Top-level keys unchanged (`CharacterSaveData`, `SaveIdentifier`, `SaveVersion`).

### Delta-serialization pitfall (editor bugs, not corruption)

The Chrissie save's `CurrentSurvivalStats_` struct contains only
`Thirst/Sanity/Fatigue/Continence` - **`Hunger_` is absent** (it sits at the blueprint
default). Consequences with the current code:

1. `PlayerSaveReader.GetDouble(..., "Hunger_")` falls back to **0** -> the editor shows
   Hunger 0 for a character whose hunger is actually full. Same risk for any
   default-valued stat (the default should be 100, not 0).
2. Every `PlayerSaveWriter.Set*` helper **silently no-ops when the property is absent**
   (`ApplyStats` -> Hunger edit lost; `ApplyFishCaught` on a save without
   `Compendium_Fish_` -> entire edit lost). Nothing is corrupted, but the user's edit
   doesn't stick. Writers should create the missing `FPropertyTag` (clone type/hash
   name from a donor save or the blueprint) instead of skipping.

JSON exports for manual diffing were written to
`%TEMP%\abiotic-newsave-deepdive\player-{new,old}.json`.

## 6. World save schema (largest new save: `Worlds/Cascade/WorldSave_Facility.sav`, 14.9 MB)

Top-level keys: all reader-consumed keys present with expected shapes
(`DeployedObjectMap` Map[937], `WorldFlags` Array[191] of FString, `SimpleDoorMap`,
`SecurityDoorMap`, `DroppedItemMap` Map[230], `NarrativeNPCMap` Map[3],
`CustomInventoryMap` Map[2]).

- **NEW key: `DestructibleMap`** (Map\<Str,Struct>, value = `ActorPath` + `Broken`
  bool; sample entry `...PersistentLevel.IceWall_BP_C_1`). Absent from the old fixture.
  Round-trips fine (preserved untouched); model it if destructible state editing is wanted.
- **Case change: `PortalMap` -> `portalmap`.** Harmless today (unmodeled), but a warning:
  top-level key matching must be case-insensitive if PortalMap is ever modeled, and
  `FindByPrefix` is case-sensitive (`Ordinal`).
- Chrissie (newest build) Facility has **no extra keys** - it's just missing
  `ResourceNodeMap`/`DestructibleMap`/`NarrativeNPCMap`/`NPCSpawnMap`/`PetNPC` because
  the fresh world hasn't populated them. No keys were added by build -647.
- `TimeOfDay` struct: `TimeOfDaySeconds`, `CurrentDay`, `LastAssaultDay`,
  `LastWeatherDay`, `LastPowerLeechDay`.

### `DeployedObjectMap` value struct (937/937 uniform)

Matches the reader's expectations. Two corrections to `docs/world-save-schema.md`
(stale doc, not a game change - old fixture has the same types):

- `ConstructionMode_82_*` is **BoolProperty** (doc says ByteProperty enum).
- `ConstructionLevel_85_*` is **DoubleProperty** (doc says ByteProperty enum).

### `NarrativeNPCMap` value struct

Reader consumes `IsDead` (Bool), `NarrativeState` (Byte), `Location` (Vector). Also
present but unmodeled: `ActorPath` (Struct), `NPCClass` (SoftObject),
`CurrentHealthMap` (Map), `CustomName` (Text), `DynamicProperties` (Array).

### Inventory slot struct (`ChangeableData`) census

Across all container slots: `AssetID`, `CurrentAmmoInMagazine`, `CurrentItemDurability`,
`CurrentLiquid`, `CurrentStack`, `DynamicState`, `LiquidLevel`, `MaxItemDurability`,
`PlayerMadeString` (all modeled) **plus unmodeled `DynamicProperties` (Array),
`GameplayTags` (GameplayTagContainer struct), `TextureVariantRow` (DataTableRowHandle)**.
All three already exist in the old fixture; they survive editing because slot writers
patch in place. `TextureVariantRow` matters if item-skin editing is ever added.

## 7. Account-level files not yet modeled

- `PlayerStatsSave.sav` (`/Script/AbioticFactor.PlayerStatsSave`, native): `Stats_Int`
  Map\<Name,Int> (10 kill-stat counters, e.g. `STAT_KILLS_PEST=418`) + `Achievements`
  Array[51] - local achievement mirror; would pair with `SteamAchievements`.
- `Unlocks.sav` (`Abiotic_CustomizationUnlocks_Save_C`): single `CustomizationUnlocks`
  Array\<Name>[38] - unlocked appearance rows; pairs with `CustomizationCatalog` to
  show locked/unlocked options.
- `UserSettings.sav` (`Abiotic_SettingsSave_C`): `FavouriteRecipesList`,
  `HasCreatedACharacter`, `HasPlayedTutorial`, `UIPopupsSeen`, `HostPreferences`
  (struct: `SinglePlayer`, `Password`), `TutorialHintPopupsSeen`, `TutorialPanelsSeen`,
  `PinnedRecipeList`, `RecentServers`. Low editing value.

All three load and round-trip byte-identical with plain `SaveGame`.

## 8. Prioritized gap list

**Round-trip breaks / corruption risks**

1. **JSON import zeroes `ABF_SAVE_VERSION`** (§4) - affects every character/world save,
   old and new. Register JSON save-class serializers or disable JSON import for these
   classes. *Highest priority: this is the only path that actively damages a save.*

**Fields that changed meaning / behave differently on new saves**

2. **Delta-serialized stats** (§5) - Hunger (and any default-valued property) absent on
   fresh characters: editor shows 0 instead of the default, and edits silently no-op.
   Writers should create missing tags; reader defaults should match blueprint defaults
   (100, not 0).
3. `PortalMap` -> `portalmap` casing change - make any future top-level key lookups
   case-insensitive.

**Fields we should model next**

4. `LastHotbarSelection_` (new in current build, Int - trivial).
5. Unread/NEW-badge arrays: `NewestRecipes_`, `Compendium_Unread_`, `Journal_Unread_`
   (and `Fish_Unread` when present) - should be updated alongside the recipe/compendium/
   journal editors so edited entries don't show stale badges.
6. `RecipesRequiringResearch_` - complements the recipe editor.
7. `DestructibleMap` (world) - new map, `ActorPath`+`Broken`.
8. `TransmogDisabledArray_`, `CompletedIntro_`, `LastControlRotation_` (player).
9. NarrativeNPC extras (`CurrentHealthMap`, `CustomName`) and slot
   `TextureVariantRow`/`GameplayTags` if skin/NPC editing is pursued.
10. Account files: `Unlocks.sav` (customization unlocks), `PlayerStatsSave.sav`
    (stat counters/achievements).

**Doc corrections**

11. `world-save-schema.md`: `ConstructionMode` is Bool, `ConstructionLevel` is Double;
    add `DestructibleMap`, `TimeOfDay` field list, and the `portalmap` casing note.
