# Changelog

All notable changes to this project are documented here.

## [2.16.2] - 2026-09-21

### Bug Fixes
- Improve world transfers, chemistry benches and character details


## [2.16.1] - 2026-09-21

### Bug Fixes
- Tidy finale controls and restore missing companion pictures


### Miscellaneous Tasks
- Point bucket at v2.16.0 [skip ci]


## [2.16.0] - 2026-09-21

### Features
- Make offline companions and world details easier to edit


### Miscellaneous Tasks
- Point bucket at v2.15.1 [skip ci]


## [2.15.1] - 2026-09-21

### Miscellaneous Tasks
- Update editor dependencies
- Update the release packaging helper
- Point bucket at v2.15.0 [skip ci]


## [2.15.0] - 2026-09-18

### Bug Fixes
- Holograms and trader stands can no longer be marked dead, and say what they are
- A refused elevator change is reported once, and tram recalls are no longer refused while they work
- Switching tabs can no longer freeze the editor when the game lists the same thing twice
- The Bases tab shows bench names, where each base is, and how far away it is
- The General tab shows only your account details
- The Moon Fish "all day" entries no longer appear as separate fish that cannot be ticked
- Deleting an item while connected to a running game now clears it from the screen right away
- Bench upgrades no longer freeze the whole editor, and errors stay inside their tab
- Live editing on Linux now ships its files, finds its helper and detects it running
- The editor no longer crashes if the live helper quits right after writing its log
- Containment units now show up in the web version
- Refresh the web version's game data and drop two retired skills in Traditional Chinese


### Documentation
- Nexus page now describes live editing on Linux and everything it can change
- The live editing guide now lists exactly what works, what needs the host, and what cannot change


### Features
- A fresh install asks for your language first, then what you want to do
- More live editing parity - pressed-once buttons, exact spawner cooldowns, placed drops, clearer bench notes
- Change a tamed pet's species while the game is running
- Browse and edit the world's "seen" lists on the Story tab
- Breakables, corpses, resource nodes, spawners, triggers, power sockets and trams in live editing
- Unlock kill-tracked compendium entries while the game is running
- Tamed Peccaries and Lamogi now show up in live editing
- Edit a vehicle's on-board storage while the game is running
- One NPCs tab with real names for story characters and creatures
- Live editing on Linux when the game runs through Steam Play
- The web version now tells you live editing exists in the desktop app
- Edit buttons and elevators while the game is running
- One Dead switch for characters and creatures instead of a separate Revive button
- The live Traders tab now looks and works like the offline one
- Warn about modded saves in the web version and remove moving items between worlds there


### Miscellaneous Tasks
- Point bucket at v2.14.1 [skip ci]


## [2.14.1] - 2026-09-17

### Bug Fixes
- Keep the game up to date automatically instead of stopping releases


## [2.14.0] - 2026-09-17

### Bug Fixes
- No console window behind the editor, and a dropped tab switch no longer breaks the live connection
- A failed action no longer freezes the editor, and story chapter changes only use flags the game really has
- The Game Pass locked-folder message now simply tells you to turn on mods in the Xbox app
- Revived creatures move and fight again, and far more creatures have a preview picture
- Adding to a container no longer freezes the game, and a chemistry flask can be sent to a nearby chest
- Items removed from the ground no longer linger on the GROUND ITEMS tab
- A close button on the mode dialog once you have chosen, "Advanced" instead of "Experimental", and one place to change your background
- Void Chests work the same when editing a save file as they do live
- Renaming a Void Chest renames every Void Chest, the way the game shows it
- Void Chests now read and write the one pool every Void Chest actually shares
- Find and remove the real cause of the container list clipping
- Extend the slow-request timeout to writes and chemistry/garden/power-chair fields too
- A slow world-wide scan could time out before the game finished answering it
- Rebuild the container row layout with explicit named areas
- Actually show disconnected once the running game closes
- Undo the change that made Void Chest writes invisible, and stop renaming spreading to every chest
- A long container name could crowd the count/health/position lines under it
- Stop the freeze when putting something into a Void Chest, and a nonsense health number
- A Void Chest right in front of you could still go missing, show a fake 0/42, and drop what you put in it
- Show trader pictures again on the live Traders screen
- Stop hiding empty containers by default
- Void Chests now show their real, shared contents live
- Several live editing screens that looked broken or did nothing
- Refresh every live tab, not just a couple, when the game itself changes something
- Stop hiding containers and dropped items you just interacted with
- Make every tab name use the same capitalization
- Dropped items and container names live, and a real explanation for the region mix-up
- Stale live world data after teleporting, and terminal teleports landing you inside the wall
- The CONTAINERS screen was still freezing and timing out while live
- Stop the game freezing every couple of seconds on the live CONTAINERS screen
- Teleporting, dropping items, and freezing while editing live
- Settings-file edits from the command line now change the line the game reads


