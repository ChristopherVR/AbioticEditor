# Changelog

All notable changes to this project are documented here.

## [2.8.0] - 2026-09-06

### Bug Fixes
- The live TELEPORT button now moves you the way the game itself does
- Live teleport and vehicle moves use positions the game script understands
- Live vehicle and pet lists no longer time out, and live teleport works
- Bring back the manual refresh button on the live flags and story screens


### Documentation
- Record what became editable live this round and what is still file-only
- Tidy a leftover comment about the story chapter being read-only live
- Record that the live teleport and vehicle move were checked in a real game
- Be upfront that one journal read is unverified against the real game


### Features
- Give the mode picker its own popup, and rework how you connect for live editing
- Pets, wrecked vehicles, bench upgrades, dropped items and NPCs can now be changed live
- More live editing for pets, vehicles, benches and story characters
- Hide or show your gear live, and set your background while playing
- Compendium entries can now be unlocked live while you play
- You can now jump the story chapter forward or back while hosting live
- Your recipes, journal and account bulk-unlocks now work while you play
- Your spawn point and carried pets can now be edited live while you play
- Containment units and world teleporters can now be edited live while you play
- Live editing now covers bases, vehicles and (partly) tamed pets
- Quest flags, main story and the world clock now use the same screens live and offline
- Live containers and dropped items now use the same screens as offline editing
- Live editing now shares the real inventory and transmog screens


### Miscellaneous Tasks
- Hold this push back from an automatic release [skip release]
- Hold this push back from an automatic release [skip release]
- Hold this push back from an automatic release [skip release]
- Point bucket at v2.7.6 [skip ci]


### Refactor
- Live door editing now uses the same doors screen you already know
- The live-editing companion can grow new areas without a rebuild


### Testing
- Catch more live-editing bugs before they ever reach the game
- The in-game script can now be checked without launching the game
- Repair the live channel tests after merging several live-editing branches
- The shared-session contract check no longer depends on interface order


## [2.7.6] - 2026-09-06

### Bug Fixes
- Items the game placed in a slot keep the item table the game chose
- Saving a character no longer leaves them exhausted or quietly rewrites their empty slots
- The live-editing companion now actually finds you in game
- Rebuild the live-editing companion on a real working mod's code
- Prevent the live-editing companion from freezing the game
- Strengthen the live-agent's connection secret to real randomness


### Build
- Bump taiki-e/install-action in the actions-all group


### Documentation
- Log that the live-editing screens were tested for real, not just the pipe
- Log the game-file library update and scope upcoming update support


### Features
- The world clock, weather, quest flags, doors, containers and dropped items can now be edited while you play
- Your backpack, gear and hotbar can now be edited live while you play
- NPCs near you can now be killed, revived, disabled or made invincible while you're playing
- Live editing's side panel now shows every player and world save
- Live editing now shows who's actually connected, and looks right while you're using it
- Live editing connects to your own game automatically
- Unblock the live-editing companion with a new setup
- Extend live editing to character skills
- Lay the groundwork for editing a running game in real time


### Miscellaneous Tasks
- Update the bundled game-file reading library to its latest version
- Point bucket at v2.7.5 [skip ci]


### Testing
- Research probes build again after the game-file library update, plus a live-editing class-layout probe


## [2.7.5] - 2026-08-26

### Bug Fixes
- Story-event search now finds events that have not happened yet


### Miscellaneous Tasks
- Point bucket at v2.7.4 [skip ci]


### Testing
- Add coverage that saving traits/skills never corrupts other data


## [2.7.4] - 2026-08-26

### Build
- Bump taiki-e/install-action in the actions-all group


### Miscellaneous Tasks
- Point bucket at v2.7.3 [skip ci]


## [2.7.3] - 2026-08-19

### Miscellaneous Tasks
- Point bucket at v2.7.2 [skip ci]


## [2.7.2] - 2026-08-19

### Bug Fixes
- Compare logic
- Side panels now slide over the screen on small windows
- The editor no longer falls apart on narrow windows and phones


