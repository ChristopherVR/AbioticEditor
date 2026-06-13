# Abiotic Editor - Session Progress (compacted 2026-06-12, round 3)

State of the .NET save editor (repo root layout). **308 assertion tests + 95 probes
green**; full solution builds clean; app multi-targets android/ios/maccatalyst/windows.
Plugin system: round-15 (core), round-16 (events/menu/JS), round-17 (web tools HTML/React +
host-UI bridge + Vite sample).

## Round-27: editable world-state maps (Features framework + 10 maps) (2026-06-13)
- Made every previously-unmodeled world-save map editable. New **`Core/WorldSaves/Features/`**
  framework: `IWorldMapFeature` (typed `WorldMapEntry`/`WorldMapField` rows; field factories
  ReadOnly/Bool/Integer/Number/Choice; `WorldEditResult`), `WorldMapFeatureBase` (implement
  `ReadFields`+`ApplyField`; `ShortLabel`, `ResolveChoice`), `WorldMapAccessor` (public
  read/write helpers mirroring the reader/writer idiom: `Entries/FindEntry/HasMap`, `SetBool/
  SetInt/SetDouble/SetFloat/SetString/SetName/SetEnumByte/SetVector`, parse helpers), and
  `WorldMapFeatures` (**reflection-discovered registry** - drop in a class, it's auto-registered;
  `IsKnownMap` makes `WorldSaveReader.LogUnmodeledKeys` stop flagging the map as unknown). Edits
  are lossless (patch existing leaves only).
- **10 feature modules** (one per map, built by parallel agents over a verified template):
  `ElevatorMapFeature` (topOpen), `ButtonMapFeature` (pressedOnce/enabled/activated/noReset),
  `ResourceNodeMapFeature` (harvested/dayPickedUp - un-harvest to refill a node), `NpcSpawnMapFeature`
  (cooldownRemaining/spawnCount/spawnedOnce/…), `TriggerMapFeature` (timesTriggered),
  `VehicleMapFeature` (driveable/destroyed + read-only class/inventory count),
  `PowerSocketMapFeature` (hasTimer editable; timerMode read-only - only one enumerator observed,
  full E_PowerTimerModes set unknown), `PortalMapFeature` (active), `TramMapFeature` (read-only
  viewer: station + inventory count), `ServerEntitlementsFeature` (metadata; comma-separated
  per-SteamID entitlement list, array-replace round-trip).
- **PortalMap "tag" finding (user asked to edit teleporter tags from a built-in list)**: the saved
  `SaveData_PortalStruct` has ONLY `ActorPath_` + `PortalActive_` - **no tag/channel leaf**, verified
  across Facility/Salem. Teleporter linking is NOT in PortalMap: the handheld Personal Teleporter
  syncs via the item's `PlayerMadeString_` = target bench's DeployedObjectMap GUID (see
  `TeleporterLinkTests`); placed teleporter labels are `Deployed_Sign_C` `PlayerMadeString_` text;
  deployables carry an (empty in saves) `GameplayTags_` container. No built-in allowed-tag vocabulary
  exists in saves/tables. So portals ship `active` only; the tag editor is deferred until a real
  DeployedObjectMap teleporter-tag vocabulary is sourced (documented in `PortalMapFeature` XML).
- **CLI** `world` group (`Cli/Commands/WorldCommands.cs`, generic over the registry): `world list
  <save>` (features present + entry counts), `world show <save> <feature> [--json] [--limit]`
  (entries + fields), `world set <save> <feature> <#index|key|substring> <field> <value> [--dry-run]`
  (writes with .bak). Verified live on a Facility copy (list 9 features, show elevators, dry-run,
  real button edit + .bak).
- **App**: `WorldMapsPage` modal (Settings → EDIT WORLD MAPS) - generic over the registry: pick
  world save → pick feature → virtualized entry list → tap an entry → typed field editors (Switch/
  Picker/Entry) → SAVE (writes .bak). Builds clean (net10.0-windows); NOT screenshot-verified.
- **+47 tests** (`tests/.../Features/*` - read/edit/round-trip/reject per feature). **374 tests
  green**; Core/CLI/App build clean (new files 0-warning).

## Round-26: self-updater (new AbioticEditor.Updater project) + log fix (2026-06-13)
- **New project `src/AbioticEditor.Updater`** (net10.0, zero deps - no MAUI/Core/CUE4Parse) so
  both the CLI and the app can reference and bundle it. Talks to the GitHub Releases API,
  downloads the matching asset, and replaces the running install IN PURE MANAGED CODE.
  - Pieces: `UpdaterOptions` (defaults to the real repo coords `ChristopherVR`/`AbioticEditor`;
    blanking/sentinel owner re-flags it unconfigured; `ForCli()`/`ForApp()` presets pick assets by
    keyword `cli|app` + `win-x64`); `GitHubReleaseClient` (releases/latest or list, System.Text.Json,
    User-Agent required); `ReleaseVersion` (lenient semver parse + compare, pre-release aware);
    `AssetSelector` (all-keywords-match, installable-ext preferred); `UpdateChecker`/
    `UpdateCheckResult` (status: UpdateAvailable/UpToDate/NoReleases/NoMatchingAsset);
    `UpdateInstaller` (download w/ IProgress, zip extract + single-root flatten); `AppUpdater`
    (the one facade hosts use). `IUpdaterLog` bridges diagnostics to each host.
  - **Self-replace is script-free** (user asked: no .cmd/.sh, avoid admin/exec-policy). Uses the
    Windows rename-in-place trick - a loaded exe/DLL can be RENAMED (not overwritten), so
    `InPlaceReplacer` moves each in-use file aside to `*.old-update`, drops the new file in,
    relaunches in managed code, and the host exits. `UpdateCleanup.Run` (called at CLI `Main` /
    App ctor startup) sweeps `*.old-update` and finishes any `*.pending-update` deferred when a
    target was locked. Bare installers (.msi/.exe) are just launched instead.
- **CLI** `update` command (`Commands/UpdateCommand.cs`, registered in CommandTree): `update`/
  `update check [--json] [--pre]` report; `update install [-y] [--pre] [--relaunch]` downloads +
  applies (CLI defaults to no relaunch). Honours `GITHUB_TOKEN`. With the placeholder repo it
  exits 1 with a clear "not configured yet" message (verified live).
- **App** `Services/UpdateService` (bridges to EditorLog, MAUI-thread apply+`Quit`) + an **UPDATES
  card** in SettingsPage (CHECK FOR UPDATES -> status; DOWNLOAD & INSTALL appears when newer;
  confirm dialog -> progress -> restart). App ctor calls `UpdateService.RunStartupCleanup()`.
- Wired into slnx, CLI/App/Tests csproj. **16 new offline tests** (`UpdaterTests`: version parse/
  order, asset selection, in-place replace + cleanup, placeholder detection). **327 tests green**;
  Updater builds 0-warning; CLI + App(win) build clean.
- **Log fix (user-reported)**: the Edit diagnostic logged the whole `ItemCatalogEntry` via the
  record's auto `ToString`, which renders collection members as type names (`Tags = System.String[]`,
  `AllowedLiquids = List`1[System.Int32]`). Added a concise `ItemCatalogEntry.ToString()` override
  (`shelf_m (Medium Shelf)`) - the general fix for any log that prints an entry.
- **World-save "unknown" review (user-requested)**: `WorldSaveReader.LogUnmodeledKeys` logs every
  top-level world property not in `ConsumedPrefixes` as "unmodeled ... not editable" (still
  preserved verbatim on save). Enumerated the real unmodeled keys from fixtures: metadata =
  `ServerEntitlements`; Facility/region = `ResourceNodeMap` (169 harvest nodes,
  SaveData_Resource_Struct), `PowerSocketMap` (223, SaveData_PowerSockets), `ButtonMap`,
  `NPCSpawnMap`, `PortalMap` (BP_Teleporter actors, SaveData_PortalStruct), `VehicleMap`
  (SaveData_Vehicle_Struct - fuel/inventory/pos), `ElevatorMap`, `TramMap`, `TriggerMap`. All are
  per-actor state maps keyed by actor path/GUID. Candidates worth surfacing as editable later
  (highest value first): VehicleMap (fuel/inventory), ResourceNodeMap (reset/refill nodes),
  PowerSocketMap (power on), ButtonMap (toggle), ServerEntitlements (admin list). Not implemented
  this round - flagged for follow-up.

## Round-25: Compare - domain-aware summary-first diff + segmented mode (2026-06-13)
- User wanted Compare to read like the rest of the app ("save 1 has Fish A with its image, save 2
  doesn't"), summary-first then deep-dive, with raw still available, and the mode picker a proper
  toggle.
- **`Views/SaveSemanticDiff.cs`** (`PlayerSemanticDiff`): builds a human-readable diff of two
  PLAYER saves reusing the editor catalogs. Sections: PROGRESSION (money + per-skill level as
  `A → B` rows), and set-difference categories Recipes / Fish / Traits / Items discovered / Items
  crafted / Maps / Journals / Emails - each as ONLY-IN-A (red) / ONLY-IN-B (green) chips. Chips
  resolve display name + icon: items via `ItemCatalog`, recipes via `RecipeInfo.CreatesItemId`,
  fish via `FishDefinition.ItemId`, traits via `TraitDetails`; icons extracted lazily off-thread
  (`provider.ExtractTextureByGameRef` + `IconColorizer`, same path as inventory slots).
- **ComparePage** rewired: a leading **WHAT'S DIFFERENT** overview card (one line per changed
  category) → per-category cards → a collapsed **RAW PROPERTY DIFF** card (the old leaf list +
  noise switch) for the deep-dive. Two player saves get the semantic view; any other pairing (or
  non-player) falls back to raw expanded. Catalogs are ensured loaded before comparing so
  names/icons resolve. Mode picker is now a real **segmented toggle** (`ModalChrome.Segmented`)
  instead of two buttons.
- Build clean to temp output (0 errors). NOT screenshot-verified.
- **World semantic diff (follow-up, same session)**: `WorldSemanticDiff.Build(WorldSaveData a, b)`
  mirrors the world editor tabs - PROGRESSION (story chapter + time-played scalars), GLOBAL RECIPES
  (set-diff w/ item icons), QUEST FLAGS (set-diff via `QuestFlagCatalog.Lookup` friendly names),
  DOORS (lock/open-state changes matched by id → scalars), GROUND ITEMS (dropped-item set-diff w/
  icons), NPCS (state / alive→dead changes), WORLD CONTENTS (container/object/ground/NPC count
  scalars). Reuses the shared `SemanticSection`/`RenderSection`. ComparePage's `TryBuildSemantic`
  now resolves two player saves → PLAYER sections, else two world saves
  (`WorldSaveReader.ReadFromFile`) → WORLD sections, else raw-only; `ShowFileResult` takes a
  `(Kind, Sections)?` and labels the summary card. Build clean (0 errors). NOTE: a concurrent
  UPDATES feature (`AbioticEditor.Updater` project + `Services/UpdateService` + Settings UPDATES
  card) is being wired by the user - left untouched.

## Round-24: Settings + Compare sheets restyled to the game look (2026-06-13)
- User: the Settings/Compare modals "don't look anything like the main game" UI. Both were bare
  code-built stacks (default buttons, plain section labels on the page bg). New shared
  `Views/ModalChrome.cs` gives the code-built sheets the facility look: `Scaffold(eyebrow, title,
  cards, footer)` = branded header (AF badge + amber eyebrow + `AfH1` title) + hazard stripe +
  centred scroll column of `Card`s + a sticky `AfChrome` footer bar; `Card(header, hint, body…)`
  = `AfPanel` border with an amber `AfFieldLabel` + `AfMuted` hint; `Button(text, primary)` =
  primary fill or `AfGhostButton`.
- **SettingsPage** rebuilt: THEME / DIAGNOSTICS / SPOILERS / GAME DATA / PLUGINS / ABOUT each a
  panel card; theme accent buttons act as a segmented control (active = filled + ✓, inactive =
  ghost); switch rows use `AfFieldValue`; CLOSE in the footer. **ComparePage** rebuilt: MODE /
  SOURCES (A/B sub-labels) / RESULTS cards; mode buttons segmented; `DiffDetailPage` drill-down
  uses the same scaffold. Logic (theme apply, plugin toggles, compare/diff, folder drill-down)
  unchanged - only the chrome.
- App builds clean to temp output (0 errors). NOT screenshot-verified (user holds the app).
  PluginsPage still uses the old plain style - left for a follow-up unless asked.

## Round-24: switch-regression fix + localization + self-contained publish + drop fixes (2026-06-13)
- **Save-switch regression fixed**: the 150ms selection debounce (round-23-era) made single
  player-save clicks feel laggy. Replaced with serialize-and-coalesce (`RequestSwitchAsync`):
  load IMMEDIATELY when idle (instant single click), and while a load runs just update
  `_pendingSelection` (no concurrent parses, only the settled save loads). Removed the fixed delay.
- **Localization (multi-language)**: `Localization/AppResources.resx` (en neutral) + es/fr/de
  satellite resx (build confirmed es/fr/de/AbioticEditor.App.resources.dll produced).
  `Services/LocalizationResourceManager` (observable indexer; raises "Item[]" on culture change →
  live re-localize), `Controls/LocalizeExtension` (`{loc:Localize Key}` markup), `Services/
  LocalizationService` (OS-default via CurrentUICulture→shipped code, persist Preferences
  "AppLanguage", ApplyStartup/SetLanguage). `LanguagePage` (code-built, rebuilds live on pick) shown
  first-run from `MainPage.StartupAsync` when `!HasChosenLanguage`; reachable from a new Settings
  LANGUAGE card. `App` ctor calls `LocalizationService.ApplyStartup()`. Wired the Settings LANGUAGE
  card + LanguagePage strings; rest of the UI is incremental (mechanism + keys in place).
- **Self-contained publish**: `Properties/PublishProfiles/win-x64.pubxml` - SelfContained +
  **WindowsAppSDKSelfContained** + WindowsPackageType=None + ReadyToRun → install-free contained
  folder. (Literal single .exe isn't reliable for WinUI; documented in the pubxml.)
- **Drop-item fixes** (user): (1) dropped item now appears in NEARBY GROUND ITEMS immediately -
  `DropActiveItemAsync` inserts a staged `GroundItemOption { IsStaged=true }` (was only visible
  after SAVE); SAVE replaces it with the real disk entry, REVERT clears it; picking up a staged
  entry cancels the pending drop (matched by the shared slot reference) instead of staging a
  removal. (2) DROP button now `IsEnabled="{Binding ActiveSlot.IsEmpty, Converter=BoolNegate,
  FallbackValue=False}"` - disabled when no occupied slot is selected.
