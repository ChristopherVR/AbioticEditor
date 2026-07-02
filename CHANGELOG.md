# Changelog

All notable changes to this project are documented here.

## [1.20.2] - 2026-07-02

### Build
- Bump actions/cache from 5 to 6 in the actions-all group


### Miscellaneous Tasks
- Point bucket at v1.20.1 [skip ci]


## [1.20.1] - 2026-07-02

### Miscellaneous Tasks
- Point bucket at v1.20.0 [skip ci]


## [1.20.0] - 2026-07-01

### Bug Fixes
- Clear old codex spoilers and offer to move players back on a story rewind
- Rewinding the story past the Reactors now actually rewinds it


### Features
- Add a Load More button to the item catalog


### Miscellaneous Tasks
- Point bucket at v1.19.0 [skip ci]


## [1.19.0] - 2026-06-27

### Bug Fixes
- Show friendly state labels and a clearer Game Pass save warning
- Stamp the save index like the game does so edits sync
- Mark added items as discovered so the game recognises them


### Features
- Full wiki-verified quest dependency tree for the main story
- Extend the quest dependency tree across Office, Manufacturing and Labs
- Follow per-quest dependencies so steps aren't left half-done
- Snapshot/compare tool to prove whether a real sync kept edits
- Repair a save stuck pointing at a missing data file


### Miscellaneous Tasks
- Point bucket at v1.18.0 [skip ci]


### Testing
- Cover the sync-recency behaviour and make edits strictly newer


## [1.18.0] - 2026-06-27

### Bug Fixes
- Warn before editing a save that hasn't finished syncing
- Say when Xbox sync has dropped a world from the index
- Stop Warren reading "classified" once you're past him
- Stop Game Pass worlds opening empty
- Show the item list when editing a base's containers
- Update trader status the moment you change a story flag
- Show your character's looks on Game Pass saves
- Hide the Achievements tab on Game Pass saves


### Features
- Send a container item straight to a player
- Keep contained creature names hidden until you reveal them


### Miscellaneous Tasks
- Point bucket at v1.17.3 [skip ci]


## [1.17.3] - 2026-06-25

### Bug Fixes
- Recover gracefully when a save blob is missing from disk
- Let Game Pass players edit their character's look


### Features
- Warn about Xbox cloud sync before editing a save


### Miscellaneous Tasks
- Point bucket at v1.17.2 [skip ci]


## [1.17.2] - 2026-06-22

### Bug Fixes
- Show each skill's real level instead of a mislabeled one


### Miscellaneous Tasks
- Point bucket at v1.17.1 [skip ci]


### Testing
- Update placeholder-padding test for the corrected skill order


## [1.17.1] - 2026-06-21

### Bug Fixes
- Add SpoilerGateFlag to all traders whose existence is story-gated


### Documentation
- Write commit messages for Nexus Mods players, not developers
- Split Pages into two first-class tracks (Guide vs Reference)


### Miscellaneous Tasks
- Point bucket at v1.17.0 [skip ci]


## [1.17.0] - 2026-06-21

### Bug Fixes
- Conceal Jimmy and Blacksmith until their story gate flag is set
- Conceal Jimmy and Blacksmith until their story gate flag is set
- Write correct Field1 (TotalRaw) in bundle serialization
- Force single-quantum Oodle compression for Game Pass bundles
- Compress bundle payload as single Oodle quantum
- Compress in 512 KB quanta to match the game's chunked Oodle reader
- Also skip timestamped .bak-<stamp> backup folders in discovery


### Documentation
- Restructure Pages - exclude research notes, add new guide pages


### Features
- Added assets


### Miscellaneous Tasks
- Point bucket at v1.16.1 [skip ci]


## [1.16.1] - 2026-06-21

### Build
- Bump the actions-all group with 3 updates


### Miscellaneous Tasks
- Point bucket at v1.16.0 [skip ci]


## [1.16.0] - 2026-06-21

### Bug Fixes
- Bak-folder discovery, temp cleanup, home page OPEN button + remove, generation increment
- Surface bundle-load errors instead of showing empty sidebar
- Correct Game Pass session UX (folder display, reveal, reload, save indicator)


### Features
- Strip auto-updater from Nexus Mods distribution build
- Platform badge colors + game-data loading indicator


### Miscellaneous Tasks
- Point bucket at v1.15.0 [skip ci]


## [1.15.0] - 2026-06-20

### Features
- Settings polish - inline compare tab, plugin clarity, language fix


### Miscellaneous Tasks
- Point bucket at v1.14.5 [skip ci]


## [1.14.5] - 2026-06-20

### Features
- Inline plugins into settings tab, centre tab content


### Miscellaneous Tasks
- Point bucket at v1.14.4 [skip ci]


## [1.14.4] - 2026-06-20