### Build
- Bump taiki-e/install-action in the actions-all group


### Features
- Block Game Pass saves in the browser version


### Miscellaneous Tasks
- Point bucket at v2.7.1 [skip ci]


## [2.7.1] - 2026-08-14

### Miscellaneous Tasks
- Point bucket at v2.7.0 [skip ci]


## [2.7.0] - 2026-08-14

### Bug Fixes
- Stop Game Pass to Steam conversions writing inside the Xbox package folder
- Stop Convert from quietly giving up your character
- Make converting a save simpler and fix a couple of Convert bugs


### Documentation
- Correct the Convert screen's description of where a copy is written


### Features
- Let every player in a shared world keep their own character when converting
- Walk you through converting a Game Pass save step by step


### Miscellaneous Tasks
- Point bucket at v2.6.2 [skip ci]


### Styling
- Put Back and Start over side by side on the Convert screen


## [2.6.2] - 2026-08-13

### Bug Fixes
- A save Xbox left marked as disputed can be unstuck


### Miscellaneous Tasks
- Point bucket at v2.6.1 [skip ci]


## [2.6.1] - 2026-08-13

### Bug Fixes
- Say a Game Pass save cannot be saved yet when you open it, not when you try


### Miscellaneous Tasks
- Point bucket at v2.6.0 [skip ci]


## [2.6.0] - 2026-08-13

### Features
- Make converting a save between Steam and Game Pass easier to get right


### Miscellaneous Tasks
- Point bucket at v2.5.2 [skip ci]


## [2.5.2] - 2026-08-13

### Bug Fixes
- Buttons that could be pressed twice, and an export that was on the wrong platform


### Miscellaneous Tasks
- Point bucket at v2.5.1 [skip ci]


## [2.5.1] - 2026-08-13

### Bug Fixes
- Stop nagging about Game Pass saves that are perfectly fine
- Move a shared world to Steam, and stop a skipped test failing the build
- Your beds come with you when a character changes account


### Miscellaneous Tasks
- Point bucket at v2.5.0 [skip ci]


### Testing
- Stop two test groups fighting over the log settings


## [2.5.0] - 2026-08-13

### Bug Fixes
- Converted Game Pass worlds are no longer rejected as incompatible
- Pop-up messages no longer linger while the app is busy
- The browser editor would not start
- Use the spare copy of a save's data before guessing at one
- Use the sync status values Xbox actually understands
- Tell Xbox the truth about an edited Game Pass save
- Write Game Pass sync stamps the way the game writes them
- Stop Game Pass edits from going missing


### Documentation
- Explain the new ways to get a lost Game Pass world back
- Rewrite the Game Pass guidance for players
- Explain the offline routine for editing Game Pass saves


### Features
- Bring the Game Pass safety net into the app
- Stop risky Game Pass saves before they happen, and rescue broken ones
- Re-home a packed Game Pass character from the command line
- Warn about Xbox cloud sync before editing a Game Pass save


### Miscellaneous Tasks
- Point bucket at v2.4.0 [skip ci]


### Testing
- Cover the Game Pass paths that touch real Xbox saves


## [2.4.0] - 2026-08-09

### Bug Fixes
- The item pictures really are renamed this time
- Worlds you opened from a zip are offered again next time
- Teleporter sync had no benches to pick from
- The editor no longer freezes while opening a zip
- Tidy up the editor's chrome and a few misleading notes
- Choosing a language in the browser editor
- Empty bed and area pickers on the spawn screen in the browser
- Importing raw JSON in the browser editor
- Sending a pet to any area other than the main facility
- Missing item pictures in the browser editor


### Build
- Bump taiki-e/install-action in the actions-all group


### Documentation
- Make the browser editor easy to find


### Features
- A world opened from a zip is kept, edits and all
- A warning before unsaved changes are thrown away
- Open a zip of saves, and pick up where you left off


### Miscellaneous Tasks
- Point bucket at v2.3.1 [skip ci]


