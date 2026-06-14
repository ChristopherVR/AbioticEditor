# Changelog

All notable changes to this project are documented here.

## [1.0.0] - 2026-06-14

### Bug Fixes
- Set git-cliff initial_tag so the first release computes v1.0.0- Supply Linux Skia native and realign SkiaSharp to CUE4Parse's pin- Resolved issues with github page styling and some wording

### Build
- Extract CUE4Parse-mirrored package versions into a submodule-adjacent file- Realign CUE4Parse-mirrored deps to submodule pins, pin Dependabot off them- Treat warnings as errors and clear first-party warnings- Bump the actions-all group with 9 updates

### CI
- Gate releases on the test suite passing

### Documentation
- Open content images in a lightbox on click- Document the plugin system (folder READMEs, site pages, wiki)- Flesh out README and docs site for newcomers, add screenshots

### Features
- Publish Core + Plugins.Abstractions to NuGet on release- Add VitePress docs site, release CI, and Dependabot

### Miscellaneous Tasks
- Gitignore transient .playwright-mcp/ snapshot output

### Styling
- Remove em dashes across source, docs, and config

### Testing
- Add reader/writer reversibility + isolation validation tests


