# Changelog

All notable changes to this project are documented here.

## [2.25.2] - 2026-10-03

### Bug Fixes
- Make Windows downloads easier for Nexus to review


## [2.25.1] - 2026-10-03

### Bug Fixes
- Keep world inspectors and live editing in sync


### Testing
- Make save memory checks reliable


## [2.25.0] - 2026-10-02

### Features
- See the 3D view getting ready from any page, and use the 3D view while it does
- Get the 3D view ready up front, add objects by dragging pictures, and a tidier 3D panel
- Pictures for teleporter pads, sconce lamps, power outlets and more trams and crates
- Edit things right in the 3D view, faster drawing, whole characters and a map that shows the level
- The 3D view fits your window, loads its surroundings by itself, and opens in seconds
- Show in 3D is much quicker, and world lists show pictures of each door, tram and more
- Drag pieces to move them, full screen 3D, and clearer clicking
- The 3D view comes with the editor, and the editor uses far less memory
- Show anything in 3D from any tab, a tidier 3D view, and the two Dr. Cahns
- Placement warnings, the 3D view in the browser, and a much faster 3D view


### Bug Fixes
- Garden plots, bench upgrades and power in the 3D view's side panel
- Bodies lying around the facility are drawn lying down in the 3D view
- Sharper pictures of doors, trams, elevators and other things in the world lists
- The plugin list shows each plugin's real version number again
- Clearer wording about the 3D models and live editing
- Picking a container on the Containers tab always works first time and feels snappier
- The Hydroplant floor shows again in the 3D view
- Clearer wording and tidier text on the Traders tab and other editor pages


### Performance
- A smaller Windows download, with file pickers that open from the editor window itself
- A 10 MB smaller download by shipping only the icons the editor uses
- The 3D view opens much faster, stays loaded between tabs, and more can be clicked
- Much faster saving, smoother walking and a quicker 3D view


### Documentation
- Fresh screenshots of the editor, including the 3D view
- A fresh player guide for the 3D view and how the editor works today


### Testing
- Point the screenshot check at the transmog picture the guide uses
- The test suite uses far less memory and cleans up after itself


### Miscellaneous Tasks
- Tidy a code comment on the Containers tab


## [2.24.0] - 2026-10-01

### Features
- Wire up new batteries straight away and lay cable routes with reroutes


## [2.23.1] - 2026-10-01

### CI
- Give the Linux app check more time to start on a busy build machine


## [2.23.0] - 2026-10-01

### Features
- Open doors swing the right way in the 3D view


### Testing
- The live connection tests stop cleanly on Linux


## [2.22.0] - 2026-10-01

### Features
- Lamps light the 3D view, open doors look open, and characters stand naturally
- People placed in the levels stand in their real poses in the 3D view
- Walls stop you when walking, and you can place things from your other worlds
- Place new objects and see characters in the 3D view
- Open and close doors and walk around in the 3D view
- Posed people, liquid levels and object decals in the 3D view
- Outdoor ground, water and painted signs look like the game in the 3D view
- Your crops grow in the 3D view
- Tram rails in the 3D view
- Outdoor ground in the 3D view
- Painted objects show their paint colour in the 3D view
- The 3D models plugin is now a download of its own
- Power across regions, and power controls in the 3D view
- Plug devices in, unplug them, and repair broken power in your saves
- 3D view moves into the Bases tab, clearer base edit warnings
- See your base with the game's real models in the 3D view (optional plugin)
- Send a ground item straight to a character, and calmer clicking on story events
- Show real names for a character's active effects
- Clearer screen for moving items between worlds