- Build: Release verified via temp `-o` output (the user's running instance locked bin). NOT
  runtime-verified (user mid-game). To pick up these changes: close the app, rebuild Release,
  relaunch. First-run language prompt only shows when no language has been chosen yet.

## Round-23: editor-host restructure - bounded viewport + per-tab scroll (2026-06-13)
- Continuing the perf work ("clunky on every tab click, no smoothness, even light tabs"). Root
  cause beyond lazy tabs: the ENTIRE editor lived in one page-level `ScrollView` (MainPage col 2),
  and tabs toggled `IsVisible` inside it - so every switch re-measured a giant scroll content, and
  every `CollectionView` inside that scroll got infinite height (virtualization dead → realizes all
  rows). User chose "do A" (the structural fix).
- **MainPage**: col-2 `ScrollView>VSL` → `Grid RowDefinitions="Auto,*"`: row 0 = fixed header
  (title, SAVE/REVERT, loading/error/compat banners, no longer scrolls); row 1 = bounded editor
  region. The 4 editors overlap there gated by IsVisible. Player/World are tabbed; IniEditor +
  EmptyState wrapped in their own `ScrollView` (gated HasIniEditor / HasNoEditor).
- **PlayerEditorView**: `VSL` → `Grid RowDefinitions="Auto,*"`: row 0 = horizontal tab bar; row 1 =
  tab host (Grid; the 11 LazyViews overlap, only active visible, fill the cell). Each LazyView's
  ContentTemplate now wraps the tab in its OWN `ScrollView`. Height chain `*→*→Fill→*` propagates
  from the window so each tab's ScrollView is a bounded viewport - switching re-lays-out only the
  open tab, not the whole editor. `VerticalOptions=Fill` on the ContentView.
- Build clean (Debug + Release, net10.0-windows). **NOT runtime-verified** - user was mid-game
  (fullscreen); could not screenshot the app. MUST verify: player editor still renders (header top,
  tab bar, tab content fills + scrolls), tabs show content, layout not clipped. Risk: structural
  layout change; revert path = restore the single outer ScrollView.
- FOLLOW-UP (not done): heavy tabs (Recipes ~600 rows, Codex, Skills/Character/Achievements) still
  wrap their CollectionViews in the per-tab ScrollView → still realize all rows. To fully virtualize
  those, their lists must move into a bounded `*` row (not inside a ScrollView). World editor wrapped
  whole in a ScrollView (bounded region) but not yet per-tab.

## Round-22: add-player, sidebar search, Material nav header (2026-06-13)
- **"+" add-player on the PLAYERS group header** (`FileSidebarView`): asks new-blank vs copy
  the selected player, prompts a 17-digit SteamID64, writes a fresh `Player_<id>.sav` into the
  world's PlayerData and selects it. Flow in `MainViewModel.AddPlayerAsync` /
  `ResolvePlayerDataDir` / `PromptForNewSteamIdAsync` / `LoadBlankPlayerTemplateAsync`. Button is
  in the group-header DataTemplate, so it fires via a code-behind `Clicked` (BindingContext VM)
  not an x:Reference across the template namescope.
- **Core player-creation** (`Core/PlayerSaves`): `PlayerSaveFactory` (`ResetToBlank` reuses the
  PlayerSaveWriter Apply* methods to zero money/skills/health-full and empty every unlock/
  compendium/inventory list; `BuildBlankTemplate`; `CreateFromTemplate`). `PlayerSaveWriter.
  ClearAllInventory` clears slots to the `Empty` sentinel. `PlayerSaveIdentity` refactored:
  `CloneToNewId` (copy keeping source, for "copy existing"), shared `WriteAs`, public
  `StampIdentifier`. **Blank template bundled** as `Resources/Raw/blank-player-template.sav`
  (MauiAsset, generated once from a Cascade fixture via PlayerSaveFactory). New-from-template
  is structure-from-the-bundled-blank; copy-existing keeps progress.
- **Sidebar search filter** (`MainViewModel.SidebarFilter` + `MatchesFilter`): one box filters
  both save rows (display/owner/kind/filename) and config files, case-insensitive. Filtered
  config via `VisibleConfigFiles`; PLAYERS group stays visible once a folder is loaded so its
  "+" is always reachable. Search box + clear "×" added to `FileSidebarView` header.
- **Dialog text input**: `DialogViewModel.PromptAsync` + `ShowInput/InputText/InputPlaceholder`;
  Entry added to `DialogHostView` (used for the SteamID prompt).
- **Material nav header** (`HeaderBarView`): top-right reworked to Material conventions - round
  icon buttons (home/folder/build glyphs) for HOME + the two pane toggles with hover states, a
  divider, then a filled primary "OPEN FOLDER" (folder_open glyph + label). Bundled **Material
  Symbols Outlined** font (`Resources/Fonts/MaterialSymbolsOutlined.ttf`, registered as
  `MaterialSymbols`). OPEN FOLDER is now a tapped Border (`OnOpenFolderTapped`).
- **Verified**: Core builds; App builds clean to temp output (0 errors, only pre-existing
  warnings); **311 tests green** (+3 `PlayerSaveFactoryTests`: blank wipes progress + reparses,
  create-from-template writes an owned player + refuses overwrite, clone keeps source + copies
  progress). NOT yet screenshot-verified (user holds the running app) - the in-game validity of
  a fabricated blank player should be confirmed in-game (a `.bak` is kept on every write).
- **UI refinement pass (same round, user feedback)**: (a) the "+" is now a clean circular
  `Border` with a Material `add` glyph (was an odd-shaped Button); (b) search box restyled -
  Material search/clear glyphs, fixed 38px height, and on Windows the native TextBox chrome
  (border + pale fill + hover/focus brushes) is stripped via `OnSearchEntryHandlerChanged` so it
  reads as part of the dark container; (c) footer COMPARE/SETTINGS are Material icon+label tonal
  buttons (hover state) instead of tiny ghost buttons; (d) **pane toggles moved out of the nav
  bar onto edge rails** - the header keeps only HOME + OPEN FOLDER; each side pane has a 22px
  vertical rail (`FileToggleRail`/`SlotToggleRail`) with a chevron that flips to point "collapse"
  vs "expand" (`ResponsivePaneController.UpdateRailGlyphs`, kept in sync across toggle/resize/
  drawer transitions). HeaderBarView's FilesToggleRequested/ToolsToggleRequested events removed.
- **2nd refinement pass (more feedback)**: rails were too subtle and not resizable -> replaced
  with **visible draggable splitters** (`FileSplitter`/`SlotSplitter`, 16px, `AfPanelElevated`
  strip with a centred grip + the chevron toggle on top). Drag the grip to resize: `PanUpdated`
  -> `ResponsivePaneController.Begin/UpdateFileResize` / `Begin/UpdateSlotResize` clamp the
  pane's `WidthRequest` ([220,600] file / [260,680] slot); the editor is the `*` column so the
  main pane resizes with them ("resizable stacks"). Grips use an opaque bg (this MAUI build
  doesn't hit-test `Transparent` - same reason the drawer scrim uses #000+Opacity0). Drawer-exit
  now restores the user's chosen widths. Footer COMPARE/SETTINGS restyled again into **outlined
  pills** (1px border, accent-orange Material icon, hover fills + accent border) - the flat tonal
  look read as "basic".

## Round-21: UI performance pass - lazy tabs + lighter render (2026-06-13)
- User: switching tabs / scrolling / typing / resizing all feel slower than a browser; fix perf
  WITHOUT removing features. (Data/parse hot paths already optimized in research-perf-review.)
- **Root cause**: ALL tabs were realized into the visual tree at once - `PlayerEditorView`
  instantiated all 11 `<player:*Tab/>` and `WorldEditorView` all 10 world tabs, toggling only
  `IsVisible`. Every layout pass (scroll/resize/keystroke remeasure) + every binding update kept
  the whole tree for every tab live (incl. 388-line Inventory, 299-line Codex).
- **Fix `Controls/LazyView.cs`**: a ContentView that builds its `ContentTemplate` (a DataTemplate
  wrapping the real tab) only on first `IsActive=true`, then keeps it; collapsed until activated.
  Wrapped every player + world tab in `LazyView IsActive="{Binding ...IsXTab}"`. Now only opened
  tabs join the live tree. Tabs are self-contained compiled ContentViews (own namescope) so the 4
  using `x:Reference` (Character/Codex/Recipes/WorldFlags) are unaffected by the template boundary;
  BindingContext flows by inheritance.
- Render: reveals now a quick ~120ms fade+rise (dropped the relayout-forcing scale tween in Fx.cs,
  210→120ms); panel drop-shadows removed (19 GPU composition shadows → 0); global hover-lift
  removed (scale-on-hover forced per-row relayout). Snappy > smooth.
- Build clean (net10.0-windows). **Runtime click-through NOT yet verified** (user mid-game,
  fullscreen). Low risk (XAMLC validated; standard lazy-template pattern); one-line revert if a
  tab shows blank. Biggest USER-controlled lever: run a **Release** build (Debug WinUI is far slower).

## Round-20: quest-flags tab simplification + dialog fix (2026-06-13)
- **Quest flags tab (`WorldFlagsTab.xaml`) now mirrors the story-aspects pattern**: shows ONLY
  the flags actually stored in THIS save (the world's reached quest flags), grouped by story
  region. Removed (per user): the ACTIVE/MISSING counts + SHOW MISSING checkbox, the category
  filter chips (ALL/TUTORIAL/QUEST/…), and the "ADD STORY FLAGS UP TO" picker row. Kept the
  text filter, ADD FLAG, the HOW QUESTS WORK help, and per-row category badge; per-row button
  relabelled TOGGLE→CLEAR (every shown flag is active) and the redundant ACTIVE status badge
  dropped. Header now just shows an IN THIS FILE count.
- **VM cleanup (`WorldEditorViewModel`)**: removed `ShowInactiveFlags`, `CategoryFilter`/
  `SetCategoryFilter`/`ClearCategoryFilterCommand`/`AllCategories`, `MissingFlagCount`, and the
  whole story-flag batch-add block (`StoryFlagTarget(s)`/`AddStoryFlagsCommand`/
  `AddStoryFlagsUpToTarget`). `UnfilteredFlagItems` now builds from `Flags` only (no catalog/
  inactive branch); `ApplyFlagFilter` dropped the category predicate. Prereq cascade still
  enforced on toggle and via `EnablePrerequisitesForSelectedFlag` (sidebar detail). Code-behind
  trimmed to just InitializeComponent.
- **Unsaved-changes dialog (`DialogHostView.xaml`)**: the 3 buttons (Cancel / Discard changes /
  Save and continue) overflowed the 460px card and wrapped, stranding the affirmative on its own
  right-aligned line. Widened the card to 520 so they sit cleanly on one right-aligned row.
- App builds clean (net10.0-windows, 0 errors; warnings all pre-existing). Not screenshot-
  verified (user holds the running app).

## Round-19: UI smoothness/refinement pass (keep game theme) (2026-06-13)
- User wanted it to feel smoother/more fluid (ShadCN-ish) but KEEP the Abiotic theme. Global
  stylesheet + motion only (low risk; user holds the running app so not screenshot-verified yet).
- **Motion** (`Controls/Fx.cs`): new `Fx.HoverLift` attached property animates a subtle scale
  (1.0↔1.015, 130ms CubicOut) on pointer hover - MAUI VSM hover snaps instantly, this eases it;
  applied app-wide via the `AfSidebarItem` style (all list/sidebar rows). Tracks its recognizer
  via a private attached BindableProperty (PointerGestureRecognizer is sealed; no CommandParameter).
  Desktop-only (no-op on touch). `Fx.Reveal` refined: fade + rise + settle with a subtle scale
  (0.99→1) over 210ms.
- **Buttons** (`AbioticStyles.xaml`): primary hover no longer flips orange→hazard-yellow (jarring
  hue jump) - now a gentle grow (scale 1.02) + slight brighten; press gentler (0.97); dropped the
  dark 1px border; radius 4→6. Ghost/tab/chip radius→6, press scales softened.
- **Visual**: panel shadow softened (opacity 0.35→0.18, radius 14→20, offset 0,3) for a calm
  modern elevation; consistent 6px control radius.
- **Typography**: tightened dated letter-spacing (H1 6→2, H2 4→1.5, field labels 4→1.5, status
  2→1); digital-7 readouts + wordmark (brand) left untouched.
- App builds clean (net10.0-windows). Needs a quick visual pass when the desktop is free.

## Round-18: user-reported bug fixes (2026-06-13)
- **Sidebar listed Backups saves**: `SaveFolderScanner.Scan` recursed into `Backups/` (AllDirectories).
  Now excludes any path with a `Backups` segment (helper `IsUnderBackups`); `SaveDiscovery.AddIfWorld`
  also ignores Backups when counting saves / computing LastPlayed. Test: `SaveFolderScannerTests`.
- **Trader "available from the start" was wrong** (Jimmy Sanders is post-game): most traders carry no
  `RequiredWorldFlags` in DT_NPC_Traders, so the editor can't infer gating from flags. Added a curated
  `Unlock` field to `TraderLore.Entry` (e.g. Jimmy: met in Botanical Garden, only trades AFTER beating
  the game at the Taco Mine) and `TraderCardViewModel.AvailabilityText` now shows it instead of
  "Available from the start". Added `Unlock`/`HasUnlock` to the card VM.
- **Trader barter clarity**: trader detail card now has explicit "WHAT THEY ACCEPT AS PAYMENT" (was a
  small muted line) + a barter note + "WHAT THEY SELL" header over the stock list (`SlotSidebarView.xaml`).
- **Drop item now reaches the world ground**: `WorldSaveWriter.AddDroppedItem` clones an existing
  `DroppedItemMap` entry (whole-save round-trip → independent copy), re-keys it with a fresh GUID
  (format-matched), swaps in the item slot + player location + NoDespawn, and appends it. Returns null
  when there's no entry to clone (never fabricates from scratch). `PlayerEditorViewModel.PendingGroundDrops`
  + `CommitGroundDropsAsync` (mirrors pickup) commit on player SAVE; `MainViewModel.DropActiveItemAsync`
  picks the region (else Facility) save that has a clonable entry off-thread, stages the drop, clears the
  slot; `GroundDropsCommitted` refreshes NEARBY GROUND ITEMS. DROP button/tooltip restored.
  Test: `DroppedItemWriterTests` (clone + write + re-read round-trips with correct id/location/slot).
  NOTE: structurally round-trip-verified; user should confirm in-game (a .bak is kept on every write).

## Round-17: plugin web tools (HTML/React) + host-UI bridge + Vite sample (2026-06-13)
- **`IWebTool` capability** (SDK `Ui/IWebTool` + `WebToolContent` + `IWebToolContext`; registry
  `AddWebTool`; `webTool` token): a plugin renders an HTML page (incl. React) in a MAUI WebView.
  Wired through PluginRegistry/Descriptor/Manager like the other capabilities.
- **WebView host + bridge** (`App/WebToolHostPage`): renders inline HTML (bridge prepended) or a
  directory-served bundle (relative `rootDirectory` resolved against the plugin folder; bridge
  injected on Navigated). Bridge = custom-scheme nav (`abiotic://request?...`) intercepted in
  `Navigating`, routed to `IWebTool.HandleMessageAsync`, Promise resolved via EvaluateJavaScript.
  Page gets `abiotic.request()/log()/onEvent()`. Surfaced in PluginsPage WEB TOOLS section.
- **JS `abiotic.registerWebTool`** (`JsWebTool` + `JsWebToolContext.playerSummaryJson()`).
- **Host-UI bridge** (`IHostUi` + `NullHostUi` in SDK; `IPluginHost.Ui`; Core
  `PluginHostEnvironment.HostUi`; App `AppHostUi` marshals to UI thread; JS `abiotic.ui`):
  plugins drive the app — `showAlert/confirm/toast`, `runSaveOperation(id)` (runs through the
  backup/write path + reloads), `reloadSave`, `openSettings/openPlugins`. Installed in App ctor
  via `PluginService.InstallHostUi`. CLI/tests get the no-op NullHostUi.
- **3 web JS samples**: `ReactDashboard` (inline React from CDN), `WebStats` (offline HTML in a
  bundled `web/` folder), and **`ReactAppDashboard`** — a real Vite+React project (`app/`:
  package.json, vite.config base:'./' + vite-plugin-singlefile, src/*) built to a single
  self-contained `dist/index.html`; its React UI reads the save AND drives the app (Max-skills
  button → `abiotic.ui.runSaveOperation`, toast button → `abiotic.ui.toast`). `npm run build`
  verified (147KB inlined, no external scripts → file:// safe).
- `WebToolContext` re-reads the save per request (live dashboard sees edits). NOTE: the user
  concurrently added an `ISaveUpgrader` capability (saveUpgrader token) — integrated alongside.
- **Verified**: Core/CLI build clean; App builds clean to temp output; Vite app builds; CLI loads
  all web samples (ReactAppDashboard registers its save op). **306 tests green** (+ web-tool
  registration/bridge round-trip, JS→app-UI bridge: showAlert/toast/runSaveOperation reach host).

## Round-16: plugin events + menu actions + JavaScript runtime (2026-06-13)
- **SDK additions** (`Plugins.Abstractions`): `Events/PluginEvent` + `PluginEvents` constants
  (`app.started`/`save.opened`/`save.closed`/`save.written`); `Ui/IMenuAction` (+context,
  `NotifyAsync`); `IPluginRegistry.AddMenuAction` + `AddEventHandler(name, Action<PluginEvent>)`.
  Manifest gained `runtime` (`dotnet`|`javascript`) + `entryScript`; `PluginRuntimes` +
  MenuAction/EventHandler capability tokens.
- **Core event hub**: `PluginRegistry`/`PluginDescriptor` carry MenuActions + EventHandlers
  (`PluginEventSubscription`); `PluginManager` aggregates `MenuActions` and adds
  `RaiseEvent(name,data)` (snapshots matching handlers, invokes each isolated in try/catch).
  Hosts raise events: CLI `plugins run` raises `save.written`; App raises `save.opened`/
  `save.closed` in `LoadEditorForAsync` and `app.started` in PluginService.Initialize.
- **JavaScript runtime** (`Core/Plugins/Scripting/`, pkg **Jint 4.4.1**, pure-managed → all
  TFMs): `JavaScriptPlugin : IAbioticPlugin` runs the `.js` on a bounded engine (recursion/
  timeout/statement caps, case-insensitive member access so JS uses camelCase) and exposes the
  `abiotic` API (log, registerSaveOperation/Command/MenuAction, on(event)). `JsCapabilities.cs`:
  JS-backed `ISaveOperation`/`IConsoleCommand`/`IMenuAction` + context facades + `JsPlayerSave`
  (money get/set, setAllSkillLevels). `PluginManager.CreatePlugin` dispatches on runtime. JS
  plugins need NO build step. `JsRuntime` serializes engine access (Jint is single-threaded).
- **App**: SettingsPage PLUGINS section gained an inline enable/disable switch per plugin (plus
  MANAGE PLUGINS); `PluginsPage` gained a MENU ACTIONS section; `MainPage.BuildPluginMenu` adds
  a real "Plugins" MenuBarItem of menu actions; `PluginService` exposes MenuActions +
  `CreateMenuActionContext` (NotifyAsync via dialog) + raises app.started.
- **JS sample `plugins/HelloScript/`** (plugin.js + plugin.json, no csproj): `rich-player` save
  op (uses `ctx.player`), `js-greet` command, `say-hi` menu action, `save.written` handler.
- **Docs/README**: full README plugins section (managed + JS usage, events, menu, settings);
  `docs/plugins.md` + `plugin-authoring.md` extended (events table, menu, JavaScript).
- **Verified**: Core/CLI build clean; App builds clean to temp output. CLI proven on the JS
  plugin: `plugins list/info`, `js-greet`, `rich-player` write+`.bak`+idempotent. **295 tests
  green** (+4: JS load registers all caps, JS save op edits+persists money on disk, RaiseEvent
  dispatches to a JS handler, throwing handler isolated).

## Round-15: PLUGIN SYSTEM (Core + CLI + App) + 4 samples (2026-06-13)
- **New SDK project `src/AbioticEditor.Plugins.Abstractions`** (net10.0, no MAUI / no
  System.CommandLine; refs only UeSaveGame). Host-agnostic contracts plugin authors compile
  against: `IAbioticPlugin` (single entry, `Configure(registry, host)`), `IPluginHost`/
  `IPluginLog`/`IPluginRegistry`, `PluginManifest` (+`PluginCapabilities` tokens), and three
  capability interfaces — `ISaveOperation` (+context/result/params, `SaveKind`), `IConsoleCommand`
  (+neutral arg/option/context), `IEditorTool` (UI view returned as `object` so the SDK stays
  MAUI-free). CA1716 on `Error` suppressed (GlobalSuppressions, justified).
- **Hosting in `Core/Plugins/`**: `PluginPaths` (user `%LOCALAPPDATA%\AbioticEditor\plugins`
  + bundled `<exe>\plugins`; `ABIOTIC_PLUGINS_DIR` override; per-plugin data dir),
  `PluginManifestIo` (parse/validate/persist plugin.json; never loads code; strict on id +
  bare-filename entryAssembly), `PluginLoadContext` (collectible ALC; unifies the editor
  contracts AND anything already loaded in Default → no duplicate CUE4Parse/MAUI identities),
  `PluginManager` (two-phase discover→load, `Shared` singleton, aggregates capabilities,
  `EnsureLoaded(hostKind, shouldLoad?)`), `PluginDescriptor`/`PluginLoadState`,
  `SaveKindDetector` (header-only class→`SaveKind`), `SaveOperationRunner` (load→kind-check→
  required-params→execute→ backup+write ONLY if `MarkChanged()` and not `--dry-run`; the one
  dangerous path, kept out of plugins). Added `PlayerSaveReader.ReadFrom(SaveGame)` so ops/UI
  build typed data over the already-loaded save (data.Raw IS the instance the host persists).
- **CLI**: `plugins list/info/run` (`PluginsCommands`) + `PluginCliBridge` adapts
  `IConsoleCommand`→System.CommandLine; `CommandTree.RegisterPluginCommands` grafts plugin
  verbs at root (collision-guarded, `ABIOTIC_NO_PLUGINS=1` to skip; CLI skips UI-only plugins).
- **App**: `Services/PluginService` (static, loads on startup in App ctor; runs ops via
  runner; builds `EditorToolContext` with lazy ActiveSave from SelectedSave path),
  `PluginsPage` (modal: installed list w/ enable toggle, SAVE OPERATIONS run against the open
  save then `MainViewModel.ReloadSelectedSaveAsync()`, TOOLS open `IEditorTool.CreateView` in a
  host page), entry from SettingsPage "MANAGE PLUGINS".
- **4 samples in `plugins/`** (shared-assembly rule: `Private=false ExcludeAssets=runtime`, so
  output = own DLL + plugin.json): `MaxSkills` (ISaveOperation, player, `--param level`),
  `SaveStats` (IConsoleCommand `save-stats <save> [--json]`), `PlaytimeDashboard` (IEditorTool,
  C#-built view), `SaveInspector` (IEditorTool, **full MVVM: compiled XAML + ViewModel**).
- **Docs**: `docs/plugins.md` (architecture + justification + security/trust),
  `docs/plugin-authoring.md` (how-to + checklist); README + slnx updated.
- **Verified**: Core/CLI/Abstractions/samples build clean (0 new warnings); App builds clean
  to temp output. CLI end-to-end proven: discover→load→`plugins list/info`, `save-stats` on
  player+world, `max-skills` dry-run / real write+`.bak` / idempotent re-run / wrong-kind
  guard. **290 assertion tests green** (21 new `PluginTests`: manifest IO, discovery+dedup,
  kind detection, runner write/backup/dry-run/no-change/required-params).

## Round-14: fish journal detail — unlocks + catch requirements (2026-06-13)
- **DT_Fish fully modelled** (`CodexCatalog.FishDefinition` + `BuildFish`): besides item/rare,
  now carries `Location` (FishName FText = water/biome), `UnlockRecipeId` (RecipeToUnlock),
  `RequiredWorldFlag`, `RequiredDlcId`, `RequiredBaitTag` (first `Fishing.Bait.*` tag in the
  CatchRequirement GameplayTagQuery's TagDictionary), and the four time-of-day catch
  multipliers (`MidnightMult`/`DawnMult`/`NoonMult`/`DuskMult`; 0 = never then, >1 = best).
  `HasTimePreference`/`RequiresSpecialCatch` are computed. Probe: FishSchemaProbeTests.
- **Fish reading pane (PlayerCodexTab)** shows two new sections via `FishBaitResolver`
  (`App/ViewModels/FishBaitResolver.cs`):
  - WHEN YOU CATCH IT: the unlocked bait (icon + name, tappable) + "+N XP on first catch".
    Bait resolves by RecipeToUnlock→recipe→item, with a **family-tag fallback** (group fish by
    base name stripping `_rare\d*`/`_AllDay`/`_torii`; map the family's `Fishing.Bait.X` tag to
    its bait item) so fish without a RecipeToUnlock (Gem Crab, etc.) still show their bait.
  - TO CATCH IT: location ("cast where there's …"), story-flag gate, a **specific** time-of-day
    sentence computed from the multipliers (e.g. "Only bites at night", "Bites best at dawn,
    midday and dusk"), DLC, plus an EQUIP-THIS-BAIT row naming the exact required bait
    (rare variants; resolved from RequiredBaitTag) — also tappable.
  - Tapping either bait calls `MainViewModel.ShowItemEncyclopedia(baitId)`; `ShowItemPalette`
    now also surfaces on the codex tab once an item is selected, so the bait opens in the slot
    editor sidebar (same path as the dropped-item encyclopedia).
- Note: a few fish (Fogfish, Reaper/Inkfish) have no craftable bait item for their tag; the
  bait row is simply omitted (null-safe). Tests: CodexTests.Fish_TimeOfDayAndBaitTagsParse +
  Fish_CarryUnlockAndCatchRequirements (269 assertion tests green).

## Round-13b: save comparison feature (2026-06-13)
- **New Core engine `AbioticEditor.Core/Compare/`** - generic property-level diff over the
  raw `IList<FPropertyTag>` tree (NOT per-type models), so it works for any save (player,
  world, metadata):
  - `SavePropertyFlattener`: walks the property tree into ordered `path -> value` leaves.
    `PropertiesStruct` recurses; arrays index `[i]`; maps key `{key}`; specialized structs
    (Vector/Guid/Color/DateTime/gameplay-tags) compare via ToString. Blueprint hash suffixes
    (`_<idx>_<hex>`) are stripped via `Normalize` so the same logical property lines up across
    saves/builds. Leaf cap (default 4M) guards the 16MB Facility save; sets `Truncated`.
  - `SaveComparer.Compare(left,right)` / `CompareFiles(a,b)` -> `SaveDiff` (Changed/Added/
    Removed leaves, left order preserved, additions appended). `SaveDiff.Summary` = "N changed,
    N added, N removed" or "identical".
  - `SaveFolderComparer.Compare(dirA,dirB)` -> `FolderDiff`: pairs `*.sav` by path relative to
    each root; per-file Identical/Differs/OnlyLeft/OnlyRight/Error + the full `SaveDiff`.
- **CLI**: `abioticeditor compare <a> <b>` (file-vs-file or folder-vs-folder; auto-detects),
  `--json`, `--limit` (text cap, default 200), `--full` (expand every folder file's diffs).
  Registered in `CommandTree`. Verified live: two player saves -> 1191 changed incl. readable
  paths like `EquipmentInventory[2].ItemDataTable.RowName: armor_legs_groupe -> armor_legs_bionic`;
  backup1-vs-backup5 folder -> "17 differing, 45 identical, 0 only A, 1 only B" and correctly
  flagged `WorldSave_V_ISLAND.sav` as B-only.
- **App**: COMPARE button added to the StatusBarView (next to SETTINGS) -> `CompareRequested`
  event -> MainPage pushes `ComparePage` (code-built modal, mirrors SettingsPage). Page has a
  FILE-vs-FILE / FOLDER-vs-FOLDER mode switch, quick-picker of currently-loaded saves + BROWSE
  (FilePicker/FolderPicker), runs the compare off the UI thread w/ busy indicator, and renders
  a virtualized diff list (+ green / − red / ~ yellow). Folder mode lists per-file status; tap a
  differing file -> `DiffDetailPage` with that file's diffs.
- **Tests**: `SaveComparerTests` (6, green) over Server/Backups/Cascade/1..5 + PlayerData:
  hash-strip, same-file identical, backup snapshots differ (with side-population invariants),
  two players differ on SaveIdentifier, folder pairing flags V_ISLAND as only-on-right, self
  vs self all-identical.
- **Difference classification (noise folding)** - a raw leaf diff is noisy: comparing two
  different players, the SteamID, every item AssetID, playtime and positions all "differ" but
  aren't real changes. `SaveDiffClassifier.Classify(path,type,left,right)` tags each
  `SaveLeafDiff` with a `SaveDiffCategory` (Gameplay / Identity / Playtime / Timestamp /
  InstanceId / Position). Heuristics: leaf-name hints (SaveIdentifier, MinutesPassed/
  PlayTime/CurrentDay, LastPlayed/DateTime, AssetID/*GUID, *Location/Rotation/Translation)
  PLUS value-shape detection (32-hex/dashed GUID -> InstanceId; 3-4 space-separated floats ->
  Position) which catches the bulk of the AssetID noise. `SaveDiff` gained MeaningfulCount /
  NoiseCount / AreMeaningfullyIdentical / MeaningfulSummary ("N gameplay difference(s) (+ M
  identity/clock/instance/position)"). CLI defaults to gameplay-only with a hidden-noise
  footer + `--all` (tags noise lines `[category]`); folder rows show "X gameplay, Y total".
  App ComparePage leads with the meaningful summary + a switch to fold the noise back in
  (noise rows tagged with their category). Verified live: two players -> "1442 gameplay (+ 49
  identity/clock/instance/position)" with AssetID handles correctly folded out. Test
  `Classify_FoldsIdentityInstanceAndPositionOutOfMeaningful` (7 comparer tests green).
- Build: Core + CLI + App (net10.0-windows) all 0 compile errors (App verified via temp output
  dir while the live app instance held bin DLLs). NOT yet screenshot-verified.

## Plugin system note (in progress, user-authored)
A separate plugin architecture is being built concurrently (`AbioticEditor.Plugins.Abstractions`
SDK, `Core/Plugins/*`, CLI `PluginsCommands`, App `PluginService`/`PluginsPage`, sample plugins
under `plugins/`, Jint-backed `Scripting/JavaScriptPlugin` + `JsCapabilities`). The original
build blocker (missing `Scripting.JavaScriptPlugin`) was resolved by the user's new Scripting
files; Core compiles. The comparison work above deliberately stays out of the `Plugins/` files
to avoid clobbering live edits.

**Fix-up contribution (new files only, no edits to in-flight Plugins/ code):**
- Two managed sample "fix-up" plugins under `plugins/`: `RepairNeeds` (`repair-needs`, player -
  tops every survival need to 100 via PlayerSaveReader/Writer, also repairs needs that read 0
  from a missing tag) and `GrantFlag` (`grant-flag`, world - adds a named entry to `WorldFlags`
  by editing `context.Save.Properties` directly, so it handles flags Core doesn't model;
  required `flag` param, idempotent). Both build clean; added to `AbioticEditor.slnx`.
- `tests/AbioticEditor.Tests/PluginFixupTests.cs` (5 green) drives the REAL sample operations
  through `SaveOperationRunner` against throwaway fixture copies: needs restored on reload +
  `.bak` only on real write, dry-run leaves bytes untouched, wrong-kind rejected, grant-flag
  add→idempotent, missing-required-param fails. Tests.csproj now references the two samples.
- `docs/plugin-fixups.md`: a fix-up cookbook (typed-writer repair, raw-property edit, backpack/
  journal/version notes, testing pattern).
- **Version fix-up hook (`ISaveUpgrader`)** - implemented end-to-end (user approved "build it
  now"). SDK: `Saves/ISaveUpgrader.cs` (+ `SaveUpgradeProbe`/`ISaveUpgradeContext`/
  `SaveUpgradeResult`), `IPluginRegistry.AddSaveUpgrader`, `saveUpgrader` capability token.
  Core plumbing mirrors the other capabilities: `PluginRegistry.SaveUpgraders` (dedup by id),
  `PluginDescriptor.SaveUpgraders` (+ HasCapabilities/summary), `PluginManager.SaveUpgraders`
  aggregate + copy in LoadOne. `Core/Plugins/SaveUpgradeService.LoadAsync(path, upgraders, log,
  persist)`: tries `SaveGame.LoadFrom`; on NotSupported/Format/InvalidData builds a header-only
  probe (magic+SaveGameVersion+UE4/UE5 read from bytes; save-class/ABF via
  `SaveFolderScanner.ReadHeaderInfo`) and offers it to each upgrader's `CanUpgrade`; the first
  to return corrected bytes wins (host loads them, optionally writes after a `.preupgrade.bak`);
  rethrows the real load error when none handle it. Sample `plugins/VersionShim/`
  (`FixSaveVersionUpgrader`) rewrites an unsupported `SaveGameVersion` field to 3. Tests
  `SaveUpgradeServiceTests` (3): valid save loads w/o upgrade, version-corrupted save recovered
  + persisted + `.preupgrade.bak` + reloads clean, no-upgrader rethrows. Only `PluginRegistry`
  implements `IPluginRegistry` (no other implementer broken); CLI + App rebuild clean against
  the extended SDK. NOT yet wired into the App/CLI open-save path (host integration left to the
  user, who owns MainViewModel/PluginService). **15 new tests this session** (7 comparison +
  5 fix-up + 3 upgrade).

## Round-13: skill milestone detail + hidden-until-unlock (2026-06-13)
- **Tap a milestone chip → detail card in the right slot panel** (parity with door/chapter/
  flag/trader detail). `SkillMilestoneViewModel` gained detail members (SkillName,
  SkillIconPath, LevelText, StatusText, RequirementText = levels/XP to go, perk + effect).
  `PlayerEditorViewModel.SelectedMilestone`/`HasSelectedMilestone`; `MainViewModel.ShowMilestoneDetail`
  (added to `RaiseSidebarContextChanged` + the `OnEditorContextChanged` name filter -
  PlayerEditor is already subscribed). New milestone card in `SlotSidebarView.xaml`
  (gated on ShowMilestoneDetail, absolute x:Reference Root bindings, ✕ → `OnCloseMilestoneDetail`).
  Chip tap handled by `PlayerSkillsTab.OnMilestoneTapped` (toggles selection; closes on re-tap).
- **Hidden-until-unlocked perks** (user note: the game hides milestone perks until reached):
  mirrored via the Round-10 SpoilerService. A LOCKED milestone is `IsConcealed` (future =
  `!IsUnlocked`); chip masks the perk name (level stays visible) + effect shows "Hidden until
  unlocked". Tapping a sealed chip prompts OVERRIDE CLEARANCE, then opens the detail; raising
  the skill to the level auto-reveals it (RefreshUnlockState now re-notifies all masked/derived
  members). Spoiler protection OFF → every perk shows as before. New `SpoilerService.Skill` ns.
- **Milestone data verified COMPLETE**: `SkillMilestoneCatalog` matches docs/research-wiki-round10.md
  exactly (all 15 skills; irregular counts 4-8 are correct; Fishing has no level-20). The
  "missing milestones" perception was the real per-skill irregularity + the in-game
  hidden-until-unlock behavior now reflected by concealment.
- Build: App compiles clean for net10.0-windows (0 errors; live instance locks bin DLLs, so
  verified via a temp `-o` output). NOT yet screenshot-verified (user holds the running app).

## Round-12: right-click "Open in Explorer/Finder" (2026-06-13)
- **`FileRevealer`** (partial class, mirrors `FolderDropHandler`): shared `Views/FileRevealer.cs`
  exposes `Reveal(path)` (safe/no-throw, logs failures) + a static `RevealLabel` ("Open in
  Explorer" on Windows, "Open in Finder" on macCatalyst, "Open File Location" elsewhere).
  Platform impls of `static partial void PlatformReveal`: Windows `explorer /select,"path"`,
  macOS `open -R "path"`. Android/iOS provide no impl, so the partial no-ops there.
- **Context menu**: `FileSidebarView` save rows AND config rows got a `FlyoutBase.ContextFlyout`
  → `MenuFlyout` with one `MenuFlyoutItem Text="{x:Static views:FileRevealer.RevealLabel}"`.
  Handler `OnRevealFileClicked` reads the row's BindingContext (SaveFileSummary.FullPath or
  ConfigFileOption.File.FullPath) and calls `FileRevealer.Reveal`. (Row style AfSidebarItem
  already has BackgroundColor=Transparent, so the Grid is hit-testable for right-click.)
- Build: 0 errors. **Screenshot-verified**: right-clicking a save row shows "Open in Explorer";
  clicking it opened a File Explorer window at the save's folder.

## Round-11: in-app dialog (replaces native popups) (2026-06-13)
- **`DialogViewModel` (ViewModels) + `DialogHostView` (Views)**: one app-global, animated,
  themed modal that replaces every `DisplayAlert`/`DisplayActionSheet`. `DialogViewModel.Current`
  is a singleton the always-present overlay binds to; callers `await` `ShowAsync(title, message,
  params (text, DialogTone)[])` (returns chosen index, -1 if scrim-dismissed), or the
  `ConfirmAsync`/`AlertAsync` convenience wrappers. `DialogTone` = Primary/Danger/Neutral →
  button fill resolved from theme resources at show time.
- **Overlay**: `DialogHostView` added to MainPage as the top-most child (`Grid.RowSpan=4`),
  hidden until opened. Code-behind animates enter (scrim fade-in + card scale 0.92→1 SpringOut)
  and exit (reverse, then hide) via `FadeToAsync`/`ScaleToAsync` (the non-Async `FadeTo`/`ScaleTo`
  are obsolete in .NET 10 MAUI - using them was the CS0618 source, now fixed). Scrim tap = cancel.
- **Routed every former native dialog through it:** `ViewUtils.ConfirmAsync`/`AlertAsync`
  (host param kept for call-site compat, now ignored) → so `ConfirmBulkAsync`/`ConfirmRevealAsync`
  and all their callers ride along; `SpoilerPrompt.RevealAsync`; and the three direct
  `MainViewModel` popups - the leave-gate (now 3 toned buttons: Cancel/Discard[Danger]/Save),
  the mappings-installed alert, and the bed-reassign confirm (Danger).
- Build: 0 errors; CS0618 cleared. NOT screenshot-verified open (coordinate automation +
  concurrent live use of the app made a clean capture impractical) - but it's exercised by the
  Round-10 spoiler reveal prompts, which now route through it.

## Round-10: spoiler protection (2026-06-13)
- **App-wide SPOILER PROTECTION** (default ON): seals content the player hasn't reached
  behind an in-universe CLASSIFIED / CLEARANCE-REQUIRED stamp; tapping a sealed item
  prompts an OVERRIDE CLEARANCE confirm and reveals just that item, permanently (per-item
  reveals persist across sessions). Scope = future/locked content only.
  - `Services/SpoilerService` (static, Preferences-backed like ThemeService): `Enabled`
    (key `SpoilerProtectionEnabled`, default true), persisted revealed-key set (key
    `SpoilerRevealedKeys`, `\n`-joined), `Key(ns,id)` / `IsRevealed` / `ShouldConceal(key,
    isFuture)` / `Reveal` / `ResetReveals` / `RevealedCount`, `Changed` event, mask copy
    constants (`ClassifiedTitle`/`ClassifiedShort`/`Redacted`/`ClassifiedHint`) + `Mask()`.
    Namespaces: flag/trader/recipe/ach/codex/containment.
  - `Services/SpoilerPrompt.RevealAsync(what,key)` routes through the in-app
    `DialogViewModel` (no page ref needed) so any row VM offers tap-to-reveal.
  - SETTINGS gained a SPOILERS section: master toggle + RE-SEAL REVEALED ITEMS + count
    hint. Toggling/reseal sets `_spoilerChanged`, so CloseAsync rebuilds the editor host
    (same path as theme) and every open surface re-evaluates concealment.
  - Per-surface masking (Shown* display props + IsConcealed + tap-to-reveal; sealed rows
    can't open their detail pane - the selection setter / tap handler redirects to a
    reveal prompt, and acting controls like checkboxes/TOGGLE are disabled while sealed):
    - **Achievements** (`AchievementRowViewModel`): generalized the old per-tab
      `ShowSpoilers` into the global service; future = `Hidden && !Unlocked`. The SHOW
      SPOILERS checkbox now mirrors the app-wide setting. Row tap = reveal.
    - **Recipes** (`RecipeRowViewModel`): future = `!IsUnlocked`; masks name/status/
      tooltip/icon, disables the unlock checkbox, guards `SelectedRecipe`.
    - **Flags** (`FlagItemViewModel`): future = `IsLocked` (gated); masks friendly/raw
      name, description, STORY chip; disables TOGGLE; guards `SelectedFlag` (rebuilds the
      grouped list on reveal since the VM is immutable/no INPC).
    - **Traders** (`TraderCardViewModel`): future = `!IsAvailableHere`; masks name/where/
      blurb/sells/availability/portrait; `OnTraderCardTapped` redirects to reveal.
    - **Codex** (`CodexItemViewModel`): future = not-known AND region-gated
      (`!ProgressContext.CanUnlockRow`); masks title/subtitle/body/icon, disables the read
      checkbox, guards `Selected`.
    - **Containment** (`LeyakContainmentViewModel`): every contained anomaly is a
      candidate; masks creature name + flips the tap hint; `OnContainmentTapped` redirects
      to reveal (detail keeps appearance/location sealed until revealed).
  - **CLI**: reviewed - the only story-content command is `flags list`, which prints
    flags ALREADY SET (achieved progress), not future/unreached vocabulary, so nothing to
    conceal under the future-only scope. No CLI change (an unused `--show-spoilers` flag
    would be noise). The CLI runs in a separate process with no shared Preferences anyway.
  - Build: Core+CLI clean; App compiles clean for net10.0-windows (0 errors; the live app
    instance locks bin DLLs so the in-place copy step fails - compiled to a temp output to
    verify). Pre-existing CS0618 (DialogHostView) / CA1305 (MainViewModel) warnings remain.
  - NOT yet screenshot-verified (user holds the running instance, PID seen locking DLLs).

## Round-9: domain content → Core + Traders UI rework (2026-06-13)
Goal: shrink the UI to presentation only; move game *facts* into Core catalogs so a CLI / future
frontend can reuse them. Move data (records/strings), not behaviour - no description-service.
- **5 domain-content moves (App ViewModels → Core):**
  1. Door lock prose: `WorldDoorViewModel.AboutText` → `DoorClassCatalog.LockExplanation(lockKind)`.
  2. `App/ViewModels/TraderLore.cs` → `Core/Codex/TraderLore.cs` (namespace `AbioticEditor.Core.Codex`).
     Added `using AbioticEditor.Core.Codex;` to WorldEditorViewModel, ItemPaletteViewModel,
     RecipeListViewModel (TraderCardViewModel already had it).
  3. Containment creature `DisplayName`/`Lore` (Leyak/Krasue) → `Core/WorldSaves/ContainmentCreatureCatalog`.
  4. NPC identity hints → `Core/WorldSaves/NpcIdentityCatalog.LabelFor(id, actorName)`
     (the `IsPet` short-circuit stays in the VM as presentation).
  5. Ini per-kind `KindLabel`/`Description` → `AbioticIniCatalog.LabelFor`/`DescriptionFor`.
  Left in the UI on purpose: LockChip, KindLabel mappings used purely for display, coordinate
  formatting in ContextText/LocationText, tooltips that just compose existing Core fields.
- **Traders UI moved into the right-hand slot panel** (parity with door/chapter/flag detail):
  - Inline detail Border removed from `WorldTradersTab.xaml`; roster + tap-to-open kept. Tab
    handlers trimmed to just `OnTraderCardTapped` (selecting a card sets `SelectedTrader`,
    which drives `ShowTraderDetail` → the sidebar card; tapping the open card closes it).
  - New trader detail card in `SlotSidebarView.xaml` (gated `ShowTraderDetail`): portrait, blurb,
    availability, barter terms, stock list, unlock buttons. Handlers `OnCloseTraderDetail`,
    `OnUnlockSelectedStock`, `OnUnlockTraderFull`, `OnTraderOfferTapped` in `SlotSidebarView.xaml.cs`.
  - **Item icons + inspection:** `TraderOfferRowViewModel` now carries `ItemId` + a
    `PaletteItemViewModel Item` (reuses the palette VM's icon extraction AND encyclopedia detail).
    Each stock row shows the real item icon; tapping a row/icon calls `TraderCardViewModel.SelectOffer`
    → `SelectedOfferItem` → an encyclopedia sub-card (icon, stats, description, crafted-by, used-in)
    inside the trader panel. `RefreshAvailability` clears `SelectedOfferItem` (rows are rebuilt).
- Build: 0 errors (Core + App). Visual pass: confirmed metadata save loads + the world tab strip
  renders; full Traders click-through was NOT completed on-screen (live desktop had the app in use
  + window-focus contention) - needs a quick visual confirm when the desktop is free.

## Round-8: phantom-dirty fix + nav/ini polish (2026-06-13)
- **Phantom discard dialog on tab→save switch FIXED**: two binding write-backs were
  dirtying clean player saves. (1) SKILLS XP slider clamp-wrote real XP — `MaxXp` now
  accommodates over-cap end-game XP, `XpSliderValue` rejects the platform slider's
  default-range (0..1) init-clamp (`value <= 1 && _xp > 1`) and tolerates sub-0.5 drift;
  (2) SPAWN region/terminal pickers replayed stored values during binding churn — snaps
  now guard against the save's own baseline. Diagnostics: `PlayerEditorViewModel.DescribeDirty()`
  lists every dirty contributor; the leave-gate logs `Leave-gate for <file>: …` when a
  player save is dirty (App channel). Fixture tab-walk + save-switch now clean.
- **HOME button**: new header button → `MainViewModel.GoHomeAsync()` runs the leave-gate,
  tears editors down directly (no double-gate), re-scans detected worlds, keeps the folder
  + save list loaded. Returns to the landing page; verified on fixture.
- **Folder-picker cancel no longer errors**: `FolderPicking.PickAndLoadAsync` treats a
  dismissed dialog (toolkit reports cancel as an exception-bearing failure) as a no-op
  via `IsCancellation` (OperationCanceledException or "cancel" in message).
- **SandboxSettings.ini key trimming fixed**: ini key column 140→320px, MiddleTruncation→
  TailTruncation + full-key tooltip; long keys (RefrigerationEffectivenessMultiplier) now
  render whole. Verified on fixture.
- **Ini SAVE/REVERT buttons resized**: were inheriting default button metrics (huge); now
  FontSize 11 / Padding 16,6 / VerticalOptions Center to match the panel-header scale.
- Trader (UnlockTrader*/OfferRow/SelectedTrader) + leyak containment detail (EnsureDetail)
  members were completed by the user concurrently; build green (0 errors).

## Round-7: UX fixes + in-app Steam sign-in + visual pass (2026-06-13)
- **STORY tab SET consolidated**: the ADD FLAGS UP TO HERE / CLEAR FORWARD FLAGS
  buttons are gone; `WorldEditorViewModel.SetChapterAsync(row)` (wired to every
  chapter's SET + the sidebar card) moves the pointer AND runs SyncFacilityFlags +
  ClearForwardFlags in one go (Facility file written immediately w/ .bak; pointer
  stages until SAVE).
- **Transmog visibility toggles limited to visual gear**: VisibleTransmogToggles =
  indices 0-5 (chest/head/legs/backpack/arms/suit); headlamp/trinket/watch/hacker
  toggles were no-ops (no body visuals). All 12 flags still round-trip.
- **Spawn coords snap**: RespawnTerminalCatalog gained the 10 terminals' world
  positions (from research-respawn-terminals.md); picking a REGION or RESPAWN
  TERMINAL snaps X/Y/Z to the matching terminal anchor. Guarded against the save's
  own stored value - Picker binding churn during load replays it (caught live:
  unguarded version overwrote coords + dirtied on load).
- **Skills XP slider** (replaces the read-only progress bar): 0..MaxXp where MaxXp =
  max(level-20 threshold, the save's own XP) - END-GAME SAVES EXCEED THE CAP (e.g.
  97,079 XP) and a threshold-only Maximum clamp-wrote them away (caught via the
  edit-trace log); XpSliderValue has 0.5-XP write-back tolerance (F0-entry gotcha).
- **In-app Steam sign-in** (subagent): SteamLoginPage (MAUI WebView -> WinUI WebView2
  CookieManager captures sessionid+steamLoginSecure; #if WINDOWS, browser fallback
  elsewhere), Services/SteamSession (memory + SecureStorage "SteamSessionCookie",
  SIGN OUT clears), SteamWebAchievements.FetchAsync(cookieHeader) sends the Cookie
  header; achievements tab: SIGN IN (IN-APP) primary in the gated panel, SIGNED IN ·
  SIGN OUT cluster in the header; comparisons also use the session.
- **Doors**: REGION WIKI button removed (ONLINE MAP stays); keycard AboutText
  corrected - keycards are NOT looted from corpses (placed in world; keypad hacking
  is the common path).
- **Visual pass done (fixture tree)**: split UI, vitals, skills 3-col grid +
  sliders, transmog 6 toggles, bases inline container list, door detail card, NPCs
  incl. PET rows all verified by screenshot; dirty-on-load regressions fixed and
  re-verified clean. LESSON: interactive click-driving must use
  ABIOTIC_EDITOR_FOLDER=tests/fixtures/... - stray clicks on the live tree staged
  real edits (leave-gate discarded them).
- 259 tests green; only upstream NU1903 warnings remain. User is concurrently adding
  pet renaming (PetName entry in WorldNpcsTab + WorldNpcViewModel; writer-side
  CustomName persistence still needed at the time of writing).

## Round-6: more UNKWN candidates + platform drop + Core layout + Steam prompt (2026-06-13)
- **PetNPC merged into the NPCs tab**: `WorldNpc` gained IsPet/CustomName/NpcClass
  (ActorName prefers the given name, then the pet's class tail); reader walks
  NarrativeNPCMap + PetNPC (same SaveData_NPCState_Struct), writer patches both maps;
  rows show a PET chip; named pets display as e.g. "Rex".
- **World unlocks (GlobalUnlocks struct, STORY tab)**: counts + additive staged bulk
  unlocks for GlobalItemsPickedUp/GlobalEmailsRead/GlobalJournalEntries/
  GlobalCompendium{Email,Narrative,Exploration} (vocab: item catalog + CodexCatalog;
  compendium rows land per SectionTypes, existing placements never moved).
  `ReadGlobalUnlockArray`/`ApplyGlobalUnlockArray` in reader/writer.
  `LastPlayed` shown next to MINUTES PLAYED. PetNPC/GlobalUnlocks/LastPlayed added to
  ConsumedPrefixes.
- **Folder drop is now per-platform** (subagent): `Views/FolderDropHandler` partial
  class; Windows = previous behavior, MacCatalyst = real UIDropSession file-url drop
  with security-scoped access (compile-verified only), Android/iOS = deliberate no-op
  (picker covers them). MainPage ctor just calls FolderDropHandler.Attach. All four
  TFMs build.
- **Core layout pass**: folders already matched namespaces except four misplaced
  files - moved AbioticSaveClasses.cs (root -> SaveClasses/), SaveJsonBridge.cs
  (root -> Saves/), SteamPersonaIndex.cs (Saves/ -> Steam/), SaveCompatibility.cs
  (SaveClasses/ -> Compatibility/), namespaces re-aligned, usings fixed repo-wide.
  Deliberately NOT merged: Saves/ (file plumbing) vs SaveClasses/ (UeSaveGame
  [SaveClass] impls + JSON serializers) - distinct concerns, both well-named.
- **Steam achievements gated-profile prompt**: AchievementsViewModel.ProfileGated +
  SignInAndViewCommand (opens steamcommunity login that redirects to the profile's
  achievements page - signed-in sessions can see own/friends' stats) +
  OpenPrivacySettingsCommand; hazard prompt panel in PlayerAchievementsTab appears on
  SteamGameDetailsPrivateException.
- 255 tests green; app/CLI build clean (pre-existing CA1859 only). Visual verify still
  pending (user instance holds the exe).

## Round-5: theme fix + MainPage split + bases/skills/selection + UNKWN modeling (2026-06-12 night)
- **Theme staleness FIXED**: MAUI Style objects are created lazily and capture
  StaticResource color VALUES once app-wide, so ThemeService's resource overwrites
  never reached already-created styles (buttons/panels/labels stayed on the old accent
  until restart). AbioticStyles.xaml now uses DynamicResource for every color (incl.
  VSM states; Brush-typed Stroke setters point at the *Brush keys because
  DynamicResource skips the Color->Brush converter). Page rebuild on switch stays (it
  covers inline StaticResources + converter output). LIVE SWITCH NOT YET SCREENSHOT-
  VERIFIED - the user's app instance held the exe all session.
- **MainPage.xaml SPLIT** (was ~3.7k lines + 800 code-behind; now ~190 + ~110):
  - `Views/`: HeaderBarView (FILES/TOOLS raise events; SetCompact), FileSidebarView,
    SlotSidebarView (all detail cards + palette; x:Name="Root" preserved so the
    absolute-binding pattern is untouched), IniEditorView, EmptyStateView,
    StatusBarView (SettingsRequested event).
  - `Views/Player/`: PlayerEditorView (tab strip) + General/Vitals/Character/Transmog/
    Spawn/Inventory/Skills/Recipes/Codex/Achievements/Raw tabs. Multi-panel tabs
    (vitals, character) wrap their panels in a VSL with the root ContentView's
    IsVisible bound to the tab flag.
  - `Views/World/`: WorldEditorView (summary + strip) + Containers/Flags/Doors/
    Dropped/Npcs/Bases/Story/Raw tabs.
  - Shared plumbing: `Views/ViewUtils` (FindBoundContext/ParentPage/Confirm helpers),
    `Views/SlotInteractions` (all slot/palette/container gesture logic; views keep
    thin instance wrappers - XamlC needs handlers on the x:Class type),
    `Views/FolderPicking`, `Views/ResponsivePaneController` (ALL breakpoint/drawer
    logic moved out of the page; subscribes to vm.SelectedSave to close the file
    drawer and vm.ActiveSlot to surface the slot pane - drawer on phones, un-collapse
    inline on desktops).
  - Pure logic moved to MainViewModel: SelectSlot, SortBackpack, DropActiveItem,
    BeginDismantlePreview/ConfirmDismantle/CancelDismantle + FindSlotCollection.
- **Skills tab large-screen fix**: FlexLayout (lone last card stretched full row) ->
  CollectionView + ResponsiveLayout.ItemWidth=430/MaxSpan=3 uniform adaptive grid.
- **Selected-slot highlight**: InventorySlotViewModel.IsSelected (maintained by
  MainViewModel.ActiveSlot setter) -> hazard-yellow 2px ring + hover bg via
  DataTrigger in pockets/hotbar/transmog/container/base slot templates + an overlay
  ring in App.xaml's EquipmentSlotTemplate (TemplateBinding IsSelected).
- **Bases tab overhaul**: container rows in "CONTAINERS IN THIS BASE" are now
  selection rows (EDIT-jump button removed) bound to NEW
  WorldEditorViewModel.SelectedBaseContainer; the selected container's slot grid
  opens IN PLACE of the base map (ShowBaseMap=false) with a "✕ MAP" close button,
  full slot gestures and the slot-editor sidebar (auto-surfaced via the controller).
  Cleared when SelectedBase changes.
- **UNKWN log review (editor-20260612.log) -> newly modeled** (probe:
  tests/AbioticEditor.Probes/UnmodeledWorldPropsProbe.cs):
  - `TimeOfDay` struct (Facility save): TimeOfDaySeconds (double 0..86400) +
    CurrentDay + LastAssaultDay/LastWeatherDay/LastPowerLeechDay ints. Editor: WORLD
    DAY entry + TIME OF DAY slider in the world editor header
    (Reader.ReadWorldClock / Writer.ApplyWorldClock).
  - `DayDiscovered` int (region saves): editable entry
    (ReadDayDiscovered/ApplyDayDiscovered).
  - `LeyakContainmentIDs` Map<Name,Str> (metadata save): creature row name (Leyak,
    Krasue) -> containment unit's DeployedObjectMap GUID (teleporter-style link).
    Editor: CONTAINMENT tab (metadata-only) with per-creature RELEASE
    (stages map-entry removal; ReadLeyakContainments/RemoveLeyakContainment) and a
    tap-to-detail card (compendium texture T_Compendium_<creature> + sector name from
    RespawnTerminalCatalog.NearestTo against the containment unit's facility deployable).
  - All three added to WorldSaveReader.ConsumedPrefixes (UNKWN noise gone).
- **Traders rework (metadata TRADERS tab)**: trader roster moved off the world NPCs
  tab onto a metadata-only TRADERS tab. Fili excluded (TraderLore.NonTraders - she is
  an Anteverse NPC, not a trader); unknown future rows still render (row-id fallback,
  sorted after known lore). Per-item stock unlocking: each locked offer row has a
  checkbox; APPLY writes the chosen RequiredFlags (plus any trader-gating flags) via
  StoryFlagSync.AddFacilityFlags into WorldSave_Facility.sav with a confirmation dialog
  listing the exact flags. Availability now reads HasWorldFlag (sibling Facility flags
  on the metadata save). TraderOfferRowViewModel replaces the OfferRow record;
  OfferDetails is cached (WinUI re-seed guard).
  - Still unmodeled (documented): GlobalUnlocks struct (world-wide pickups/emails/
    journal/compendium/distilled arrays - candidate for world bulk unlocks),
    LastPlayed DateTime, ServerEntitlements, PetNPC (same struct as NarrativeNPCMap -
    candidate to merge into the NPCs tab), Destructible/Elevator/ResourceNode/Button/
    NPCSpawn/PowerSocket/Trigger/Portal/Tram/Vehicle/Corpse/Decal maps; player-side
    CompletedIntro, LastControlRotation, CurrentBuffDebuffs, unread/favorites arrays.
- 255 assertion tests green; app compiles with zero NEW warnings (CA1859 in
  DoorLocationResolver pre-exists from the door-locator work). VISUAL VERIFY PENDING
  for: theme live-switch, all split views render, bases inline editing, skills grid,
  slot highlight - blocked because the user's app instance held bin\...\exe.

## Round-4: UI rework (2026-06-12 evening) - game style + fluidity + mobile
- **Theme**: default palette is now the game-accurate blue-teal facility look
  ("FACILITY BLUE" = ThemeAccent.Cascade, colors lifted from the shipped inventory UI:
  panes 306481/5292B7, headers 71C5F6, cyan readouts ~8CFFFB, CTA orange F89A4F,
  caution yellow FFE563). Pref key bumped to `ThemeAccentV2` so existing installs
  re-default; HAZARD ORANGE (old amber-CRT) stays as the alternate. Colors.xaml static
  values mirror the new default.
- **Motion**: `Controls/Fx.Reveal` attached property fades+rises panels when IsVisible
  flips (all player/world tab panels carry it); Button styles gained Pressed states
  (scale dip); drawers slide with eased TranslateToAsync + scrim fade.
- **Responsive system** (all in `src/AbioticEditor.App/Controls/`):
  - `AdaptiveGrid`: stacks its cells vertically below CompactWidth (own measured
    width); lifts child WidthRequests while stacked; if the grid had a fixed height it
    moves StackedChildHeight onto height-less children. Used on codex/recipes/
    containers/bases master-details, inventory 3-col, body health, spawn coords,
    steamid row, background row, story-flag batch row.
  - `ResponsiveLayout.ItemWidth/MaxSpan`: recomputes GridItemsLayout.Span from the
    CollectionView's width (pockets/hotbar/container slots 96px, palette 110, traders
    340). Hotbar becomes a multi-column grid when the inventory stacks.
  - Tab bars (player 11 tabs, world 8, codex apps) are horizontal ScrollViews of
    `AfTabButton` chips - no more crushed star columns.
- **Drawer mode** (MainPage.xaml.cs): below 800px width both side panes re-home from
  the inline Auto columns into overlay ContentView hosts (`*InlineHost`/`*OverlayHost`)
  and slide in over the editor with a tap-to-close scrim; FILES/TOOLS toggle them,
  tapping a slot auto-opens the tool drawer, picking a save auto-closes the file
  drawer (MainViewModel.SelectedSave PropertyChanged hook). Header drops breadcrumb +
  version below 900px. Inline auto-collapse below 1150px unchanged. Pane re-homing
  back to desktop verified by screenshot both directions.
- **Crash logging**: App.xaml.cs writes unhandled/unobserved exceptions to
  `%TEMP%\AbioticEditor-crash.log` (WinUI otherwise exits silently). Added because one
  launch died with exit -1 before logging existed; not reproduced since - if the app
  vanishes again, read that file.
- Verified by screenshots (tools/shots/rework-*, step*-*.png): desktop empty/player/
  skills/inventory + slot sidebar, phone (440px) vitals/skills/world containers,
  FILES + slot drawers, 700px inventory, desktop restore after drawer mode.
- Known cosmetic leftover: transmog slot row keeps Span=6 on phones (6 slots shrink
  rather than wrap - its CollectionView has a fixed 104px height).

## Round-3 additions (2026-06-12 late)
- **Repo flattened**: the old `dotnet/` wrapper is gone; `src/ tests/ docs/ assets/
  tools/` plus slnx/props sit at the repo root. `Saved/` deleted; `Saved/` +
  `*.upipelinecache` ignored. README rewritten for the dotnet product.
- **Projects**: `src/AbioticEditor.Cli` (abioticeditor: scan/info/export-json/
  import-json/flags/steamid/ini/version, exit codes 0/1/2, --json), probes split into
  `tests/AbioticEditor.Probes` (dotnet test on Tests = assertions only). Central
  package management (`Directory.Packages.props`, transitive pin Microsoft.Bcl.Memory
  9.0.14); submodules shielded via `submodules/Directory.*.props`. Analyzers
  latest-recommended, ZERO warnings in our projects (probes exempt by design; CA1707
  off in test projects; XAML-bound members carry justified suppressions).
- **slnx lists all submodule projects** (CUE4Parse, CUE4Parse-Conversion,
  UeSaveGame.Json added) - required for IDE IntelliSense; reload the C# language
  server after pulling this change.
- **Core**: `Saves/PropertyTagExtensions` (shared FindByPrefix/Get*/TryGet* used by
  both readers+writers), `Saves/SaveDiscovery` (client tree + Steam-library dedicated
  server scan, Backups skipped), `Ini/IniFile`+`AbioticIniCatalog` (order/comment
  preserving; Admin.ini, SandboxSettings.ini, client config),
  `Compatibility/SaveVersionRegistry`+`CompatibilityAnalyzer`+`CompatibilityReport`
  (severities Exact/NewerMinor/NewerVersion/Unknown; bump versions in the registry on
  a game update), `BackpackSpecialSlotCatalog` (DYNAMIC from DT_ItemCosmetics ->
  data-asset slot arrays; unknown *Slots kinds get derived badges + UNKWN log;
  verified table as fallback).
- **App**: ini editor (CONFIG FILES sidebar section -> section/key/value editor,
  .bak on save); SETTINGS modal (`SettingsPage` + `Services/ThemeService`: Hazard
  Orange / Cascade Blue accents x dark/light, persisted, applied in App ctor, page
  tree rebuilt on switch); per-save dirty gate (`ConfirmLeaveCurrentEditorAsync`:
  Save and continue / Discard / Cancel on every navigation incl. theme rebuild,
  folder drop, discovery load); startup world discovery list on the empty state
  ("WORLDS FOUND ON THIS MACHINE" + LOAD); folder drag-and-drop onto the window
  (#if WINDOWS, accepts a folder or any file inside one). Status bar slimmed to
  status + logging text + SETTINGS.
- **Standalone saves**: folders with only player saves or only the metadata save
  load with no world context (gating off, pickers empty) - pinned by
  StandaloneSaveTests.
- **Style scrub done**: em-dashes/ellipses/arrows and telltale phrasing removed from
  comments and docs repo-wide (129 files); functional UI glyphs kept.
- Verified by screenshot: startup discovery list shows Cascade + Chrissie (CLIENT)
  with LOAD buttons; app launches clean.

## Older layout notes (round 2)
- **Rust product REMOVED**: `uesave/`, `uesave_cli/`, `uesave_wasm/`, `web/`, Cargo files,
  `.github/` rust/web workflows - all git-rm'd (uncommitted).
- `src/AbioticEditor.App` (MAUI, net10 win), `src/AbioticEditor.Core` (net10),
  `tests/AbioticEditor.Tests`.
- **Fixtures (all save trees now under `tests/fixtures/`)**: `Cascade/` (51 MB),
  `ClientSaved/SaveGames/` (~390 MB, client tree, newer build), `Server/` (~389 MB,
  dedicated server, complete story, NAMED benches). Total ~828 MB - consider Git LFS or
  dropping `Backups/` generations before committing. `Fixtures.cs` exposes `CascadeDir`,
  `ClientSavedDir`, `ServerWorldsDir` (walk-up + legacy fallbacks). `.gitignore` no longer
  ignores them; leftover `Saved/` (Config/Logs/pipelinecache only) is untracked - user to
  decide delete vs re-ignore (may contain private data).
- Submodules at repo-root `submodules/CUE4Parse` (@1125f5bc) + `submodules/UeSaveGame`.
- usmap at `assets/Mappings.usmap` (bundled via csproj link); user override at
  `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap` - installable in-app via the
  status-bar IMPORT USMAP button (`GameAssetProvider.InstallUserMappings`, validates the
  0xC4 0x30 magic; restart required). App icon = game's ABF logo.

## Feature inventory (all shipped + tested)
- **Editors**: player (stats/inventory/equipment/skills/traits/recipes/codex/fish/kills/
  maps/transmog/respawn/steamid), world (containers/flags/doors/dropped/NPCs/bases/story),
  metadata (story chapter + world research recipes - other tabs hidden there),
  ScientistCustomization (13 appearance fields, per-account), JSON export/import.
- **Player tabs**: GENERAL (SteamID change - renames file AND rewrites internal
  `SaveIdentifier`; bulk unlocks w/ confirmations) · VITALS (stats+health) · CHARACTER
  (background/traits/appearance w/ preview icons+swatches) · TRANSMOG (6 slots with
  CHEST/HEAD/LEGS/BACK/ARMS/SUIT roles + 12 visibility toggles) · SPAWN (XYZ + region
  picker by friendly name, respawn-terminal picker (10 known terminals), bed picker) ·
  INVENTORY (3-col game-like layout, POCKETS titled by equipped pack + capacity,
  special-slot tags COLD/FREEZER/SHIELDED/WARM, money/SORT/DROP ITEM footer, vertical
  hotbar) · SKILLS (2-up cards, milestone chips w/ visible effects) · RECIPES (icons +
  wiki-style detail pane) · GATEPAL (PDA-look chrome) · ACHIEVEMENTS · DATA.
- **Slot editor**: enum-strict equip validation (`EquipSlot`, wildcard 2), upgrade/
  downgrade via DT_ItemUpgrades + dynamic special chains (keypad_hacker t2..t9 probed
  from catalog), ▲ badge, dismantle (preview+confirm, hidden when no recipe), teleporter
  ↔ bench sync, LIQUID section (type picker limited to the item's `AllowedLiquids`,
  level capped at `MaxLiquid`).
- **World**: quest flags grouped in story order (01 OFFICE ... 11 FINALE, anomalies last)
  with per-flag descriptions, lock chips, sidebar detail (region card art, prereqs ✓/✗,
  gated TOGGLE + SET PREREQUISITES); chapter list w/ DONE/READY/LOCKED (facility flags
  read cross-file) + sidebar quest cards (map_* art, all 37 summaries); trader detail in
  sidebar w/ UNLOCK TRADER+STOCK (warning lists flags); NPCs (identities, REVIVE-only,
  script-phase note); bases (per-bench list w/ custom names, reworked map: glyph legend,
  bench labels, selected-base ring); doors tab shows sector card banner (game has NO
  per-door art - verified).
- **Gating**: FlagGate (story-linear prereqs + area->chapter); ProgressContext gates
  codex rows (area-prefix) and recipes (email-attachment link) against world progress
  loaded from sibling facility save; ungated when no world context.
- **Compat/diagnostics**: ABF_SAVE_VERSION JSON header serializers (was silent
  corruption); version warnings (world v3 / character v1 known-good); unknown recipe
  tables -> "Misc"; unknown skills/fish/rows preserved + labeled; writers create missing
  delta-serialized tags (survival stats, slot ChangeableData - exact full names
  hardcoded); `EditorLog` (opt-in toggle in status bar, 7-day rotation, edit-trace of
  every staged change, `UNKWN` channel for unmodeled save properties, dedup per folder).

## Research docs (all under docs/)
`player-save-schema.md`, `world-save-schema.md`, `research-customization.md`,
`research-wiki-round10.md` (skill milestones/appearance/item-infobox),
`research-respawn-terminals.md` (GUID->location table),
`research-transmog-appearance.md` (slot indices, EquipSlot enum, customization icons),
`research-narrative-npcs.md`, `research-backpack-traits.md` (capacity/special slots,
cut traits), `research-new-save-gaps.md` + `research-server-saves.md` (round-trip
audits; DayDiscovered/CorpseMap unmodeled), `research-slot-types.md` (EquipSlot map,
bench-name verdict, SaveIdentifier), `research-gatepal-quests.md` (PDA spec, inventory
spec, 37-chapter table), `reference-inventory-ui.png` (user's target screenshot).
Liquids: enum + LiquidData findings live in `tests/LiquidDoorProbeTests.cs` output
(E_LiquidType displaymap hardcoded in `Core/Items/LiquidTypes.cs`).

## 2026-06-12 round-2 additions
- **Steam achievements fix**: CHECK STEAM failures were misblamed on private profiles.
  Live-probed: profile `privacyState: public` is NOT enough - Steam's separate
  "Game details" dropdown gates `.../stats/<appid>/achievements?xml=1`, and denials come
  back as an HTML error page served with a `text/xml` content type (verified: 1 of the
  4 co-op accounts works anonymously, the others are denied). `SteamWebAchievements`
  now detects the HTML page, extracts Steam's real message, throws typed
  `SteamGameDetailsPrivateException`; the VM shows precise guidance (Game details ->
  Public) and stops blaming privacy for unrelated failures. `ParseResponse`/`ExtractHtmlError`
  exposed + unit-tested (`SteamWebAchievementTests`).
- **Usmap import**: status-bar IMPORT USMAP button -> file picker -> magic-validated copy
  to the `%LOCALAPPDATA%` override; alert explains restart + how to revert. Tests in
  `UsmapInstallTests`.
- **Forward-compat (unknown data)**: unknown door classes/states, equip-slot and liquid
  enumerators now log on the UNKWN channel (deduped); door-state Picker appends an
  unknown current state (e.g. "State 7") so future-version saves display it and
  re-selecting it is a no-op instead of data loss (label-based mapping, not positional).
  Already-graceful paths confirmed: `EquipSlotTypes.NameOf` -> "slot type N",
  `LiquidTypes.NameFor` -> "Liquid #N", unknown flag areas -> "OTHER · ANOMALIES & META"
  group, `DoorClassCatalog.Lookup` -> echo + Unknown lock kind.
- **Perf/memory review COMPLETE** (`docs/research-perf-review.md`). Fixed:
  `WorldLevelIndex` streams instead of LOH `ReadAllBytes`; flag VMs cached (filter
  keystrokes no longer rebuild all `FlagItemViewModel`s; bulk ops batched via
  `RunFlagBatch`); `StoryProgressionCatalog`/`FlagGate` lookups memoized; recipe/skill
  icon extraction batched off the UI thread; hot-path `EditorLog.Info` interpolations
  guarded by `Enabled`. MainViewModel follow-ups applied separately: bench/world-flag
  caches are instance fields cleared per folder load, `ProgressContext.WorldFlags`
  reset on folder switch, editor-setter event detach moved inside the `Set` branch.
  Audited-clean: editor subscriptions, texture disk cache, `SeenUnknown` reset,
  virtualization (big lists all CollectionView).

## Known open items / verify-next
0. NEW (2026-06-12 late): story-timeline checklist REMOVED from region flags tab
   (redundant with grouped list; checklist remains on metadata STORY tab). Door rows
   are now click-selectable -> sidebar door card (sub-level card art, lock kind +
   required-key name, LocationText = sub-level + actor - saves store NO door
   coordinates, stated in UI; state picker + OPEN/ONE-WAY toggles editable there).
   Both built + 285 tests green; screenshot verification pending.
1. **Grouped flag list after ctor fix**: ApplyFlagFilter now runs in the world editor
   ctor (was: groups empty until a filter changed). Built, not yet screenshot-verified
   (user had the screen). Check QUEST FLAGS tab shows grouped rows.
2. **Flag row click -> sidebar detail**: hit-test fix applied (explicit row background);
   verify a row click opens the detail card.
3. Transmog durability retention fix (sparse ChangeableData) is tested at Core level;
   user-flow verification pending.
4. NU1903 advisory: Microsoft.Bcl.Memory 9.0.0 transitive inside CUE4Parse upstream.
5. Nothing is committed - entire working tree awaits user review/commit (submodule
   moves are staged by necessity of git mv semantics).

## Conventions / gotchas (see also memory: abiotic-save-schema-facts)
- Property names hash-suffixed -> always prefix-match; delta-serialization omits
  default-valued tags -> writers must FindOrCreate (full names in PlayerSaveWriter.FullNames).
- Gameplay-tag containers render comma-separated (split on ',' AND '|').
- WinUI: Grid w/o background is hit-test transparent; Border>ScrollView never measures;
  hoist records for x:DataType; stored command instances; F0-format two-way Entry
  bindings write rounded values back (use tolerant dirty thresholds).
- Visual verification loop: `tools/capture.ps1` + env `ABIOTIC_EDITOR_FOLDER` /
  `ABIOTIC_EDITOR_AUTOSELECT`; always check foreground window first - the user may be
  using the machine; never Stop-Process the app while the user has it open.