### Performance
- The editor only reads your world the slow way once
- Opening a world tab no longer copies the whole world first
- The editor stops freezing when it reads your world
- Clicking an item in your inventory is about five times faster


## [2.3.1] - 2026-08-08

### Bug Fixes
- The browser-editor link no longer 404s the first time you click it
- Sending a pet to another world now works in the browser [skip release]
- The home and new-world links no longer throw you out of the browser editor [skip release]


### CI
- Look for the editor's styling where it now lives [skip release]
- Allow a push to skip cutting a release [skip release]


### Documentation
- Record what the live browser editor actually downloads [skip release]


### Miscellaneous Tasks
- Point bucket at v2.3.0 [skip ci]


## [2.3.0] - 2026-08-05

### Bug Fixes
- Picking a file in the browser works again
- The settings editor no longer looks broken in a browser
- Making a new world no longer opens a dead page in the browser
- The world day and time of day can be saved again
- Item names, recipes and pictures now actually load in the browser
- The browser editor can open saves again, and takes dropped folders
- Linux/Steam Deck download now runs even if the "allow execute" flag gets lost


### Documentation
- Point people at the browser editor from the front page
- Record why the browser download cannot be trimmed the easy way
- Record what is still unfinished in the browser build
- Record the bundled icons and the browser-only failure modes
- Record the browser host switching to the shared screens
- Record the browser file system and shared asset move
- Record the save file-access seam
- Record how the shared screens are laid out


### Features
- Get single saves out of the browser, and fix raw JSON export
- Edit your character's look in the browser, and one less wasted request
- Item names and story text now show in your own language
- Firefox and Safari can open saves now
- Export a whole world as a zip, and tell Firefox users the truth
- The browser editor now shows recipes, traders, the codex and its pictures
- Real item pictures in the browser editor
- The browser editor now shows real item and recipe names
- The browser editor is now the same editor, not a cut-down one
- Let the browser version open your real save folder
- The browser editor now covers skills, traits, inventory and progress
- Add a browser version of the editor, no download required
- Add a true one-click launcher for Linux and Steam Deck downloads
- Add an advanced option to skip equipment/transmog slot checks


### Miscellaneous Tasks
- Point bucket at v2.2.2 [skip ci]


### Performance
- The browser editor now downloads about half as much


### Refactor
- Move the editor's look and feel where both versions can reach it
- Let the editor read and write saves from somewhere other than a disk
- Put the editor's screens in one place both versions can use


## [2.2.2] - 2026-08-05

### Build
- Bump the actions-all group with 2 updates


### Miscellaneous Tasks
- Point bucket at v2.2.1 [skip ci]


## [2.2.1] - 2026-07-26

### Bug Fixes
- Build the Windows version again


## [2.2.0] - 2026-07-26

### Bug Fixes
- The editor now keeps a record when something goes wrong


### Features
- Change a player's account id from any save, and see pets before you save


### Miscellaneous Tasks
- Point bucket at v2.1.6 [skip ci]


## [2.1.6] - 2026-07-26

### Bug Fixes
- No console flash, a proper window icon, and a tidier download


### Miscellaneous Tasks
- Point bucket at v2.1.5 [skip ci]


## [2.1.5] - 2026-07-26

### Bug Fixes
- The Linux download can now reach Nexus Mods
- The recipe book no longer names traders you have not met


### Miscellaneous Tasks
- Point bucket at v2.1.4 [skip ci]


## [2.1.4] - 2026-07-26

### Bug Fixes
- The Linux download can now be published to Nexus Mods


### Miscellaneous Tasks
- Point bucket at v2.1.3 [skip ci]


## [2.1.3] - 2026-07-25

### Bug Fixes
- The Linux release build no longer trips over its own health check


## [2.1.2] - 2026-07-25

### Bug Fixes
- The Nexus Mods download no longer contains any update checking


### Miscellaneous Tasks
- Point bucket at v2.1.1 [skip ci]


## [2.1.1] - 2026-07-25

### Build
- Bump the actions-all group with 2 updates


### Miscellaneous Tasks
- Point bucket at v2.1.0 [skip ci]


