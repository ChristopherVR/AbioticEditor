# Changelog

All notable changes to this project are documented here.

## [1.8.1] - 2026-06-18

### Miscellaneous Tasks
- Point bucket at v1.8.0 [skip ci]

## [1.8.0] - 2026-06-18

### Features
- Added nexus mod deployment- Added virus scanning to the release packages

### Miscellaneous Tasks
- Point bucket at v1.7.1 [skip ci]

## [1.7.1] - 2026-06-17

### Bug Fixes
- DOWNLOAD & INSTALL now works from the Settings modal- Config discovery no longer leaks sibling-world sandbox settings

### Miscellaneous Tasks
- Point bucket at v1.7.0 [skip ci]

## [1.7.0] - 2026-06-17

### Features
- Add Create New World wizard for starting fresh save games- Auto-discover all ItemTable_* files for DLC resilience

### Miscellaneous Tasks
- Point bucket at v1.6.0 [skip ci]

## [1.6.0] - 2026-06-16

### Bug Fixes
- Pet placement respects Main slot kind, not just companion/hotbar- INI switch leaves stale entries; enable + surface diagnostic logging- INI editor was blank - drop the broken Source=Root bindings- Send-pet-to-player falls back between companion slot and hotbar- Wrap the player editor tab bar instead of horizontal scroll

### Features
- Grant future/unknown server entitlements via a free-text add field- Server entitlements as per-grant toggles with player names

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
- Resolve teleporter sync name; clarify tram station picker- Pet-to-bed picker, drop duplicate Vehicles tab, door/elevator clarity

### Features
- Show friendly names for cross-world power-socket devices- True cross-world navigation to a power socket's plugged-in device- Identify and navigate to a power socket's plugged-in device

### Miscellaneous Tasks
- Point bucket at v1.3.0 [skip ci]

## [1.3.0] - 2026-06-15

### Bug Fixes
- INI file switching, appearance guidance, and richer edit logging

### Features
- Editable crafting-bench upgrades in the Bases tab- Editable trams, per-feature area + remove labels, vehicle/pet fixes, drop NPCs tab- Safer, exportable, richer save comparison; clearer doors; settings language row- Shared area-name catalog, soft-path setter, bench-upgrade tags

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
- Stop dialog-host theme leak, dead-click reselect, stacked leave-gates- Verify download size, block asset-name traversal, fix prerelease order- Close save-write corruption, pet-XP loss, and icon-cache races

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
- Use the real Teleporter Pad image; show nothing when no image exists- Only show a feature image when the wiki really pictures it- Keep pets in the hotbar/Companion slot, never the backpack- Keep the right sidebar to a single detail context- Wrap the editor tab bar so every tab stays visible

### CI
- Don't let the Mac Catalyst build block the release

### Features
- Name, picture, link and remove world-state map entries- Show pet portrait; fix vehicle open-container jump- Move world-state map editing into world-editor tabs- Correct containment/vehicle art and group vehicles by world

## [1.0.1] - 2026-06-14

### Miscellaneous Tasks
- Drop master branch alias, use main only- Relicense MIT -> Apache-2.0 and add NOTICE

## [1.0.0] - 2026-06-14

### Bug Fixes
- Disable the optional CUE4Parse-Natives CMake build- Set git-cliff initial_tag so the first release computes v1.0.0- Supply Linux Skia native and realign SkiaSharp to CUE4Parse's pin- Resolved issues with github page styling and some wording

### Build
- Extract CUE4Parse-mirrored package versions into a submodule-adjacent file- Realign CUE4Parse-mirrored deps to submodule pins, pin Dependabot off them- Treat warnings as errors and clear first-party warnings- Bump the actions-all group with 9 updates

### CI
- Gate releases on the test suite passing

### Documentation
- Open content images in a lightbox on click- Document the plugin system (folder READMEs, site pages, wiki)- Flesh out README and docs site for newcomers, add screenshots

### Features
- First-class pet & vehicle systems + cross-save pet movement- Publish Core + Plugins.Abstractions to NuGet on release- Add VitePress docs site, release CI, and Dependabot

### Miscellaneous Tasks
- Gitignore transient .playwright-mcp/ snapshot output

### Styling
- Remove em dashes across source, docs, and config

### Testing
- Add reader/writer reversibility + isolation validation tests


