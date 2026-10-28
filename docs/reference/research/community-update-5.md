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

Installed does not mean enabled in every world. Official mods start unselected in the
editor; select the same mods as the world in Settings. Selection uses stable mod.io
IDs and affects editor catalogs only. It does not subscribe, install, update, enable
or reorder mods in the game, or enforce multiplayer version agreement.

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
SDK cache conventions were checked against the
[mod.io SDK source](https://github.com/modio/modio-sdk) and
[Unreal plugin documentation](https://docs.mod.io/unreal/plugin-structure).

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
