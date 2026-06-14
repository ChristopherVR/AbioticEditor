# Changelog

All notable changes to this project are documented here.

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


