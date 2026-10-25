# Where recipes are learned

Investigated against the installed base game on 26 October 2026, with mods disabled.
The current catalog contains 584 recipe definitions: 503 crafting, 48 soup and 33
chemistry recipes. Previously the editor exposed only 26 distinct email/fish learning
grants. The new catalog records at least one supported learning method for every current
recipe ID. That is method coverage, not a complete map of prerequisites, every alternative,
NPC availability, item locations or player access.

The extraction is part of `GameDataRegistry.BuildRecipeLearning` and the coherent recipe
snapshot. The UI separates discovering a recipe from finishing its research and from
finding its materials. Explicit editor Unlock still uses the existing guarded writer.
No quest, conversation, item history or research completion is changed by browsing.

The selected recipe's **How to unlock** view presents this evidence as a connected,
expandable tree. Alternative discovery methods are separate branches. The standard pickup
route requires all input types, with an any-one branch for each actual substitute group.
Linked recipes expand into their own discovery routes, not a fabricated requirement to
finish their research. Only the selected root has the subsequent research phase. Projection
is bounded to 200 nodes and depth 8 with explicit cycle, depth and size boundaries. Source
conditions such as fish bait or approximate email-region access remain contextual notes,
not additional mandatory discovery branches.

## Primary evidence and extraction

The authoritative source is the installed cooked assets, their class defaults and Blueprint
bytecode. Ignored investigation output is under `artifacts/recipe-learning-current` and
`artifacts/recipe-special-investigation`. The former includes the input manifest, raw table
and progression exports, typed catalog, recipe definitions and coverage census. The
manifest records container metadata and the mappings SHA-256. These large game exports
are research artifacts, not committed application resources.

| Method | Distinct current recipe IDs | Authoritative relation |
| --- | ---: | --- |
| Historical ingredient pickup | 391 | Configured recipe table and progression input scan |
| Starting recipes | 2 | Progression class-default arrays |
| Skill level | 42 | `DT_SkillRecipes.Skill`, `LevelGainedAt`, `PerkBuffID` |
| Linked discovery | 66 | Valid recipe-row `LinkedRecipesToUnlock` handles |
| Email attachment | 16 | Email attachment recipe handles |
| First fish catch | 10 | Fish `UnlockRecipeId` |
| NPC conversation | 22 | Selected conversation-line `RecipesToUnlock` |
| Trait/background | 2 | Trait buff class-default `Recipes To Give` |
| Compendium | 10 | Explicit section recipe grants |
| Inspect a placed object | 21 | Level actor `RecipesOnInteract` |
| Cooking experiment | 48 | Cooking proxy soup-table lookup and unlock calls |
| Processing | 33 | Processing bench chemistry-table matching and unlock calls |
| Open a workbench | 15 | Bench class-default `RecipesToUnlockOnAccess` |

Counts overlap and must not be added. The union is 584 of 584 known definitions. There
are 190 raw special-source records: 58 conversation, 4 trait, 10 compendium, 22 placed
inspection, 48 cooking, 33 processing and 15 bench records. Equivalent visible actions
are consolidated in the guide; raw records keep their row, section and object provenance.

### Standard pickup, substitutes and research

Asset: `/Game/Blueprints/Characters/Abiotic_CharacterProgressionComponent`.

- The class-default `RecipeTable` is `DT_Recipes`. Sharing its row structure does not make
  the soup or chemistry tables participate in this algorithm.
- `Server_CheckForFoundRecipeItems` skips `NotUnlockableByPickup` at bytecode statement
  407. It reads `RecipeItems` at 666 and checks historical `ItemsPickedUp` membership at
  1028. For actual `ItemTable_RecipeSubstitutes` handles, it iterates `ItemsOfType` at
  1525/1715/1783 and accepts one recorded alternative. The all-input condition leads to
  `AddPendingRecipesToUnlock` at 2051. Ingredient quantities are not checked.
