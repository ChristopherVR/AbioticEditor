# Recipe Tree: investigation and proposed design

Status: implemented on 25 October 2026 after mock review. The shared editor now provides crafting steps and a missed-recipe check with explicit unlock actions. Existing save writers and live recipe operations are reused; browsing does not edit saves.

Request: [issue #53, Recipe Tree](https://github.com/ChristopherVR/AbioticEditor/issues/53). Help players find recipes they may have missed, follow recipes back to their ingredients, and understand approximate progression without treating enemy drops as craftable items.

## Recommendation

The existing **Recipe book** remains the entry point. It now provides a per-recipe **Show crafting steps** action and a prominent **Check for missed recipes** action. These answer different questions: how to make an item, and which unlearned recipes are worth investigating. Each action opens its own focused task with a clear return path. Suggestions offer **Unlock recipe** through the same spoiler and progression checks as the book. File changes stage until SAVE, with **Undo unlock** while that recipe is staged. Live operations apply only after the existing game operation acknowledges success.

A recipe dependency graph is feasible with today's catalogs. The later [learning-catalog investigation](./recipe-learning-catalog.md) verifies discovery methods from progression bytecode and source assets, including the standard historical pickup rule. Consuming an ingredient alone does not prove that rule, current stock or complete progression availability.

The interactive wireframe, `recipe-tree-wireframe.html` beside this note in the repository, shows the proposed integration. Open that local HTML file directly in a browser; it is self-contained and makes no network requests. It is a review artifact and is not part of the published documentation site. Recipe definitions and item names come from the committed English registry. Unlocks, seen-item history, story context and gap rankings are fictional demonstration state, labeled in the mock. Unlocks, Undo, SAVE and REVERT in that mock are simulations. The illustration is not a game walkthrough or evidence of discovery rules.

## Repository evidence

| Existing surface | What it supplies | What it does not establish |
| --- | --- | --- |
| `Core/Catalogs/Items/RecipeCatalog.cs`, `RecipeInfo` | Output ID/count, ingredient IDs/counts, required bench IDs, category, table source, duration; extra mod tables discovered by row struct | Discovery conditions, earliest sector, enemy acquisition sources, substitution membership |
| `Web.Shared/Services/RecipeVocabularyService.cs` | Coherent recipe/item/codex snapshots, automatic file/settings fingerprint checks at task boundaries, source/version/checked-time metadata, fallback retry | Bundled/browser data is a snapshot and still requires distribution or import updates |
| `Core/Infrastructure/GameAssets/GameDataRegistry.cs` | Existing offline/browser catalog format, nullable payloads, optional game-version field | Current English snapshot has no `GameVersion` value, so do not invent one |
| `Web.Shared/Components/Player/PlayerRecipesTab.razor` | Search, category/state filters, case-insensitive recipe edits, ingredient/bench details, reverse ingredient index, trader lookup, existing selection inspector | Tree layout, missing-recipe explanations, general progression mapping |
| `Web.Shared/Services/RecipeProgressGateService.cs` | Known email attachment regions can block unlocking; alternatives and recorded grants prevent false universal blocks; sibling or staged Facility flags | No block means no known restriction, not proof of availability; live/standalone context can be unknown |
| `Core/Catalogs/Codex/CodexCatalog.cs` | Explicit email attachment recipe links, fish first-catch recipe grants and catch restrictions | General recipe discovery or a complete enemy-drop map |
| `Core/Catalogs/Codex/TraderCatalog.cs` | Trades and relevant trader/stock story flags | The only acquisition route or proof of current stock/inventory |
| `Core/Domain/Player/PlayerSaveData.cs`, `Web.Shared/Models/PlayerSaveSession.cs` | Learned recipes, historical items picked up/crafted, separate research queue | Pickup history is not current stock; its own model comment describes NEW-badge use |
| `Web.Shared/Models/IPlayerRecipesSession.cs` | Existing file/live recipe boundary; file edits staged until SAVE, live unlocks immediate, with re-locking dependent on agent capability | Historical discovery/story inputs for a general gap report; use a separate read-only context adapter |
| `Web.Shared/Models/IWorldStorySession.cs` | World-wide item discovery and unlock collections | A global unlock is not the selected player's learned recipe; never silently union these scopes |
| `Web.Shared/Services/HostSpoilerPreferences.cs` and recipe tab | Locked recipes concealed by default, reveal/reseal flow | A tree must also seal ingredient labels, counts, search results and tooltips that could disclose hidden recipes |

Paths in the table are relative to `src/`. The architecture puts pure graph and analysis logic in Core, shared UI under Web.Shared, and data loading/session adapters in services. Both the desktop Razor host and browser WASM host use the shared UI; the live player session uses the same recipe interface.

### Measured snapshot

Read `assets/registry/registry.en.json` on 25 October 2026. These counts describe that committed file, not the currently installed game or all mods:

- 1,622 items and 584 recipe rows: 503 Crafting, 48 Soup, 33 Chemistry.
- 1,944 ingredient entries, referring to 369 distinct ingredient IDs.
- 583 distinct produced item IDs. `frecipe_noxious` and `frecipe_unstable` both produce `flask_unstable`, through different inputs. Keep distinct recipe nodes and offer alternative routes.
- Every recipe has an output and at least one input in this snapshot. Future/missing data still needs explicit handling.
- 270 ingredient IDs have no producing recipe in this catalog: 253 known concrete items and 17 groups/unresolved IDs. Of the 253 concrete inputs, 139 have explicit salvage, trader or fishing acquisition evidence. This is not an enemy-drop map.
- 17 distinct ingredient IDs do not resolve to an item row. Eleven begin with `Any` (including `AnyGlue`, `AnyPipe`), one is `BigOfficeCouches`, and five end in `_test`. Treat group-like rows as unresolved requirements until actual substitution rules are extracted. Do not silently match them by prefix or delete the test-like rows.
- Email attachments refer to 16 distinct recipe IDs. Fish grants supply another explicit source of discovery evidence; neither source covers all recipes.
- No cycles found in the ingredient-only item projection. Mods, future rows and bench dependencies still require cycle detection. This is not proof that the graph including stations or every acquisition route is acyclic.
- Repeated input entries occur, including three `distillation_wildcard` entries in `frecipe_unstable`. Sum quantities for display of that recipe while retaining original rows and recipe identity in the underlying data.

The wireframe's Keypad Hacker route is literal registry data: one Controller, one LCD Screen and one Infrared Emitter; all three have producing recipes. It also includes the Peccary Helmet (`gib_peccary_skull`, `AnyGlue`, paper) and both Unstable Flask routes to demonstrate acquisition leaves, unresolved groups and alternatives.

## UX contract

### Two explicit tasks, with the recipe book as home

The earlier mocks used Tree and Missing as navigation labels, mixed the book's filters with a different task, and showed only two levels of dependencies. Matching the app's colors did not make those choices clear. The revised mock removes those labels and the unrelated research queue. It uses actions that say what the player will learn.

1. **Browse recipes in the existing book.** Keep its search, category and All/Locked/Unlocked filters. Selecting an item name opens the established details pane. A per-row **Show crafting steps** action answers "How do I make this?" The Locked filter remains a complete list of unlearned recipes; it is not a missed-discovery report.
2. **Check for missed recipes.** A prominent action above the book filters opens a separate screen. It suggests unlearned recipes supported by a specific condition: verified historical ingredient pickups (including actual substitute membership), known starting/skill/linked discovery conditions, or a granting email/fish recorded for this player. A pickup-disabled recipe is not suggested merely because its crafting ingredients were seen. Each suggestion explains the method and current condition. These remain clues to investigate, not confirmed missed unlocks. The fictional mock state is distinct from the real analyzer. A separate **See all unlearned recipes** action returns to the book's Locked filter.
3. **Show crafting steps for one item.** The item to make is the root of a connected hierarchy. Its consumed ingredients are children; craftable ingredients can expand into their own recipes, recursively to known noncraft materials. Native expand controls, indentation and connector lines make the parent/child relationship explicit. Initial expansion shows the root and one example branch; **Expand all** reveals the complete sample hierarchy and **Collapse ingredients** restores a compact overview. There is no arbitrary two-level truncation. The editor body owns vertical scrolling.
4. **Distinguish Make from Find.** A Make branch has a recipe, a learned/not-learned label, its output yield and its required station. A Find leaf has no producing recipe in the catalog. Its Sources action opens explicit salvage, trader or fishing evidence where available, prioritizing visible/familiar routes with eight initially shown. Unknown acquisition stays unknown: the editor does not infer an enemy drop from the lack of a recipe. A group input such as AnyGlue says acceptable substitutes are not known. Bench requirements are displayed beside recipe steps and are not consumed ingredients.
5. **Count ingredients through every step.** The mock guides making one target item. At each selected recipe, crafts = ceiling(required items / output yield), and child needs = ingredient count per craft multiplied by those crafts. Show surplus when output exceeds need. Shared materials repeat under each branch so each dependency remains legible; the mock makes no claim to optimize batches across branches or subtract inventory. Alternatives use a plain **Choose ingredients** control containing ingredient names, not raw recipe IDs.
6. **Keep navigation and inspection coherent.** Crafting opened from a suggestion returns to the missed-recipe check; crafting opened from the book returns to the book. A branch's Details action changes the existing inspector without replacing the crafting target. Task screens do not show irrelevant book filters, unrelated queue controls or a second navigation system.
7. **Respect uncertainty and spoilers.** Pickup history does not establish current stock. The learning catalog explicitly identifies which table rows use the proven pickup discovery algorithm; other recipes use their recorded methods. Unknown schemas, missing history, source availability and unverified bait/DLC conditions stay unknown. Sources and linked recipes have independent spoiler reveals, even when the target recipe is known. Story ordering stays unknown unless supported by evidence. Conceal hidden names/icons and their dependency branches; search must not reveal them. Standalone files use current staged history without inventing world context. Live metadata unavailable from the agent remains unknown.
8. **Make editing explicit.** Browsing, expanding, route selection and inspecting never unlock recipes, add research, stage changes or send live commands. Each visible suggestion provides **Unlock recipe**. The canonical handler refreshes world gates, refuses blocked recipes with the existing warning, and checks the actual session result before reporting success. File unlocks show **Unlocked, save to apply** and **Undo unlock** until saved; Save/revert update the status from the current session baseline. Live actions are awaited, immediate and respect the current agent capabilities. Unknown save recipe IDs remain preserved. Live target generations prevent a delayed result from appearing under a different player. The shipped research queue is a separate save concept (`RecipesRequiringResearch`), not a feature proposed by this workflow; removing it from the mock does not propose deleting it from the editor.

### Fidelity to the current editor

Claude's bundled frontend-design skill was reviewed. Existing identity takes priority: use the default Dark/Hazard palette (shell `#0c0b07`, page `#16140e`, panel `#201d15`, orange `#f08418`, yellow `#f5c518`, green `#8ccb58`), bundled OpenSans and existing readout typography. Other supported themes remain valid.

The portable mock embeds the current source CSS, fonts, logo and sample item icons. Source references:

- `src/AbioticEditor.Web.Shared/Components/Pages/MainLayout.razor`: masthead, active folder, application actions and status/footer.
- `src/AbioticEditor.Web.Shared/Components/Shared/WorkspaceShell.razor` and `.razor.css`: file list, 16px collapse rails, save title/actions, independent details pane and narrow-window drawers. The mock starts with the file list collapsed and lets users expand it.
- `src/AbioticEditor.Web.Shared/Components/Player/PlayerEditor.razor` and `.razor.css`: horizontal editor sections and viewport.
- `src/AbioticEditor.Web.Shared/Components/Player/PlayerRecipesTab.razor` and `wwwroot/parity.css`: Recipe book controls, compact typography and right-hand recipe card.
- `src/AbioticEditor.Web.Shared/Components/Player/InventorySlotEditor.razor`: current DETAILS host for a tab-supplied card.
- `docs/public/screenshots/13-player-recipes.png` and the local selected-recipe audit screenshot were visually compared with source. Newer source rules take precedence where older screenshots differ.

The small disclaimer and scenario controls remain outside the app shell. The mock simulates explicit unlocks and staged SAVE/REVERT without loading or writing a real save. The visible additions are the two task actions and their guided screens, not redesigned editor chrome.

### Mock review checks

| First-user action | Expected result |
| --- | --- |
| Open the preview | Familiar Recipe book with seven sample targets; no Tree/Missing navigation or research queue |
| Choose Check for missed recipes | Three explained suggestions, distinct from six unlearned recipes; one concise qualification |
| Open a suggestion's crafting steps | Root target, connected ingredients and recursive expandable recipes; Back returns to suggestions |
| Expand the Keypad Hacker hierarchy | All 15 craft-step occurrences and 22 terminal-material occurrences are reachable; no cropped depth |
| Collapse a branch or all ingredients | Parent/sibling context stays visible; no change to recipe unlocks |
| Open Peccary Helmet | Skull is a Find leaf with unknown source; glue remains an unresolved group |
| Choose the other Unstable Flask ingredients | A distinct real recipe; repeated wildcard input entries sum to a quantity of three |
| Inspect a material | Details pane changes while the crafting target stays fixed |
| Reseal locked names or search for a hidden name | Hidden recipes, icons and branches stay concealed |
| Select live-without-history or unavailable data | An honest empty state rather than zero missed recipes |
| Expand files/details, or use a narrow window | Current rails and drawers work; no document-width overflow |

The embedded 16-recipe sample recursively includes every catalog producer for its selectable ingredients. Definitions and counts are real; player unlocks and pickup history are fictional. The hierarchy repeats shared dependencies to show each branch, while the underlying implementation should preserve separate recipe/item identities and alternatives. The mock does not load real saves. The implemented feature does not check current inventory, optimize shared batch plans, establish a complete progression order, or provide verified enemy acquisition sources.

## Implemented scope and verification

- `Core/Services/Items/RecipeGraph.cs`: case-insensitive recipe/item indices, alternative routes, aggregated inputs, yield-aware quantities, conservative pickup-history clues and explicit cycle/depth/node/invalid-data boundaries.
- `PlayerRecipesTab` and shared workflow components: existing Book home, distinct report with Unlock/Undo, recursive connected crafting guide, exact-route inspection and spoiler-safe names/search/relationships. The existing research queue stays in Book.
- `RecipeDiscoveryAssessment` reads explicit email attachment and first-catch grants automatically. Alternatives are OR routes; source access is not a complete prerequisite map. Current staged read/caught histories provide stronger candidates independently of pickup history.
- `RecipeLearningCatalog`, `RecipeSpecialLearningCatalog` and `RecipeLearningAssessment` now cover verified pickup/substitute rules, defaults, skills, links, conversations, traits, compendium, placed inspectables, bench access, cooking and processing. All 584 current bundled definitions have at least one recorded route. Discovery and pending research are separate; methods remain visible for learned recipes. See the dedicated investigation for provenance and limits.
- `dump-registry --recipe-learning-only` safely refreshes compatible snapshots while preserving localized and unknown fields. All ten bundled cultures contain the new versioned metadata; old/future incompatible metadata stays unknown.
- Task entry, missed checks, crafting entry and explicit unlocks compare pak/container, mappings, mod/settings and registry fingerprints. Changed inputs replace recipe, item and codex definitions together; failed installed loads fall back coherently and retry after a bounded delay. Metadata distinguishes source checks from snapshot extraction date. Refresh preserves unknown saved IDs and staged edits, with captured-session/generation guards.
- File sessions expose current pickup history and per-recipe staged state. Live sessions expose target generations and discard stale writes/refresh results after player switching. No serializer or new save-write path was added.
- The original workflow copy is localized in English, French, German, Spanish and Russian. New learning-guide copy uses neutral English resources with the existing fallback; extracted source labels remain in each snapshot's game culture.
- Both shared/desktop and WASM hosts build. The focused verification passes 81 checks, including graph edge cases, real fixture-copy Save/revert, rendered report states, delayed live-target operations and the repository render-key contract.
- The full suite completed with 2,298 passing and one existing Lua-harness skip. Its sole failing render-key policy check was corrected, then passed in the 81-check focused rerun. The broad suite was not repeated.
- Actual desktop-host browser checks used a disposable fixture copy: report reasons, stable card order, Unlock/Undo, global REVERT, SAVE with `.bak`, post-save status, recursive expansion, alternate-route details and a narrow viewport. No user save was edited. Missing installed-game icons use existing placeholders in the offline test profile; native live-game unlocking was not exercised in a running game.

The following phases record the original implementation proposal, rather than remaining release blockers.

## Original implementation plan

### Phase 0: close the evidence boundary (0.5-1 day)

Add a reproducible registry census/probe that reports recipe coverage, duplicate IDs/output routes, missing references, group-like requirements and cycles including station edges separately. Inspect raw recipe structs and recipe-learning blueprint paths from an installed game. Determine whether discovery conditions are already extractable and capture a few before/after discovery examples. Do not make that research a blocker for the honest read-only graph. Return a coverage object with source, optional build and unresolved counts from the vocabulary service.

Acceptance: describe what is known, unavailable and inferred. Inventory at least one email grant, fish grant, group input, multiple-output route and noncraft acquisition leaf. No fabricated chronology.

### Phase 1: Core graph and gap analysis (1-1.5 days)

Introduce immutable item/recipe/requirement nodes and typed `ConsumedBy`, `Produces`, `RequiresStation` and evidence-backed acquisition/discovery edges under `Core/Services/Items/`. Build case-insensitive indices once per catalog revision. Use stable recipe identities rather than output IDs as recipe keys; report collisions instead of silently overwriting a route. Unknown save recipe IDs remain visible/preserved. Aggregate duplicate input quantities only in projections.

Bound traversals by visited path, depth and visible-node budget. Report cycles, collapsed branches and missing definitions explicitly. For ordering, condense strongly connected components before computing dependency depth. Use deterministic tie breaks. No recursive "all ingredients craftable" assumption when a recipe/group/alternative is unknown.

Separate static graph from a read-only player context (learned, seen, crafted, queue, relevant world state, freshness). Analysis returns reasons and confidence, never a boolean "should have". Use alternatives as choices, not AND requirements. Cache static indices but invalidate on catalog/language/mod reload; refresh dynamic suggestions when staged/live state changes.

Acceptance: pure Core tests cover mixed-case IDs (#49 regression), multiple recipes per output, shared inputs, duplicate quantities, counts/yields, station edges, cycles, disconnected rows, unknown IDs, missing catalog sections and unknown context.

### Phase 2: shared crafting and missed-recipe workflows (2-3 days)

Extend `PlayerRecipesTab` with the two explicit task actions and reusable hierarchy/report/inspector components. Reuse `InventorySelectionService`, `ItemIcon`, translations and existing category/recipe filters. Prefer a nested, keyboard accessible disclosure hierarchy with connector lines. Lazy-load icons, preserve expansion state and expose complete recursive expansion within a safe node budget. Report any collapsed budget/cycle boundary explicitly rather than silently clipping depth. Use plain ingredient labels for alternative recipes.

Add Show crafting steps to a book row and Check for missed recipes above the book filters; keep unlock controls separate. The missed-recipe screen contains explained suggestions and a return to all unlearned recipes, rather than another generic locked list. Connect optional file-only discovery history through a context adapter, not a cast scattered throughout the components. Live mode can omit unavailable evidence and must not invent it.

Acceptance: complete keyboard navigation, screen-reader relationship list, spoiler-safe labels/search/tooltips, narrow-window use, selections survive filter changes, empty/loading/error states, file/live and desktop/browser display parity. An unloaded catalog does not become "0 recipes missing".

### Phase 3: validation and release polish (1.5-2 days)

Add focused analyzer and UI contract checks, using synthetic graphs for pathological cases and the committed registry for representative checks. Exercise fixture copies with learned/locked/unknown recipe IDs, stage/revert context updates, no sibling world, missing game data and stale live snapshots. Confirm pure browsing leaves dirty state unchanged and sends no unlock RPCs.

Build both hosts, run focused recipe/round-trip checks and the repository's required checks. Render and interact with desktop and browser at typical and narrow sizes. Measure the full catalog and a deliberately large/modded synthetic graph; target bounded interaction work, not a layout of every row on each keystroke. Re-check translations, contrast and spoiler resealing. No save writer/schema change should be required for this first release.

Acceptance: crafting navigation is responsive, traceable and read-only; suggestions explain their limits; existing recipe editing still passes its round-trip/gating checks.

## Original estimate and follow-on scope

Original planning estimate: **6-9 working days for one developer**: phases 0-3 total 5-7.5 days, plus 1-1.5 days of contingency for integration and review fixes. This assumes the existing catalogs and shared session APIs remain the source, a bounded graph, and no new save writes or comprehensive progression/drop database. It is an engineering estimate from source inspection, not a delivery commitment; it excludes waiting for user/in-game captures and external review. Re-estimate after phase 0 if discovery rules change the scope.

| Optional follow-on | Additional effort for one developer | Boundary |
| --- | --- | --- |
| Reviewed sector/progression hints with coverage reporting | 3-5 working days | Curated verified subset, not automatic perfect chronology |
| Enemy/loot/salvage acquisition extraction and UI | 5-10+ working days | Research allowance; dynamic drops, butcher/salvage rules and game captures may expand it |
| Current inventory quantity planner across containers/stations | Estimate separately | Select inventory scope, aggregate batches/yields, alternative routes and freshness |
| Whole-catalog pan/zoom map, bulk unlock or shared research editing | Estimate separately | Not required to solve the first useful exploration/missing-recipes workflow |

The main risk is misleading certainty: learned state is directly observable, but "missed by this point" is only as good as discovery and acquisition coverage. The second risk is spoilers through graph edges, counts or search. The third is catalog identity/group handling: the data already has alternatives, unresolved requirement groups and mixed-case saves. Keeping evidence, scope and graph topology separate makes these manageable without changing save formats.

## Review decision

Implemented first release: Show crafting steps and Check for missed recipes with ingredient and recorded-grant clues, source-specific learning steps, explicit guarded unlock actions, honest condition states, automatic source-change detection and the existing spoiler policy. Defer a definitive progression tree and comprehensive enemy drop mapping until their data is verified. The implementation and updated mock are ready for review. Complete progression and acquisition extraction remain separate follow-on work.
