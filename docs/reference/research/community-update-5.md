# Community Update 5: native saves, official mods and multi-quest support

Evidence collected 2026-10-27 from the installed Steam game, version 1.5.0.31459,
Steam build 26092436, UE 5.4.4 changelist 1050002.
[Developer patch notes](https://steamcommunity.com/app/427410/discussions/7/569301136948069172/).

## Mapping and save evidence

`assets/Mappings.usmap` was dumped from the running updated game using the existing
UE4SS installation. Its source filename is
`AbioticFactor-5.4.4-1050002+++DF+ABF-01e0a584.usmap` (1,792,668 bytes).

The game was launched with an isolated `-UserDir` under ignored artifacts. A new
EditorUpdateTest world supplied the three fixtures in `tests/fixtures/Update1_5`.
No existing player world was used for fixture writes. The player header still carries
ABF_SAVE_VERSION 1; world and metadata headers now carry 4. Top-level SaveVersion 4
and native `/Script/AbioticFactor` struct types identify the native layout.

Native fields no longer have Blueprint hash suffixes. Some also changed names:
SurvivalStats, RecipesUnlocked, CurrentXP, XPMultiplier, bCompletedIntro and bDead.
Emails, journal entries, fish and compendium sections moved inside Compendium.
Typed item, recipe, PhD and quest handles carry RowName without a DataTable member.
Global recipe collections are sets. Empty native inventory handles use None.

The reader registers the owner of each native property list without changing the raw
tree. Writers use that ownership to preserve legacy naming and create native fields
when needed. Tests cover unchanged byte-exact player/world/metadata round trips,
native inventory and appearance handles, nested codex edits, PhD, traits, money,
recipe sets, tracking and a first carried pet with no dynamic-property template.
These are serializer checks, not proof of every possible edited save reloading in-game.
Placed native pet transfers and Game Pass saves from this update still need dedicated
game-written fixtures.

## Recipes and catalogs

Native fields and enums are read alongside legacy Blueprint fields. Equipment-slot
names map back to the editor's established slot IDs; numeric native enum ordinals are
not interchangeable with those IDs. Native liquid names also map to the established
liquid choices. Skill perks now come from DT_SkillPerks.

NPC spawn points now select NPCSpawnTags from DT_NPCSpawns. The extractor resolves
the current exact-tag entries to their first listed creature, preserving the existing
representative thumbnail behavior. All ten bundles include 81 spawn mappings.

The base-game dump contains 1,624 items, 584 recipes, 585 appearance variants,
28 pet definitions, 137 distillations and 10 weapon coatings. The new item rows are
Fog Peccary and Tie Bandana; ten Tie Bandana appearance rows are also present. All ten bundled
registries are generated with mods disabled. Recipe ingredients use AnyItemRowHandle;
skill grants use native skill identities. The current progression component's
Server_CheckForFoundRecipeItems calls GetAllRecipeRowHandles and GetRecipeRow,
checks NotUnlockableByPickup and RecipeItems, and uses the native main recipe table.
Missing historical RecipeTable configuration is therefore not a reason to drop the
pickup discovery routes. Workbench grants moved from RecipesToUnlockOnAccess name
arrays to RecipeRowsToUnlockOnAccess typed handles. Reading that field restores the
11 otherwise missing bench-granted learning routes. Each of the 584 recipes again
has at least one recorded learning route, including 186 explicit grants and two
starting recipes. Full-color icons now retain their artwork instead of being treated
as inverted masks; 147 bundled PNGs changed, including the two new items. This does not establish completeness of every alternate
unlock method or item location.

Narrative NPC extraction filters export metadata before loading actors. Its names and
placements are compared with a full-export baseline. Data tables are cached for the
lifetime of a mounted provider, avoiding repeated table decoding for each pet option.

## Official mod support

The editor discovers completed installations in mod.io game 13105's local cache,
including configured storage roots and the Windows public cache. It reads the SDK's
metadata/state.json, accepts installed State 1, and verifies paths remain within that
game's storage directory. Partial downloads, another game's profiles and reparse
paths are excluded. An IoStore archive needs its matching ucas file.

Installed does not mean enabled in every world. A mod.io mod the player has not chosen for
in the editor follows the game's own `EnabledMods` list (`AbioticGameUserSettings` in
`Saved/Config/Windows|WinGDK/GameUserSettings.ini`). An explicit editor choice wins
(`mods-official-enabled.txt` / `mods-disabled.txt`). Selection uses stable mod.io IDs and
affects editor catalogs only. It does not subscribe, install, update, enable or reorder
mods in the game, or enforce multiplayer version agreement.

SDK states: 1 (installed) and 2 (update pending, old version still on disk) load. 0, 3 and
4 (pending, downloading, extracting) plus subscribed ids from the per-user `user.json`
`subscriptions` list are reported as still downloading; 5 (uninstall pending) is ignored.
Without a `state.json`, numeric `13105/mods/<id>` folders are used. Names fall back to the
archive's `.uplugin` `FriendlyName`. Login tokens in `user.json` are never read.