### Bug Fixes
- Door states use the game's own names, and "Locked" is gone
- The 3D view finds an area's level from any kind of file path
- Moss, stone paths and other fifth-layer ground now show in the 3D view
- The start page no longer goes blank in a mid-sized window
- The 3D view shows your things after you open another area
- The editor reads the latest game update's data correctly
- Shorter base names in the 3D view's base picker
- The 3D view shows the surroundings in portal worlds too
- The 3D view shows the base you pick and no longer hides it behind light beams
- Trams are listed by route with real stop names, and save comparisons see tram moves
- Trams can now only be moved to stops on their own line
- The containers list no longer slows down in big regions
- Keep the virus scan links in release notes when one upload is refused
- Make long lists and game data loading faster


### Documentation
- Power changes made in the editor hold up in the real game


## [2.21.0] - 2026-09-29

### Features
- Remove active effects, friendlier spawner names, and fix the missing upgrade buttons
- Click a world-wide seen entry to read about it
- New Distilled app in the GATEPal tab


### Bug Fixes
- Bring back upgrade and downgrade buttons and tidy world screens


## [2.20.0] - 2026-09-29

### Features
- Hide the experimental 3D base view unless you turn it on


### Bug Fixes
- Tidy up transmog and character screens
- Show the mod warning once per world instead of on every save you click


## [2.19.1] - 2026-09-29

### Testing
- Stop two Game Pass conversion checks from tripping over each other


## [2.19.0] - 2026-09-29

### Features
- Delete, copy and arrange base objects in the 3D view
- Command line tools to move, turn, copy and delete base objects
- Safer delete, copy and group moves for base objects
- Add an experimental 3D view of the objects in a world region


### Bug Fixes
- REVERT ALL in the 3D view now undoes every staged edit
- Copies of a turned base object now keep the turn


### Documentation
- Note that base editing now works from the 3D view
- Record the 3D view and the new base editing tools


### Testing
- Spell the dash checks without the dash itself


### Miscellaneous Tasks
- Bring in the 3D view and base editing work


## [2.18.0] - 2026-09-28

### Features
- Read-only view of what a saved pet remembers
- Plant or clear the spot on an empty small garden plot
- Count which save fields the editor does not read yet
- Read every saved detail of story characters
- Preview what a story rewind would leave behind
- Show recipe-style entitlements per player, read-only
- Show power connections for each socket
- Trace how power devices connect across save files
- Check what a copied group of objects would lose
- Prepare safe moving and turning of placed objects
- Add a read-only census of placed base objects
- Show saved character extras in the Advanced data tab
- Read favorites, distillery history, effects and hotbar choice from character saves
- Warn when a save is older than any the editor was tested with
- Read your account unlocks, stats and settings files
- Add a shared location index behind future show-on-map


### Bug Fixes
- Show saved character extras on the tabs they belong to


### Documentation
- Fix broken links that stopped the documentation site from building
- Record what the roadmap work delivered and what is still open
- Record what saves do and do not say about gardens, pets, chemistry, story phases and summons
- Note that active effects and the last hotbar slot are now read
- Write up what the sample saves show about world objects
- Record which placed objects the save positions really control
- Write down how the game stores power connections
- Map out how Game Pass save support is split up
- Record what has been proven for each kind of save
- Describe how level floor plans could be built from game files
- Plan reusable Game Pass support and modding extensions
- Plan full level maps and power network building
- Clarify editor gaps and refresh game data


### Refactor
- Use the standalone Game Pass storage library
- Move Xbox save folder handling into its own reusable library


### Testing
- Make two editor checks reliable again
- Check the new Xbox save library on its own
- Pin down how Xbox save containers are read and written today


## [2.17.2] - 2026-09-27

