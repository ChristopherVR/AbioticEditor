# Performance + memory-leak review (2026-06-12)

Scope: AbioticEditor.Core + AbioticEditor.App view-models. Fixes applied everywhere
except the concurrently-edited files (`MainPage.xaml`, `MainPage.xaml.cs`,
`MainViewModel.cs`) - findings there are recommendations only. Suite green after the
changes: 296/296 (`dotnet test tests/AbioticEditor.Tests`).

## Findings

| # | Issue | Severity | Status | Where |
|---|-------|----------|--------|-------|
| 1 | `WorldLevelIndex.TryReadLevelGuid` read **entire world saves (15 MB+) via `File.ReadAllBytes`** for every `WorldSave_*.sav` in the folder scan -> repeated LOH allocations on every player-save load (respawn level picker). | High | **Fixed** - streams the file in 256 KB chunks with a carried overlap window; same match semantics (needle + 32-hex run + NUL), SIMD `Span.IndexOf`. | `Core/WorldSaves/WorldLevelIndex.cs` |
| 2 | `ApplyFlagFilter` rebuilt **every `FlagItemViewModel` on every keystroke** of the filter box; with SHOW INACTIVE on it also recomputed `MissingPrerequisitesFor` per catalog flag, where each prereq check was a **linear scan of the active-flag ObservableCollection** -> O(flags × prereqs × activeFlags) per keystroke. | High | **Fixed** - item VMs cached in `_flagItemCache` and rebuilt only when the flag set (or the inactive toggle / async facility flags) changes; keystrokes only filter+group. `HasWorldFlag` now hits a synced `HashSet` (O(1)). | `App/ViewModels/WorldEditorViewModel.cs` |
| 3 | `StoryProgressionCatalog.IndexOf` allocated a **new `List` per call** (`Chapters.ToList().FindIndex`); `ChapterForFlag`/`Find` were linear scans. All three sit on per-flag hot paths (flag rebuilds, grouping, `FlagItemViewModel` ctor). | Medium | **Fixed** - precomputed `RowIndex` and `ChapterByTriggerFlag` dictionaries (OrdinalIgnoreCase), no per-call allocation. | `Core/WorldSaves/StoryProgressionCatalog.cs` |
| 4 | `FlagGate.PrerequisitesFor` / `RegionChapterFor` re-derived results (string parsing + chapter scans + list allocs) for the same flags on every rebuild. | Medium | **Fixed** - memoized in `ConcurrentDictionary` caches; key space bounded by the flag vocabulary (~109 known flags + save-specific ones). | `Core/WorldSaves/FlagGate.cs` |
| 5 | Bulk flag mutations (UNLOCK STORY THROUGH HERE ≈ 37 adds, trader unlock, prereq fill, revert) fired `Flags.CollectionChanged` -> **full list rebuild per added flag** (O(n²) rebuild churn), plus per-add story-timeline notifications. | Medium | **Fixed** - `RunFlagBatch` suppresses per-item refresh and does one rebuild + one timeline notification at the end. | `App/ViewModels/WorldEditorViewModel.cs` |
| 6 | Single-flag operations (`ToggleFlag`/`AddFlag`/`RemoveFlag`) rebuilt the visible list **twice** (once via `CollectionChanged`, once via redundant explicit `ApplyFlagFilter()+Refresh()`). | Low | **Fixed** - the `CollectionChanged` handler is the sole refresher. | `App/ViewModels/WorldEditorViewModel.cs` |
| 7 | `ToggleFlagCommand`, `RemoveFlagCommand`, `ClearCategoryFilterCommand` returned a **fresh `RelayCommand` on every getter access** (allocation per binding evaluation; orphans `CanExecuteChanged` - the documented project gotcha). | Low | **Fixed** - stored instances via `??=`. | `App/ViewModels/WorldEditorViewModel.cs` |
| 8 | `RecipeListViewModel.ApplyFilter` spawned **one `Task.Run` per visible row** for icon extraction - the first unfiltered pass queues ~600 tasks (thread-pool flood); also constructed for every world editor via `GlobalRecipeBrowser`. | Medium | **Fixed** - unrequested rows are claimed (`TryClaimIconRequest`) and extracted on **one** background task per filter pass; `EnsureIcon` kept for single-row use (detail pane). Repeat passes are no-ops per row. | `App/ViewModels/RecipeListViewModel.cs` |
| 9 | `PlayerEditorViewModel.BuildSkillList` extracted skill-icon textures **synchronously during editor construction** (UI thread; first run per machine decodes 15+ textures). | Medium | **Fixed** - VMs build with null icons, one background task extracts and assigns; `SkillViewModel.IconPath` now raises `PropertyChanged` so icons pop in. | `App/ViewModels/PlayerEditorViewModel.cs`, `SkillViewModel.cs` |
| 10 | Hot-path `EditorLog.Info` calls **interpolated the log message even when logging is disabled** - slider drags (vitals, durability, XP) allocate a string per tick. | Low | **Fixed** - guarded with `EditorLog.Enabled` in `InventorySlotViewModel.Set`, `PlayerEditorViewModel.Set`, `SkillViewModel.Xp`. | `App/ViewModels/*` |
| 11 | `MainViewModel._benchCache` / `_worldFlagCache` are **static dictionaries keyed by facility path that grow unbounded** across folders/sessions and pin entire `WorldDeployable` lists (thousands of records per world) for the process lifetime. | Medium | **Recommended** (constrained file) - see below. | `App/ViewModels/MainViewModel.cs` |
| 12 | `ProgressContext.WorldFlags` (static) keeps the **previous world's flags after switching folders** - the codex/recipe gates can then judge a player against the wrong world until a facility save is re-parsed. | Low | **Recommended** (constrained file) - clear it in `LoadFolderAsync`. | `App/Services/ProgressContext.cs` + `MainViewModel.cs` |
| 13 | `PlayerEditor`/`WorldEditor` setters **unsubscribe `OnEditorContextChanged` before `Set`**; re-assigning the same instance would silently drop the subscription (`Set` returns false -> no resubscribe). Currently unreachable (loads always null first), but fragile. | Low | **Recommended** (constrained file) - unsubscribe inside the `if (Set(...))` branch using the captured old value. | `App/ViewModels/MainViewModel.cs` |