Each world records its mods: `AbioticWorldMetadataSave.LastUsedModIds` (Int64 array in
`WorldSave_MetaData.sav`, game load order). `WorldSaveData.LastUsedModIds` reads it and
`WorldModUsage` compares it with installed/loaded mods. The desktop editor shows this per
world and can switch on exactly that set.

Packaging (all mods checked): a mod.io Windows zip extracts to `13105/mods/<id>/` as
`<Plugin>.uplugin` (Category UGC, CanContainContent) plus
`Content/Paks/Windows/<Plugin>AbioticFactor-Windows.pak/.utoc/.ucas`. Content mounts at
`AbioticFactor/ModPlugins/<Plugin>/Content/` but refers to itself as `/<Plugin>/...`. The
archives carry no `.uplugin`, so CUE4Parse cannot map that root. `GameAssetProvider` now
registers every mounted `ModPlugins/*` and `Plugins/**` content root as a virtual path.
Without it, mod item icons failed to extract and mod blueprint classes did not resolve.

The reader recognizes AbioticModDataTable and official AbioticModConfig assets.
Native ModRowAsset rows use ModPrefix plus RowName for additions and the unchanged
RowName when bOverrideExistingRow is true. The generated DataTables/<target>/ layout
identifies the table; matching native row structs are required before applying rows.
Item rows also populate the global item table. LoadAfter mod.io IDs order the overlays;
cycles and their dependents are skipped rather than applied in arbitrary order.
Generic DataOverrideMapping object replacements are not interpreted as row additions.

A public Windows archive for mod.io #6482657, New syringes, was inspected in ignored
artifacts without installing it. Its NR69O prefix resolved 81 additional item rows and
81 recipe rows through the official row-asset path. This proves catalog extraction for
that archive, not arbitrary Blueprint execution or all future mod packaging styles.
Eight public Windows archives were then extracted into a scratch cache laid out like the
SDK's (not the game's real cache), and mounted on Steam build 1.5.0.31459:

| mod.io id | Mod | What the editor reads |
| --- | --- | --- |
| 6368513 | Linked Deployables | 12 `bOverrideExistingRow` item rows that swap `DeployedItemClass` to `/LinkedBenchPowermFqPC/Deployables/*_Linked` subclasses |
| 6486047 | Jotun LMG | new weapon item (`NU8NS_jotun_lmg`), salvage, FP/weapon animation rows |
| 6485777 | Paul's Goat Hoof | trinket item, recipe, buff |
| 6334435 | Robocart | craftable item, recipe, vehicle blueprint |
| 6485758 | Interdimensional Chemist | PhD trait, gloves item, buffs, job banner |
| 6486490 | Adnormal Traits | five traits plus buffs |
| 6484772 | Craftable PETN | one recipe |
| 6379837 | Additional cosmetic items | 21 `Customization*ModRowAsset` rows (bLocalOnly) |

Results: 4 new items, 3 recipes and 5 traits/PhDs merge; every mod item icon extracts. Linked
Deployables' classes resolve with parents `Deployed_CraftingBench_Default_C` /
`Deployed_Freezer_C`, inheriting RequiresPower, SupportsUpgrades, the storage tag query and
`PaintedDeployableRow`. Its override rows carry 11 of the base row's 43 fields. The editor
applies a full row replacement (DataTable row semantics), which only changes the
catalog's EquipSlot for those benches. A mod class that extends a paintable base class and
keeps a paint row now offers the paint picker. Base-game paintability is unchanged. The
`ABIOTIC_MODIO_ROOT`-gated test `Real_mod_io_mods_supply_items_pictures_and_placed_object_classes`
repeats these checks. No mod was installed into the game and no subscription changed.
No world played with Linked Deployables was available, so its saved placement shape (any
extra plug component in `DeployedObjectMap`) remains an open check.