### Bug Fixes
- Refresh world discovery after creating a new world
- Stop CLI build matrix legs from cancelling each other


### Features
- Vertical settings tabs, compare rework, modal dialog fixes


## [1.14.3] - 2026-06-20

### CI
- Add manual force-release trigger (workflow_dispatch)


## [1.14.2] - 2026-06-20

### CI
- Only publish a NuGet package when its sources changed


## [1.14.1] - 2026-06-19

### Bug Fixes
- Resolve wgs folder from any nearby level, log discovery verdicts


## [1.14.0] - 2026-06-19

### Documentation
- Record the UserEntitlements coverage gap and round-38 progress


### Features
- Per-mod enable/disable in Settings
- Craft minimal region saves for unvisited regions


## [1.13.0] - 2026-06-19

### Build
- Silence vendored submodule warnings (CUE4Parse/UeSaveGame)


### Documentation
- Give the Nexus mod page the same flair as the docs site


### Features
- Support Abiotic Factor mods (mount mod paks + discover mod data tables)
- Offline fallback bundle for wiki images


### Miscellaneous Tasks
- Log save-switch breadcrumbs and world-editor dirty reasons


### Refactor
- Route remaining string.Format sites through the Format helper


### Testing
- Point fixture locators at the platform-grouped layout


## [1.12.0] - 2026-06-19

### Bug Fixes
- Incorrect data registry test analysis isuse
- Platform-aware default folder, native alerts, build-clean localized formatting
- Keep one changelog bullet per line
- Route wgs folders from every open path; fix empty-sidebar overlap
- Open wgs folders directly, lock the id for non-Steam, clearer convert UI
- Validate extracted member paths stay in the working dir


### Documentation
- Log the non-Steam + Game Pass round in PROGRESS.md


### Features
- Added registry catalog fallback if no game is found
- Added additional localization
- Auto-detect Game Pass install + saves; show locations; docs
- Platform choice, account dropdown, open MetaData
- SAVE writes straight to the container; drop the banner; add save-type badge
- Convert saves Steam <-> Game Pass, and create for both
- Platform tags + open Game Pass worlds in the app
- Read+write Game Pass / Xbox container saves


### Testing
- Add a sanitized real Game Pass container fixture


## [1.11.1] - 2026-06-18

### Bug Fixes
- Make the game-data banner action match the failure
- Extend keypad upgrade chain to the Tier 6 Gatekey
- Pin the Settings tab strip full-width and move diagnostics to General


### Features
- Support non-Steam saves (Game Pass / Epic) via opaque player ids


## [1.11.0] - 2026-06-18

### Bug Fixes
- Repair items left on the empty-slot table, target each item's real table
- Pin the header version tag to the build's release version
- Repair mojibake in localized UI strings
- Point an added item's row handle at ItemTable_Global so it renders


### Features
- Fall back to built-in trader data and flag missing game data
- Tabbed Settings, clearer Game Data section, drop About
- Let users set the game folder when auto-detection fails
- Translate UI to de/es/fr, add localization tests and docs


## [1.10.0] - 2026-06-18

### CI
- Make release push rebase-safe and cancel pre-publish runs on new push
- Cache NuGet packages and the MAUI workload to speed up the release pipeline


### Features
- Add JavaScriptPlugin based capability for localization
- Localize the UI and let plugins contribute translations
- Log previously-unlogged mutating user actions


### Miscellaneous Tasks
- Point bucket at v1.9.0 [skip ci]


## [1.9.0] - 2026-06-18

### Bug Fixes
- Assign and persist per-instance AssetID for added inventory items


### CI
- Depend nexus on build-app-win only, not publish
- Scan release zips with VirusTotal and publish to NexusMods


### Features
- Show update download progress with cancel; stop auto-opening a world on startup
- Make diagnostic logging opt-in, but always log critical errors
- Add RELOAD-from-disk with unsaved-changes confirm


### Miscellaneous Tasks
- Point bucket at v1.8.1 [skip ci]


## [1.8.1] - 2026-06-18

### Miscellaneous Tasks
- Point bucket at v1.8.0 [skip ci]


## [1.8.0] - 2026-06-18

### Features
- Added nexus mod deployment
- Added virus scanning to the release packages


### Miscellaneous Tasks
- Point bucket at v1.7.1 [skip ci]


## [1.7.1] - 2026-06-17

### Bug Fixes
- DOWNLOAD & INSTALL now works from the Settings modal
- Config discovery no longer leaks sibling-world sandbox settings


### Miscellaneous Tasks
- Point bucket at v1.7.0 [skip ci]


## [1.7.0] - 2026-06-17

### Features
- Add Create New World wizard for starting fresh save games
- Auto-discover all ItemTable_* files for DLC resilience