## Verified non-issues (audited, no action)

| Area | Verdict |
|------|---------|
| Event-handler leaks across editors | None found. Slot/skill/door/NPC `PropertyChanged` subscriptions are **intra-editor** (editor subscribes to objects it owns); editor + children become garbage together on switch. `MainViewModel` correctly detaches `OnEditorContextChanged` from outgoing editors. `TraderCardViewModel`'s `_worldHasFlag` closure captures only its own editor. `StoryTimeline`/ctor `Flags.CollectionChanged` lambdas are self-subscriptions. |
| `ProgressContext.Notify` | Holds a closure over the shell VM - an app-lifetime singleton, so no growth. (It does keep the *last* status message path alive; harmless.) |
| Texture cache growth | `ExtractTextureAsPng` checks the on-disk cache (`File.Exists`) before any decode - **no re-extraction**; cache size is bounded by the set of distinct game assets actually viewed. `IconColorizer` likewise caches per (mask, tint suffix). No unbounded in-memory texture cache exists. |
| `GameDataServices` | Load-once catalogs + reverse lookups; bounded by game data; dictionary-backed `Find` (no scans). |
| `InventorySlotViewModel._specialChains` | Static but computed once after the catalog loads; a handful of small arrays. |
| `EditorLog.SeenUnknown` | Deduped per (area\|key), cleared on every folder load (`ResetUnknownDedup`); bounded. Log files pruned to 7. |
| UI virtualization (report-only - XAML constrained) | All large lists already use `CollectionView` (flag groups, doors, dropped, containers, recipes, codex, traders, bases, chapters). `BindableLayout` is used only for small, fixed-size collections: skills (15), milestone chips, traits, transmog toggles (12), appearance fields (13), narrative NPCs (~a dozen), per-base benches, category chip rows. Dropped items are capped at 200 rows, the item palette at 72 - fine as-is. |

## Allocation sanity (reasoned)

Worst pre-fix path: flag filter keystroke with SHOW INACTIVE on ≈ 109 inactive + ~100
active `FlagItemViewModel` constructions (each doing a linear 37-chapter scan + flag
parse), plus ~109 × `PrerequisitesFor` (each allocating a per-call chapter list and
doing up-to-36 linear `ObservableCollection.Contains` scans), plus one `List` alloc per
`StoryProgressionCatalog.IndexOf` call in grouping - several hundred thousand string
comparisons and ~1k transient allocations per keystroke. Post-fix the same keystroke is
a filter + group over ~200 cached VMs with dictionary lookups. The folder-scan path no
longer allocates a 15 MB+ LOH array per world file (a Cascade-style folder has dozens).

## Notes

- `UsmapInstallTests.cs` (added concurrently by another session) had a compile error
  (`dir` local shadowing) that blocked the test suite; renamed the inner local to
  `tempDir`. No behavioral change.
- One transient `SchemaDumpTests.DumpWorldFacilityAsJson` failure (file lock during a
  concurrent fixture move) passed on re-run; full suite green at 296/296.

## Recommendations for the constrained files (apply when free)

1. **`MainViewModel._benchCache` / `_worldFlagCache`** - make them instance fields (the
   shell VM is a singleton anyway) or clear both in `LoadFolderAsync`; better, keep only
   the *current* folder's entry (single-slot cache keyed by facility path). They
   currently pin every visited world's deployables forever.
2. **`ProgressContext.WorldFlags`** - set to `null` in `LoadFolderAsync` so gates can't
   run against a previous world after switching folders.
3. **Editor setters** - move the `PropertyChanged -=` detach inside the `if (Set(...))`
   branch (capture the old instance first) so a same-instance reassignment can't drop
   the subscription.
4. **Flag filter box** - consider a ~250 ms debounce on the `FlagFilter` Entry binding
   (UI-side); with the VM-side caching this is now optional polish.