## [2.1.0] - 2026-07-25

### Bug Fixes
- Saving no longer fails on a world that has never unlocked anything


### Features
- Ship the editor as one executable, with no server console


### Miscellaneous Tasks
- Point bucket at v2.0.0 [skip ci]


## [2.0.0] - 2026-07-25

### Bug Fixes
- Say why Game Pass saves cannot be converted on Linux or macOS


### Build
- Only offer the Windows build on Windows
- Bump postcss from 8.5.15 to 8.5.23 in /docs
- Bump postcss


### Features
- Version 2, with Linux and macOS support


### Miscellaneous Tasks
- Point bucket at v1.23.6 [skip ci]


## [1.23.6] - 2026-07-22

### Bug Fixes
- Keep the boosted max durability showing when you reselect an item


### Miscellaneous Tasks
- Point bucket at v1.23.5 [skip ci]


## [1.23.5] - 2026-07-19

### Build
- Bump the actions-all group with 3 updates


### Miscellaneous Tasks
- Point bucket at v1.23.4 [skip ci]


## [1.23.4] - 2026-07-17

### Bug Fixes
- Translate the last remaining interface labels
- Translate the rest of the editor into German, Spanish, French and Russian


### Miscellaneous Tasks
- Point bucket at v1.23.3 [skip ci]


## [1.23.3] - 2026-07-17

### Bug Fixes
- Detect the anniversary update companions (Speedogi, Sir Ogi, Verdant Skink)


### Miscellaneous Tasks
- Point bucket at v1.23.2 [skip ci]


## [1.23.2] - 2026-07-15

### Miscellaneous Tasks
- Point bucket at v1.23.1 [skip ci]


## [1.23.1] - 2026-07-12

### Miscellaneous Tasks
- Point bucket at v1.23.0 [skip ci]


## [1.23.0] - 2026-07-11

### CI
- Stop publishing the Linux CLI to Nexus Mods
- Ship a Linux / Steam Deck build to Nexus Mods


### Features
- Translate skills, traits, equipment slots, and world NPC labels
- The editor now works on Linux and Steam Deck
- Translate door names, lock explanations, and save-discovery badges


### Miscellaneous Tasks
- Point bucket at v1.22.0 [skip ci]


### Refactor
- Share the tag-editing helpers between the player and world save writers
- Break the three biggest save read/write files into focused parts
- Give every data model, catalog, and helper its own file
- Organize the engine into clear layers


## [1.22.0] - 2026-07-10

### Bug Fixes
- Big Hive Larva's unlock condition now actually triggers, trader list scrolls properly


### Features
- Add Russian, fix several language bugs, and translate trader/story text that always stayed in English


### Miscellaneous Tasks
- Point bucket at v1.21.2 [skip ci]


## [1.21.2] - 2026-07-10

### Bug Fixes
- Rewinding past a region now clears its flags even if you reached it early


### Miscellaneous Tasks
- Point bucket at v1.21.1 [skip ci]


## [1.21.1] - 2026-07-10

### Documentation
- Explain how to grab the diagnostic log when reporting a bug


## [1.21.0] - 2026-07-10

### Features
- Let you set a per-skill XP rate


### Miscellaneous Tasks
- Point bucket at v1.20.4 [skip ci]


## [1.20.4] - 2026-07-08

### Miscellaneous Tasks
- Point bucket at v1.20.3 [skip ci]


## [1.20.3] - 2026-07-02

### Bug Fixes
- Fix new traits not saving for characters who started with none
- Fix a save-corrupting bug in the offline Oodle library caching


### Miscellaneous Tasks
- Point bucket at v1.20.2 [skip ci]


## [1.20.2] - 2026-07-02

### Build
- Bump actions/cache from 5 to 6 in the actions-all group


### Miscellaneous Tasks
- Point bucket at v1.20.1 [skip ci]


## [1.20.1] - 2026-07-02

### Bug Fixes
- Warn more clearly before repairing a save that hasn't finished syncing
- Stop needing internet every time you open a Game Pass save


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