### Documentation
- The live-editing connection token goes inside the hello payload, not top-level
- Record the limitation sweep


### Features
- Choose which copy of the game to live-edit, with Game Pass now supported
- A START MIXING button for chemistry benches
- List chemistry benches closest to you first, and clean up the intro text
- Show a picture of each container next to its name
- Chemistry bench output updates live, and can send straight to a player
- A real recipe browser and pictures for chemistry benches and garden plots
- Write live-editing connection activity into the app's own log
- Show what's new the first time you open an updated app
- Let you back out of the mode-switch screen if you already had something open
- Rename containers, both in a save file and live in the running game
- Bundle a lot more creature pictures for offline use
- Change a garden plot's crop live, and see what's planted
- Give chemistry benches their own live editing screen
- Scan the live-editing helper for viruses before every release
- A "nearby only" filter for live containers and dropped items
- Show every region a co-op player is in, not just your own
- Show a picture of each creature on the live CREATURES screen
- Choose what a garden plot grows and which mutation a pet is working toward
- Carry your unlocks, stats, settings and looks between Game Pass and Steam
- Keep NEW badges in step with edits and expose the research queue
- Explain why world-wide recipe edits are unavailable and add wall-art choices
- Show what an item does in its details
- Repair broken walls and clear corpses in a saved world


### Miscellaneous Tasks
- Temporary diagnostic logging for the still-missing Void Chest report
- Point bucket at v2.13.0 [skip ci]


## [2.13.0] - 2026-09-16

### Documentation
- Record the release fix and the new colour, coating and pet tools


### Features
- Repaint placed objects in a saved world or a running game
- Edit a carried pet's mutation progress and tidy pet limits


### Miscellaneous Tasks
- Point bucket at v2.12.0 [skip ci]


## [2.12.0] - 2026-09-16

### Bug Fixes
- Ship the bundled UE4SS files beside the Windows editor


### Features
- Offer colour and artwork choices for many more items
- Change weapon coatings while connected to a running game


## [2.11.0] - 2026-09-16

### Bug Fixes
- Keep live item details from looking unsaved and describe the new live tools
- Load large live bases faster and restore recipes reliably
- Polish player and world details and correct pet levels
- Keep live items linked to the correct game data
- Guide players through installing UE4SS separately


### Documentation
- Record the live parity and bundled UE4SS session
- Record Cascade live editing checks
- Add illustrated guides for players and live setup
- Match the handbook to official game references
- Bring the handbook closer to the game's inventory style
- Give the player guides a Facility field-manual feel


### Features
- Include UE4SS in the Windows release and install it for you
- Edit traits, appearance and bench upgrades in a live game
- Expand live progression and inventory editing
- Add weapon coatings and world care tools
- Edit live weapon ammo and speed up bulk unlocks
- Add a game-inspired GATE Teal theme
- Make world controls and settings files easier to edit
- Make world and player tabs clearer and easier to use
- Simplify finding worlds and navigating settings
- Simplify editing screens and handle live setup automatically
- Add visual variant choices for items


### Miscellaneous Tasks
- Keep local test output out of the project
- Point bucket at v2.10.0 [skip ci]


### Testing
- Run the live-agent checks without a separate Lua install


## [2.10.0] - 2026-09-14

### Documentation
- Refresh the guides and documentation website
- Identify items with visual variants


### Features
- Make offline and experimental live editing clearer


### Miscellaneous Tasks
- Point bucket at v2.9.1 [skip ci]


## [2.9.1] - 2026-09-13

### Miscellaneous Tasks
- Point bucket at v2.9.0 [skip ci]


## [2.9.0] - 2026-09-10

### Features
- Fix a game-crashing bug in live editing, speed up bulk edits, and clean up the creatures tab


### Miscellaneous Tasks
- Point bucket at v2.8.0 [skip ci]


## [2.8.0] - 2026-09-06

### Bug Fixes
- The live TELEPORT button now moves you the way the game itself does
- Live teleport and vehicle moves use positions the game script understands
- Live vehicle and pet lists no longer time out, and live teleport works
- Bring back the manual refresh button on the live flags and story screens
- Items the game placed in a slot keep the item table the game chose
- Saving a character no longer leaves them exhausted or quietly rewrites their empty slots
- The live-editing companion now actually finds you in game
- Rebuild the live-editing companion on a real working mod's code
- Prevent the live-editing companion from freezing the game
- Strengthen the live-agent's connection secret to real randomness


### Documentation
- Record what became editable live this round and what is still file-only
- Tidy a leftover comment about the story chapter being read-only live
- Record that the live teleport and vehicle move were checked in a real game
- Be upfront that one journal read is unverified against the real game
- Log that the live-editing screens were tested for real, not just the pipe


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
- Research probes build again after the game-file library update, plus a live-editing class-layout probe


