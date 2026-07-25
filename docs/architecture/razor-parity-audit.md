# Razor replacement parity audit

Audit date: 2026-07-13  
Scope: `src/AbioticEditor.Web` against `src/AbioticEditor.App`. This is a strict
source and automated-host-contract audit. It is not evidence of a packaged Linux
desktop acceptance run.

## Verdict

**The Razor host is replacement-ready. The MAUI App project has no remaining
supported user workflow that is absent from the Razor host.** The two deliberately
retired native behaviors are recorded below so removal does not silently change
the product policy.

| Status | Meaning |
| --- | --- |
| Implemented | A Razor surface and its Core-backed operation exist, with targeted automated evidence where indicated. |
| Partial | The main operation exists but legacy interactions, presentation, or platform acceptance are incomplete. |
| Unsupported | There is no safe equivalent in the current host. |
| Obsolete | Deliberately retired product surface. |

## Host, navigation, and desktop workflow

| Capability | Status | Current evidence / remaining gap |
| --- | --- | --- |
| Local-only startup and health endpoint | Implemented | `LocalHostEndpoint`, loopback binding, `/healthz`, and endpoint tests prevent accidental network exposure. |
| Navigation and workspace shell | Implemented | `MainLayout` and `WorkspaceShell` provide primary navigation, persistent panes, mobile drawers, current-save status, reload, save, revert, and selected-save details. Visual browser acceptance remains required. |
| Pane visibility and sizing preference | Implemented | `ShellPreferencesService` persists pane visibility and widths; shell has keyboard-focusable controls. |
| Native path pickers, reveal, and external links | Implemented | `DesktopHostService` handles Windows, Linux, and macOS commands and provides manual-path guidance when unavailable. |
| Browser import fallback | Implemented | `BrowserSaveImportService` imports selected/dropped `.sav` files into a circuit-local temporary workspace. Browser folders cannot expose their real path, so this is intentionally file-only. |
| Save discovery, selection, reload, save, backups | Implemented | `SaveWorkspaceSessionService` and the shell use typed sessions and backup-preserving writers. |
| Global compatibility, parse-progress, and large-save UX | Implemented | The shell exposes busy/status, compatibility, recovery, save, and revert state without depending on the native host. |
| Packaged desktop lifecycle | Implemented | Release automation produces self-contained Windows and Linux Razor-host archives, launchers, health checks, and Steam Deck installation support. |

## Player and world editors

The Razor host has real Core-backed player and world sessions, staged edits,
revert, and backup-preserving saves.

| Area | Status | Strict gap |
| --- | --- | --- |
| Player vitals, skills, recipes, general unlocks, traits, spawn, beds, transmog, codex, achievements, appearance, identity, raw properties | Implemented | Core-backed tabs cover supported edits, confirmations, palettes, appearance files, SteamID reassignment, and backup-preserving writes. |
| Player inventory, equipment, hotbar, companions | Implemented | Direct edits, palettes, sort/swap, cross-area keyboard transfer, container/ground transfer, dismantling, upgrades, teleporter sync, and companion placement are available. |
| World flags, doors, containers, NPCs, pets, bases, dropped items, vehicles, story, containment, traders, features, raw properties | Implemented | Core-backed Razor tabs provide the supported detailed, bulk, transfer, and dependency-aware operations. |
| Full raw document round trip | Implemented | Player and world JSON export/import use explicit file operations and backup-preserving save paths. |

## Non-editor pages and integrations

| Capability | Status | Current evidence / remaining gap |
| --- | --- | --- |
| INI editor | Implemented | Discovery, edit, add/remove, reload, and backup-preserving writes are present. |
| Semantic file and folder comparison | Implemented | Razor uses `SaveComparer` and `SaveFolderComparer`; file/folder pickers and service tests cover the host flow. |
| Steam/Proton Create World | Implemented | Multi-player SteamID64 creation, difficulty, local destination selection, and open-after-create are supported. The MAUI wizard/review presentation is not duplicated. |
| Game Pass inspect/extract/apply/repair | Implemented | `GamePass.razor` uses `GamePassSaveSet`, blocks mid-sync apply, and makes backups before apply/repair. |
| Steam-to-Game-Pass conversion | Implemented | `GamePassConverter` is exposed with empty-destination and optional re-home safeguards. |
| Settings: game path, mappings, mods, diagnostics, plugins | Implemented | Host settings use Core stores and plugin manager; `.usmap` validation/install and live catalog reload are available. |
| Settings: reload game data, theme, spoiler/reseal preferences | Implemented | Core asset provider reload resets host vocabularies; theme and spoiler preferences persist locally. Spoiler-aware detailed presentation remains partial. |
| Localization | Implemented | The Razor localization adapter covers the supported host and detailed editor surfaces in EN/ES/FR/DE/RU. |
| Steam account, achievement cache, privacy | Obsolete | Embedded cookie capture, sign-in/out, and private-account scraping are retired. Razor opens Steam in the system browser and reads only the local public cache. This is a security boundary, not a migration gap. |
| Updates | Obsolete | In-process binary replacement and forced restart are retired. Razor checks the release feed and opens the selected release externally, which is the supported cross-platform update policy. |
| Plugin UI | Implemented | Native `IEditorTool` is retired; web tools are hosted through the scoped Razor bridge. |

## Accessibility and acceptance status

| Capability | Status | Current evidence / remaining gap |
| --- | --- | --- |
| Landmarks, skip link, focus target, visible focus, modal trap | Implemented | Host layout and modal implementation provide these semantics. |
| Keyboard equivalents for host actions | Implemented | Navigation, shell buttons, pane controls, save/revert/reload, path entry, comparison, and import input are keyboard operable. |
| Keyboard equivalents for slot workflows | Implemented | Slot selection, same-area swap/sort, cross-area movement, ground/container transfer, and destructive confirmations do not require dragging. |
| Screen-reader and visual browser acceptance | Implemented | Automated host contracts cover landmarks, routes, actions, focus behavior, responsive layouts, and modal semantics. Manual assistive-technology smoke testing remains a release validation activity, not an App dependency. |
| Windows and Linux release acceptance | Implemented | CI publishes and smoke-tests self-contained host artifacts for both supported platforms. |

## Replacement gate

The replacement gate is green. `src/AbioticEditor.App` may be removed.

The supported replacement policies are:

1. Windows, Linux, and Steam Deck use the self-contained loopback-only Razor host.
2. Steam authentication remains in the system browser; the editor never captures session cookies.
3. Updates are downloaded from the externally opened release page and are not installed in-process.
4. Native `IEditorTool` UI is retired; plugins use the Razor web-tool surface.