### Miscellaneous Tasks
- Point bucket at v1.6.0 [skip ci]


## [1.6.0] - 2026-06-16

### Bug Fixes
- Pet placement respects Main slot kind, not just companion/hotbar
- INI switch leaves stale entries; enable + surface diagnostic logging
- INI editor was blank - drop the broken Source=Root bindings
- Send-pet-to-player falls back between companion slot and hotbar
- Wrap the player editor tab bar instead of horizontal scroll


### Features
- Grant future/unknown server entitlements via a free-text add field
- Server entitlements as per-grant toggles with player names


### Miscellaneous Tasks
- Point bucket at v1.5.0 [skip ci]


## [1.5.0] - 2026-06-15

### Bug Fixes
- Robust cross-world power-socket device resolution + diagnostics


### Features
- Friendly resource-node names, search filter, location per row


### Miscellaneous Tasks
- Point bucket at v1.4.0 [skip ci]


## [1.4.0] - 2026-06-15

### Bug Fixes
- Resolve teleporter sync name; clarify tram station picker
- Pet-to-bed picker, drop duplicate Vehicles tab, door/elevator clarity


### Features
- Show friendly names for cross-world power-socket devices
- True cross-world navigation to a power socket's plugged-in device
- Identify and navigate to a power socket's plugged-in device


### Miscellaneous Tasks
- Point bucket at v1.3.0 [skip ci]


## [1.3.0] - 2026-06-15

### Bug Fixes
- INI file switching, appearance guidance, and richer edit logging


### Features
- Editable crafting-bench upgrades in the Bases tab
- Editable trams, per-feature area + remove labels, vehicle/pet fixes, drop NPCs tab
- Safer, exportable, richer save comparison; clearer doors; settings language row
- Shared area-name catalog, soft-path setter, bench-upgrade tags


### Miscellaneous Tasks
- Point bucket at v1.2.1 [skip ci]


## [1.2.1] - 2026-06-15

### CI
- Disable macOS app builds


### Miscellaneous Tasks
- Point bucket at v1.2.0 [skip ci]


## [1.2.0] - 2026-06-15

### Features
- Version-stamped zips and a self-contained single-file Windows app


### Miscellaneous Tasks
- Point bucket at v1.1.3 [skip ci]


## [1.1.3] - 2026-06-14

### Bug Fixes
- Stop dialog-host theme leak, dead-click reselect, stacked leave-gates
- Verify download size, block asset-name traversal, fix prerelease order
- Close save-write corruption, pet-XP loss, and icon-cache races


### Miscellaneous Tasks
- Point bucket at v1.1.2 [skip ci]


## [1.1.2] - 2026-06-14

### Documentation
- Note trunk-based development (commit to main, no branches)


## [1.1.1] - 2026-06-14

### Bug Fixes
- Re-publish orphaned tags so a release can't get stranded


## [1.1.0] - 2026-06-14

### Bug Fixes
- Use the real Teleporter Pad image; show nothing when no image exists
- Only show a feature image when the wiki really pictures it
- Keep pets in the hotbar/Companion slot, never the backpack
- Keep the right sidebar to a single detail context
- Wrap the editor tab bar so every tab stays visible


### CI
- Don't let the Mac Catalyst build block the release


### Features
- Name, picture, link and remove world-state map entries
- Show pet portrait; fix vehicle open-container jump
- Move world-state map editing into world-editor tabs
- Correct containment/vehicle art and group vehicles by world


## [1.0.1] - 2026-06-14

### Miscellaneous Tasks
- Drop master branch alias, use main only
- Relicense MIT -> Apache-2.0 and add NOTICE


## [1.0.0] - 2026-06-14

### Bug Fixes
- Disable the optional CUE4Parse-Natives CMake build
- Set git-cliff initial_tag so the first release computes v1.0.0
- Supply Linux Skia native and realign SkiaSharp to CUE4Parse's pin
- Resolved issues with github page styling and some wording


### Build
- Extract CUE4Parse-mirrored package versions into a submodule-adjacent file
- Realign CUE4Parse-mirrored deps to submodule pins, pin Dependabot off them
- Treat warnings as errors and clear first-party warnings
- Bump the actions-all group with 9 updates


### CI
- Gate releases on the test suite passing


### Documentation
- Open content images in a lightbox on click
- Document the plugin system (folder READMEs, site pages, wiki)
- Flesh out README and docs site for newcomers, add screenshots


### Features
- First-class pet & vehicle systems + cross-save pet movement
- Publish Core + Plugins.Abstractions to NuGet on release
- Add VitePress docs site, release CI, and Dependabot


### Miscellaneous Tasks
- Gitignore transient .playwright-mcp/ snapshot output


### Styling
- Remove em dashes across source, docs, and config


### Testing
- Add reader/writer reversibility + isolation validation tests