- `Abiotic_InventoryComponent.CheckForRecipeUnlocks` calls `Server_CheckNewItemPickedUp`
  at 118. The progression event records the item type, then the pending-item path invokes
  the historical scan. This establishes a discovery rule, rather than inferring one from
  a crafting shopping list.
- There are 15 explicit substitute groups. Sixty-four main recipes use them, including
  59 pickup-enabled recipes. `BigOfficeCouches` is a real group despite lacking an `Any`
  prefix. The discovery guide uses table membership, not a name heuristic.
- `Server_TryUnlockRecipe` adds the ID to `RecipesUnlocked` at 213. Research difficulty
  1 or 2 queues the ID in `RecipesRequiringResearch_Array` at 1673; difficulty 0 skips the
  queue. `Server_TryCompleteRecipeResearch` removes the pending ID at 262. The main table
  has 252 difficulty-0, 179 difficulty-1 and 72 difficulty-2 rows.
- The two starting IDs are `recipe_bench_crafting` and `recipe_brick_power`. Both also
  occur in the default pending-research array. A discovered/Unlocked checkbox therefore
  does not establish that research is complete. Current saved pending research takes
  precedence over the table's usual research setting in the UI.

**Plasma Disc:** `recipe_disc_plasma` allows standard pickup discovery. The recorded
requirements are Grinder Disc (`ammo_disc`), Detonation Cord (`detcord`) and Arcane Scrap
(`scrap_arcane`). Picking up one of each item type is the learning condition; the crafting
amounts are separate. Its research difficulty is 0, so no research minigame is listed.
The guide shows each type's historical pickup state for the selected player.

### Skills, links and defaults

`Server_CheckForUnlockedRecipes` reads `DT_SkillRecipes`, compares actual skill identity
and level, and passes `PerkBuffID` to `Server_TryUnlockRecipe` at 485. All 42 grants refer
to existing recipe definitions. Four table row names differ from the actual target ID.
The table is not a second source of crafting recipes.

Blueprint skill enum values are resolved through the actual enum/native mapping and
`DT_Skills.SkillType`, then matched to the editor's canonical save order. Enum suffixes
are not save indices: Accuracy is Blueprint `NewEnumerator1` but save index 6; Cooking
is `NewEnumerator11` but save index 12. The native Fishing value is named `Repair`.

`Server_TryUnlockRecipe` queues explicit linked recipes at 922. The load path schedules
`DelayedLinkedRecipeCheck` after 20 seconds. Only handles pointing to a loaded recipe
table and a real target in that table are accepted. Two malformed current references
remain diagnostics: duct tape points to a crafting-bench item row, and carbon plating
points to missing `recipe_powerdrill`. The bench-press/exercise-bike cycle remains direct
data; neither assessment nor UI recursively follows links without a bound.

### Conversations, traits and compendium

- `NarrativeNPC_ParentBP.TryUnlockCollectiblesAndWorldFlags` selects `BeckoningLines`,
  the game's misspelled `InitalContactMessages`, or `ReturnMessages` from the conversation
  row and finished-line index. At 1201 it calls each player's `Server_TryUnlockRecipe`
  with that selected line's recipe handles. The extracted 27 grant-bearing lines yield
  58 per-recipe records and 22 unique IDs. Co-triggered `WorldFlagsToTrigger` are effects,
  not prerequisites, and are never reinterpreted as availability requirements.
- `CDT_AllTraits` points through its buff row and `DT_BuffsDebuffs.BuffLogic_Object` to
  the buff class defaults. `Buff_Trait_RecipeGranter.Server_OnBuffApply` loops `Recipes
  To Give` and calls `Server_TryUnlockRecipe`. Gardener and Agriculture background grants
  overlap on two gardening recipes.
- `DT_Compendium` has 10 explicit grant-bearing sections. The progression component calls
  native `GetCompendiumRecipesToUnlock` at 5 and then `Server_TryUnlockRecipe` at 397.
  Nine sections have kill requirements; another belongs to the Snowman entry. Section
  kind and kill threshold are retained, but native eligibility may include other context.
  The editor does not pretend to have evaluated all compendium prerequisites.

