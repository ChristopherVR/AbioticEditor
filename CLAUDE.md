# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A save-game editor for **Abiotic Factor** (a Unreal Engine GVAS game). It reads/writes save
files byte-for-byte and understands the game's own data tables by mounting the installed game's
pak archives. Ships a local Razor desktop host and a CLI, both thin front-ends over a shared Core.

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

### Commit trailers

**Never put a chat/session link in a commit message.** No `Claude-Session:` trailer, no
`https://claude.ai/code/...` URL, in the subject, the body or a trailer. These commits are
public and the changelog built from them is published to Nexus Mods, so a session link
would be shipped to players. A `Co-Authored-By:` line is fine.

## Build / test commands

Requires the **.NET 10 SDK**. Clone with `--recursive` (or `git submodule update --init`); the
build depends on the `submodules/` source projects.

```console
dotnet build src/AbioticEditor.Core                                       # core library
dotnet build src/AbioticEditor.Cli                                        # CLI (abioticeditor)
dotnet build src/AbioticEditor.Web                                       # local editor host
dotnet test  tests/AbioticEditor.Tests -f net10.0                         # all tests
dotnet test  tests/AbioticEditor.Tests -f net10.0 --filter "FullyQualifiedName~PlayerSaveFactoryTests"  # one class/test
```

- The local Razor host targets `net10.0` and builds with the standard SDK on Windows and Linux.
- **A running host instance can lock its `bin` DLLs.** To verify a change while it is open, build
  to a throwaway output dir: `dotnet build src/AbioticEditor.Web -o "$env:TEMP\verify"`.
- **Any build rewrites the scoped-CSS bundle the running host serves**, including a `-o` build to a
  throwaway dir: the per-component `*.razor.css` styles are bundled into a single generated
  `AbioticEditor.Web.styles.css` that lives under `obj/<config>/<tfm>/scopedcss/bundle/` and is
  regenerated in place. `Program.cs` serves it via `MapStaticAssets()` and `App.razor` references it
  through `@Assets[...]`, so the URL carries a content stamp and a rebuild no longer leaves an open
  window unable to fetch it. **Do not** revert either to `UseStaticFiles()` / a plain
  `href="AbioticEditor.Web.styles.css"`: that combination returned a 404 during the rebuild window
  and left the app rendering with `parity.css` alone, which looks like a catastrophic UI regression
  (collapsed grids, unsized item icons, overlapping labels) with no source change behind it. If a
  screen ever does look like that, check that URL before suspecting recent commits, and reload the
  window.
- The `CUE4Parse-Natives build failed … 'cmake' is not recognized` line during builds is **benign**:
  the native texture decoder is optional; managed parsing/extraction still works.
- Packages are centrally managed (`Directory.Packages.props`); analyzers run at
  `latest-recommended` with `EnforceCodeStyleInBuild` (warnings are *not* errors).

## Architecture

### Core is the engine; the Razor host and CLI are thin shells

`src/AbioticEditor.Core` holds all parsing/editing. `Web` (Razor) and `Cli` are front-ends over it,
so the CLI writes byte-identical output to the app. New tooling should live in Core and be reused.

Core is organized into layers, each split further into `Player`/`World`/etc. subfolders that
mirror each other:
- **`Domain/`** - plain data models (records) with no I/O: `WorldContainer`, `PlayerInventory`, etc.
- **`Catalogs/`** - curated, mostly-static game-knowledge lookup tables (door classes, quest flags,
  item/recipe/skill catalogs, codex/trader data).
- **`Serialization/`** - byte-exact GVAS read/write: the `*SaveReader`/`*SaveWriter` pairs and the
  `SaveClasses` GVAS class registrations. The three largest writer/reader classes are split into
  `ClassName.Concern.cs` partial-class files by editing area (e.g.
  `WorldSaveWriter.Containers.cs`, `WorldSaveWriter.NpcsAndPets.cs`) purely for navigability; they
  are still one class.
- **`Services/`** - higher-level editing operations built on the models (compare/diff, compatibility
  checks, story-flag sync, world-map features).
- **`Infrastructure/`** - the outside world: game install detection, Steam, Game Pass/wgs, `.ini`
  files, save discovery/backup, logging.
- **`Plugins/`** stays its own top-level folder (the plugin hosting layer is a distinct subsystem,
  not part of the layered engine above).

**Namespaces were deliberately left unchanged by this layering** (`AbioticEditor.Core.PlayerSaves`,
`.WorldSaves`, `.Items`, etc., regardless of which folder a type now lives in) because they are
part of the published `AbioticEditor.Core` NuGet package's public API and are what the test suite's
`using` directives target - moving files must never be a breaking change for either.

### Read → mutate-in-place → re-serialize (the central save contract)

For each save kind a **reader** parses the raw GVAS tree into a typed model whose `.Raw` property
**is** the same `SaveGame` instance; a **writer** mutates that raw tree in place and re-serializes
it byte-perfect for everything it didn't touch. Key files: `Serialization/Player/PlayerSaveReader`
+ `PlayerSaveWriter`, `Serialization/World/WorldSaveReader` + `WorldSaveWriter`.

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
them in sync (`Services/Player/PlayerSaveIdentity`).

### Game data comes from the installed game's paks

Item/recipe/skill/flag/fish/trait catalogs are read from the game's pak archives via **CUE4Parse**
+ the bundled `assets/Mappings.usmap`. Core's `Infrastructure/GameAssets/GameAssetProvider` mounts
them, and the host exposes the resulting catalogs through singleton services. **Everything degrades gracefully when
assets are absent**: catalogs return empty and icon resolution no-ops, so the editor still runs.
Item icons are extracted lazily off-thread (`provider.ExtractTextureByGameRef` → `IconColorizer`).

### Razor host: staged sessions and local-only desktop delivery

- `src/AbioticEditor.Web` is an interactive Razor server restricted to an HTTP loopback endpoint.
  Do not loosen `LocalHostEndpoint`: the host can read and write user-selected files.
- `PlayerSaveSession` and `WorldSaveSession` keep loaded Core models and staged editor state alive
  across component renders. Razor components call the same Core writers used by the CLI.
- The self-contained executable opens a Photino native window on Windows and Linux. Linux uses
  `launch-linux.sh` and an optional per-user desktop entry. CI uses headless mode for `/healthz`.
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