## [2.7.6] - 2026-09-06

### Build
- Bump taiki-e/install-action in the actions-all group


### Documentation
- Log the game-file library update and scope upcoming update support


### Miscellaneous Tasks
- Update the bundled game-file reading library to its latest version
- Point bucket at v2.7.5 [skip ci]


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

### Bug Fixes
- Compare logic
- Side panels now slide over the screen on small windows
- The editor no longer falls apart on narrow windows and phones


### Features
- Block Game Pass saves in the browser version


### Miscellaneous Tasks
- Point bucket at v2.7.2 [skip ci]


## [2.7.2] - 2026-08-19

### Build
- Bump taiki-e/install-action in the actions-all group


### Miscellaneous Tasks
- Point bucket at v2.7.1 [skip ci]


## [2.7.1] - 2026-08-14

### Bug Fixes
- Stop Game Pass to Steam conversions writing inside the Xbox package folder


### Documentation
- Correct the Convert screen's description of where a copy is written


### Features
- Let every player in a shared world keep their own character when converting


### Miscellaneous Tasks
- Point bucket at v2.7.0 [skip ci]


### Styling
- Put Back and Start over side by side on the Convert screen


## [2.7.0] - 2026-08-14

### Bug Fixes
- Stop Convert from quietly giving up your character
- Make converting a save simpler and fix a couple of Convert bugs


### Features
- Walk you through converting a Game Pass save step by step


### Miscellaneous Tasks
- Point bucket at v2.6.2 [skip ci]


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
- Picking a file in the browser works again
- The settings editor no longer looks broken in a browser
- Making a new world no longer opens a dead page in the browser
- The world day and time of day can be saved again
- Item names, recipes and pictures now actually load in the browser
- The browser editor can open saves again, and takes dropped folders


### CI
- Look for the editor's styling where it now lives [skip release]
- Allow a push to skip cutting a release [skip release]


### Documentation
- Record what the live browser editor actually downloads [skip release]
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


### Miscellaneous Tasks
- Point bucket at v2.3.0 [skip ci]


### Performance
- The browser editor now downloads about half as much


### Refactor
- Move the editor's look and feel where both versions can reach it
- Let the editor read and write saves from somewhere other than a disk
- Put the editor's screens in one place both versions can use


## [2.3.0] - 2026-08-05

### Bug Fixes
- Linux/Steam Deck download now runs even if the "allow execute" flag gets lost


### Features
- Add a true one-click launcher for Linux and Steam Deck downloads
- Add an advanced option to skip equipment/transmog slot checks


### Miscellaneous Tasks
- Point bucket at v2.2.2 [skip ci]


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

### Bug Fixes
- Translate the last remaining interface labels


### Build
- Bump the actions-all group with 3 updates


### Miscellaneous Tasks
- Point bucket at v1.23.4 [skip ci]


## [1.23.4] - 2026-07-17

### Bug Fixes
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

### Bug Fixes
- Fix new traits not saving for characters who started with none


### Miscellaneous Tasks
- Point bucket at v1.20.3 [skip ci]


## [1.20.3] - 2026-07-02

### Bug Fixes
- Fix a save-corrupting bug in the offline Oodle library caching
- Warn more clearly before repairing a save that hasn't finished syncing
- Stop needing internet every time you open a Game Pass save


### Miscellaneous Tasks
- Point bucket at v1.20.2 [skip ci]


## [1.20.2] - 2026-07-02

### Build
- Bump actions/cache from 5 to 6 in the actions-all group


### Miscellaneous Tasks
- Point bucket at v1.20.1 [skip ci]


## [1.20.1] - 2026-07-02

### Bug Fixes
- Clear old codex spoilers and offer to move players back on a story rewind


### Miscellaneous Tasks
- Point bucket at v1.20.0 [skip ci]


## [1.20.0] - 2026-07-01

### Bug Fixes
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
- Recover gracefully when a save blob is missing from disk


### Features
- Send a container item straight to a player
- Keep contained creature names hidden until you reveal them
- Warn about Xbox cloud sync before editing a save


### Miscellaneous Tasks
- Point bucket at v1.17.3 [skip ci]


## [1.17.3] - 2026-06-25

### Bug Fixes
- Let Game Pass players edit their character's look


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
- Conceal Jimmy and Blacksmith until their story gate flag is set
- Conceal Jimmy and Blacksmith until their story gate flag is set


### Documentation
- Write commit messages for Nexus Mods players, not developers
- Split Pages into two first-class tracks (Guide vs Reference)
- Restructure Pages - exclude research notes, add new guide pages


### Miscellaneous Tasks
- Point bucket at v1.17.0 [skip ci]


## [1.17.0] - 2026-06-21