### Cooking, chemistry and world interactions

- `CookingProxy_BP.Get Next Food Stage Item and Set` calls `FindClosestSoupRecipe` at
  304 and grants the found soup row at 607. `Update Ingredient To Soup Data` calls
  `FindSoupRecipe(DT_SoupRecipes, ...)` at 716 and requests the new recipe at 906.
  The closest-recipe function contains near-match/known-input conditions, not the standard
  all-pickups rule. Guidance directs experimentation with the listed ingredients in a
  cooking pot. It does not call all 48 soup recipes ordinary ingredient-pickup unlocks.
- `Deployed_ProcessingBench_ParentBP.DetermineProcessingOutcome` enumerates
  `DT_ChemistryRecipes` at 341 and checks input matches at 696. Its process/consume path
  calls `Server_TryUnlockRecipe`. These 33 routes come from the explicit handler/table
  relation, not the word Chemistry in a display category.
- Workbench class-default `RecipesToUnlockOnAccess` arrays establish 15 recipe grants.
  The source records retain the actual bench asset and the observed field. Crafting,
  repair and ammo benches contribute overlapping grant families. The UI says to open
  that workbench; a source record does not establish that the player can already build it.
- `InteractableProp_BP.ExecuteUbergraph_InteractableProp_BP` loops `RecipesOnInteract`
  at 838 and requests each unlock. Current placed actors establish 21 recipe IDs,
  including the Pest Dummy and Canaan objects. The loader scans package name maps and
  class metadata, then selects only matching actor exports: 77 level name maps, 29
  selected actors, 20 actors with grants. It does not load every level object or inventory.
  Object display text is inherited from class defaults where available. An absent label
  remains a generic object with its known area; an internal actor ID or crafted output
  name is never substituted as the inspected object's name.

## Limits and update behavior

- At least one method is recorded for every current ID. Some recipes have additional
  methods not represented, including literal scripted grants. NPC story availability,
  some native compendium predicates and complete world-object access remain unassessed.
- Crafted-output pickup is a separate progression route, populated only for eligible
  cached recipes. Caller/passive restrictions are not fully established, so the catalog
  deliberately leaves `OutputPickupDiscoveryEnabled` unknown and does not advertise
  unconditional output pickup or recipe sharing.
- No enemy loot/drop locations are inferred. Material salvage, trader and fish sources
  remain separate from learning sources. Pickup history is not current inventory.
- New metadata has its own explicit schema version. Missing arrays, unknown enum values,
  unresolved substitutes, unavailable research history and old imported snapshots remain
  unknown. A missing ingredient array is distinct from an explicit empty array.
- The installed snapshot reloads learning metadata alongside recipes/items/codex/traders
  when existing input fingerprints change. Extraction stays inside the background,
  generation-guarded load and healthy snapshots are reused. Rendering never scans assets.
  Special-source extraction failure keeps standard metadata and logs its limitation.
- Bundled/browser snapshots are distribution data, not an online service. They require
  an editor update or new import after a game update. A checked-at time is not an asset
  verification or snapshot extraction date.
- `dump-registry --recipe-learning-only` refreshes this optional section in a compatible
  existing snapshot. `--all-cultures -o assets/registry` validates every existing language
  first, preserving all unrelated and unknown JSON fields, names and existing stamps.
  Changed crafting definitions require a full dump, not a misleading metadata overlay.
- File research history is read-only guidance using the existing current staged queue.
  Live sessions without that information retain unknown research state. Existing explicit
  unlock, Undo, SAVE, backups and live target-generation guards are unchanged.

## Verification

Focused checks cover field/schema absence, substitute membership, historical versus stock
semantics, research phases, correct skill identity, link-table/target validation and cycles,
explicit special-source extraction, privacy, duplicate visible actions and additive registry
preservation. The final coverage census joins all route kinds against actual recipe IDs,
not sample data or raw grant counts. UI verification is non-interactive rendered-component
testing: no desktop/browser control was resumed after the user's Escape instruction.