### Miscellaneous Tasks
- Bump taiki-e/install-action in the actions-all group (#39)


## [2.17.1] - 2026-09-26

### Bug Fixes
- Make Game Pass offline editing risks clear before every open


## [2.17.0] - 2026-09-21

### Features
- Add digital gardens and sconce controls


## [2.16.2] - 2026-09-21

### Bug Fixes
- Improve world transfers, chemistry benches and character details


## [2.16.1] - 2026-09-21

### Bug Fixes
- Tidy finale controls and restore missing companion pictures


## [2.16.0] - 2026-09-21

### Features
- Make offline companions and world details easier to edit


## [2.15.1] - 2026-09-21

### Miscellaneous Tasks
- Update editor dependencies
- Update the release packaging helper


## [2.15.0] - 2026-09-18

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


## [2.14.1] - 2026-09-17

### Bug Fixes
- Keep the game up to date automatically instead of stopping releases


## [2.14.0] - 2026-09-17

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


### Miscellaneous Tasks
- Temporary diagnostic logging for the still-missing Void Chest report


## [2.13.0] - 2026-09-16

### Features
- Repaint placed objects in a saved world or a running game
- Edit a carried pet's mutation progress and tidy pet limits


### Documentation
- Record the release fix and the new colour, coating and pet tools


## [2.12.0] - 2026-09-16

### Features
- Offer colour and artwork choices for many more items
- Change weapon coatings while connected to a running game


### Bug Fixes
- Ship the bundled UE4SS files beside the Windows editor


## [2.11.0] - 2026-09-16

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


### Testing
- Run the live-agent checks without a separate Lua install


### Miscellaneous Tasks
- Keep local test output out of the project


## [2.10.0] - 2026-09-14

### Features
- Make offline and experimental live editing clearer


### Documentation
- Refresh the guides and documentation website
- Identify items with visual variants


## [2.9.0] - 2026-09-10

### Features
- Fix a game-crashing bug in live editing, speed up bulk edits, and clean up the creatures tab


## [2.8.0] - 2026-09-06

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


### Refactor
- Live door editing now uses the same doors screen you already know
- The live-editing companion can grow new areas without a rebuild


### Testing
- Catch more live-editing bugs before they ever reach the game
- The in-game script can now be checked without launching the game
- Repair the live channel tests after merging several live-editing branches
- The shared-session contract check no longer depends on interface order
- Research probes build again after the game-file library update, plus a live-editing class-layout probe


### Miscellaneous Tasks
- Hold this push back from an automatic release [skip release]
- Hold this push back from an automatic release [skip release]
- Hold this push back from an automatic release [skip release]


## [2.7.6] - 2026-09-06

### Documentation
- Log the game-file library update and scope upcoming update support


### Build
- Bump taiki-e/install-action in the actions-all group


### Miscellaneous Tasks
- Update the bundled game-file reading library to its latest version


## [2.7.5] - 2026-08-26

### Bug Fixes
- Story-event search now finds events that have not happened yet


### Testing
- Add coverage that saving traits/skills never corrupts other data


## [2.7.4] - 2026-08-26

### Build
- Bump taiki-e/install-action in the actions-all group


## [2.7.3] - 2026-08-19

### Features
- Block Game Pass saves in the browser version


### Bug Fixes
- Compare logic
- Side panels now slide over the screen on small windows
- The editor no longer falls apart on narrow windows and phones


## [2.7.2] - 2026-08-19

### Build
- Bump taiki-e/install-action in the actions-all group


## [2.7.1] - 2026-08-14

### Features
- Let every player in a shared world keep their own character when converting


### Bug Fixes
- Stop Game Pass to Steam conversions writing inside the Xbox package folder


### Documentation
- Correct the Convert screen's description of where a copy is written


### Styling
- Put Back and Start over side by side on the Convert screen


## [2.7.0] - 2026-08-14

### Features
- Walk you through converting a Game Pass save step by step


### Bug Fixes
- Stop Convert from quietly giving up your character
- Make converting a save simpler and fix a couple of Convert bugs


## [2.6.2] - 2026-08-13

### Bug Fixes
- A save Xbox left marked as disputed can be unstuck


## [2.6.1] - 2026-08-13

### Bug Fixes
- Say a Game Pass save cannot be saved yet when you open it, not when you try


## [2.6.0] - 2026-08-13

### Features
- Make converting a save between Steam and Game Pass easier to get right


## [2.5.2] - 2026-08-13

### Bug Fixes
- Buttons that could be pressed twice, and an export that was on the wrong platform


## [2.5.1] - 2026-08-13

### Bug Fixes
- Stop nagging about Game Pass saves that are perfectly fine
- Move a shared world to Steam, and stop a skipped test failing the build
- Your beds come with you when a character changes account


### Testing
- Stop two test groups fighting over the log settings


## [2.5.0] - 2026-08-13

### Features
- Bring the Game Pass safety net into the app
- Stop risky Game Pass saves before they happen, and rescue broken ones
- Re-home a packed Game Pass character from the command line
- Warn about Xbox cloud sync before editing a Game Pass save


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


### Testing
- Cover the Game Pass paths that touch real Xbox saves


## [2.4.0] - 2026-08-09

### Features
- A world opened from a zip is kept, edits and all
- A warning before unsaved changes are thrown away
- Open a zip of saves, and pick up where you left off


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


### Performance
- The editor only reads your world the slow way once
- Opening a world tab no longer copies the whole world first
- The editor stops freezing when it reads your world
- Clicking an item in your inventory is about five times faster


### Documentation
- Make the browser editor easy to find


### Build
- Bump taiki-e/install-action in the actions-all group


## [2.3.1] - 2026-08-08

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


### Performance
- The browser editor now downloads about half as much


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


### Refactor
- Move the editor's look and feel where both versions can reach it
- Let the editor read and write saves from somewhere other than a disk
- Put the editor's screens in one place both versions can use


### CI
- Look for the editor's styling where it now lives [skip release]
- Allow a push to skip cutting a release [skip release]


## [2.3.0] - 2026-08-05

### Features
- Add a true one-click launcher for Linux and Steam Deck downloads
- Add an advanced option to skip equipment/transmog slot checks


### Bug Fixes
- Linux/Steam Deck download now runs even if the "allow execute" flag gets lost


## [2.2.2] - 2026-08-05

### Build
- Bump the actions-all group with 2 updates


## [2.2.1] - 2026-07-26

### Bug Fixes
- Build the Windows version again


## [2.2.0] - 2026-07-26

### Features
- Change a player's account id from any save, and see pets before you save


### Bug Fixes
- The editor now keeps a record when something goes wrong


## [2.1.6] - 2026-07-26

### Bug Fixes
- No console flash, a proper window icon, and a tidier download


## [2.1.5] - 2026-07-26

### Bug Fixes
- The Linux download can now reach Nexus Mods
- The recipe book no longer names traders you have not met


## [2.1.4] - 2026-07-26

### Bug Fixes
- The Linux download can now be published to Nexus Mods


## [2.1.3] - 2026-07-25

### Bug Fixes
- The Linux release build no longer trips over its own health check


## [2.1.2] - 2026-07-25

### Bug Fixes
- The Nexus Mods download no longer contains any update checking


## [2.1.1] - 2026-07-25

### Build
- Bump the actions-all group with 2 updates


## [2.1.0] - 2026-07-25

### Features
- Ship the editor as one executable, with no server console


### Bug Fixes
- Saving no longer fails on a world that has never unlocked anything


## [2.0.0] - 2026-07-25

### Features
- Version 2, with Linux and macOS support


### Bug Fixes
- Say why Game Pass saves cannot be converted on Linux or macOS


### Build
- Only offer the Windows build on Windows
- Bump postcss from 8.5.15 to 8.5.23 in /docs
- Bump postcss


## [1.23.6] - 2026-07-22

### Bug Fixes
- Keep the boosted max durability showing when you reselect an item


## [1.23.5] - 2026-07-19

### Bug Fixes
- Translate the last remaining interface labels


### Build
- Bump the actions-all group with 3 updates


## [1.23.4] - 2026-07-17

### Bug Fixes
- Translate the rest of the editor into German, Spanish, French and Russian


## [1.23.3] - 2026-07-17

### Bug Fixes
- Detect the anniversary update companions (Speedogi, Sir Ogi, Verdant Skink)


## [1.23.0] - 2026-07-11

### Features
- Translate skills, traits, equipment slots, and world NPC labels
- The editor now works on Linux and Steam Deck
- Translate door names, lock explanations, and save-discovery badges


### Refactor
- Share the tag-editing helpers between the player and world save writers
- Break the three biggest save read/write files into focused parts
- Give every data model, catalog, and helper its own file
- Organize the engine into clear layers


### CI
- Stop publishing the Linux CLI to Nexus Mods
- Ship a Linux / Steam Deck build to Nexus Mods


## [1.22.0] - 2026-07-10

### Features
- Add Russian, fix several language bugs, and translate trader/story text that always stayed in English


### Bug Fixes
- Big Hive Larva's unlock condition now actually triggers, trader list scrolls properly


## [1.21.2] - 2026-07-10

### Bug Fixes
- Rewinding past a region now clears its flags even if you reached it early


## [1.21.1] - 2026-07-10

### Documentation
- Explain how to grab the diagnostic log when reporting a bug


## [1.21.0] - 2026-07-10

### Features
- Let you set a per-skill XP rate


## [1.20.4] - 2026-07-08

### Bug Fixes
- Fix new traits not saving for characters who started with none


## [1.20.3] - 2026-07-02

### Bug Fixes
- Fix a save-corrupting bug in the offline Oodle library caching
- Warn more clearly before repairing a save that hasn't finished syncing
- Stop needing internet every time you open a Game Pass save


## [1.20.2] - 2026-07-02

### Build
- Bump actions/cache from 5 to 6 in the actions-all group


## [1.20.1] - 2026-07-02

### Bug Fixes
- Clear old codex spoilers and offer to move players back on a story rewind


## [1.20.0] - 2026-07-01

### Features
- Add a Load More button to the item catalog


### Bug Fixes
- Rewinding the story past the Reactors now actually rewinds it


## [1.19.0] - 2026-06-27

### Features
- Full wiki-verified quest dependency tree for the main story
- Extend the quest dependency tree across Office, Manufacturing and Labs
- Follow per-quest dependencies so steps aren't left half-done
- Snapshot/compare tool to prove whether a real sync kept edits
- Repair a save stuck pointing at a missing data file


### Bug Fixes
- Show friendly state labels and a clearer Game Pass save warning
- Stamp the save index like the game does so edits sync
- Mark added items as discovered so the game recognises them


### Testing
- Cover the sync-recency behaviour and make edits strictly newer


## [1.18.0] - 2026-06-27

### Features
- Send a container item straight to a player
- Keep contained creature names hidden until you reveal them
- Warn about Xbox cloud sync before editing a save


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


## [1.17.3] - 2026-06-25

### Bug Fixes
- Let Game Pass players edit their character's look


## [1.17.2] - 2026-06-22

### Bug Fixes
- Show each skill's real level instead of a mislabeled one


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


## [1.17.0] - 2026-06-21

### Features
- Added assets
- Strip auto-updater from Nexus Mods distribution build


### Bug Fixes
- Write correct Field1 (TotalRaw) in bundle serialization
- Force single-quantum Oodle compression for Game Pass bundles
- Compress bundle payload as single Oodle quantum
- Compress in 512 KB quanta to match the game's chunked Oodle reader
- Also skip timestamped .bak-<stamp> backup folders in discovery
- Bak-folder discovery, temp cleanup, home page OPEN button + remove, generation increment
- Surface bundle-load errors instead of showing empty sidebar


## [1.16.1] - 2026-06-21

### Build
- Bump the actions-all group with 3 updates


## [1.16.0] - 2026-06-21

### Features
- Platform badge colors + game-data loading indicator


### Bug Fixes
- Correct Game Pass session UX (folder display, reveal, reload, save indicator)


## [1.15.0] - 2026-06-20

### Features
- Settings polish - inline compare tab, plugin clarity, language fix
- Inline plugins into settings tab, centre tab content


## [1.14.5] - 2026-06-20

### Features
- Vertical settings tabs, compare rework, modal dialog fixes


### Bug Fixes
- Refresh world discovery after creating a new world


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

### Features
- Per-mod enable/disable in Settings
- Craft minimal region saves for unvisited regions


### Documentation
- Record the UserEntitlements coverage gap and round-38 progress


## [1.13.0] - 2026-06-19

### Features
- Support Abiotic Factor mods (mount mod paks + discover mod data tables)
- Offline fallback bundle for wiki images


### Documentation
- Give the Nexus mod page the same flair as the docs site


### Refactor
- Route remaining string.Format sites through the Format helper


### Testing
- Point fixture locators at the platform-grouped layout


### Build
- Silence vendored submodule warnings (CUE4Parse/UeSaveGame)


### Miscellaneous Tasks
- Log save-switch breadcrumbs and world-editor dirty reasons


## [1.12.0] - 2026-06-19

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


### Bug Fixes
- Incorrect data registry test analysis isuse
- Platform-aware default folder, native alerts, build-clean localized formatting
- Keep one changelog bullet per line
- Route wgs folders from every open path; fix empty-sidebar overlap
- Open wgs folders directly, lock the id for non-Steam, clearer convert UI
- Validate extracted member paths stay in the working dir


### Documentation
- Log the non-Steam + Game Pass round in PROGRESS.md


### Testing
- Add a sanitized real Game Pass container fixture


## [1.11.1] - 2026-06-18

### Features
- Fall back to built-in trader data and flag missing game data
- Tabbed Settings, clearer Game Data section, drop About
- Let users set the game folder when auto-detection fails


### Bug Fixes
- Make the game-data banner action match the failure
- Extend keypad upgrade chain to the Tier 6 Gatekey
- Pin the Settings tab strip full-width and move diagnostics to General
- Repair items left on the empty-slot table, target each item's real table
- Pin the header version tag to the build's release version
- Repair mojibake in localized UI strings
- Point an added item's row handle at ItemTable_Global so it renders


## [1.11.0] - 2026-06-18

### Features
- Translate UI to de/es/fr, add localization tests and docs


## [1.10.0] - 2026-06-18

### Features
- Add JavaScriptPlugin based capability for localization
- Localize the UI and let plugins contribute translations
- Log previously-unlogged mutating user actions
- Show update download progress with cancel; stop auto-opening a world on startup
- Make diagnostic logging opt-in, but always log critical errors


### CI
- Make release push rebase-safe and cancel pre-publish runs on new push
- Cache NuGet packages and the MAUI workload to speed up the release pipeline


## [1.9.0] - 2026-06-18

### Features
- Add RELOAD-from-disk with unsaved-changes confirm


### Bug Fixes
- Assign and persist per-instance AssetID for added inventory items


### CI
- Depend nexus on build-app-win only, not publish
- Scan release zips with VirusTotal and publish to NexusMods


## [1.8.0] - 2026-06-18

### Features
- Added nexus mod deployment
- Added virus scanning to the release packages


## [1.7.1] - 2026-06-17

### Features
- Add Create New World wizard for starting fresh save games


### Bug Fixes
- DOWNLOAD & INSTALL now works from the Settings modal
- Config discovery no longer leaks sibling-world sandbox settings


## [1.7.0] - 2026-06-17

### Features
- Auto-discover all ItemTable_* files for DLC resilience


## [1.6.0] - 2026-06-16

### Features
- Grant future/unknown server entitlements via a free-text add field
- Server entitlements as per-grant toggles with player names


### Bug Fixes
- Pet placement respects Main slot kind, not just companion/hotbar
- INI switch leaves stale entries; enable + surface diagnostic logging
- INI editor was blank - drop the broken Source=Root bindings
- Send-pet-to-player falls back between companion slot and hotbar
- Wrap the player editor tab bar instead of horizontal scroll


## [1.5.0] - 2026-06-15

### Features
- Friendly resource-node names, search filter, location per row


### Bug Fixes
- Robust cross-world power-socket device resolution + diagnostics


## [1.4.0] - 2026-06-15

### Features
- Show friendly names for cross-world power-socket devices
- True cross-world navigation to a power socket's plugged-in device
- Identify and navigate to a power socket's plugged-in device


### Bug Fixes
- Resolve teleporter sync name; clarify tram station picker
- Pet-to-bed picker, drop duplicate Vehicles tab, door/elevator clarity


## [1.3.0] - 2026-06-15

### Features
- Editable crafting-bench upgrades in the Bases tab
- Editable trams, per-feature area + remove labels, vehicle/pet fixes, drop NPCs tab
- Safer, exportable, richer save comparison; clearer doors; settings language row
- Shared area-name catalog, soft-path setter, bench-upgrade tags


### Bug Fixes
- INI file switching, appearance guidance, and richer edit logging


## [1.2.1] - 2026-06-15

### CI
- Disable macOS app builds


## [1.2.0] - 2026-06-15

### Features
- Version-stamped zips and a self-contained single-file Windows app


## [1.1.3] - 2026-06-14

### Bug Fixes
- Stop dialog-host theme leak, dead-click reselect, stacked leave-gates
- Verify download size, block asset-name traversal, fix prerelease order
- Close save-write corruption, pet-XP loss, and icon-cache races


## [1.1.2] - 2026-06-14

### Documentation
- Note trunk-based development (commit to main, no branches)


## [1.1.1] - 2026-06-14

### Bug Fixes
- Re-publish orphaned tags so a release can't get stranded


### CI
- Don't let the Mac Catalyst build block the release


## [1.1.0] - 2026-06-14

### Features
- Name, picture, link and remove world-state map entries
- Show pet portrait; fix vehicle open-container jump
- Move world-state map editing into world-editor tabs
- Correct containment/vehicle art and group vehicles by world


### Bug Fixes
- Use the real Teleporter Pad image; show nothing when no image exists
- Only show a feature image when the wiki really pictures it
- Keep pets in the hotbar/Companion slot, never the backpack
- Keep the right sidebar to a single detail context
- Wrap the editor tab bar so every tab stays visible


## [1.0.1] - 2026-06-14

### Features
- First-class pet & vehicle systems + cross-save pet movement


### Bug Fixes
- Disable the optional CUE4Parse-Natives CMake build


### Miscellaneous Tasks
- Drop master branch alias, use main only
- Relicense MIT -> Apache-2.0 and add NOTICE


## [1.0.0] - 2026-06-14

### Features
- Publish Core + Plugins.Abstractions to NuGet on release
- Add VitePress docs site, release CI, and Dependabot


### Bug Fixes
- Set git-cliff initial_tag so the first release computes v1.0.0
- Supply Linux Skia native and realign SkiaSharp to CUE4Parse's pin
- Resolved issues with github page styling and some wording


### Documentation
- Open content images in a lightbox on click
- Document the plugin system (folder READMEs, site pages, wiki)
- Flesh out README and docs site for newcomers, add screenshots


### Styling
- Remove em dashes across source, docs, and config


### Testing
- Add reader/writer reversibility + isolation validation tests


### Build
- Extract CUE4Parse-mirrored package versions into a submodule-adjacent file
- Realign CUE4Parse-mirrored deps to submodule pins, pin Dependabot off them
- Treat warnings as errors and clear first-party warnings
- Bump the actions-all group with 9 updates


### CI
- Gate releases on the test suite passing


### Miscellaneous Tasks
- Gitignore transient .playwright-mcp/ snapshot output