SDK cache conventions were checked against the
[mod.io SDK source](https://github.com/modio/modio-sdk) and
[Unreal plugin documentation](https://docs.mod.io/unreal/plugin-structure).

### Traders, companions, jobs and traits (2026-10-31)

Fifteen more public Windows archives were added to the same scratch cache (game build 1.5.0):

| mod.io id | Mod | What it does to game data |
| --- | --- | --- |
| 6421993 | Kizz Trader | new trader `MGXP4_KizzMachine` (shown name "IS-0053"), new stock row, 6 food items, 1 buff, conversation and class-manager rows |
| 6472275 | Dr Kemp Companion | new trader/companion `NK932_DrKemp` ("Dr Kemp"), stock, pager item and recipe, an `EntityDirector` class-manager row, its own `Container_DrKemp` backpack and follow AI |
| 6488601 | Gloop Trader | new trader `NW61I_glooptrader` ("PV2 Gloop"), radio item and recipe |
| 6484711 | Loaded Grayson | one `bOverrideExistingRow` row: replaces `trader_grayson` in `DT_NPC_TraderItems` |
| 6483078 | PhD/Job Overhaul | overrides 7 base `DT_PhDs` rows and their buffs (renames Trans-Kinematic Researcher to "Fluid Dynamics Specialist", reprices two), adds 17 items and a trait, replaces 2 base game files |
| 6487749 / 6487498 | Gatekeeper Starting Classes / Order Starting Ranks | 5 / 10 new `DT_PhDs` rows (jobs) with buffs and job banners |
| 6485981 / 6485758 | Metallurgy Inspector / Interdimensional Chemist | one new job each, plus gloves |
| 6485865, 6485893, 6486614, 6486490, 6490728, 6485724 | trait mods | new `DT_Traits` rows with buffs |

Findings:

- Trader rows carry `NPCClass`, `NPCConversation` (its `NPCName` is the shown name), stock row,
  flags and portrait. Mod traders are spawned at run time by their `EntityDirector`, so no
  level names them; the class is the only link from a saved actor back to the trader row.
- `CDT_AllTraits` is a cooked composite with its parents' rows baked in. Mod rows target the
  parents (`DT_PhDs`, `DT_Traits`), so overrides never reached the composite and the editor
  showed the base job name. The provider now applies a composite's parents' mod rows to it.
- Jobs were told from traits by an id prefix (`Trait_`); a mod prefix (`NU0RB_Trait_IBS`) broke
  that. Membership of `DT_PhDs` (mod rows included) now decides.
- Many mods keep the mod editor's placeholder plugin name (`MyProject`, `MyProject_Test`; two
  unrelated mods here share one). Without the SDK index's profile name the editor shows
  `mod.io #<id>` instead. `UUGC_Metadata` holds no name.
- Some archives carry other mods' `AbioticModConfig` assets and copies of base macro
  libraries; harmless for data, but counted as replaced game files.
- No world played with Dr Kemp was available, so the saved shape of a spawned mod companion
  (its `NarrativeNPCMap` key and the backpack container) is still to confirm.

## Multi-quest GATEPal

DT_Quests and DT_QuestLines provide the objective graph, descriptions, world flags,
priorities, subquests and parent quest lines. The new lines include Grayson, Kylie and
Stewart's Lab. Multiple objectives can be active at once; completion wins over a start
flag. Missing world evidence remains unknown. Tutorial objectives have no world flags
and must not all be inferred active; their personal ServerQuest and CompletedIntro
provide the limited character-side evidence.

The file editor's GATEPal includes a quest view with explicit completed/upcoming
filters. Tracking stages a change to Compendium.TrackedQuestLine, a QuestLineRowHandle,
and writes through the existing Save and backup workflow. It does not change ServerQuest
or world story flags. Legacy saves can view progress but cannot write native tracking.
This change does not add quest tracking to the live-editing protocol.

### Live editing review (2026-10-28)

The live agent was checked against the 1.5 class layouts (`LiveClassPropsProbe`). Player
progression (recipes, emails, journal, fish, compendium sections), vitals and the world-wide
unlock lists still exist under the same names, so those live tabs are unchanged. The one
confirmed break was the story indicator: the game state no longer has `CurrentQuest` or
`OnRep_CurrentQuest`. It now has `CurrentQuestline` (a quest-line handle) and
`OnRep_CurrentQuestline`; the game mode has `ActiveQuestLine` and `GetStoryProgressionRow`; each
player controller has `TrackedQuestLine` and `Request_ChangeTrackedQuestLine`; the player state has
`ServerQuestLine`. `story.get` now also reports the quest line, and the desktop app works the
chapter out from the world flags. Chapter moves still go through world flags. Live quest-line
tracking and a live objectives list were added afterwards (`quests.get` / `quests.track`, see the
protocol reference). Both are covered by stub-harness and unit tests only: the tracking call takes
a struct handle and its calling convention, and `FindAllActiveQuests`, have not been confirmed in a
running game.

## Pets

DT_Pets now has explicit PetNPC and PetItem handles. The new DT_PetMutations describes
mutation sources, targets and food. Fog Peccary is present; its listed foods are raw Fog
Fish filet, Phasing Tincture and Fog Slice Nigiri.

BuffActor_Tamed.Server_FedSuccessfully compares saved PetMutation minus one with the
global mutation-row index and writes that index plus one. Fog Peccary is row index 16,
so its native saved target is 17. Assign indices before filtering by source pet.
Older saves retain their legacy family-list numbering and use a separate picker map.
Native DynamicProperty arrays can be created with complete native types even when a
fresh character has never carried a pet or coating before.

The patch's pet teleporting, hunger display, laser targeting and furniture fixes are
game behavior changes. No extra save fields were invented to represent those fixes.

## Reproduction

`CommunityUpdateFiveProbe` is opt-in via ABIOTIC_UPDATE_PROBE_DIR, with optional
ABIOTIC_UPDATE_PROBE_SAVES, ABIOTIC_UPDATE_PROBE_MODS and ABIOTIC_UPDATE_PROBE_ASSETS.
It writes read-only extraction evidence under the requested ignored artifact folder.
Raw config extracts can contain the game's public integration key and must not be
committed. Normal tests never run these research dumps.

Regenerate bundles with `dump-registry --all-cultures --game-version 1.5.0.31459`.
Use `-p:CUE4PARSE_SKIP_NATIVE=true` for builds where the optional native decoder is absent.