### Bug Fixes
- Write correct Field1 (TotalRaw) in bundle serialization
- Force single-quantum Oodle compression for Game Pass bundles
- Compress bundle payload as single Oodle quantum
- Compress in 512 KB quanta to match the game's chunked Oodle reader
- Also skip timestamped .bak-<stamp> backup folders in discovery
- Bak-folder discovery, temp cleanup, home page OPEN button + remove, generation increment
- Surface bundle-load errors instead of showing empty sidebar


### Features
- Added assets
- Strip auto-updater from Nexus Mods distribution build


### Miscellaneous Tasks
- Point bucket at v1.16.1 [skip ci]


## [1.16.1] - 2026-06-21

### Build
- Bump the actions-all group with 3 updates


### Miscellaneous Tasks
- Point bucket at v1.16.0 [skip ci]


## [1.16.0] - 2026-06-21

### Bug Fixes
- Correct Game Pass session UX (folder display, reveal, reload, save indicator)


### Features
- Platform badge colors + game-data loading indicator


### Miscellaneous Tasks
- Point bucket at v1.15.0 [skip ci]


## [1.15.0] - 2026-06-20

### Features
- Settings polish - inline compare tab, plugin clarity, language fix
- Inline plugins into settings tab, centre tab content


### Miscellaneous Tasks
- Point bucket at v1.14.5 [skip ci]


## [1.14.5] - 2026-06-20

### Bug Fixes
- Refresh world discovery after creating a new world


### Features
- Vertical settings tabs, compare rework, modal dialog fixes


### Miscellaneous Tasks
- Point bucket at v1.14.4 [skip ci]


## [1.14.4] - 2026-06-20

### Bug Fixes
- Stop CLI build matrix legs from cancelling each other


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
- Support non-Steam saves (Game Pass / Epic) via opaque player ids


### Testing
- Add a sanitized real Game Pass container fixture


## [1.11.1] - 2026-06-18

### Bug Fixes
- Make the game-data banner action match the failure
- Extend keypad upgrade chain to the Tier 6 Gatekey
- Pin the Settings tab strip full-width and move diagnostics to General
- Repair items left on the empty-slot table, target each item's real table
- Pin the header version tag to the build's release version
- Repair mojibake in localized UI strings
- Point an added item's row handle at ItemTable_Global so it renders


### Features
- Fall back to built-in trader data and flag missing game data
- Tabbed Settings, clearer Game Data section, drop About
- Let users set the game folder when auto-detection fails


## [1.11.0] - 2026-06-18

### Features
- Translate UI to de/es/fr, add localization tests and docs


## [1.10.0] - 2026-06-18

### CI
- Make release push rebase-safe and cancel pre-publish runs on new push
- Cache NuGet packages and the MAUI workload to speed up the release pipeline


### Features
- Add JavaScriptPlugin based capability for localization
- Localize the UI and let plugins contribute translations
- Log previously-unlogged mutating user actions
- Show update download progress with cancel; stop auto-opening a world on startup
- Make diagnostic logging opt-in, but always log critical errors


### Miscellaneous Tasks
- Point bucket at v1.9.0 [skip ci]


## [1.9.0] - 2026-06-18

### Bug Fixes
- Assign and persist per-instance AssetID for added inventory items


### CI
- Depend nexus on build-app-win only, not publish
- Scan release zips with VirusTotal and publish to NexusMods


### Features
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


### Features
- Add Create New World wizard for starting fresh save games


### Miscellaneous Tasks
- Point bucket at v1.7.0 [skip ci]


## [1.7.0] - 2026-06-17

### Features
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


### CI
- Don't let the Mac Catalyst build block the release


## [1.1.0] - 2026-06-14

### Bug Fixes
- Use the real Teleporter Pad image; show nothing when no image exists
- Only show a feature image when the wiki really pictures it
- Keep pets in the hotbar/Companion slot, never the backpack
- Keep the right sidebar to a single detail context
- Wrap the editor tab bar so every tab stays visible


### Features
- Name, picture, link and remove world-state map entries
- Show pet portrait; fix vehicle open-container jump
- Move world-state map editing into world-editor tabs
- Correct containment/vehicle art and group vehicles by world


## [1.0.1] - 2026-06-14

### Bug Fixes
- Disable the optional CUE4Parse-Natives CMake build


### Features
- First-class pet & vehicle systems + cross-save pet movement


### Miscellaneous Tasks
- Drop master branch alias, use main only
- Relicense MIT -> Apache-2.0 and add NOTICE


## [1.0.0] - 2026-06-14

### Bug Fixes
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
- Publish Core + Plugins.Abstractions to NuGet on release
- Add VitePress docs site, release CI, and Dependabot


### Miscellaneous Tasks
- Gitignore transient .playwright-mcp/ snapshot output


### Styling
- Remove em dashes across source, docs, and config


### Testing
- Add reader/writer reversibility + isolation validation tests



