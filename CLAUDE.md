# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A save-game editor for **Abiotic Factor** (a Unreal Engine GVAS game). It reads/writes save
files byte-for-byte and understands the game's own data tables by mounting the installed game's
pak archives. Ships a .NET MAUI desktop app and a CLI, both thin front-ends over a shared Core.

## Git workflow

This repo uses **trunk-based development**: commit straight to `main`. Do **not**
create a feature branch unless the user explicitly asks for one. **Commit freely without
asking** once a change is complete; you do not need permission to commit. **Pushing is the
only step that requires explicit user approval** - never `git push` unless the user asks for
it. The release automation (`.github/workflows/release.yml`) is driven by Conventional-Commits
on `main`, so commit messages must follow that convention.

### Commit message tone

The changelog published to Nexus Mods is generated directly from commit messages, so the
audience is **players, not developers**. Write every commit message body as if explaining the
change to a friend who plays Abiotic Factor but has no programming background:

- Describe **what changed for the player** ("traders you haven't met yet are now hidden by
  default"), not the internal mechanics ("add SpoilerGateFlag to TraderLore.Entry").
- Avoid jargon: no class names, method names, file paths, or compiler terms in the body.
  The Conventional-Commits **type prefix** (`fix:`, `feat:`, `docs:`, etc.) is still
  required for the automation, but keep the subject line plain too.
- If you must mention something technical (a flag name, a file format), put it in a
  parenthetical so the plain sentence reads fine without it.
- Short, friendly sentences. One idea per sentence. No bullet walls.

## Build / test commands

Requires the **.NET 10 SDK**. Clone with `--recursive` (or `git submodule update --init`); the
build depends on the `submodules/` source projects.

```console
dotnet build src/AbioticEditor.Core                                       # core library
dotnet build src/AbioticEditor.Cli                                        # CLI (abioticeditor)
dotnet build src/AbioticEditor.App -f net10.0-windows10.0.19041.0         # desktop app (Windows)
dotnet test  tests/AbioticEditor.Tests -f net10.0                         # all tests
dotnet test  tests/AbioticEditor.Tests -f net10.0 --filter "FullyQualifiedName~PlayerSaveFactoryTests"  # one class/test
```

- The **App is multi-targeted** (`net10.0-android;net10.0-ios;net10.0-maccatalyst`, plus
  `net10.0-windows10.0.19041.0` on Windows). Always pass `-f <tfm>` when building it; non-Windows
  targets need the MAUI workload (`dotnet workload install maui`).
- **A running app instance locks its `bin` DLLs.** To verify an App change compiles while the app
  is open, build to a throwaway output dir: `dotnet build src/AbioticEditor.App -f net10.0-windows10.0.19041.0 -o "$env:TEMP\verify"`.
- The `CUE4Parse-Natives build failed … 'cmake' is not recognized` line during builds is **benign**:
  the native texture decoder is optional; managed parsing/extraction still works.
- Packages are centrally managed (`Directory.Packages.props`); analyzers run at
  `latest-recommended` with `EnforceCodeStyleInBuild` (warnings are *not* errors).
- App XAML uses **source-generated XAML compilation** (`<MauiXamlInflator>SourceGen</…>`), so XAML
  binding/handler errors surface at build time.

## Architecture

### Core is the engine; App and CLI are thin shells

`src/AbioticEditor.Core` holds all parsing/editing. `App` (MAUI) and `Cli` are front-ends over it,
so the CLI writes byte-identical output to the app. New tooling should live in Core and be reused.

### Read → mutate-in-place → re-serialize (the central save contract)

For each save kind a **reader** parses the raw GVAS tree into a typed model whose `.Raw` property
**is** the same `SaveGame` instance; a **writer** mutates that raw tree in place and re-serializes
it byte-perfect for everything it didn't touch. Key files: `PlayerSaves/PlayerSaveReader` +
`PlayerSaveWriter`, `WorldSaves/WorldSaveReader` + `WorldSaveWriter`.

Two non-obvious rules govern every edit (the game **delta-serializes**: any property still at its
blueprint default is omitted from the file):
- **Readers match by prefix** (`FindByPrefix("Hunger_")`) because property names carry
  blueprint-compiler hash suffixes (`Hunger_2_A6C5CC6E…`) that can change across game patches.
- **Writers must create a missing tag using its exact full hash-suffixed name** (see the
  `FullNames` tables in the writers), because a prefix lookup legitimately fails on a healthy save,
  and a silent no-op would lose the edit.

Save kinds: `Player_<steamid64>.sav` (player), `WorldSave_<Region>.sav` (region), and
`WorldSave_MetaData.sav` (story/metadata); the Facility region save is the large one (~16 MB). A
player's SteamID lives in **both** the filename and the top-level `SaveIdentifier` property; keep
them in sync (`PlayerSaves/PlayerSaveIdentity`).

### Game data comes from the installed game's paks

Item/recipe/skill/flag/fish/trait catalogs are read from the game's pak archives via **CUE4Parse**
+ the bundled `assets/Mappings.usmap`. Core's `Assets/GameAssetProvider` mounts them; the App
wraps this in the process-wide singleton `Services/GameDataServices` (call `EnsureLoadedAsync()`
before reading `Catalog`, `AllFish`, `AllRecipeInfos`, etc.). **Everything degrades gracefully when
assets are absent**: catalogs return empty and icon resolution no-ops, so the editor still runs.
Item icons are extracted lazily off-thread (`provider.ExtractTextureByGameRef` → `IconColorizer`).

### App: MVVM with a shared, theme-survivable view-model

- `App.SharedViewModel` (a single `MainViewModel`) is the hub. A **theme switch rebuilds the whole
  page tree on the same shared VM**, so loaded saves and staged edits survive the swap (inline
  `StaticResource`/converter output only re-resolves on a fresh tree; styles recolor live via
  `DynamicResource`).
- The former giant `MainPage` is split into `Views/*` ContentViews + editor sub-VMs
  (`PlayerEditorViewModel`, `WorldEditorViewModel`). `ResponsivePaneController` owns all
  responsive/drawer/splitter behavior so the page stays declarative.
- **MAUI binding gotchas in this codebase:** `SlotSidebarView` uses explicit
  `Source={x:Reference Root}` on every binding because plain `BindingContext` propagation proved
  unreliable here. Controls inside a `DataTemplate` (e.g. a CollectionView group-header button)
  can't reach an outer element via `x:Reference` across the template namescope, so wire those through
  a code-behind `Clicked`/`Tapped` handler that reads `BindingContext`.
- **Modal sheets are built in code, not XAML** (`SettingsPage`, `ComparePage`, plugin pages) so
  they pick up the live palette each time they open. They share `Views/ModalChrome` for the
  "facility" look (branded header + hazard stripe + `Card`s + footer + `Segmented` toggle).
- Edits stage until **SAVE**; every write keeps a `.bak`. Quest-flag edits are gated by story
  prerequisites (the editor offers to set missing prereqs rather than create inconsistent state).

### Plugin system

`src/AbioticEditor.Plugins.Abstractions` is the host-agnostic SDK plugin authors compile against.
`Core/Plugins` is the hosting layer (collectible `AssemblyLoadContext`, manifest IO,
JavaScript runtime via **Jint**). `SaveOperationRunner` is the **single dangerous write path**
(load → kind-check → required-params → execute → backup+write only if the op marked a change) and
is deliberately kept out of plugins. Plugins run with full trust (no sandbox). Sample plugins live
under `plugins/` and build into standalone DLLs/`.js` (not part of the app).

## Conventions specific to this repo

- **`docs/PROGRESS.md` is the running session log**: read it first when resuming editor work; it
  records the feature inventory, what's been verified, and open follow-ups.
- Tests assert against **real save fixtures** under `tests/fixtures/` (located via an upward
  directory walk; tests skip gracefully when a fixture is absent). `tests/AbioticEditor.Probes` are
  research dumps of game structures, **not** run as part of normal tests.
- Why submodules (not NuGet): the editor needs current-`master` CUE4Parse behavior and byte-exact
  UeSaveGame serialization, and must be debuggable into both; pinned commits guarantee a
  tested combination. UeSaveGame isn't on NuGet at all.
- Plugin dev env vars: `ABIOTIC_PLUGINS_DIR` (point at a dev plugins folder),
  `ABIOTIC_NO_PLUGINS=1` (disable loading).

## Writing style

- **Do not use em dashes (the U+2014 `—` character) anywhere** in code, comments, XML/XAML, docs, commit messages, or
  generated output. Use a spaced hyphen (` - `), a comma, a colon, or parentheses, or split the
  sentence in two. This applies to all source and documentation in this repo (the vendored
  `submodules/` are external and excluded).
