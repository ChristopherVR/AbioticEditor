# Abiotic Editor - Session Progress (compacted 2026-06-12, round 3)

State of the .NET save editor (repo root layout). **308 assertion tests + 95 probes
green**; full solution builds clean; app multi-targets android/ios/maccatalyst/windows.
Plugin system: round-15 (core), round-16 (events/menu/JS), round-17 (web tools HTML/React +
host-UI bridge + Vite sample).

## Round-76: one set of screens for offline and live editing, and live parity for the remaining areas (2026-09-06)

The user's direction after round 75: "we are supposed to be using the same UI components as the
offline editing capabilities - it's meant to be shared. Seamless experience", plus "add support
for all the remaining gaps", fanned out to Sonnet subagents. Also: commit and push without
cutting a release (the release workflow's own `[skip release]` marker on the last pushed commit).

**Integration seam first** (`688b4d9`): the native helper now forwards ANY command by name to
the Lua mod (`Server::RegisterDefaultHandler`), so a new area no longer needs the exe rebuilt;
`main.lua` exposes its proven helpers as a `ctx` table and loads every module listed in
`Scripts/areas/manifest.lua` (`return function(ctx) ... end`, contract in `areas/README.md`),
so eight agents could add areas without all editing one file. Verified under a stubbed Lua
environment before fan-out.

**Eight agents, eight worktree branches, merged one by one** (conflicts were all "both sides
appended" - manifest lines, the `WorldSaveSession`/`PlayerSaveSession` interface lists, the
LiveConnect tab strip, the fake-agent switch, the protocol doc). Result: every duplicate
`Live*Tab` is gone except `LiveNpcsTab` (no offline twin exists) and `LiveTradersTab` (the
offline tab depends on the file-only split-pane and story-sync services). The pattern
everywhere is the round-64 `IPlayerVitalsSession` one: a narrow interface (`IWorldFlagsSession`,
`IWorldStorySession`, `IWorldDoorsSession`, `IWorldContainersSession`,
`IWorldDroppedItemsSession`, `IPlayerInventorySession`, `IPlayerTransmogSession`,
`IPlayerRecipesSession`, `IPlayerCodexSession`, `IPlayerGeneralSession`, `IPlayerSpawnSession`,
`IPlayerCompanionsSession`, `IWorldBasesSession`, `IWorldVehiclesSession`, `IWorldPetsSession`,
`IWorldContainmentSession`, `IWorldFeaturesSession`) with `AppliesImmediately` (false for the
file session, true live) and async-capable mutators; the offline tab binds to it and is rendered
by both `PlayerEditor`/`SaveEditorSurface` and `LiveConnect.razor`. Source-contract tests pin
that no `Live*Tab` duplicate comes back.

**New live areas, each grounded in the game's own class layouts** (`LiveClassPropsProbe` dumps
plus PDB signatures, same method as round 75): `story.get` (read-only: `CurrentQuest` on the
game state; no native setter exists), `bases.list/set` (rename any deployable via
`AlternativeObjectName`), `vehicles.list/set` (`VehicleDriveable` + `OnRep`, position via
`K2_TeleportTo`), `pets.list` (honestly `available:false` - tame/name/health fields differ per
creature family), `containment.list/set` (the reference mod's own trap/free Leyak sequence),
`traders.list/unlock` (trader gating IS world-flag state), `portals.list/set`
(`IsTeleporterActive`), `spawn.get/set` (position + `TeleportPlayer`, respawn terminal via the
controller's `TerminalRespawnID`), `companions.list/set` (pet slots incl. `DynamicProperties`
XP/mutation), `recipes.get/set` (`Request_UnlockNewRecipe`), `codex.get/set`
(`Server_AddEmailToReadList` / `Server_AddNoteToJournal` / `Request_UnlockNewFish`; compendium
read-only), `general.get/set` (`Server_CheckNewItemPickedUp`, `Server_AddMapToJournal`), and a
fourth `transmog` kind on `inventory.*` (`TmogInventory`, same component class). Achievements,
raw data and entitlements are offline-only by nature and the live page says so.

**Verified against the real game** (Chrissie world, hosting): every read returned real data
(542 deployables, 113 portals, 239 recipes, 30 emails, 6 transmog slots, the forklift, the
current quest row `quest_RES_EndInterlude`); writes with readback and revert: deployable renamed
and restored, a portal pad toggled and restored, the forklift made undriveable and back, a
pet-slot name set and cleared, a transmog slot filled and cleared, a trader flag set and cleared,
`recipe_ammo_9mm` unlocked, `Email_Crossbow` marked read, `story.set` refused as designed.
Three bugs found only by running live, all fixed: `vehicles.list` and `companions.list` put an
FString userdata straight into the reply so `json.encode` threw inside the reply path and the
editor saw a timeout (now converted, and the reply path reports an unencodable result as an
error instead of silence); `spawn.set` used `FRotator(...)`/`FVector(...)` constructors that do
not exist as UE4SS Lua globals (plain `{X,Y,Z}`/`{Pitch,Yaw,Roll}` tables now). Note UE4SS hot
reload is off in this install (`EnableHotReloadSystem = 0`), so every script fix costs a game
restart. Full suite 1236/1236 after one copy-guard fix (a code comment said "Blazor").

**Still open**: the shared tabs were exercised live through the raw protocol and a headless
Playwright pass of the page, not every button; the live door swing, the containment assign/
release sequence (no units were loaded in the test world) and any non-host client run remain
untested; `LiveNpcsTab`/`LiveTradersTab` are the two remaining live-only screens.

## Round-75: live world editing (clock, weather, quest flags, doors, containers, dropped items), plus the real cause of Nexus bug #1 (2026-09-06)

Three asks: extend live editing to the remaining world areas, merge the open PRs, and re-check
the Nexus bug report the previous round had closed as "not a bug" after the reporter added detail.

**PRs**: #33 (git-cliff install-action bump) and #34 (System.Numerics.Tensors 10.0.11) merged on
GitHub; local `main` (14 unpushed commits from rounds 64-74) rebased onto the result. Nothing
pushed - that remains the user's call.

**Nexus bug #1 IS a real editor bug, two of them.** The reporter's follow-up ("black, not the
grey rad-suit visor; happens after ANY save incl. a skill change; revert fixes it") pointed at
something the app writes on every save. Built a no-edit round-trip test through the app's own
`PlayerSaveSession.SaveAsync` (which re-applies EVERY section, not just what changed) and a
per-writer byte-impact test, and found:
1. `PlayerSaveReader.ReadStats` assumed a delta-omitted survival stat meant 100. The game's own
   defaults are 0 for all five - confirmed from the pak, not guessed:
   `Default__Abiotic_CharacterSave_C.CharacterSaveData.CurrentSurvivalStats` and
   `CharacterStatsSave_Struct` both carry 0 (new `LiveClassPropsProbe` dumps them). Fatigue also
   runs the OTHER way from the rest: 0 = just slept, it climbs while awake (the CDO's
   `FatigueIncreaseAmount`, `FatigueRequiredToSleep = 40`; CheatConsoleCommands' "no fatigue"
   writes `CurrentFatigue = 0.0`; the live "Chrissie" read 96 while nearly collapsing). So a
   player who slept right before quitting has fatigue exactly 0, the game omits the tag, the
   editor reads 100 and writes it back on any save -> the character loads fully exhausted, with
   the black drowsiness bands top and bottom of the screen. Exactly the report. Fixed: missing
   stat reads 0, and `ApplyStats` only creates a missing tag for a non-zero value (byte-identical
   otherwise). `RepairNeeds` sample plugin and the blank-character template now put fatigue at
   0, not 100 (both were silently writing "about to pass out").
2. `ApplySlot` (player + world container writers) retargeted EVERY empty slot's row-handle
   `DataTable` from the game's `ItemTable_Pickups` default to `ItemTable_Global` on every save
   (+595 bytes on one fixture with no edit at all) - the June "repair items on the wrong table"
   logic never excluded the `Empty` sentinel. Now: retarget only when the write changes the
   slot's row (or the loaded catalog positively knows the item lives elsewhere), never the
   sentinel, never a slot the game itself wrote.
New permanent tests: `PlayerSaveSessionRoundTripTests` (no-edit app save == original, arrays
compared as multisets since the app sorts several string lists), `PlayerSaveWriterByteImpactTests`
(every in-place writer handed back the reader's own values is byte-identical),
`PlayerSurvivalStatDefaultTests`. Round-62's `Player_AddTrait_ChangesOnlyThatLeaf` was right
about the Core writer in isolation and wrong about the app, because the app applies everything.
Not verified in-game which of the two the reporter saw (the fatigue one matches every detail);
a reply for the Nexus thread is drafted in the session summary.

**Live world editing - research that overturned rounds 72/74.** Those rounds concluded quest
flags and containers had "no evidenced live path" because no installed mod touched them. This
round went to the game's own data instead: `LiveClassPropsProbe` dumps blueprint property and
function lists from the paks (DayNightManager_C, SimpleDoor_ParentBP_C, SecurityDoor_C,
Deployed_Container_ParentBP_C, Abiotic_Item_Dropped_C, the GameMode/GameState/GameInstance,
Abiotic_WorldSave_C) plus native usmap layouts, and the shipped `AbioticFactor-Win64-Shipping.pdb`
gave the mangled native signatures of the flag system: `UWorldFlagSubsystem::SetWorldFlag(
FWorldFlagRowHandle, bool, UObject*)`, `GetWorldFlags(TArray<FName>&)`, `HasWorldFlag(...)`, and
`UWorldFlagHandleFunctionLibrary::GetAllWorldFlagRowNames/RowHandles` - the exact objects every
`Trigger_WorldFlag_C` and story-gated door in the game calls (their ubergraphs reference
`GetWorldSubsystem` -> `HasWorldFlag`). Containers turned out to be the same
`Abiotic_InventoryComponent_C` as the player backpack (`ContainerInventory`), so the slot write
is shared.

**Built**: `world.get/set`, `flags.list/set`, `doors.list/set`, `containers.list/set`,
`dropped.list/remove` in `main.lua`; helper allowlist extended and the exe rebuilt (MSVC via
`vcvars64`); five `Live*Channel`s in `Core/LiveEditing/World/`, five `Live*Session`s and five
tabs (`LiveWorldTab`, `LiveFlagsTab`, `LiveDoorsTab`, `LiveContainersTab`,
`LiveDroppedItemsTab`) wired into `LiveConnect.razor`. World areas connect independently and
degrade to an "not available, load a world" note (a player at the main menu has no
DayNightManager yet). The flags tab reuses `QuestFlagCatalog`/`FlagGate`: SET on a flag with
unmet prerequisites applies them in the same request, the way the file editor offers to.
Protocol doc, user guide and live-agent README updated; 5 new fake-agent channel tests; a Lua
5.4 interpreter built from source (scratchpad) syntax-checks `main.lua`.

**Verified against the real game** (Chrissie world, day 22, hosting; character found dead on
load, healed + respawned first): every list returned real data (257 flags/59 set, 76 doors,
193 containers, 112 loose items, 7 weather rows); `MapReveal_Security` set then cleared;
`SimpleDoor_ParentBP_C_9` opened (state 0->1) and closed; `scrap_metal x3` placed in a tram
storage slot and cleared back to `Empty`; clock to 21:00 (night on the next game tick) and
back to midday; `Fog` triggered and cleared; a warning sign removed (`removed: 1`, gone on the
next list - `InitDespawn` is timer-based). Then the desktop UI itself, headless + Playwright:
auto-connected, all nine tabs render with the live data (WORLD readout "Day 22, 13:28", flags
list with SET / SET (+1 BEFORE IT), doors with state pickers, containers master/detail with
real slots). One correction from the live run: loot-spill bags carry row `None` in unused
slots, so `isEmpty` treats `None` like `Empty`.

**Open**: hinged-door writes are direct `DoorState` + `OnRep_DoorState` + `DoorUpdateState`
(no mod precedent for the last call) - the readback is right but the swing animation was not
watched on screen; a client (non-host) run of any world area is still untested; flags shown for
rows the catalog does not know (e.g. `91Contained`) print the raw name as their area.

## Round-74: live player inventory editing, plus quest/story flags and world containers confirmed as a genuine live-editing limitation (2026-09-03)

The user asked for two things: research and, if real, build live quest/story flag editing; and
build live editing for player inventory and world containers, being honest if any of it turns out
to be a genuine limitation.

**Fresh, dedicated research pass** (a fork agent, grepping every installed reference mod for
inventory/container/flag-related UObject access): confirms round-72's finding on quest/story
flags - no `QuestFlag`/`StoryFlag`/`FlagManager`/`ProgressManager` or any `FindAllOf` against a
flag-tracking actor exists anywhere. **This is a genuine limitation of live editing**, not
something this round declined to build - there is nothing real to build it on. One narrower,
adjacent thing IS real (not built this round, flagged for later if wanted): journal/codex
**section unlocking** via `AFUtils.GetMyCharacterProgressionComponent():Request_UnlockCompendiumSection`
(`CheatConsoleCommands/scripts/Features.lua:894-900`) - a narrow server RPC, not the general
quest-flag system. **World containers** (chests, storage furniture): also no evidence anywhere -
no `FindAllOf` against any storage/container actor class in any installed mod. Also a genuine
limitation, confirmed independently this round, not carried over unverified from round-72.

**Player inventory: real evidence, built and verified live.** `CheatConsoleCommands/scripts/
AFUtils/ObjectsGetter.lua:60-86` (`GetMyInventoryComponent`/`GetMyEquipmentInventory`/
`GetMyHotbarInventory` - real getters returning `CharacterInventory`/`CharacterEquipSlotInventory`/
`CharacterHotbarInventory`, each with a `.CurrentInventory` array of item-slot structs) and
`AFUtils/AFUtils.lua:682-695` (`SetItemSlot`, the exact hash-suffixed field names). **Honestly
weaker evidence than every other live-editing area**: grepping confirmed these getters/setters are
real, defined functions with exact field names, but unlike vitals/skills/NPCs, `SetItemSlot`/
`AddToItemStack` are never actually CALLED by any shipped, ENABLED command in the reference mod -
the only two call sites are both commented out, and both are about slot COUNT, not slot content.
Built and tested live anyway (low blast-radius, direct field write, same shape as every other
confirmed area) - and it worked cleanly. New `inventory.list`/`inventory.set` handlers in
`main.lua`, `LiveInventoryChannel.cs`, `LiveInventorySession.cs` (immediate-apply, like NPCs, but
per-row since a slot edit is usually several fields at once), `LiveInventoryTab.razor` (reuses the
file editor's existing `ItemCatalogService`/`ItemPaletteDatalist` for the item-id autocomplete and
display names - no new catalog needed).

**One real bug found and fixed before it shipped**: the game's own empty-slot sentinel string is
`"Empty"` (confirmed live), not `"None"` - `isEmpty` was computed wrong on the first pass
(`rowName == "None"`), and the CLEAR path was writing `NAME_None` instead of the real `"Empty"`
FName. Fixed both before the live test round that would have caught it anyway, but worth noting:
this project already knew "Empty" is the sentinel (see memory: abiotic-save-schema-facts) and
still nearly re-guessed it wrong for the live path specifically.

**Verified, in order**: raw-protocol `inventory.list` (real backpack/equip/hotbar data - actual
equipped gear: Cold Storage Pack, Crystalline Vial, Keypad Hacker T5, Shredshot, Hand Drill,
Electron Grenades, matching the real character exactly); raw-protocol `inventory.set` placing
`scrap_metal x3` in an empty backpack slot, confirmed both via a follow-up `inventory.list` AND
**visually in the actual game's inventory UI** (correct icon, correct stack count, no manual
refresh needed - unlike vitals/NPCs, no `OnRep`-style call exists for inventory and none was
needed); `inventory.set` clearing the same slot back to empty, confirmed the same two ways; the
actual desktop UI's INVENTORY tab (headless + Playwright) showing all 51 real slots with correct
catalog-resolved display names, filling and applying a slot, and clearing it via the UI's own
CLEAR SLOT button - both confirmed via the rendered `<strong>` label changing
(`"Slot 0"` -> `"Slot 0 · Metal Scrap"` -> `"Slot 0"`).

**Screen-automation practice, carried over from round-73 with two more real bugs fixed**: the
scroll-wheel helper crashed on a negative `WHEEL_DELTA` (`[uint32](-120)` throws in PowerShell -
fixed via `[BitConverter]`, not a plain cast). More importantly: **the native helper's C++ server
only serves one client connection at a time** (confirmed in round-73, re-encountered here) - after
closing the Playwright browser tab without clicking DISCONNECT first, the Blazor Server circuit
kept its `TcpLiveGameChannel` connection open server-side, and the helper's blocking accept loop
kept serving it, so raw-protocol test connections got refused (not just delayed) until the
headless host process itself was killed. Always stop the headless app process explicitly at the
end of a round, not just close the browser tab - see [[live-game-screen-automation]] memory,
updated with this.

**Character safety**: the real "Chrissie" character loaded dead twice this round (a save captured
mid-death from the prior round's force-stopped process, both before and after the mod-script
relaunch) - respawned at YOUR BED both times, not a code bug. Vitals otherwise fine
(hunger/thirst/sanity/fatigue/continence all reasonable); head health was at 80, healed to 100.
Money still correctly 1000.

## Round-73: NPC live editing verified against the real game, plus a screen-automation coordinate bug (2026-09-03)

Continuation of round-72, resumed after a context compaction. Round-72 had already built
`npcs.list`/`npcs.set` (kill/revive, Disabled, Invincible, Faction) end to end - Lua handlers,
native-helper allowlist, `LiveNpcChannel`/`LiveNpcSession`, the NPCS tab - but left it unverified
against the real game. This round finished that verification.

**The tool used to drive the game window in rounds 69-72 was not available this round** (a fresh
tool context after compaction). Rebuilt the same capability from scratch as inline PowerShell:
`System.Drawing` `CopyFromScreen` for screenshots, `user32.dll` `SetCursorPos`/`mouse_event` for
clicks, `keybd_event` for keys - saved as a small reusable helper script for future rounds.

**Two real automation bugs found and fixed while getting this working, both worth remembering**:
1. The desktop is multi-monitor with the game's monitor as *primary* (origin `0,0`) and the
   second monitor at a *negative* X offset (`VirtualScreen.Left = -1920`). A screenshot bitmap's
   pixel coordinates and `SetCursorPos`'s real coordinates are **not the same numbers** on this
   kind of layout - clicking at a bitmap-read coordinate silently clamped the cursor to the
   screen edge and did nothing, with no error. Every click helper now converts
   `bitmap coordinate + VirtualScreen.Left/Top` before calling `SetCursorPos`.
2. Windows' foreground-lock meant a plain `SetForegroundWindow` call from a script with no message
   pump was silently ignored (clicks landed on an unfocused game window and did nothing) - fixed
   with the standard `keybd_event`-then-`SetForegroundWindow` trick (a synthetic Alt key press
   relaxes the lock for the next foreground switch from the same thread).

**A third bug, in this round's own throwaway test tooling, not the product**: a PowerShell
`$PayloadJsons = if (...) { $x | ForEach-Object {...} }` pattern silently unwraps a one-element
pipeline result into a bare string, so `$PayloadJsons[0]` indexed a *character* of the JSON
(`{`) instead of the whole payload - a revert command silently sent an empty payload and did
nothing, caught by re-listing and comparing rather than trusting the "ok" response. Fixed by
wrapping the whole expression in `@(...)`.

**Verified, in order**: raw-protocol `npcs.list` (16 real NPCs, correct ids/faction/dead/disabled/
invincible, `isHost: true` on this singleplayer save); raw-protocol `npcs.set` toggling and
reverting `Invincible` on one NPC, round-tripped via a follow-up `npcs.list` each time; the actual
NPCS tab in the desktop UI (headless + Playwright) showing all 16 real NPCs correctly, a checkbox
click applying immediately and the list refreshing, reverted the same way; a fresh reconnect
confirming NPC state matched the pre-test baseline exactly (same three `Disabled` Pests, nothing
left `Invincible`). Also learned the native helper's C++ server is single-threaded
(`accept()` -> `ServeClient()` in one loop) - it serves exactly one client connection at a time,
so the raw-protocol test client and the Blazor UI's live connection can't be open simultaneously.

**Character safety, again**: on reconnecting this round, the real "Chrissie" character (idle
since round-72 while this round's tooling was rebuilt) was at thirst 0.0 and hunger 5.9 - not as
severe as round-72's near-death find, but still fixed immediately via the live editor (HUNGER and
THIRST to 85, HEAD to 100) and confirmed via a fresh reconnect. `money: 1000` was still correct
(no repeat of the earlier autosave-race artifact).

## Round-72: merged players+world-saves sidebar, and a real pre-existing routing bug found and fixed (2026-09-02)

Continuation of round-71, same session. Two user requests: (1) offline players (not currently
connected) should still be editable, live wherever a live path exists, file otherwise; (2) the
live sidebar should also show world saves, not just connected players.

**Research first, honestly reported** (a dedicated fork agent, same rigor as round-71's player-
directory research): quest/story flags and containers have **no evidenced live UObject path** in
any installed mod - building that now would mean guessing, exactly what burned this session once
already (`GetMyPlayerController`). NPCs have a narrow real path (`FindAllOf("NPC_Base_ParentBP_C")`,
`.IsDead`/`.Faction`/`.Invincible`, `CommandsManager.lua:1408-1420`) but no health/position field,
and - a first for this session - genuinely need `IsHost()` gating (`CommandsManager.lua:1394-1399`
wraps the kill-all command in `CheckHasNoAuthority`). A promising but **unconfirmed** lead for
offline players specifically: `gameInstance:GetPlayerSave(uniquePlayerId, false)`
(`ObjectsGetter.lua:159-188`), never exercised with an actually-offline id by any real mod found.
Told the user plainly: no safe live path exists yet for world data, so this round's scope is the
file-based merge (safe, evidenced, buildable today), not a live world-editing feature.

**What was actually built**: the live sidebar (when a world folder is ALSO opened via the header's
existing OPEN FOLDER, live-connected or not) now shows the SAME merged save list the offline editor
already builds - every player (online or offline) and every world save. A player currently in
`players.list` gets a "LIVE" badge and clicking it switches the live view to them
(`LiveSessionService.PlayerSwitchRequested`, a pub/sub the sidebar uses to ask `LiveConnect.razor` -
which owns the actual vitals/skills sessions - to switch, since the sidebar has no access to those
itself). Everything else (an offline player, or any world save) opens in the ordinary file editor,
exactly as it always has - reusing `WorkspaceShell.OpenAsync` unchanged apart from one thing below.

**Found and fixed a real, separate, pre-existing regression while verifying this against the real
game**: selecting ANY save from the sidebar silently showed the "open a save folder" empty state
instead of the actual editor, ever since `ModeSelect` claimed the `/` route earlier this session
(round-64/`226a7ad`). `Home.razor`'s `IsBrowsing` flag compared the current path against
`"browse"` to distinguish "clicked Home" from "picked a save" - correct back when this page also
owned `/`, but with only one route left, that comparison is permanently true, so
`<SaveEditorSurface />` never rendered. Confirmed via `git show 226a7ad` before fixing, then fixed
with a `?home` query flag on the Home links instead of a path comparison, and verified live: the
same `WorldSave_MetaData.sav` click that showed the empty state before this fix now shows the real
37-chapter story editor with real save data (untouched by any of today's live changes - the file
editor's own long-standing behavior, just unblocked). This had been silently broken for every user
of the offline editor since earlier today, not just this round's own new work - worth flagging
loudly since nothing about round-71's live-only testing would ever have caught it.

**Two more real bugs from the same root cause, found and fixed alongside it**: `WorkspaceShell.
OpenAsync` and `MainLayout.OpenFolderAsync` both navigated to `"./"` after selecting a save/folder
from a non-editor page - also a leftover from when `/` meant the editor surface. Since ModeSelect
now owns `/`, `"./"` sent players to the mode chooser instead. Fixed to `NavigateTo("browse")`
(kept relative, no leading slash, so `SubpathNavigationTests` - the test guarding exactly this
class of regression for the published sub-folder browser build - still passes). `OpenFolderAsync`
additionally now stays on `/live` if that is where OPEN FOLDER was clicked from, instead of always
leaving it, since that is exactly how a live session picks up the merged sidebar.

**A second real near-miss with the live character, caught and fixed**: mid-round, a rebuild
required killing and restarting the desktop app process, and by the time testing resumed the real
"Chrissie" character (left alone in a cold area again) had reached hunger 26.6, **thirst 0.0, and
all six limb healths at 0** - effectively dead or about to be. Fixed immediately via the live
editor itself (HEAL ALL + setting hunger/thirst to 100 + APPLY), confirmed via a fresh reconnect
before continuing. Round-70's and round-71's own leftover `money: 1050` (from earlier autosave
races) was also caught and reverted to `1000` again in the same pass.

**Verified live, in order**: `players.list` still correct after the helper's command-allowlist fix
from round-71; merged sidebar renders (1 world-story save, 1 player with a LIVE badge, 20 world
region saves, 5 settings files - all real, from the real "Chrissie" folder); clicking the LIVE
player row stays on `/live`; clicking a world save navigates to `/browse` and shows the real story
editor (the `IsBrowsing` fix); the Home link still forces the world list even with a save selected
(`?home` query flag confirmed working); a full fresh reconnect confirmed every one of today's
edits (health, hunger, thirst, money) genuinely stuck on the real running game, not just in a
stale UI state.

## Round-71: connected-player list, host/client status, and correct live-mode shell labeling (2026-09-02)

Continuation of round-70, same session. Three user requests: (1) local play should never require
typing a host/port/token by hand - only a genuinely different computer (a dedicated server)
should; (2) live editing should show which players are actually connected, not silently assume
"one process = one player"; (3) the sidebar/header still showed leftover file-editing chrome
("SAVE FILES," "ACTIVE SAVE FOLDER," "Select a save to begin editing.") while live-connected,
which is exactly what round-70's own screenshots had already shown without it being called out.

**Auto-connect for a local game** (`ILiveEditingCapability.TryReadLocalToken()`, desktop-only):
reads the live-agent's token straight from `%LOCALAPPDATA%\AbioticEditorLiveAgent\token.txt` and
connects automatically on page load. Verified live: navigating straight to `/live` connected with
zero typing, showed real data immediately, and a "Connect to a different game instead" link still
reveals the manual form (pre-filled) for the dedicated-server case. Fixed one real test-suite
regression this caused: `Player_facing_copy_does_not_expose_application_architecture` flags the
literal word "server" anywhere in `.razor` markup outside the INI screen (a deliberate rule, per
that test's own comment) - a C# comment inside `LiveConnect.razor`'s `@code` block tripped it via
the test's naive `>...<` regex; reworded to avoid the word rather than weakening the test.

**Connected-player directory** (research-first, via a dedicated fork agent, before writing any
code - see that agent's findings): `UEHelpers.GetAllPlayerStates()` (confirmed real, backed by
`AGameStateBase.PlayerArray`, a base-engine field - works identically for a joined client, not
just the host) plus `HasAuthority()` (confirmed real, same call a published mod uses to decide if
a direct property write will stick). New `players.list` command, `LivePlayerDirectoryChannel`,
every vitals/skills handler now accepts an optional `playerId` to target a different connected
player. Real live result on this machine (singleplayer): one player, "Tribbes", `isHost: true`.
Research also showed vitals/skills (everything this editor currently writes) need NO authority
gating - the reference mod calls those exact kinds of writes unconditionally on any client, only
movement/physics properties are gated - so `isHost` is surfaced for transparency today, not to
block anything.

**One real bug found via live testing**: `players.list` returned `"unknown command"` at first -
the native helper (`AbioticEditorLiveAgentHelper/src/main.cpp`) only forwards a hardcoded command
allowlist to the Lua mod, and the new command was never added to it. Fixed (one line), rebuilt,
reverified live.

**Shell labeling** (`LiveSessionService`, new, registered on both hosts so `WorkspaceShell`'s
unconditional injection never fails to resolve, mirroring `SaveWorkspaceSessionService`): while
`/live` is connected, the header now reads "LIVE-EDITING A RUNNING GAME. / HOSTING" instead of
"ACTIVE SAVE FOLDER / NONE SELECTED", the sidebar shows "CONNECTED PLAYERS" (name + a YOU chip)
instead of "SAVE FILES" / "NO FOLDER LOADED", and the center status line drops the stray "Select a
save to begin editing." Verified live in both states (connected and disconnected) via a full
reconnect - the shell correctly falls back to ordinary file-mode chrome the instant `/live` is
left or disconnected, since the live connection's lifetime is tied to that page's component
lifetime already.

**A real, unrelated risk noticed and handled during this round's live verification**: the
"Chrissie" character had been sitting in a cold area for the entire testing session and its
health had dropped to genuinely dangerous levels (torso 3, both arms 0) by the time this round's
final check ran - not caused by any edit here, just real gameplay continuing in the background
while testing took a while. Used the editor's own HEAL ALL + APPLY to bring it back to full health
before finishing, and reverted an unrelated leftover `money: 1050` (from an earlier round's
autosave race, see round-70) back to `1000`. Both reconfirmed via one final fresh reconnect.

## Round-70: proved the actual desktop UI against the real game, not just the raw protocol (2026-09-02)

Continuation of round-69, same session, user asked to "go with option A [drive the desktop app
UI against the real game]" after being told round-69 only exercised the raw wire protocol, not
the UI a player would actually use. Also confirmed: the launch-time chooser ("Edit a save file"
vs "Live-edit a running game," `ModeSelect.razor`) already existed from round-64 and needed no
new work, just verification.

Ran the real `AbioticEditor.Web` build (`ABIOTIC_EDITOR_NO_DESKTOP=1`, the established headless
verification mode - same served Blazor content as the Photino window, just without the native
frame, so Playwright can drive it) against the real running game (the "Chrissie" save again) and
the real compiled helper:

- The mode-select screen showed both cards; clicking "Live-edit a running game" routed to `/live`.
- The connect form's host/port were correctly prefilled (`127.0.0.1`, `42117`); pasting the
  helper's real token and clicking CONNECT succeeded immediately and landed on the live editor
  surface with real values already populated - matching round-69's raw-protocol numbers exactly
  (hunger 94.3, sanity 100, money 1000, etc.).
- Edited CURRENT MONEY to 1050 through the UI and clicked APPLY: "Applied live - this took effect
  in the running game immediately." Disconnected and reconnected (forcing a completely fresh read,
  not a locally-cached UI value) - money read back as 1050, confirming the write reached the real
  game through the real UI code path, not just the raw protocol.
- Same for SKILLS: the tab showed all real skill XP values matching round-69's numbers exactly
  (Sprinting 51103, Strength 56335, ...). Edited Sprinting's XP to 60000 through the UI, applied,
  confirmed live.
- Reverted both edits back to their original values (money -> 1000, Sprinting XP -> 51102) through
  the same UI flow, confirmed via one final fresh reconnect, since this ran against a real save.

**One non-bug worth recording**: attempting a second, concurrent raw TCP connection while the app
was already connected hung until timeout. Read `Shared/LiveAgentServer.h`'s own doc comment - this
is intentional ("one connection at a time"), not a defect. Verification that needs to double-check
a live value while the UI holds the connection should disconnect/reconnect through the UI itself,
not open a second client.

**Net result**: the entire user-facing flow - the mode chooser, the connect form, both live tabs,
apply, and revert - is now proven against the real game, not just the underlying protocol.

## Round-69: live editing CONFIRMED WORKING against the real game, end to end (2026-09-02)

Continuation of round-68, same day, user asked to "test against actual game now." Launched the
game, loaded a real save with real progress ("Chrissie", 2h57m played, via the game's own main
menu - not a fresh/empty character), and exercised all six live commands for real.

**One real bug hit and fixed first**: `GetMyPlayerController()` failed immediately with `attempt
to call a nil value (global 'GetMyPlayerController')` - round-68's assumption that it was a bare
UE4SS global was wrong. It is CheatConsoleCommands' own locally-defined function
(`AFUtils/BaseUtils/BaseUtils.lua`), built on the real UE4SS-bundled global
`UEHelpers.GetPlayerController()` (`require("UEHelpers")`, from
`ue4ss/Mods/shared/UEHelpers/UEHelpers.lua`). Fixed `main.lua` to call
`UEHelpers.GetPlayerController()` directly (`.MyPlayerCharacter` was already right) and
redeployed.

**After the fix, everything worked, first try, on real data**:
- `ping`/`diag.findplayer` - baseline dispatch and player lookup both correct (`found: false` at
  the main menu before a world loaded, `found: true` once in gameplay).
- `vitals.get` - all twelve fields came back with real values, including `CurrentSanity: 100`
  (round-68's one unconfirmed guess - now confirmed correct).
- `vitals.set` - set money and head health; confirmed both via a follow-up `vitals.get` and
  visually (a screenshot showed the HUD's head-injury indicator clear after healing to 100).
- `skills.get` - real non-zero XP for all 15 file indices on a save with actual playtime,
  confirming every entry in round-68's `FileIndexToLiveSkillId` table actually resolves against
  the live game, not just that the code runs without error.
- `skills.set` - set Sprinting (file index 0) from `51102.9` to `60000` via the remove-then-add
  RPC pair; confirmed exact via a follow-up `skills.get`, with every other skill untouched.

All test edits were reverted back to their original values before closing the game, since this
ran against a real save rather than a disposable fixture. Full log in `live-agent/README.md`
("Confirmed working end to end against the real game").

**Net result**: the Lua+helper hybrid architecture (round-66) is now proven working end to end,
not just individually-verified-but-never-connected-to-a-real-game. Phase 0 (vitals) and Phase 1
(skills) of live editing are both real and working today.

## Round-68: rewrote main.lua around a real published mod's source (2026-09-02)

Continuation of round-67, same day. User pointed at
[Nexus mod 28](https://www.nexusmods.com/abioticfactor/mods/28) (Igromanru's
CheatConsoleCommands) - which turned out to already be installed in the test environment, so its
full Lua source was readable directly off disk. Read it instead of guessing at round-67's
`GetClass()` mystery further, and it explains that mystery: a real, working, ~800-line mod for
this exact game never calls `GetClass()`/`ForEachProperty` anywhere, gets the player through
`GetMyPlayerController().MyPlayerCharacter` (never `FindFirstOf`), and reads/writes most vitals
by direct dot-indexing with NO hash suffix at all (`myPlayer.CurrentHunger`,
`myPlayer.CurrentHealth_Head = 70.0`). The likely truth: `FindFirstOf("AbioticCharacterPlayerState")`
was probably returning the wrong kind of instance (a CDO or stale proxy), and `GetClass()` never
hung in general - it hung on THAT specific wrong object.

Also found, and genuinely surprising: skills are not a plain array on PlayerState at all, but a
key/value map (`CharacterSkills_Keys`/`CharacterSkills_Values`) on a
`CharacterProgressionComponent`, keyed by a `CharacterSkills` enum with its own numbering
completely unrelated to this repo's file-position order. Built and verified the mapping between
the two by matching skill names between `Core/Catalogs/Player/SkillCatalog.cs` (file order,
already tested against real fixtures) and the mod's `AFUtils/Enums.lua` (live enum) - the two
lists share no formula (index+1 etc.), had to be matched name-by-name. Setting XP is not a
property write either - it goes through `Server_RemoveAllXPFromSkill` + `Server_AddXPToSkill`
RPCs, the game's own validated progression system, confirmed exact from that mod's
`Skills.AddXp`/`RemoveXp`.

`main.lua` rewritten around all of this. Re-verified the same way as every round since round-66:
real Lua 5.4 interpreter, a fake environment now shaped like the confirmed real object graph
(`GetMyPlayerController` instead of `FindFirstOf`, direct fields instead of `GetClass` scanning),
and the full real-compiled-helper-plus-real-.NET-client pipeline - all passing, including the
corrected skill-id mapping and the remove-then-add RPC write pattern. Not yet re-tested against
the actual game (stopped deliberately, same reasoning as round-67: each live round carries real
cost/risk) - but confidence is much higher this time, since nearly every name and access pattern
is now copied verbatim from code proven to already work in this exact game, not guessed by
analogy from the save-file format.

## Round-67: real-game live testing - one real bug fixed, one still open (2026-09-02)

Continuation of round-66, same day, user asked to "run the game and test it." Did exactly that -
installed the Lua mod and helper into the real, currently-installed game and iterated against it
directly, not simulated. Full session log lives in `live-agent/README.md` ("Real-game debugging
session"); summary here.

**Launching the game itself needed troubleshooting first** (the user was away from the PC for
most of this round, so all of it had to be handled without anyone able to manually intervene on
screen). `steam://run/427410` initially got silently stuck behind a leftover "Set Launch Options"
dialog from earlier in the session; a graceful Steam client restart (`steam.exe -shutdown`, wait,
relaunch) cleared it and launches worked reliably afterward.

**Two real bugs found and fixed, one real bug found and still open:**
1. **Fixed - a genuine game-freezing bug**: the original design called `GetClass()`/
   `ForEachProperty` directly from `LoopAsync`'s own callback. That callback does not run on the
   game thread, and calling Unreal reflection APIs off it froze the *entire game* (not just this
   mod - every mod's logging stopped for 2+ minutes; had to force-kill). Root-caused precisely via
   isolated diagnostic commands (`diag.findplayer`, `diag.getclass`) added specifically to bisect
   which single call was responsible. Fixed by routing every game-touching call through
   `ExecuteInGameThread` (UE4SS's documented mechanism for exactly this), which required
   restructuring the whole dispatch flow from synchronous handler returns to an async
   `respond(result, err)` callback, since `ExecuteInGameThread` has no synchronous return.
2. **Fixed - a real performance bug, not a freeze**: `vitals.get` called `findPropertyNameByPrefix`
   once per field (12 full `ForEachProperty` scans for one request), consistently blowing the 5s
   round-trip budget. Fixed by scanning each object's properties once (`collectPropertyNames`) and
   reusing the list for every prefix lookup against it.
3. **Still open**: even after both fixes, with `FindFirstOf` confirmed correct and
   `ExecuteInGameThread` confirmed to dispatch to the game thread in ~5ms (via debug timestamps),
   `GetClass()` on the live player object still does not return within budget - without freezing
   anything else this time (`ping` kept answering throughout). Ruled out API misuse: found a real
   published UE4SS mod on GitHub (`Matraweber/PalWorkPriority`) using the identical
   `object:GetClass():ForEachProperty(...)` call successfully. Next candidate, not yet tried: call
   it from `RegisterHook` on an already-game-thread-bound function instead of
   `ExecuteInGameThread`, which is the pattern most working per-frame-reflection mods actually use.

Every finding came from genuinely isolating one variable at a time with purpose-built diagnostic
commands (`ping` -> `diag.findplayer` -> `diag.getclass` -> instrumented `collectPropertyNames`),
each re-verified against the real Lua interpreter (built from source this round - see round-66)
before being redeployed, rather than guessing at the live game repeatedly. Also verified along the
way: re-tested the Epic-GitHub-account-linking theory from round-66 with the user confirming their
account was linked - `Re-UE4SS/UEPseudo` still 404s, confirming that gate really is separate from
Epic's own org access, not a lingering propagation delay.

Stopped deliberately at this point rather than continuing to guess live: each restart cycle costs
real time and real risk (the game needed force-killing more than once), and the remaining question
needs either a different hook pattern or a native debugger, not more of the same kind of guess.

## Round-66: unblocked the live-agent with a Lua + native-helper hybrid (2026-09-02)

Continuation of round-65, same day, user asked "is there another way around it." There was.

**Confirmed the SDK-from-source block was real, then found the actual gap in it.** Epic account
GitHub-linking (round-65's "unlocker" theory) is real and documented, but re-tested this round: it
grants access to *Epic's own* repos, not automatically to `Re-UE4SS/UEPseudo` - a separate
third-party org's own private mirror, gated independently (confirmed by retrying the clone
authenticated after linking; still 404s). Inspecting the checked-out source at the pinned commit
confirmed why this is a hard stop for the pure-C++ approach specifically: `UE4SS/include/` has no
top-level `Unreal/` folder at all - every `Unreal::UObject`/`FProperty` type a mod's C++ would
touch lives entirely inside that gated submodule.

**But UE4SS's public Lua API doesn't need any of that.** `FindFirstOf`, `GetPropertyValue`/
`SetPropertyValue`, `ForEachProperty`, and `LoopAsync` are all documented, public, and usable with
zero build step. Only the TCP networking piece genuinely needs C++ - and pure Winsock networking
needs no UE4SS dependency at all. Result: a new hybrid design, approved by the user before
building it.

- **`live-agent/AbioticEditorLiveAgentHelper/`** (new, primary): a standalone native `.exe`, zero
  UE4SS dependency, that does the TCP networking and forwards every command to the Lua mod
  through a two-file mailbox in `%LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\` (atomic
  temp-file-then-rename on both sides), polled every 50ms via `LoopAsync`. Reuses the token
  generation fixed in round-65's security review (`BCryptGenRandom`).
- **`live-agent/AbioticEditorLiveAgentLua/`** (new, primary): the actual UE4SS Lua mod - does all
  the real property get/set work, with a hand-rolled `json.lua` (no bundled Lua JSON library
  assumed) and the same prefix-matching discipline as the file-format writers.
  `live-agent/AbioticEditorLiveAgent/` (the pure C++ mod) is kept as the secondary approach for
  if/when SDK access closes.
- **`live-agent/Shared/`** (new): `JsonLine.h`/`LiveAgentServer.{h,cpp}` moved here so both the
  primary helper and the secondary C++ mod use the identical, already-verified transport code
  instead of duplicating it.

**Verification went further than round-64/65's, because more of this could actually be tested
without the game.** Built a real Lua 5.4.7 interpreter from lua.org source with the same MSVC
toolchain (no local Lua was installed) and used it for real: `json.lua` round-trip tested (caught
and fixed a real bug - `isArray`'s heuristic miscounted a decoded array's own `__forceArray`
marker key), then the actual unmodified `main.lua` driven under a fake-but-shaped UE4SS
environment (stub `FindFirstOf`/`LoopAsync`, a fake player-state object matching UE4SS's
documented property-access shape) - every command's dispatch, property-prefix matching, and
file-mailbox read/write exercised for real. Then the **full pipeline together**: real .NET
`TcpLiveGameChannel` to real compiled `AbioticEditorLiveAgentHelper.exe` to the real file mailbox
to the real Lua interpreter running the real `main.lua` against the fake player state - vitals and
skills reads, writes, and re-reads-after-write (to confirm a write actually stuck, not just "no
error") all passed. The only thing still outside reach is the real UE4SS Lua runtime and a real
running game; `live-agent/README.md` is explicit about exactly that boundary.

## Round-65: live editing, Phase 1 (skills) + a real SDK-from-source attempt (2026-09-02)

Continuation of round-64, same day. Two threads, both user-directed ("both, SDK attempt first"):

**Tried to build the real UE4SS C++ Mod SDK from source, hit a genuine access-control wall.**
Cloned `UE4SS-RE/RE-UE4SS` and checked out the installed game's exact commit (`01e0a584` -
confirmed present, not a typo). Its `deps/first/Unreal` submodule
(`git@github.com:Re-UE4SS/UEPseudo.git`) 404s even over HTTPS - a private, presumably
Epic-access-gated dependency this project has no credentials for. The public `v3.0.1` tag's own
release SDK is a *different* commit (`d935b5b`) and upstream's own release notes say "C++ mods
must be rebuilt to work on 3.0.1," i.e. it is documented as ABI-incompatible with our target
build - not a usable substitute. This is a real, external blocker (credentials this project does
not and should not try to obtain around), not a shortcut that was merely skipped; `live-agent/README.md` records it for whoever picks this up next.

**Expanded live editing to skills (`skills.get`/`skills.set`)**, the same shape as vitals:
- `Core/LiveEditing/Player/LivePlayerSkillsChannel.cs` mirrors `PlayerSaveReader.ReadSkills`/
  `PlayerSaveWriter.ApplySkills`, working over the same positional `PlayerSkill` list.
- New `IPlayerSkillsSession` interface, extracted from `PlayerSaveSession`'s existing skills slice
  (exactly the members `PlayerSkillsTab.razor` uses: `Skills`, `MarkChanged()`, `MaxAllSkills()`,
  `IsDirty`/`Status`/`SaveAsync`/`Revert`) - `PlayerSaveSession` now implements it too, and
  `PlayerSkillsTab`'s `Session` parameter is retyped from the concrete class to this interface.
  This is the second data point (after `IPlayerVitalsSession`) for the "introduce a narrow
  interface per widget, incrementally" reuse pattern the round-64 exploration predicted - it held
  up exactly as expected, and the widget needed literally zero other changes.
- `live-agent/AbioticEditorLiveAgent/src/SkillsCommands.{h,cpp}` (new, same unverified-pending-SDK
  status as `VitalsCommands.cpp`), registered in `Mod.cpp` alongside vitals.
- **`JsonLine.h` gained real JSON array support** (`JsonArray`, `AsArray()`, array parsing/writing)
  - the protocol doc's original "no protocol-level change needed" claim for a new area was wrong
    for an array-shaped one; skills' per-skill rows need an actual array, not vitals' flat object,
    so this was a real (small, contained) gap the skills slice exposed and closed, not a
    hypothetical. `docs/reference/live-editing-protocol.md` corrected to say so.
- **Verified the same way as round-64's vitals slice, and re-verified vitals too**: recompiled the
  standalone smoke test (now asserting a real `"result":[{...}]` array shape), ran a real
  cross-language check (compiled C++ server &lt;-&gt; real `LivePlayerSkillsChannel`), added
  `LivePlayerSkillsChannel`-specific xUnit tests (1145 total, all green), and drove the actual
  desktop app UI via Playwright against the real compiled C++ agent end to end: connected, switched
  to the new SKILLS tab, saw three real skills (names/icons/milestones from the real
  `SkillCatalog`, not placeholders), clicked MAX ALL, saw milestone perks unlock and reveal their
  real text, clicked Apply, got "Applied live." The vitals tab and its own Apply/Revert were
  re-checked in the same pass and are unaffected.

## Round-64: live in-game editing, Phase 0 (2026-09-02)

First slice of a new capability alongside the existing offline file editor: editing a **running**
game's memory in real time instead of a `.sav` file. Scoped deliberately small (player vitals
only) to prove the whole pipeline end to end before expanding feature-by-feature the same way the
file editor itself grew over many rounds; see the approved plan this round worked from for the
full architecture and the Phase 1+ roadmap it intentionally deferred (inventory, skills, quest
flags, world state, pets, vehicles, ...).

**Architecture**: a new `Core/LiveEditing/` layer (`ILiveGameChannel` + `TcpLiveGameChannel`, a
newline-delimited-JSON TCP protocol - see `docs/reference/live-editing-protocol.md`) parallels
`Serialization/` the way a live reader/writer pair parallels a file reader/writer pair, but both
sides produce/consume the exact same Domain records (`CharacterStats`, `LimbHealth`) the file
writer already uses - confirmed this round that Domain and Catalogs needed zero changes to be
reusable for a live backing, only Serialization needed a parallel (not shared) implementation. A
new `LivePlayerVitalsSession` implements the same narrow `IPlayerVitalsSession` interface
`PlayerSaveSession` already implements, so the existing `PlayerVitalsTab` widget binds to it with
**zero changes** - the exact reuse pattern the plan bet on. A new `ModeSelect.razor` landing
screen ("what do you want to do?") replaces `Home.razor` at `/` (which moved to `/browse`, already
its second route); an `ILiveEditingCapability` marker service the desktop host registers and the
WASM host does not is the entire mechanism keeping live editing out of the browser build, with no
`#if`/conditional-compile split anywhere in the shared screens.

**A load-bearing assumption in the original plan turned out wrong, caught before writing the mod**:
UE4SS's Lua environment has no networking module at all (confirmed against its own docs), so a
pure-Lua TCP mod - what was originally planned - is not buildable. Switched to a C++ UE4SS mod
per the user's choice among three options presented (the alternatives: local-only file-polling
IPC, or a bundled native helper process a Lua mod launches over stdio).

**What is genuinely verified, end to end, this round** (not just "compiles"): the real desktop
app's Blazor UI, connected through the real `TcpLiveGameChannel`/`LivePlayerVitalsSession`
classes, to a real MSVC-compiled instance of the mod's transport+JSON layer
(`live-agent/AbioticEditorLiveAgent/src/LiveAgentServer.cpp` + `JsonLine.h`) - connect, the
`hello` token handshake (both accepted and rejected), reading live-shaped vitals data into the
UI, editing a value, clicking Apply, and seeing "Applied live" - all actually happened, driven
through Playwright against the running app, not simulated. `tests/TcpLiveGameChannelTests.cs`
covers the same protocol against an in-process fake agent for CI (no native build needed there).

**What is NOT verified**: `VitalsCommands.cpp`/`Mod.cpp`, the part that reads/writes real live
UObjects, because that needs UE4SS's actual C++ Mod SDK (matched to the installed build, `UE4SS
v3.0.1 Beta, git SHA 01e0a584`), which was not available this round - no vendored copy, and no
reliable way to fetch and build one in-session. The property names it guesses (`Hunger_`,
`Thirst_`, ...) are reasonable by analogy with the save-file property names but **unconfirmed
against a real live property dump** - `live-agent/README.md` spells out exactly what is and is
not trustworthy here and the steps to close the gap (get the matching SDK, dump real property
names, build, verify against the running game).

Also solved along the way: launching the actual game from tooling turned out unreliable (the bare
exe exits immediately - Steam DRM wrapper; `steam://run/427410` can leave Steam stuck on an
unattended "Set Launch Options" popup no scripted `SendKeys`/`AppActivate` attempt could dismiss),
so the cross-language interop proof above used a standalone compiled instance of the mod's
transport layer instead of the real game - which is exactly why that layer was split out to have
zero UE4SS/game dependency in the first place.

## Round-63: CUE4Parse bump + live-game content gap scoping (2026-09-02)

**CUE4Parse submodule bumped** from `1125f5bc` (2026-06-09) to `b4e95441` (2026-08-31), 494
commits; `submodules/UeSaveGame` was already current and untouched. One API break: the usmap
type-mappings provider moved namespace, fixed at its two call sites. Package mirror
(`submodules/CUE4Parse.PackageVersions.props`) updated to match (Blake3 to 3.0.2, SharpGLTF now a
stable 1.0.6 release instead of the alpha CUE4Parse used to pin, and a new `System.Numerics.Tensors`
dependency added). Full suite green: 1139/1139. Committed as `18b7ecf`.

**Usmap refresh attempted, not completed.** The installed game (Steam, real local install) is at
engine build `5.4.4-1040001+++DF+ABF` per the player's own `AbioticFactor.log` from a real
2026-09-01 session, ahead of the `5.4.4-1030002+++DF+ABF-01e0a584` this editor is validated
against (`SaveVersionRegistry.ValidatedGameBuild`). UE4SS is already installed in the game folder
and is confirmed to be how the bundled `assets/Mappings.usmap` was originally produced (a
byte-identical copy, `AbioticFactor-5.4.4-1030002...usmap`, sits directly in the game's `ue4ss/`
folder, dated the same 2026-05-19). **It does not regenerate automatically on every launch** - the
player's real 2026-09-01 session at build 1040001 did not produce a new file - so refreshing it
needs a deliberate maintainer step (a console command or mod trigger) not yet identified/documented
here. Launching the game from tooling was also unreliable: the bare exe exits immediately (code 1,
Steam DRM wrapper), and `steam://run/427410` can leave Steam stuck on an unattended "Set Launch
Options" popup that scripted `SendKeys`/`AppActivate` could not dismiss. Follow-up: find and
document the actual UE4SS usmap-dump trigger, then redo this.

**Missing-feature scoping for the live game's Cosmic Companions (2026-05-04) and Community
Update #4 / Anniversary Update (2026-05-13) content**, from wiki/patch-note research
cross-referenced against `Core`: grepping for Chemistry/Tincture/Coating/Flask/Distill/Companion
across `Core` returns zero matches, so none of that is implemented. Believed already covered by
existing generic mechanisms, but **not verified against a real fixture**: pet mutation into new
species (the existing pet-upgrade feature already rewrites `NPCClass_`) and the new portal world
(`PortalMapFeature` is generic/tag-based, not hardcoded per portal). Genuine gaps, effort estimated
by analogy to how the pet/vehicle systems were originally built (Domain + Catalog + writer + UI +
CLI each):
- Companion pet equipment slot (new dedicated slot) - **S**, one `FullNames` entry once a real tag
  name is known.
- Pet "downed"/stabilization state (`PetHealth`/`WorldPet` have no such flag today) - **M**.
- Chemistry system (Distillation/Chemistry Benches, Tinctures, Coatings, Flasks) - **L**, real
  uncertainty until a save with an in-progress brew is inspected (could be M if brews are stateless
  items).

**Deliberately not started.** This repo's byte-exact write discipline (writers must create a
missing tag using its exact hash-suffixed name, see the root CLAUDE.md) needs a real save fixture
exercising each system before any writer code is written - guessing a tag name risks silently
writing to the wrong property in a player's save. No such fixture was available this round: the
player's own dev/test world saves under `Saved/SaveGames/` predate this content. Parked until a
fixture with this content turns up.

## Round-62: investigated Nexus bug #1 - "stuck radiation-suit visor after editing traits/skills" (2026-08-27)

Player report (mod page bug tracker): adding `Trait_FannyPack` to an existing character, and
separately maxing a skill's XP, both left a stuck curved black-bar "helmet vision" overlay
(~1/5 of the screen top and bottom) after reloading - not tied to actually wearing a hazmat suit,
since the reporter confirmed the suit was unequipped before editing/saving.

Audited the write path for both edits (`PlayerSaveWriter.ApplySkills`, `.ApplyTraits`, and the
shared `GvasTags.FindOrCreate` create-on-miss helper both go through). No corruption found:
`FPropertyTag.Size` is always back-patched from the real serialized length at write time
(`WriteSize` seeks to a placeholder and rewrites it), so a freshly created tag can't leave a stale
size header regardless of whether the source property existed before. Confirmed empirically too,
not just by reading the serializer: `SaveReaderWriterValidationTests` gained
`Player_EditUntouchedSkillXp_CreatesTagAndChangesOnlyThatLeaf` (a skill whose XP/multiplier tags
were still delta-omitted - the exact "max a skill" repro, since the existing isolation test only
ever touched an already-present tag) and `Player_AddTrait_ChangesOnlyThatLeaf` (the fanny-pack
repro) - both against real fixture saves, both show exactly one leaf changed and nothing else in
the file moved. Ruled out: no `Hazmat`/`IsWearing`/visor-related boolean exists anywhere in the
schema for the writer to have mishandled; the effect is driven purely by the Suit equipment slot,
which neither edit touches.

Conclusion: this is very likely the base game's own known "helmet vision" desync (Steam
discussions describe the same hazmat-suit visor effect getting stuck after a reload/session-state
change, unrelated to any save editor, fixed only by a session restart in at least one report) - not
something the save file format or this editor's writer can corrupt into existing. No code fix
applied; the two new tests stand as permanent regression coverage for the create-on-miss path,
which had a real gap (only the already-present-tag case was isolation-tested before).

Closes follow-up 5 of `docs/reference/research/research-gamepass-to-steam.md`. A claim is
`<ownerId>}|!|{<name>` in a deployable's `CustomTextDisplay_`; `WorldSteamIdPatcher` could only
swap ids of equal length (SteamID64 -> SteamID64) and **threw** otherwise, which is every Game Pass
case (Xbox 16 digits vs SteamID64 17).

- `WorldSteamIdPatcher` now picks its route from the id lengths. Same length keeps the byte
  replacement (byte-identical everywhere else, still ASCII + UTF-16LE). Different length goes
  through `WorldSaveWriter.RewriteDeployableClaims` + a full re-serialize, which recomputes the
  FString length prefixes. New `PatchBytes` overload for in-memory callers. A world with no
  matching claim is never written (no `.bak`, no re-serialize); re-homing to the same id is a
  no-op. Writes go through `SaveBackup.WriteWithBackup`.
- `RewriteDeployableClaims` matches only text that **starts with** `<oldId>}|!|{`, because the game
  reuses the same separator as the line break in sign text (`"Stolas}|!|{castle"` is a two-line
  sign, not a claim). Everything from the separator on is carried over verbatim, so names keep the
  private-use glyphs `ParseClaim` strips for display.
- Wired into all four places an owner id changes: `GamePassConverter` both directions (old id read
  off the save being converted, not asked of the caller), `GamePassSaveSet.RenamePlayerToAccount`
  (in the same repack), the CLI `steamid` command, and the app's
  `ChangeSelectedPlayerIdentifierAsync` - which re-homed the player file only, so the confirm
  dialog's "any beds they claimed move across too" had been untrue since it was written. The
  contradicting `PlayerGeneral_ChangeSteamIdHelp` string was corrected in all five languages.
- **Where claims actually live (measured, not assumed):** a tree walk of the reference dump and
  every fixture found the separator only in `DeployedObjectMap[*].CustomTextDisplay_` (claims) and
  `.PlayerMadeString_` (sign text line breaks); tree-walk occurrence counts equal the raw
  ASCII+UTF-16 byte counts in every file, so nothing hides outside the walk.
- **Other owner-id references, deliberately left alone**: `CookingData_.ChefID_` on cooked-food item
  proxies (who cooked it), item `PlayerMadeString_` name tags a player typed an id into, and the
  metadata save's `ServerEntitlements` / `UserEntitlements` maps, which are **keyed** by owner id.
  Entitlements are account purchases, so following a character to a new account is not obviously
  right, and a key rewrite could collide with an id already in the map.

### Shared worlds can be converted at all now (same round)

Closes the other half of the same report. `GuardSingleRehome` refused any re-home on a world with
several characters, so the reporter's nine-character `ForScience` could not be moved: the Steam game
found no save for their account and offered character creation on top of a 200-hour world. The
reasoning ("one id can't own several characters") was true but led to the wrong conclusion - the
player only ever wanted **their own** character re-homed, never their friends'.

- Both converters now take `sourcePlayerId` naming which character moves; the rest are carried over
  untouched. Only the ambiguity is refused, and the message **lists the candidate ids**, which is the
  one thing a player cannot look up anywhere else. A collision (moving a character onto an id another
  character in the same world already has) is refused too, since the game would load one and leave
  the other unreachable.
- Fixed alongside: `SteamWorldToGamePass` re-homed **every** character save it saw, which on a shared
  world would have packed all nine over each other under one name. And `GamePassToSteamWorld`
  resolved the re-home *after* extracting, so a refusal still left a full copy of the world in the
  destination, which then tripped the empty-destination check on the next attempt.
- Surfaced as CLI `--from` on both `gamepass to-steam` / `to-gamepass`, and in the app as a
  "which character is yours?" step mirroring the existing "which world?" one
  (`Settings_ConvertPickCharacter`, five languages).
- Verified on the real dump: all nine characters convert, the named one becomes the target SteamID64
  with its bed claim, the other eight and their claims are untouched.

### CI: a skip was being reported as a failure

`GamePassRenamePlayerTests` used plain `[Fact]` with a `Skip.IfNot` helper, so on any machine without
Oodle (i.e. CI) the skip surfaced as a red build. Now `[SkippableFact]`, matching every other
Oodle-dependent class; a sweep confirmed it was the only file with the mistake.

The reason it went red *intermittently* was separate: `OodleCodec` re-ran the one-off library
**download** on every availability probe, inside its lock, with no memory of having failed. Two
probes seconds apart could therefore disagree - one caught a transient failure, the next caught a
working retry - which is exactly the "one test of six failed" pattern. A failed download is now
remembered for the run. Only the network step is; the cheap local lookups still run every time, so
setting `ABIOTIC_OODLE_DLL` or installing the game mid-session is still picked up.

## Round-60: Game Pass end-to-end audit and repair (2026-08-13)

Full review of the Xbox/Game Pass path (Core, CLI, Web, tests, docs). The format engineering was
sound; almost everything that made Game Pass "finicky" was either **safety UX lost in the MAUI ->
Blazor port** or a **data-loss window in the save/convert paths**. Verified throughout against the
real 70-member wgs dump, not just the sanitized fixture.

**The port had dropped every Game Pass dialog.** `Main_Gp*` existed in all five languages and was
referenced by nothing: no cloud-sync warning, no mid-sync repair offer, no write-failure recovery.
Restored via a new `GamePassSafetyGuard` (Web.Shared), hooked into `Home.SelectWorldAsync` - the one
funnel every route into a world already passes through. Warning shows once per run; the repair
dialog now actually calls `RepairMidSync`, which was CLI-only before.

**Save-path data loss (the "my edit vanished" class):**
- `SaveSelectedAsync` cleared the dirty flag, then packed. A failed pack left the edit *only* in a
  temp working copy that the next open / dispose / startup sweep deleted. Now the copy is marked
  (in memory **and** with an on-disk `.unwritten-edits` marker, because the sweep runs in a later
  process), the failure reaches the caller, and the shell shows a dialog naming the folder. Cleared
  on a successful retry.
- `ApplyWorld` silently no-opped when no member matched (`changed == 0` -> no repack, no error):
  SAVE looked successful and wrote nothing. Now throws.
- Player-id change was a two-phase write that repacked the real container *outside* SAVE. Split
  into `StagePlayerRename` (in-memory, rides the next SAVE) + `RenamePlayerSave`/
  `RenamePlayerToAccount` (immediate, for the CLI). Staging happens first because it is the step
  that can refuse; the file rewrite is unwound if it fails, so the two can never disagree.
- `containers.index` was rewritten with `File.WriteAllBytes` - a crash mid-write loses every
  container at once. Now temp-file + atomic replace (`WriteFileAtomic`), manifests too.

**Container-store correctness:**
- `FindFallbackBlob` accepted a sole candidate with **no size check**, and `RepairMidSync` then made
  that guess permanent. Now a recorded size must match.
- `WriteBlob` deliberately kept old generations, which is exactly what made that fallback ambiguous.
  Now prunes to one manifest + one blob per folder, matching what the game itself leaves.
- `WriteNewContainer` would happily overwrite a real store's index, orphaning every other world in
  it. Now refuses; `AddOrReplaceContainer` is the merge path (CLI `to-gamepass --into`).
- Truncated-header case no longer silently skips the recency stamp (that stamp is the only thing
  stopping cloud sync from rolling an edit back), and backups are capped at 8.

**Conversion:** `SandboxSettings.ini` was dropped in both directions, so every converted world
silently reset to default difficulty. Now carried (flag=1 member, stored as plaintext with every
byte **decremented by one** - confirmed against the real dump). Unreadable saves now abort instead
of vanishing from the output with an Info log. Destinations are guarded and never clobber. Non-ASCII
world names work (UE writes those FStrings as UTF-16 with a negative length; the reader was
ASCII-only). Web convert card shows the real refusal text and offers a world picker.

**Oodle on Linux:** CUE4Parse downloads `liboodle-data-shared.so`, but the cache lookup only listed
the Windows DLL names - so the "downloaded once, cached forever" promise never held on Linux and an
offline machine could not open a Game Pass save at all. Fixed, and the misleading "cannot be read on
this system" message replaced.

**Tests: 966 -> 988.** xUnit v2 has no dynamic skip, so ~14 Game Pass tests reported **passed** while
asserting nothing whenever Oodle was unavailable (i.e. plausibly all of CI). Added
`Xunit.SkippableFact` (test-only) and converted every gate; verified a skip now reports `[SKIP]`.
New `GamePassSafetyTests` covers the previously untested paths that touch real Xbox data:
missing-blob recovery + the wrong-size refusal, `RepairRecoveredManifests`, merge, generation
pruning, monotonic index stamps, the `ApplyWorld` no-op refusal, ini round-trip and byte shift,
`ToMemberBody` for **all three** save classes (the 33-vs-8 custom-header constant had no direct
test), and hostile member paths staying inside the working folder.

Also: CLI refusals were surfacing as stack traces (`InvalidOperationException` was not in the
user-error bucket); a repo test correctly caught raw `exception.Message` reaching player copy, so
that judgement now lives in `UserFacingErrorService.Detail`.

Still unverifiable from here: whether Xbox accepts a rewritten container in-game. That needs a real
console/PC sync cycle - `gamepass snapshot` / `compare` exist for exactly that.

## Round-59: browser-editor field report, worked through (2026-08-09)

A list of things a player hit in the published browser build. Most turned out to be one of three
shapes, which is the useful part to carry forward.

### Shape 1: code reaching for a file system the browser does not have
`System.IO` in shared code is invisible on the desktop and dead in a tab.

- **Raw JSON import** wrote the converted save straight to a disk path
  (`SaveBackup.WriteWithBackup`), failing with `DirectoryNotFound /Cascade/...sav.tmp`. Split the
  conversion (`SaveJsonBridge.ReadJsonAsSaveBytes`) from the write, which now goes through
  `ISaveFileSystem`. Both raw tabs also **reload the save afterwards** - without that the session
  still held the pre-import parse and the next SAVE would have written it straight back over.
- **The spawn screen's region list** used `WorldLevelIndex.ScanFolder`, which enumerates a folder
  and streams each file from the front. `LevelGUID` sits near the **end** of a save (242 bytes
  from the end in a small region; 3.4 MB from the end of the 16 MB facility save), so
  `ISaveFileSystem` gained `ReadTailAsync` and `WorldLevelIndexService` reads tails at growing
  sizes. Listing a 68 MB world's regions costs a few hundred KB.
- **Settings** (language, theme, game-data language) were one-line files under
  `LocalApplicationData`. A WebAssembly file system is in memory and dies with the tab - and
  choosing a language *reloads the page*, so the setting was gone before it could be read back.
  `HostPreferenceStore` (and a matching hook on `GameDataLanguageStore`) is a seam the browser
  fills with localStorage.

### Shape 2: delta serialization, again
`AddPet` refused any world whose `PetNPC` map was absent - which is **every region save but the
facility one**, because the game omits a map it has never written. Staging succeeded and SAVE
then failed with "Could not place pet". The map is now created by copying `NarrativeNPCMap`'s own
tag (same NPC-state struct, so the key/value type names are right). One caveat is now documented
and tested rather than hidden: a pet cloned from a story NPC **loses its XP**, because experience
lives in a dynamic-property list story NPCs do not carry, and `PetTransfer` no longer claims a
level it did not write.

### Shape 3: a case-insensitive file system hiding a case-sensitive one
Item icons are dumped as `<item id>.png`, but the id is spelled differently in different places -
a save says `Bandage`, the data-table row is `bandage`. Windows and the desktop host do not care;
GitHub Pages does, so those items 404ed and drew "?". All 1,622 shipped icons are now lower-case
and so is the URL. `File.Exists` cannot guard this (case-insensitive on Windows), so the test
compares names ordinally.

### Also in this round
- **Zips as an input** (`SaveBundle` + `ISaveBundleReader`): the other end of EXPORT, by picker or
  drop, and the only way in at all on Firefox/Safari. Handles both layouts - saves at the top of
  the zip, or under a folder named after the world.
- **Recent worlds** (`IRecentWorldStore`): FileSystemDirectoryHandles kept in IndexedDB. The
  permission is deliberately dropped when the tab closes, so reopening asks again and must hang
  off a click.
- **Unsaved-work guards**: `UnsavedChangesGuard` before anything replaces the open session, plus a
  `beforeunload` handler armed only while edits are staged.
- Host-only notices hidden in the browser (door positions, "no character look on this computer"),
  toasts given real contrast in the blue theme, and the container grid's scrollbar fight fixed
  (`overflow-x:hidden` + `scrollbar-gutter:stable`).
- A **churn bug** in the spawn tab: `ReferenceEquals(_bedSource, WorldSession)` was false on every
  pass with no world open, so the 16 MB facility read restarted on every render and the bed list
  never settled. It looked exactly like "the picker is empty".

Verified in a real browser (drop a zip, open the save, switch language to German, reload and see
it stick). 966 tests green.

## Round-58: the browser download, halved (2026-08-08)

Round 57 concluded the payload could not be cut without multi-targeting Core. That was the wrong
problem. The lever was never *which assemblies get referenced* - it was **how hard the trimmer is
allowed to work on them**.

### Correcting round 56
Round 56 recorded "No trimming is configured anywhere (`PublishTrimmed` is unset)". Unset does not
mean off: the Blazor SDK defaults `PublishTrimmed=true` for a Release publish. What it also
defaults is **`TrimMode=partial`**, which only trims assemblies whose authors marked them
trimmable. None of our dependencies are marked, so the pak-reading and scripting libraries were
shipping **whole** through a trimming publish. Measured proof: BouncyCastle publishes at 4,912,409
bytes against an original of 4,924,576.

`<TrimMode>full</TrimMode>` on the browser project is the entire fix.

| | raw | brotli | requests |
| --- | --- | --- | --- |
| Before (partial) | 33.9 MB | 10.7 MB | 133 |
| After (full + roots) | **22.9 MB** | **7.0 MB** | **94** |

Actually transferred on a cold load, measured in the browser: **31.0 MB -> 19.9 MB**. Jint,
CUE4Parse-Conversion, Fmod5Sharp, OggVorbisEncoder, SkiaSharp and ImageSharp are gone entirely;
BouncyCastle drops 4.9 MB -> 1.1 MB, CUE4Parse 2.9 MB -> 1.3 MB.

### Full trim on its own ships a broken editor
Unrooted, full trim reaches **17.1 MB / 5.2 MB** - and **deletes the save engine**. UeSaveGame and
UeSaveGame.Json were dropped outright and Core fell from 1007 KB to 268 KB, because save parsing
reads a property's type name out of the file and builds that type by name, so a trimmer sees
almost none of it as reachable. Rooting the five reflection-driven assemblies costs about 5.8 MB
of the saving and buys back the one thing that must never break.

The trimmer emitted **zero IL2xxx warnings** in either case. Silence is not evidence: those
warnings are only produced for assemblies marked trimmable, which is the same reason partial mode
skipped them. Do not read a clean build as a safe one here.

Rooting the browser project itself was needed too, and was found only by clicking: the little
records handed back from JavaScript are built by the JSON reader, so their constructors were
trimmed and **the very first action, OPEN FOLDER, failed** with `DeserializeNoConstructor`.

### Verified by round-tripping real saves through the trimmed build
Driven in a browser against a published trimmed build, with the CLI as an independent reader:

| Check | Result |
| --- | --- |
| Player save edit (money 116 -> 4242) | CLI reads the written file back: `Money: 4242`, SteamID/PhD/15 skills/467 recipes intact |
| Backup written | `.bak` **byte-identical** to the original fixture |
| Trimmed vs untrimmed output | **Byte-identical** (same SHA-256) for the same edit - trimming changes nothing about what gets written |
| 14.2 MB region save | Opens in 1.3 s, all 16 tabs; saves in 2.2 s; CLI reports **"no gameplay differences"** beyond the intended world-day edit |
| Item names, icons, recipes | Resolve normally ("Hyperion Helm", "Nuke-Vac"); registry and bundled art unaffected |

The byte-identical result between trimmed and untrimmed builds is the load-bearing one: it also
proved the `ItemsPickedUp` array re-sorting seen in the diff is the **pre-existing** save-path
behaviour, not something trimming introduced.

`tests/AbioticEditor.Tests/BrowserTrimmingTests.cs` pins the trim mode and all five roots, because
removing one produces no build error - just an editor that ships, starts, and then cannot open a
save.

### Every save kind re-checked on the trimmed build
Asked to confirm the editor still works, not just start. Each kind edited in a browser against the
published trimmed build and read back with the CLI:

| Save kind | Result |
| --- | --- |
| Player | Money 116 -> 4242; byte-identical to the untrimmed build's output |
| World region (14.2 MB) | Opens 1.3 s, saves 2.2 s, "no gameplay differences" beyond the edit |
| World metadata / story | Minutes played 40127 -> 55555; **exactly one difference**, chapter (DarkLens) intact |
| Character look | `Head_M01a` -> `Head_F01a`; valid GVAS, old value gone, `.bak` written |
| Settings (.ini) | **Did not work** - see below |

Every one wrote a `.bak` first, byte-identical to the original.

### The settings editor was broken in the browser, and is now honest about it
`/ini` had **no browser gate at all**. It rendered its folder picker as normal, and FIND SETTINGS
FILES then threw on an empty path, surfacing as "Settings files could not be checked" - which
reads as a broken editor rather than something a browser cannot do. The whole screen is path-based
(a typed folder, `File.FullPath`, `AbioticIniCatalog.Discover(path)`), so a tab holding only
granted handles can never drive it. It now shows the same "needs the desktop editor" panel as
compare and create-world.

Unrelated to trimming: the failure is an empty-string argument, not a missing type.

This is the **second** time this exact bug shape has shipped (round 55 found `/create-world` had
its link hidden but its page reachable). Hiding a link never helps - the route survives typing it,
a bookmark, or a refresh. `DesktopOnlyPageGateTests` now asserts all three pages carry the gate
and the shared wording.

### Answered on the first real deploy: GitHub Pages DOES compress wasm
The round-58 unknown below is settled. Measured against the live site:

| | |
| --- | --- |
| Decoded | 19.9 MB |
| **Actually transferred** | **7.7 MB** |
| Requests | 94 |
| `.wasm` files compressed by Pages | **89 of 89** |

So the worry that players might pull the full raw payload was unfounded - Pages gzips every
assembly. The trimming work therefore lands as roughly **7.7 MB downloaded instead of ~12 MB**.

Confirmed live it is the trimmed build: BouncyCastle 1.14 MB (was 4.9), CUE4Parse 1.28 MB
(was 2.9). Jint still ships at 2.2 MB, as expected - rooting Core keeps the plugin entry point
reachable, which is the known cost recorded below.

### Still open
- ~~Whether GitHub Pages serves `.wasm` compressed~~ - answered above.
- The remaining CUE4Parse (1.3 MB) and BouncyCastle (1.1 MB) are only still there because Core is
  rooted, which keeps the pak-mounting entry points reachable. A linker feature switch
  (`FeatureSwitchDefinition` + `RuntimeHostConfigurationOption`) could fold those branches away as
  constants without any multi-targeting. Not attempted.

## Round-57: build gates for the browser payload - reviewed, prototyped, NOT landed (2026-08-08)

Round 56 measured ~10 MB of assemblies the browser downloads and can never run: CUE4Parse and
BouncyCastle (pak mounting, impossible in a tab per round 45) and Jint (the script-plugin engine,
not registered on that host). This round tried to gate them out at build time. **The obvious
mechanism does not work. Nothing was landed.** Read this before trying again.

### What was tried
`AbioticNoScripting` as a property: conditional `<PackageReference Include="Jint">`, a
`<Compile Remove="Plugins\Scripting\**">`, an `ABIOTIC_NO_SCRIPTING` constant guarding the one
call site (`PluginManager.CreatePlugin`), and `AdditionalProperties="AbioticNoScripting=true"` on
the browser app's project references to Core AND Web.Shared (both, since Core is reached two ways
and a global property flows down the whole graph).

Jint was chosen first deliberately: it touches **2 files** and **1 call site**, so if the mechanism
worked anywhere it would work there.

### Why it does not work
The compile half worked - Core genuinely rebuilt without the scripting sources. **The package half
did not: `Jint.wasm` still shipped after a clean build.**

`PackageReference` conditions are evaluated at **restore**, and restore writes ONE
`obj/project.assets.json` per project. It does not know about build-time global properties.
Proved directly: `dotnet restore src/AbioticEditor.Core -p:AbioticNoScripting=true` dropped the
Jint references in the assets file, while a normal `dotnet build` of the browser app left them.

So the result depends on whichever restore ran last - the desktop build and the browser build
would fight over one shared assets file. That is worse than no gate: it would work on the machine
that last restored the right way and silently regress everywhere else, including CI.

### The mechanism that would work
Make it a **TargetFramework** difference, not a global-property difference, because restore
naturally produces one dependency graph per TFM. That means multi-targeting Core and conditioning
both the package references and the source excludes on `$(TargetFramework)`.

That is real work with real blast radius: Core is a published NuGet package whose TFM list is part
of its contract, and the CLI, desktop host and test project all reference it. It also only pays
off fully if CUE4Parse goes too, and CUE4Parse is far more entangled than Jint - 19 files use it
directly and `GameAssetProvider` is referenced by 22 Core files and 5 in Web.Shared, so the browser
TFM would need a stubbed provider (present but always reporting no game) rather than a plain
exclusion.

### Also found
`BouncyCastle.Cryptography` is a `PackageReference` in Core's own csproj but **no Core source uses
it** - it is there because CUE4Parse needs it. Dead config; removing it changes nothing today
(CUE4Parse still pulls it transitively) but it should not be re-declared as if Core needed it.

### Recommendation
Do it as its own piece of work, TFM-based, CUE4Parse and Jint together, with the save round-trip
suite re-run against the browser TFM before believing it. Do not retry the AdditionalProperties
route.
## Round-56: the 404 probe, appearance in the browser, and a payload measurement (2026-08-08)

### The per-load 404 is gone
The browser used to guess: ask for `registry.en-US.json`, take a 404, then ask for
`registry.en.json`. Replaced with `GameDataRegistry.BundledCultures` (the list of what actually
ships) plus `BestCultureFor`, so exactly one file is requested. Exact match wins first, then the
same language, so `pt-PT` gets the Brazilian text rather than falling back to English, and
`de-AT` gets German. A test asserts the hardcoded list matches the files in `assets/registry/`,
because the whole scheme only works while those two agree. **Verified: zero 404s on load.**

### Appearance editing works in the browser now
Round 55 found the constraint: a character's look lives beside `Worlds/`, one level ABOVE the
world folder, so a browser given a single world can never see it. The panel now offers **OPEN
APPEARANCE FILE**, which reads the file the player points at and saves it back as a download
(a picked file is a snapshot with nowhere to write).

This needed `filePicker.js` fixing too: it threw outright without `showOpenFilePicker`, so single
file open was Chromium-only. It now falls back to a plain `<input type="file">`, which works
everywhere - so `IFilePicker` is usable on every browser, not just for this screen.

**Verified in Firefox** with only a world folder open: button and hint shown, file picked, 13
fields with real option names, edit saved as a download.

### Performance: measured, with one large finding I did NOT act on
Published payload (what GitHub Pages would serve):

| | |
| --- | --- |
| Framework, raw | **33.9 MB** |
| Framework, brotli | **10.7 MB** |
| Largest assemblies | BouncyCastle 4.9 MB, dotnet.native 3.0 MB, CUE4Parse 2.9 MB, CoreLib 2.3 MB, Jint 2.2 MB |

**Roughly 10 MB of that can never run in a browser.** CUE4Parse and BouncyCastle exist to mount
the game's pak archives, which round 45 measured as impossible in a tab; Jint is the plugin
JavaScript runtime, and plugins are not registered on this host at all. They ship because
`AbioticEditor.Core` references them and the browser app references Core.

No trimming is configured anywhere (`PublishTrimmed` is unset). **I deliberately did not enable
it.** UeSaveGame resolves save property types by reflection, so a trimmer could break save
parsing itself - the one thing that must never break - and verifying that properly means
re-running the whole save-round-trip suite against a trimmed build, which is its own piece of work.

The real fix is architectural: split the pak-mounting and plugin-hosting code out of Core so the
browser build never references them. That would cut the download by about a third and is worth
doing deliberately, not as a footnote.

The data folders (`registry` 28 MB, `icons` 28 MB, `art` 17 MB) are NOT a startup cost - one
registry file is fetched, and icons and art load on demand.
## Round-55: the remaining unverified paths, checked (2026-08-08)

Everything left on the round-52/53 "not covered" list except Safari, driven in a real browser.

| Path | Result |
| --- | --- |
| **Drag and drop a folder** | Works. Dropping a folder handle opens it ("Opened Account.", 22 saves listed), no errors. Driven by dispatching a `drop` event carrying the shape the handler reads (`dataTransfer.items[].getAsFileSystemHandle()`), backed by a real OPFS handle. |
| **Appearance editor - read** | Works. Finds `ScientistCustomization_1.sav`, 13 fields, and real option names from the registry's Customization payload ("Hubert", "Beth", "Alessandro"). |
| **Appearance editor - write** | Works. Head `Head_M01a` -> `Head_F01a`, SAVE APPEARANCE enabled, file hash changed, **`.bak` written**, and the result is valid GVAS containing `Head_F01a` with the old value gone. |
| **Game Pass in a browser** | Degrades correctly. With no customization file present the Xbox-container fallback runs, finds nothing, and says "No saved character look was found on this computer." No crash. The Convert tab and the conversion entry points are hidden there anyway. |
| **Plugins / web tools** | Not reachable. Neither has a route in the shared library (the web-tool host lives in the desktop project), and the Plugins settings tab is hidden. All seven routes render with no page errors and no error bar. |

### Found and fixed while checking
`/create-world` still rendered the wizard in the browser. Round 52 hid the *link* on the home
screen but never gated the *page*, so the address was still reachable and the wizard would have
failed at the first folder it tried to write. Now shows the same "needs the desktop editor" panel
as `/compare`. Both confirmed in a browser.

### Appearance editing has a real constraint worth knowing
Character looks are stored **per account, one level above the world folder**
(`SaveGames/<id>/ScientistCustomization_*.sav`, beside `Worlds/`). The desktop walks up to find
them; a browser can only see inside the folder that was opened. So in the browser the appearance
editor only works when the player opens the **account** folder rather than a single world. Opening
the account folder does work - every world under it is listed together - but it is not obvious,
and nothing currently tells the player.

### Minor
Each page load probes one registry file that does not exist (e.g. `registry.en-US.json`) before
falling back to `registry.en.json`. Harmless, one 404 per load, but it is avoidable noise if the
shipped culture list is ever emitted as a manifest.
## Round-54: game data in every language, and no dead settings in the browser (2026-08-08)

### The bundled game data was English only
Round 50 recorded that "icons are not multiplied by language ... only the names beside them are
localised, and those already live in `registry.json`". The first half is right; the second was
misleading, and I wrote it. **`registry.json` held one language.** Neither `dump-registry` nor
`GameDataRegistry` knew what a culture was, so every player - German, Russian, Japanese - read
English item names, recipes, emails and journal entries in the browser build.

Fixed: `GameDataRegistry.Culture` + `FileNameFor(culture)` (`registry.ru.json`, plain
`registry.json` for the default), `dump-registry --culture` and `--all-cultures`. The game ships
text for **de, en, es-419, fr, ja, pt-BR, ru, zh-Hans, zh-Hant**; all ten dumps now ship (~23 MB
total in `assets/registry/`, but **only the matching one is ever downloaded**, ~2-3.5 MB).

Each culture needs its own pak mount - the translations are applied at mount time, so one provider
cannot produce two languages.

Selection is most-specific-first: `de-DE` tries `registry.de-DE.json`, then `registry.de.json`,
then the default. Verified in Chromium with `locale: de-DE` and `ru-RU`: both fall through to the
right file. The desktop's offline path (`LoadBundled`) now picks by `GameDataLanguageStore.Saved`
the same way.

Guarded by `BundledGameDataTests`, which asserts every culture ships, carries its own `Culture`
stamp, meets the same row-count floors, and - the part that matters - that its `scrap_metal` name
**differs** from the default. Sizes alone would not catch ten copies of English.

### The browser no longer offers settings it cannot honour
Four Settings tabs existed only to point at things on the player's own machine: **Game Data** (the
installed game's folder, its usmap, the game-data language, mods), **Convert** (Xbox container
folders), **Plugins** (loads DLLs) and **Compare** (two files picked from anywhere on disk). The
browser now shows **General** and **Editor** only.

Note the game-data language picker is desktop-only for a real reason: there it re-mounts the paks
in the chosen language. The browser instead follows the browser's own language, because that is
what picks the dump at startup.

### Known gap found while here
`Settings.razor` has a hardcoded English `"EXPORT LOG FILE"` where every sibling string is
localised. Not fixed here.
## Round-53: Firefox and Safari can open saves (2026-08-08)

Round 52 left those browsers unable to open anything. They can now, through the route flagged
there, and the whole flow is verified in real Firefox 153.

### How it works
`showDirectoryPicker` is Chromium-only, but `<input type="file" webkitdirectory>` works
everywhere. `abioticSaveFs` therefore keeps two kinds of folder:

- **Writable** (Chromium): a real directory handle, read and written in place. Unchanged.
- **Read-only** (Firefox, Safari): every file handed over as a snapshot, plus an **in-memory
  overlay** that writes go into and reads prefer.

The overlay is what makes this cheap. Every existing write path keeps working untouched -
including the cross-save ones from round 52 (story flags rewriting the Facility save, "move
players" rewriting every character) - and EXPORT then hands back the edited set. The player's own
files are never written, so the originals are the backup.

`ISaveFileSystem.CanWrite` tells the shared screens which kind is open. Where it is false the
sidebar shows a plain notice, and the SAVE button becomes **DOWNLOAD** (apply the staged edits,
then download that one save). Nothing about the desktop host changes.

### The near-miss this turned up
The first Firefox zip **looked perfect and contained the ORIGINAL character, not the edited one.**
`ExportWorkspaceAsync` builds its zip by reading saves back, and the staged edit had never been
written, so it silently exported stale bytes.

Fixed by splitting the two cases, which are genuinely different:
- **Read-only folder:** staged edits are flushed first. That costs nothing - it only updates the
  copy held in the tab - so they belong in the download.
- **Normal folder:** they are NOT flushed (the player asked to export, not to save) and the toast
  says so, because an export that quietly omits the edit just made is the worst outcome available.
  `FlushStagedEditsAsync` throws if called on a writable workspace, so this cannot be got wrong later.

### Verified in real Firefox 153
Open folder read-only (62 saves, names and sizes right, read-only notice shown) -> edit a
character's money to 4242 -> **DOWNLOAD** gives a valid save the CLI reads back as `Money: 4242`
-> **EXPORT** gives a 62-entry zip whose player carries the edit while every untouched sibling
compares `identical` to the fixture. The source folder on disk: **unchanged, zero `.bak` files**.

Driving it needs one piece of scaffolding: headless Firefox cannot show the OS folder dialog and
fires `cancel` at once, so the test catches the `<input>` as it is added (before the editor
attaches its own listeners), swallows that cancel, and calls `setInputFiles` with the **directory
path** - Playwright rejects a file list for a `webkitdirectory` input.

### Not covered
No unit test for the export-flush rule. It needs `SaveExportService` to take an interface rather
than the concrete session service, which is a DI change across both hosts and was not worth
half-landing here. The behaviour is verified end to end in a real browser; the rule itself is
guarded at runtime by the throw in `FlushStagedEditsAsync`. Safari is untested (no binary to hand)
though it takes the same path as Firefox.

## Round-52: Firefox, cross-save effects, and export (2026-08-08)

### Firefox: tested for real (superseded by round 53 - it works now)
Probed with actual Firefox 153 (`playwright install firefox`), not a simulation. The app **loads
and runs fine** - WebAssembly, the registry, the artwork, all of it. But `showDirectoryPicker` and
`showOpenFilePicker` are both undefined, so OPEN FOLDER is the only way in and it fails.

**The "single-file mode" the code comments promised does not exist.** There is no
`<input type="file">` anywhere in the editor and `IsSupportedAsync` is never called by any screen;
those comments (in `saveFileSystem.js` and `BrowserSaveFileSystem`) described an intention, not a
feature. Corrected here.

What was fixed now: Firefox showed *"Could not open that folder: Check that the folder still
exists and that you can read it"* - advice for a fault that does not exist - while the real error
underneath said "This browser cannot open a folder". The two cases are now told apart
(`UserFacingErrorService.IsFolderPickerUnavailable`) and Firefox gets the honest message.

**Still open:** Firefox and Safari cannot open saves at all. The most promising route is
`<input type="file" webkitdirectory>`, which Firefox *does* support: it yields every file in a
chosen folder with its relative path, read-only. Combined with the export below that is a complete
story (open read-only -> edit -> download the zip), and it reuses the existing screens. It needs a
read-only `ISaveFileSystem` whose `WriteAllBytesAsync` refuses, plus UI that steers those hosts to
export instead of SAVE.

### Cross-save effects: five actions write files you never opened
This is why export matters. Three of them write **immediately**, with no staging and no undo,
which quietly breaks the editor's usual "edits stage until SAVE" contract:

| Action | Writes | When |
| --- | --- | --- |
| STORY tab -> SET chapter | `WorldSave_Facility.sav` | immediately |
| STORY tab -> SET chapter with "move players" ticked | **every** `Player_*.sav` in the world | immediately |
| STORY / TRADERS tab -> unlock | `WorldSave_Facility.sav` | immediately |
| Player COMPANIONS -> send pet to bed | a world save | on SAVE WORLD |
| Player SPAWN -> claim bed | a world save | on SAVE WORLD |

`PlayerRespawnRevert.MoveToChapterTerminal` (the second row) was **an eighth direct-file-I/O site
that round 51's sweep missed** - it walked `PlayerData` with `Directory.EnumerateFiles` and wrote
each player with `PlayerSaveWriter.WriteToFile`, so it threw in the browser. Now split into
`PlanFor`/`Apply` in Core with `StoryFlagSyncService.MovePlayersToChapterTerminalAsync` driving it
through `ISaveFileSystem`.

### Export
New `ISaveExporter` seam (browser: a download; desktop: writes to Downloads and reveals it) and
`SaveExportService`. The EXPORT button in the save sidebar takes the **whole world** as one zip,
not the selected file - precisely because of the table above. Entries keep their folder layout, so
`PlayerData/Player_*.sav` lands back where the game expects it and the zip can be copied straight
over a save folder.

Verified end to end in the browser: real download of `Cascade.zip` (65,198,858 bytes, 62 entries),
extracted and compared file by file against the fixtures - **all 62 byte-identical**, folder layout
intact.

### Also fixed
`Player_facing_copy_does_not_expose_application_architecture` caught `exception.Message` in a razor
file (used to classify, not display, but the rule is right). Moved into
`UserFacingErrorService.IsFolderPickerUnavailable` where exception internals belong.

## Round-51: both browser root causes closed (2026-08-08)

Round-50 left the browser build unusable for two reasons, both now fixed. Read this before
touching the browser host again.

### Cause 1 (closed): the `ISaveFileSystem` seam is now applied everywhere
Round-47 converted `SaveWorkspaceSessionService` and the two session writers and stopped. Every
other place that opened a save by path threw the moment its screen was used. All of them now go
through the seam, or hide where the feature genuinely cannot work in a tab:

| Site | What changed |
| --- | --- |
| `Services/SiblingWorldBedService.cs` | Reads via `ISaveFileSystem`; `FacilityPathForAsync` finds the facility save in the workspace first. This was the `DirectoryNotFoundException` in the user's report. |
| `Services/RecipeProgressGateService.cs` | `ResolveWorldFlagsAsync` (was sync) reads through the seam; facility save found via the workspace. |
| `Core/Services/World/StoryFlagSync.cs` | Split into `Plan*` (pure, takes an already-read save) and the file-based wrappers. New `Services/StoryFlagSyncService.cs` drives the plans through the seam - this was a second write path that bypassed it entirely. |
| `Components/World/WorldFeaturesTab.razor` | Cross-region power-socket names read through the seam. |
| `Components/Player/PlayerAchievementsTab.razor` | Comparison candidates come from the workspace's saves, not `Directory.EnumerateFiles`. |
| `Models/CustomizationSaveSession.cs` | New `LoadFromBytes`/`SaveToBytes` reusing the Game Pass byte round-trip; the appearance editor discovers slots through the seam. |
| `Components/Shared/WorkspaceShell.razor` | Bed-claim personas find the facility save in the workspace. |
| `Compare.razor`, Home's "New world" link | Gated on `HasLocalPaths` with a localized "needs the desktop editor" panel (`Host_NeedsDesktop*`, all 5 languages). Both genuinely need arbitrary local folders. |

New seam member: `GetVersionStampAsync` (desktop = last-write ticks, browser = `lastModified:size`),
because the caches these services keep were all keyed on `File.GetLastWriteTimeUtc`.

The preference stores (`Host*Preferences`, `HostThemeService`, `HostLanguageService`,
`ShellPreferencesService`, `HostDiagnosticsStore`) still touch files. That is harmless: they land
in the in-memory file system and simply do not persist across a reload. Left alone deliberately.

### Cause 2 (closed): the registry now carries every catalog, at schema v2
`GameDataRegistry` had `Items` and `ItemTableRefs` and nothing else, so recipes, the codex,
traders, traits and appearance options were empty in the browser regardless of anything else.
`CurrentSchemaVersion` is now **2** and `BuildFromInstall` reads each catalog through an
`Optional(...)` wrapper, so one table a game patch renamed costs its own payload rather than the
whole dump. What ships now (`assets/registry/registry.json`, ~2.0 MB):

| Payload | Count | Payload | Count |
| --- | --- | --- | --- |
| Items | 1622 | Emails | 197 |
| ItemTableRefs | 1622 | Journals | 138 |
| Recipes | 584 | Compendium | 195 |
| ItemUpgrades | 86 | Fish | 33 |
| Maps | 11 | Traders | 9 |
| Skills | 15 | SectorMaps | 11 |
| SkillMilestones | 15 | Traits | 50 |
| Customization | 13 tables | | |

Each vocabulary service now prefers live pak data and falls back to the registry
(`RecipeVocabularyService`, `ItemUpgradeVocabularyService`, `CodexVocabularyService`,
`TraderVocabularyService`, `ProgressionVocabularyService`, `CustomizationCatalogService`), matching
what `ItemCatalogService` already did.

### Pictures beyond item icons
`GameArtService.ArtUrl`/`WikiImageUrl` were hardcoded to the desktop's own endpoints, so skill
icons, trader portraits, chapter cards, sector maps and creature portraits silently never rendered
in a browser (the components gate on "can we extract it?" and drew their fallback symbol).

- New CLI `dump-art` (companion to `dump-icons`) writes those textures plus a manifest of what it
  managed to decode. Result: **351 pictures, 17 MB** in `assets/art/`.
- New `Core/Infrastructure/GameAssets/BundledArt.cs` mirrors `GameDataRegistry`'s `Supply`/`TryRead`
  pattern. `GameArtService` takes `ISaveFileSystem` and, in a browser, answers "does this picture
  exist?" from the manifest instead of firing a request that 404s.
- `assets/wiki/` (41 offline wiki images) is now bundled too.
- 61 refs "could not be decoded" during the dump: those are `PetCatalog.CompendiumTextureRefs`
  *candidates* that legitimately do not exist. Expected noise, not a gap.

### Tests: `tests/AbioticEditor.Tests/BundledGameDataTests.cs` (new)
Asserts against the files that actually ship, read the way the browser reads them
(`TryRead(bytes)`, no install). Four tests: every registry payload present with a sane lower
bound; a recipe's `CreatesItemId` resolves to a real named item; every art-manifest entry has its
PNG beside it; every `WikiImageManifest` name has an offline copy.

**This test immediately caught a real bug**: wiki files are stored under `SafeNameFor` (spaces
folded to underscores, `"Item Icon - Gem Crab.png"` -> `"Item_Icon_-_Gem_Crab.png"`), and the
browser URL I had written used the raw name. Every wiki image would have 404'd.

### Verified end to end in a real browser (round-51b)
The save round-trip has now been driven with Playwright against the Cascade fixture (62 saves,
62 MB, including the 14 MB Facility region save).

**How to repeat it.** The OS folder dialog cannot be driven, but it is the *only* part that
cannot. The browser's own Origin Private File System hands out genuine
`FileSystemDirectoryHandle` objects, so:
1. Copy a fixture world to `src/AbioticEditor.Web.Wasm/wwwroot/e2e-fixture/` (gitignored) and
   rename each `.sav` to `.sav.bin` - **the dev server 404s unknown MIME types**, and a plain
   `.sav` will not serve.
2. In page context, fetch each file (**with a cache-busting query** - even `cache: 'no-store'`
   can be answered `204 No Content` from a stale entry) and write it into OPFS.
3. `window.showDirectoryPicker = async () => opfsHandle`. Everything downstream -
   `saveFileSystem.js`, `BrowserSaveFileSystem`, the Core readers and writers - runs untouched.

**Results.** Player save: money 116 -> 31337, `.bak` written **byte-identical to the untouched
fixture**, and the CLI (an independent reader) parses the browser-written file and reports
`Money: 31337` with skills, traits and recipes intact. World save: day 292 -> 777 on the 14 MB
Facility save, `.bak` byte-identical, and `compare` against the original fixture reports **"no
gameplay differences (1 clock difference only)"** - `TimeOfDay.CurrentDay: 292 -> 777`. Screens
confirmed live: recipes (467/586 with names, ingredients, benches, icons), GATEPAL (158/197 emails
with senders), traits with descriptions, all 15 skill icons decoding from `assets/art/`, and the
SPAWN tab resolving `Furniture CraftedBed T2 (claimed by Tribbes)` out of the Facility save - the
exact lookup that used to throw `DirectoryNotFoundException`.

**Two pre-existing bugs this surfaced** (neither introduced by round 51):
- **World clock edits could never be saved.** `SetWorldDay`/`SetWorldTime`/`SetDayDiscovered` in
  `SaveEditorSurface.razor` staged into the session but never called `Workspace.NotifyEdited()`,
  so SAVE and REVERT stayed disabled while the panel read "Unsaved changes". Affected the desktop
  host equally. Fixed, with `tests/AbioticEditor.Tests/WorldClockEditorWiringTests.cs` guarding
  all three (verified failing on the old code).
- **Saving a player rewrites four arrays in sorted order** even when untouched:
  `PlayerSaveSession.Save()` always re-applies `ItemsPickedUp`, `CraftedItems`, `MapsUnlocked` and
  the recipe list sorted. Content is preserved (1804 items in, 1804 out) and the game treats these
  as sets, so it looks harmless - but it means a player save is never byte-stable across a no-op
  save, and `compare` reports thousands of differences. Long-standing shared behaviour; left alone
  rather than changed blind, but worth a decision.

**Not covered by this run:** drag-and-drop folder open, the appearance editor's write path, and
Firefox/Safari single-file mode.


Shipping game data to the browser build, plus the bugs found by actually driving it.

- **`assets/icons/`: 1,622 item icons at 256x256 (28 MB), committed**, produced by a new
  `abioticeditor dump-icons` maintainer command (sibling of `dump-registry`; applies the same
  per-item tinting the app draws with, so shipped art matches the desktop). Wired into the
  **browser build only** - the desktop decodes from the installed game on demand and carrying a
  copy would only grow its download. `ItemCatalogService.IconUrl` is host-aware: `/item-icons/{id}`
  on the desktop, `icons/{id}.png` in the browser.
  - **Icons are NOT multiplied by language.** They are untranslated artwork; only the names beside
    them are localised, and those already live in `registry.json`. One dump covers every locale.
  - `registry.json` was regenerated from a real install and came back **byte-identical** - already
    current for this game build. Verified, not assumed.
- **TRAP: `<Content Include="..\..\x" Link="wwwroot\x" />` does NOT create a Blazor static web
  asset.** The file lands in the output folder and the dev server answers
  **`200 OK, Content-Length: 0`** for it. Nothing throws. The registry parsed as empty, so the
  editor came up looking perfectly healthy with no item names, no recipes and no pictures at all.
  Blazor takes static web assets from the `wwwroot` folder itself, so a build target now COPIES
  them in (`CopyBrowserDataAssets`, before `ResolveStaticWebAssetsInputs`); the generated copies
  are gitignored and `assets/` stays the source of truth. This bit templates, the registry and
  the icons simultaneously.
- **TRAP: `EditorLog` is a FILE log, so in a browser it is a black hole.** The registry failure
  above reported itself only there. Startup failures on this host now also write to
  `Console.Error` so they show up in the browser console, which is the only diagnostic surface a
  player (or a maintainer) actually has.
- **`GameDataRegistry.Supply(...)`** added: a host with no usable file system hands the registry
  over directly rather than staging bytes into WebAssembly's in-memory file system and hoping
  `AppContext.BaseDirectory` resolves the same way on both sides. The staging approach failed
  silently and was the harder half of the bug above.
- **Fixed, all browser-only**: `SelectAsync` still ran `Path.GetFullPath` on the identifier, so
  every save in the sidebar was unselectable ("not part of the open workspace") - the same fix
  `OpenAsync` already had, missed on its sibling; folder drag-and-drop (via
  `getAsFileSystemHandle`, asking for write permission during the drop gesture so SAVE does not
  prompt later); Chrome's bare "contains system files" refusal now explains to pick the world
  folder; the log button EXPORTS the log file instead of revealing a folder that cannot exist
  (`IDiagnosticsLogDelivery`); the header falls back to the bundled logo instead of an "AF" tile.
- **Fixed: the error bar flashed on every load.** `#blazor-error-ui` is plain markup always
  present in the page, revealed by Blazor only on failure. Switching `index.html` to the shared
  stylesheet had dropped the link to `css/app.css`, which holds the rule hiding it - so it showed
  when nothing was wrong. `css/app.css` is now trimmed to Blazor's own furniture only.
- **Fixed: stale-cache 404s.** Each build re-stamps the runtime files (`dotnet.<hash>.js`); a
  browser holding an older `index.html` asks for names that no longer exist and shows a permanent
  error that ordinary reloads cannot clear (they re-serve the same stale page). `index.html` now
  detects that specific failure and reloads once bypassing cache, guarded by `sessionStorage` so a
  real outage cannot become a reload loop. **This would have hit every returning visitor after
  each Pages deploy.**

## Round-49: the browser host renders the SHARED screens (2026-08-07)

Phase 4 done. `src/AbioticEditor.Web.Wasm` no longer has screens of its own: its `App.razor`
points the router straight at `AbioticEditor.Web.Shared`, and the round-45 duplicates
(`Pages/{Home,Stats,Skills,Traits,Inventory,Progression,NotFound}.razor`, `Components/*`,
`Layout/MainLayout.razor`, `Services/PlayerSaveSession.cs`) are deleted. Confirmed by
screenshot: the browser now draws the real masthead, workspace shell, status bar, start screen
and the full five-step Create World wizard.

- **Host-specific implementations added**: `BrowserSaveTemplateSource` (fetches the blank
  templates from `Templates/` static files - they are copied into `wwwroot` by the csproj, since
  there is no folder beside an executable here) and `BrowserNavigationService` (`window.open`
  for links; `RevealPathAsync` is a deliberate no-op because a tab has no file manager and these
  are not local paths anyway).
- **One registry of directory handles, not two.** `IFolderPicker` on this host delegates to
  `BrowserSaveFileSystem.PickFolderAsync` rather than picking a folder itself. Before that fix
  OPEN FOLDER silently did nothing: `MainLayout` returns early when `PickedFolder.Path` is null,
  and the old browser picker only returned a name. `PickedFolder.Path` now carries the file
  system's folder identifier. `filePicker.js` lost its own `pickFolder` so a granted handle can
  only ever land in the registry that later reads and writes the saves.
- **`SaveLibraryService` gained `CanDiscover`** (`ISaveFileSystem.HasLocalPaths`). It is injected
  by a shared screen so it must resolve, but scanning the game's install locations is meaningless
  in a browser; it now returns nothing there instead of scanning a virtual file system, and the
  start screen shows its "pick a folder" path rather than an empty "worlds found" list that reads
  like a failed search.
- **Deliberately NOT registered on this host** (they cannot work in a browser): the plugin host
  and `WebToolHostService` (loads assemblies), `HostUpdateService` (replaces an executable), the
  desktop pickers and `DesktopWindowHost`. `Updates.razor` and `WebToolHost.razor` stayed in the
  desktop project in round-46 precisely so they cannot be routed to here.
- The header logo showing an "AF" tile rather than the wordmark is CORRECT, not a bug: the logo
  is a pak-extracted game asset and `GameArtImage` nests down to that fallback wherever the game
  is not installed - the same thing the desktop app does without a game install.
- **922/922 tests pass**; desktop host re-verified unchanged.
- **Not verified end-to-end, and cannot be from here**: picking a real save folder in the browser
  opens a native OS dialog that browser automation cannot drive (clicking it would freeze the
  automation session), so the pick -> list -> read -> edit -> write round trip through
  `BrowserSaveFileSystem` still needs one manual pass in Chrome or Edge. Everything up to the
  dialog is verified. **Do that before announcing the browser build to players.**

## Round-48: browser file system + shared static assets (2026-08-07)

Phase 3 of the shared-front-end work, plus the first half of phase 4.

- **`BrowserSaveFileSystem`** (`src/AbioticEditor.Web.Wasm/Services`) implements
  `ISaveFileSystem` over the File System Access API, with the handles themselves living in
  `wwwroot/js/saveFileSystem.js` (they cannot cross the interop boundary). Identifiers are
  `"<folderName>/<pathInsideFolder>"` - path-shaped so existing editor code is happy, but only
  that JS file may interpret them, which is exactly what `HasLocalPaths == false` advertises.
  - Bytes cross as `IJSStreamReference` (JS -> .NET) and `DotNetStreamReference` (.NET -> JS),
    NOT as `byte[]`: default interop marshals arrays as base64 JSON, which for a 16 MB region
    save would dominate the time to open a world.
  - `readHeader` uses `Blob.slice`, so identifying 65 saves stays lazy exactly as the desktop's
    header probe does.
  - Writes copy to `<name>.sav.bak` first and use `createWritable()` (which swaps in on close,
    so a failure partway cannot truncate). Unlike the desktop there is no file history to fall
    back on, so a failed backup aborts the write rather than pressing on.
  - `showDirectoryPicker` is Chromium-only; `IsSupportedAsync()` is the gate, and Firefox/Safari
    keep single-file open + download.
- **Static assets moved into the RCL** (`AbioticEditor.Web.Shared/wwwroot`): `parity.css`, the
  four fonts, the images, `modal.js`, `compare.js`, `transmog-dnd.js`, `workspace-shell.js`.
  They are served at `_content/AbioticEditor.Web.Shared/...`, so:
  - the six components that did `import "./transmog-dnd.js"` (and friends) now import
    `"./_content/AbioticEditor.Web.Shared/..."`;
  - `App.razor`'s `@Assets[...]` entries changed to match. **The scoped-CSS bundle is still
    named after the consuming app** (`AbioticEditor.Web.styles.css`) - the SDK folds a
    referenced library's per-component styles into it - so the CLAUDE.md warning about that
    bundle still applies unchanged.
  - font `url(...)`s inside `parity.css` are relative to the CSS file, so moving the CSS and
    `fonts/` together kept them correct with no edit.
- **Two test false positives, fixed properly rather than by rewording.** `RazorHostAcceptanceTests`
  scans `.razor` text nodes for implementation jargon and `RazorVisualParityTests` measures the
  ORDER `parity.css` is linked in - both read raw source, so a `@* ... *@` comment explaining the
  markup counted as player-facing copy and as a stylesheet link. Both now strip Razor comments
  first, which is strictly more accurate (the compiler strips them; they never reach the page).
- Verified in the running desktop app after the asset move: logo, fonts, colours, pak-extracted
  item icons, the drag-and-drop inventory grid and the resizable sidebar all behave as before.
  **922/922 tests pass.**
- **Phase 4 is NOT finished.** Still to do before the browser host renders the shared screens:
  register the ~25 shared services in the Wasm host; browser implementations for
  `ISaveTemplateSource` (fetch the blank templates from static files) and the remaining
  `AbioticEditor.Ui` interfaces; switch off what cannot work there (plugins/web tools, the
  updater, Game Pass containers, the ini editor, the JSON side-car, "reveal in folder", and
  local world discovery); an `App.razor`/`Routes.razor` for that host; and finally delete the
  round-45 duplicate pages (`Pages/{Stats,Skills,Traits,Inventory,Progression}.razor`,
  `Components/*`, `Services/PlayerSaveSession.cs`), which are still what the deployed site uses.

## Round-47: `ISaveFileSystem` - save IO behind a swappable seam (2026-08-07)

Phase 2 of the shared-front-end work. `SaveWorkspaceSessionService` was path-centric throughout
(`Directory.EnumerateFiles`, `FileInfo`, `ReadFromFile`, `WriteToFile`); it now reaches files
only through `ISaveFileSystem`, so a host without a disk can supply its own.

- **The seam is deliberately small**: `HasLocalPaths`, `FolderExistsAsync`, `ListSavesAsync`,
  `ReadAllBytesAsync`, `ReadHeaderAsync`, `WriteAllBytesAsync`, plus a `SaveFileEntry` record.
  `DesktopSaveFileSystem` (in the desktop host) is a thin pass-through that still writes via
  Core's `SaveBackup`, so the desktop's backup-then-atomic-replace behaviour is unchanged rather
  than reimplemented.
- **`path` is now explicitly an opaque identifier, not necessarily a real path.** Only the
  implementation that produced it may interpret it. `HasLocalPaths` is the gate for anything
  that hands a path to something outside the editor (revealing a file, Game Pass container
  packing, the JSON side-car, and `ContainmentDirectory.SyncUnitRecords`, which reaches sibling
  region saves and is now skipped when paths are not local).
- **`ReadHeaderAsync` exists for a reason, do not collapse it into `ReadAllBytesAsync`.**
  Discovery identifies every save from its GVAS header. Reading whole files instead would pull
  the ~16 MB Facility region save (and 64 others in the Cascade fixture) into memory just to
  read a few dozen bytes. Core gained a `SaveFolderScanner.ReadSaveClassFromHeader(Stream)`
  overload so that probe can run against bytes rather than a path.
- Sessions take `ISaveFileSystem? files = null`; null keeps the old direct-to-disk write, which
  is what the pre-existing session tests exercise. Both hosts pass a real one.
- **Coverage gap this created, and closed.** All 11 existing `SaveAsync` tests construct
  sessions with `files: null`, so they exercise the *fallback* - not the path the shipping app
  now takes. `tests/.../SaveFileSystemSeamTests.cs` covers the seam directly: player save, world
  save, and the full app flow (open folder -> select -> edit -> save), each asserting the `.bak`
  holds the original bytes and the edit really landed. **922/922 pass.**
- Verified in the running desktop app as well as by tests (round-46's lesson): folder discovery,
  header classification into WORLD STORY / PLAYERS, and reading a player save all work through
  the seam. Write behaviour was verified against a **copy** of the Cascade fixture, never the
  user's real save folder.

## Round-46: `AbioticEditor.Web.Shared` - one screen set for both front-ends (2026-08-07)

Round-45 built the browser host its OWN pages, which was the wrong shape: two editors would
drift. Corrected here. New Razor Class Library `src/AbioticEditor.Web.Shared` holds the screens
and their host-agnostic services; `AbioticEditor.Web` (desktop) references it and is otherwise
unchanged, and the browser host will render the same components (phases 2-4 below).

- **`RootNamespace` is deliberately `AbioticEditor.Web`, not the new project's name**, so every
  moved type kept the namespace it already had and not one `using` in the 53 moved component
  files had to change. Same reasoning as Core's folder layering (namespaces left alone on
  purpose because they are the published API).
- **What moved**: `Components/{Pages,Player,Shared,World}` + `Components/_Imports.razor`,
  `Models/`, `Localization/` (all five resx), `HostVersion.cs`, and 41 of 48 `Services/`.
  **What stayed** (cannot leave the desktop host): `Program.cs`, `App.razor`, `Routes.razor`,
  `Components/Pages/{Updates,WebToolHost}.razor` (bundled updater + plugin web tools) and their
  services, `DesktopHostService`, `DesktopWindowHost`, `WindowsDesktopPicker`,
  `LocalHostEndpoint`, `BrowserSaveImportService`, `Diagnostics/`, `wwwroot/`.
- **The one real code change**: `CreateWorldService` took `IWebHostEnvironment` purely to find
  `ContentRootPath/Templates/*.sav`. That is ASP.NET-hosting-specific, so it now asks a new
  `ISaveTemplateSource` instead (`DesktopSaveTemplateSource` reads the folder beside the
  executable; the browser host will fetch them from its static files). Everything else moved
  byte-for-byte - `git` recorded all 111 moves as 100% renames, so history follows the files.
- **Two traps hit, both worth remembering.**
  1. `_Imports.razor` does NOT cross project boundaries, and a Razor Class Library gets no
     implicit usings from the Web SDK. `NavLink`/`LocationChangedEventArgs` stopped resolving
     until a project-root `_Imports.razor` was added to the library (component tags resolve from
     `@using`, not from C# `global using`, so GlobalUsings alone did not fix it). The desktop
     host needed its own `Components/_Imports.razor` for the same reason - keep the two in step.
  2. **`AddAdditionalAssemblies` on `MapRazorComponents` is required, not just
     `AdditionalAssemblies` on `<Router>`.** Endpoint routing discovers routable components per
     assembly and only scans `App`'s by default. With only the Router updated the app started
     fine and `/healthz` answered 200 while EVERY shared route including `/` returned 404. All
     919 tests still passed. Only actually opening the app caught it - do not treat a green
     suite plus a healthy `/healthz` as evidence this host renders.
- `CA1822` ("can be marked static") is suppressed in the library with a reason: the Razor Class
  Library SDK reports it a level higher than the Web SDK did, and these are DI-injected service
  members that must stay instance members (making them static would not compile at the call
  sites, and would remove the seam the browser host swaps implementations into).
- **Tests**: the six UI-parity classes each hardcoded `src/AbioticEditor.Web/...`, so 40 failed
  on paths alone. Replaced their per-class root locators with one `tests/.../UiSource.cs` that
  probes the shared library first and the desktop host second - correct today, and survives the
  next file that moves between them. **919/919 pass.**
- **Verified in a real browser against a real save folder**, not just built: world discovery
  list, workspace sidebar (65 saves, player names resolved), the whole `PlayerEditor` tab strip,
  and the inventory tab with real pak-extracted item names and icons. The RCL's scoped CSS does
  bundle into the host's `AbioticEditor.Web.styles.css` as before (see the round-45 note and
  CLAUDE.md on that bundle - it was the thing most likely to break here, and did not).
- **Still to do**: `ISaveFileSystem` seam so `SaveWorkspaceSessionService` (525 lines, injected
  by 18 components, path-centric throughout) stops assuming `System.IO`; a browser
  implementation over the File System Access API's `showDirectoryPicker` (real read/write
  directory handles, which map 1:1 onto the existing world-folder model, Chromium-only); then
  pointing the Wasm host at these components and deleting the round-45 duplicates.

## Round-45: browser-only editor (Blazor WebAssembly, deployed to GitHub Pages) (2026-08-07)

**New host**: `src/AbioticEditor.Web.Wasm` - a standalone Blazor WebAssembly app referencing
Core + Ui.Abstractions directly (no server, no SignalR circuit). Deployed by
`.github/workflows/docs.yml` alongside the VitePress docs, at
`https://christophervr.github.io/AbioticEditor/app/` (docs stay at the site root; Pages serves
one artifact per deploy, so both are built into the same `docs/.vitepress/dist/` before upload -
a second independent `deploy-pages` run would silently replace the other one). Local dev:
`dotnet run --project src/AbioticEditor.Web.Wasm`.

- **Proven, not assumed, that Core is WASM-loadable.** `dotnet publish -c Release` for
  `browser-wasm` succeeds with the FULL dependency graph - CUE4Parse, CUE4Parse-Conversion,
  UeSaveGame, Oodle.NET, OodleSharp, SkiaSharp, Jint, all of it. Verified end-to-end in a real
  Chrome tab: uploaded a real fixture `Player_*.sav`, parsed it with the unmodified
  `PlayerSaveReader`/`PlayerSaveWriter`, edited a stat, downloaded the result, and re-read the
  download with a throwaway console app - the edited field changed and every other field was
  byte-identical to the original. `Directory.Packages.props` gained the two
  `Microsoft.AspNetCore.Components.WebAssembly*` package versions for this project.
- Vertical slice lives on `Pages/Home.razor`: plain `<InputFile>` (works in every browser, no
  API gating) -> `PlayerSaveReader.ReadFromStream` on the in-memory bytes -> six bound stat
  fields -> `PlayerSaveWriter.ApplyStats` -> `SaveGame.WriteTo(MemoryStream)` -> browser download
  via `DotNetStreamReference` + `wwwroot/js/downloadFile.js` (the documented Microsoft pattern
  for Blazor file downloads). Nothing the user opens ever leaves the tab.
- **`AbioticEditor.Ui.IFilePicker`/`IFolderPicker` now have a browser implementation**
  (`Services/BrowserFilePickerService.cs` + `wwwroot/js/filePicker.js`), backed by the File
  System Access API (`showOpenFilePicker`/`showDirectoryPicker` - Chromium only; throws a
  catchable, host-appropriate message elsewhere, same pattern `DesktopHostService` already
  uses for an unavailable OS picker). Not yet wired into any page - `Home.razor` uses plain
  `InputFile` because it's universally supported - but it's the seam a future port of the full
  desktop component tree onto this host would render through, matching how `DesktopHostService`
  backs the same interfaces on the Photino host. `PickedFolder.Path` is always null here (routed
  through `Ui.Abstractions`' pre-existing doc comment for exactly this case); reading a picked
  folder's *contents* needs a different, richer surface than this interface offers (see below).
- **Investigated in-browser game-pak mounting (item icons/names from the real game data) and
  concluded it does not fit today, with real numbers, not a guess.** `Oodle.NET`/`OodleSharp`
  are pure-managed (no native `oo2core` binary anywhere in a wasm publish output - confirmed by
  inspecting the NuGet packages' `lib/` layout, no `runtimes/` folder), so the codec itself isn't
  the blocker. The blocker is `GameAssetProvider.CreateForPaks` -> CUE4Parse's
  `DefaultFileProvider(path, ...)`, which requires a real directory and memory-maps pak content
  lazily from disk. A browser has no such path; the only way to hand it bytes at all is to stage
  picked files into the Mono/Emscripten in-memory virtual filesystem first, which means the
  *entire* file has to be WASM-heap-resident at once (no lazy/range reads once it's "just a
  file" to .NET). Measured this installation's actual paks
  (`C:\...\Steam\steamapps\common\AbioticFactor`): `pakchunk0-Windows.ucas` alone is **4.6 GB**,
  and Blazor WASM builds this ship at (`--max-memory=2147483648`, i.e. 2 GB; wasm32 tops out
  around 4 GB regardless without memory64, which isn't viable here). Not close. A real fix would
  mean teaching CUE4Parse's pak/IoStore readers to do on-demand `Blob.slice()` range reads over
  JS interop instead of assuming a seekable local file/memory-mapped region - a substantial
  CUE4Parse-level project of its own, not attempted here. Until then the browser host simply
  doesn't call `GameAssetProvider.CreateForLocalInstall` at all: editing works fully off typed
  fields and (for anything backed by `Catalogs/`) curated static game-knowledge data, with no
  icons/live game text, the same "degrades gracefully when assets are absent" story the desktop
  app already tells when it can't find the game install.
- **Follow-ups, not yet done**: only the stats page exists (no inventory/skills/traits/world
  editors on this host yet - porting those means either duplicating Razor markup or extracting
  the desktop `Components/` tree into a shared Razor Class Library referenced by both hosts,
  which wasn't attempted this round); no multi-route client-side navigation yet, so the GH Pages
  SPA-fallback (`404.html`) trick was skipped as unneeded for now; large-file browser
  transfer uses `byte[]` JS interop (fine for saves, would need `IJSStreamReference` if ever
  reused for something bigger).

## Round-44: doors - real sector-map pins, real story flags, ONLINE MAP removed (2026-07-25)

**Area map.** The door detail card's "Area map" was an abstract 200x120 SVG scatter of dots on
a decorative grid - no geometry, so it never answered "where is this door". It now draws the
game's own sector-map pamphlet with the door pinned on it, falling back to the old scatter
where no usable drawing exists (plus a line saying so).

- **The ONLINE MAP link is gone** (`https://gamemappers.com/abiotic-factor-map/`). Reviewed it
  in the browser: it is a community marker map built on these same pamphlet drawings, and the
  editor passed it no parameters at all - the identical static link rendered for every door, so
  it could never point at *your* door. Resx keys `Slot_OnlineMap` /
  `Slot_OpensACommunityInteractiveMapOfTheFacility` deleted from all five locales.
- **There is no calibration data in the game.** `DT_MapPamphlets` has exactly four columns
  (sector, DT_Levels handle, image, StrippedFromBuild) - no bounds, no origin, no scale - and
  the game never draws a "you are here" marker on a pamphlet. Every fit therefore had to be
  solved and then verified by eye.
- `SectorMapCalibration` rewritten: `SectorMapFit(PamphletRow, Variant, ScaleX, ScaleY,
  OffsetX, OffsetY)` is a plain affine from world units to texture fractions, so the runtime
  needs only the one door's position (the old `BuildProjector` needed the whole actor cloud
  loaded to derive bounds). `Project` is the only entry point callers need.
- `SectorMapCalibrationProbe` rewritten into the tool that produced those numbers:
  `Solve_Fits` rasterises the drawn plan into a mask and searches orientation x scale x offset
  for best IoU (dilating the point cloud first - a bare footprint is holes, and IoU then
  rewards stretching the level over the whole page); `Composite_Fits` overlays door pins plus
  **named landmark actors** (toilets/sinks -> the drawing's restroom icon, elevators -> its
  lifts) which is what actually settles a fit, since rotations 180 degrees apart score within
  a percent of each other; `Dump_RawTexturesWithGrid` for measuring the drawing area by eye.
  Output -> `tools/shots/calib/` (gitignored).
- **6 of 77 sub-levels ship a usable, verified fit**: Office1 (v1), Office3 (v1), Labs (v1),
  MFWest (v3), Pens (v6), DarkFusion (v2, which is what `Map_Reactors` actually depicts).
  Deliberately excluded: only 11 pamphlets exist at all; Secure Area's drawing literally reads
  "SITE MAP UNAVAILABLE FOR SECURITY PURPOSES"; Residence's is a washed-out blank (the game's
  asset is named `Map_ResidenceTerribleMap`); the game itself ships `Map_Containment` pointing
  at the Office Level 1 artwork; and Office2 + Dam never settled (all eight orientations within
  a few percent, none putting their lifts where the drawing labels them).
- Game-data quirks confirmed from the dump: `Map_Security` and `Map_Reactors` both claim level
  "Dam", `Map_Residence` claims "None". `SectorMapCatalog.ForRow` added so calibration pairs
  levels with rows itself instead of trusting those handles.
- Pins are positioned `<span>`s, not SVG circles: the drawings are 2:1, so a stretched viewBox
  would squash every dot into an oval. Doors projecting off the drawing are dropped rather than
  clamped, and if the *selected* door falls off, the card drops to the scatter instead.

**Story flags.** "STORY" was a per-*blueprint-class* guess in `DoorClassCatalog`, so it
labelled hundreds of ordinary hinged doors story-controlled.

- New `DoorGateResolver` reads `WorldFlagToUnlock` / `WorldFlagToRemainOpen` off the placed
  actor in the cooked `.umap` (same mechanism as door positions; cached per map).
- A sweep of all 77 sub-levels (`DoorWorldFlagProbe`) found **11 gated doors in the whole
  game**: Containment `SlidingCellDoor_BP_C_1`/`StaticMeshActor_2102`/`_3613` +
  Labs `SlidingCellDoor_BP_C_13`/`_19` -> `LABS_TurretsDeactivated`; Containment
  `SlidingCellDoor_BP_C_4` -> `LABS_ReachedCommandCenter`; Residence
  `SimpleDoor_ParentBP_C_45` -> `Res_Objective1_Complete`; Residence
  `SimpleDoor_ParentBP_C_3`/`_53` -> `Res_HastaTria_EndCutscene` (stay-open, the only two);
  V_Signal `SimpleHatch_BP_C_2` + `SlidingDoor_VOTV_ASO_C_14` -> `V_Signal_Complete`.
- **No door class carries `LockKind == "Flag"` any more** (asserted by test). SimpleDoor /
  SimpleHatch / Sliding* dropped to `None`, the BlastDoor variants to `Part`. The per-instance
  gate *upgrades* a door to Flag at render time; with no game install the class default stands.
- Detail card gained an OPENS WITH block: friendly flag name, whether it unlocks or props the
  door open, the raw flag name for cross-referencing STORY EVENTS, and - when the save has an
  editable WorldFlags array - whether you have reached that point yet.
- Orphan resx keys `WorldDoors_MapCaptionSectorPin` / `WorldDoors_ActorNotFound` /
  `WorldDoors_SubLevelNotReadable` (authored in the localization round, referenced by nothing)
  are now wired; 7 new keys added and translated across de/es/fr/ru.
- Tests: `DoorStoryGateAndSectorMapTests` (19) - flag names per door, no-Flag-class invariant,
  which levels are and are not calibrated, pamphlet rows resolve, and >60% of each calibrated
  level's actors land on its drawing.
- Verified in the running app against `WorldSave_Facility_Labs.sav`: the two cell doors show
  STORY + "Labs: Turrets Deactivated", the map draws the Cascade Laboratories pamphlet with the
  crosshair on the containment blocks; `Facility_Labs_Control` (no pamphlet) shows FREE doors
  and the scatter fallback.

## Round-43: localization sweep - remaining hardcoded UI strings + Core-override wiring (2026-07-18)
- **Milestone: no user-facing English left hardcoded in App .cs/.xaml** (~310 new resx keys;
  neutral resx now ~1720 keys/locale across en/de/es/fr/ru).
- **Discovery: four override services existed but were never wired.** `SkillLocalization`,
  `TraitLocalization`, `NpcLocalization`, `EquipSlotLocalization` (plus their translated resx
  keys) were authored in an earlier round but the ViewModels still bound Core's English.
  Wired now: `SkillViewModel` -> `SkillLocalization` (with a guard: milestones served live
  from the game's DT_SkillPerks keep the game text, which already follows the game-data
  language), `TraitItemViewModel`/`PlayerEditorViewModel` -> `TraitLocalization`,
  `WorldNpcViewModel` -> `NpcLocalization` (and `IsHologram` now keys on
  `NpcIdentityCatalog.MatchedHint`, not the localized label text),
  `InventorySlotViewModel` -> `EquipSlotLocalization`.
- **New plumbing:** `LocalizationResourceManager.GetOrNull(key)` (missing key -> null instead
  of the raw key, for Core-English fallbacks); `Controls/LocalizeFormatExtension`
  (`{loc:LocalizeFormat Key, Arg0={Binding ...}}`, up to Arg0..Arg2) replaces hardcoded
  English inside XAML binding `StringFormat`s and stays live on language switch (MultiBinding
  of the loc indexer + args; proven under the source-gen XAML compiler);
  `LocalizationTests.EveryLocalizeKeyInXaml...` now also scans LocalizeFormat keys.
- **New override services** (Core stays English SoT): `ContainmentLocalization`
  (`WorldContainment_Creature_*` names + lore) and `CompatibilityLocalization` (the
  load-time warning bar; reproduces `SaveCompatibility.WarningFor`'s two-branch check from
  the public registry inputs).
- **Sweep coverage** (fanned out over 4 file-batch agents, keys merged via
  `tools/loc_extract`-style ledgers + `tools/loc_merge_resx.py`): WorldEditorViewModel
  (~95 sites: tab titles, all statuses/dialogs, story/door/containment/vehicle text; region
  titles now route through the existing `WorldStory_Region*` keys), Achievements/
  Customization/Codex/RecipeList/FishBaitResolver/IniEditor/ItemPalette VMs,
  PlayerEditor stragglers + SkillViewModel lock texts + TraderCard stock status +
  FlagItemViewModel purpose ladder + WorldVehicle/WorldBase/WorldFeature +
  `GameDataServices.StatusMessage` + ProgressContext gates, SaveSemanticDiff labels,
  ComparePanel A/B chips, AppShell/MainPage titles, sidebar tooltips/formats, pet
  family/status labels (`WorldPets_Family_*` incl. Lamogi), carried-pet slot/status.
  Deliberately NOT localized: brand marks (ABIOTIC FACTOR wordmark, UESAVEGAME · MAUI),
  decorative PDA date + weekday strip, coordinate axis labels (X/Y/Z), wiki-URL fragments,
  EditorLog diagnostics, file names inside messages.
- **Skill_Milestone resx keys synced to v1.4.0**: `Strength_12_*` removed (Nerd Rage ->
  `_10_`), Heavy Armor -> `_13_`, `Strength_15_*` re-valued to Centrifugal Force,
  Construction re-leveled (`_8_` Razed / `_10_` re-valued to Lift With Your Legs / `_12_`
  Experimental Fortification), + new Sprinting_8 / Reloading_8 / Fortitude_3+13 /
  Crafting_13 / Cooking_13 keys.
- Translations for all new/changed keys done by 4 parallel per-locale agents (de/es/fr/ru),
  parity-verified by `LocalizationTests`.
- Known limitation (pre-existing pattern): code-built text assigned once at construction
  (e.g. picker option lists) refreshes on reload, not live on language switch - same as the
  earlier rounds' code-behind text.
- **Leftover pass (same day):** the stragglers flagged by the sweep were localized too
  (+86 keys, resx now 1808/locale): status badges (UNLOCKED/LOCKED, READ/UNREAD, ACTIVE/
  MISSING, WRECKED/DRIVABLE), shared category chips (`Palette_Category*`, reused across
  palette/recipes/codex), codex email/journal render labels (FROM:, SOURCE:, unlock lines),
  recipe/palette stat fragments, flag category labels (switch over `FlagCategory`, no more
  enum `ToString()`), compare-view remaining section labels + folder-row details,
  skills LEVEL/status text, backpack titles, world-base summary, feature-device sentences
  restructured into whole-sentence keys, `UnlockedLabelConverter`, the recipes filter
  RadioButtons, and the Plugins menu item. Remaining known-unlocalized (deliberate):
  `WorldBaseViewModel.BaseMapDrawable` canvas legend labels, ComparePanel's exported
  Markdown report text.

## Round-42: anniversary-update (v1.4.0) sync - data-driven companions + skill perks (2026-07-17)
- **Reported bug (Nexus): Speedogi / Sir Ogi / Verdant Skink not detected in COMPANIONS.**
  Root causes: carried-pet reading hard-filtered on `PetItemCatalog`'s 22 curated rows, and
  the new Lamogi family matched no `PetCatalog` token. Several curated class paths were also
  stale vs the real game (`NPC_Monster_Pest_Rattus` -> `NPC_Monster_Pest_Rat`, `NPC_Skink` ->
  `NPC_Skink_Basic`, five Peccary classes actually live under `NPC_Monster_Peccary*`).
- **The game ships its own pet tables - the editor now reads them.** New
  `Core/Catalogs/World/PetGameData.cs`: joins `DT_Pets` (27 rows, struct `Pet_Struct`:
  `DefaultParent` inheritance chain = family, `PettingCompendiumUnlock` = portrait row,
  `Mutations` = real mutation graph, per-limb `HealthBase`/`HealthBonusPerLevel`) with
  `DT_NPCList` (DisplayName + `NPCSpawnClass`) and the `Item.Pet`-tagged rows of
  `ItemTable_Global` (carried forms; row names match the pet rows except the legacy
  `pet_skink`/`biocannon` pair, bridged by display name). Mod tables merge via
  `ModTableDiscovery`. **Future pets appear with no code change.**
- **Overlay pattern:** `PetCatalog.ApplyGameData(PetGameData?)` is a process-wide snapshot
  consulted first by every static lookup (`Categorize`/`IsPetClass`/`IsSummon`/`FriendlyName`/
  `CompendiumTextureRefs`/`BuildVariants`) and by `PetItemCatalog` (item list, item<->class
  bridge). Applied in App `GameDataServices.LoadCore` (reset to null on reload) and CLI
  `PetCommands.TryCreateProvider`. Offline fallback = curated seed, now pak-verified and
  extended (Lamogi family = tamed WinterSprite + LamogiPlated/LamogiSpeedy; Verdant Skink;
  crafted skink weapon forms; summons corrected to `NPC_Exor_Ally`/`NPC_MageEye_Ally`).
  New `PetCategory.Lamogi` appended after `Other` (binary compat); UI/family ordering via
  `PetCatalog.DisplayOrder`. IMPORTANT: no `WinterSprite` token - hostile Bombogi/Bigogi
  share the class stem and are NOT pets (guarded by test).
- **Reader gate loosened:** anything in the Companion equipment slot (index 12) is read as a
  carried pet even when its row is unknown to every catalog ("Empty" excluded) - a future
  pet is shown instead of silently dropped (`PlayerSaveReader.ReadCarriedPetsFrom`).
- **Skill milestones now table-driven too:** `SkillMilestoneCatalog.LoadFrom(provider)` reads
  `DT_Skills.Perks[] -> DT_SkillPerks` (DisplayName/DisplayDescription/RequiredLevel) with the
  same ApplyGameData overlay; static fallback synced to v1.4.0 (added Anaerobic Recovery 8,
  Centrifugal Force 15 = `skillperk_kendotraining`, Ammo Scavenger 8, Riposte 3, Enduring
  Stamina 13, That'll Buff Right Out 13, Experimental Fortification 12, Lift With Your Legs 10,
  Kitchen Technician 13; moved Nerd Rage 12->10, Heavy Armor Spec 15->13, Razed With Care
  10->8).
- **Rest of the v1.4.0 audit** (research + code audit agents): items/recipes/deployables
  (Digital Garden Plot, Advanced Oven 2-slot, cartridges, watches) are table-driven and flow
  through automatically; sandbox INI settings are generic key/value (2 new options appear);
  customization tables unchanged (16 `DT_Customization_*` - new IDs/hairs/clothes are rows in
  existing tables; Labcoats/FannyPacks/Makeup still have no save property); buffs not modeled
  (no schema impact). No new regions/fish. Pet leveling curve unchanged (4/750 anchors).
- Probes: `tests/AbioticEditor.Probes/CompanionUpdateProbe.cs` (pak survey, DT_Pets/DT_NPCList/
  ItemTable pet rows, DT_Skills/DT_SkillPerks dumps). Tests: `PetGameDataTests` (curated
  fallback for new pets, item<->class bridging, live-table load incl. mutation graph +
  compendium rows, unknown-row-in-companion-slot round-trip; live tests skip without install),
  `PetCatalogTests` extended (Lamogi family, hostile WinterSprite variants excluded, class-path
  spot checks), `PetCatalogPortraitTests` synced to real class names.
- Note: tests never call `ApplyGameData` (process-wide overlay would race parallel test
  classes); live behavior asserted via pure `PetGameData` results + `BuildVariants(provider)`.

## Round-41: Linux / Steam Deck (Proton) support + Nexus upload for it (2026-07-11)
- **Core**: `AfInstallLocator.FindSteamInstallPath` now resolves Steam on Linux/macOS
  (`~/.local/share/Steam`, `~/.steam/steam|root`, Flatpak, Snap, macOS app support; validated by
  a `steamapps` child). `SaveDiscovery.DiscoverProtonClientWorlds(libraryRoot)` probes every
  `steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/Local/AbioticFactor/Saved/SaveGames`
  (fixed sub-path per prefix, appid not hard-coded) and is wired into `DiscoverAll()` per library
  (harmless no-op on Windows). Proton worlds surface as platform STEAM with the steamid64 account.
- **CLI**: new top-level `discover` command (table + `--json`) listing `SaveDiscovery.DiscoverAll()`
  - the missing "where are my saves" entry point; paths feed the other commands. Registered first
  in `CommandTree`.
- **Pipeline** (`release.yml`): the linux-x64 `build` leg also publishes a `-p:NexusMods=true`
  variant (updater stripped, like the Windows Nexus app) zipped as `...-nexus.zip`; new
  `nexus-proton` job uploads it to the mod page as a second file, gated on `NEXUSMODS_API_KEY`
  + new `NEXUS_PROTON_FILE_ID` repo variable (create the file once by hand on Nexus, then set the
  variable). GitHub release now ships 7 zips.
- Tests: `Discovers_Proton_worlds_inside_a_compatdata_prefix` + missing-root case (627 green).
  Docs: getting-started (Proton save path + Deck tip), cli.md (`discover`).
- NOTE: verified in an isolated `git archive` copy because a concurrent session was mid-refactor
  in the shared working tree (files moved to Catalogs/Domain/Services/Infrastructure layers).

## Round-40: story rewind now clears out-of-sequence region flags too (GitHub issue #12) (2026-07-10)
- **Bug**: a player reached the Hydroplant (via a tram-network sequence break) while still in
  Cascade Labs and talked to an NPC there, which set the region's flags directly. Rewinding the
  metadata save's chapter back to Mycofields cleared the Hydroplant chapter *triggers*
  (`Dams_ReachedCentral`, `Dams_SpillwayOpen`, ...) but left the granular flags set
  (`Dams_MetElwyn`/`MetIsaiah`/`MetSwimInstructor` - the three Hydroplant survivors, exactly
  matching the "find the other survivors" objective the reporter still saw; also the pump flags),
  so the game kept reading the world as having reached the Hydroplant.
- **Root cause**: `StoryFlagSync.ClearForwardFlags` only cleared flags reachable through
  `FlagGate.DependentsOf` (the curated `QuestFlagDependencies` graph). That graph *deliberately*
  leaves any-order region steps unwired (Dams pumps, Hydroplant survivors, Security gates - see
  the comment on `QuestFlagDependencies.Direct`), since their order isn't verified against the
  wiki. That same omission meant a rewind never found them either.
  - **Fix**: new `FlagGate.FlagsPastChapter(chapterIndex, currentlySet)` clears any currently-set
  flag whose region opens strictly after the target chapter (via the existing
  `RegionChapterFor`/`AreaToChapterRow` area gate), independent of the curated graph.
  `ClearForwardFlags` now unions this with `DependentsOf`'s result. Covers any region reached out
  of sequence, not just Hydroplant.
- Test: `ClearForwardFlags_ClearsOutOfSequenceRegionFlags_NotJustDependencyGraphMembers` seeds
  Hydroplant survivor/pump flags onto an otherwise-early save and asserts a rewind to Mycofields
  clears them while earlier progress (Office/Labs) survives.

## Round-39: per-mod enable/disable UI (2026-06-19)
- **Selective mounting instead of "all or nothing":** `GameAssetProvider.CreateForPaks` no longer
  uses `AllDirectories`. It mounts base paks (`TopDirectoryOnly`) then explicitly
  `RegisterVfs`-es only the ENABLED mod paks before `SubmitKey`. Mods register after the base game,
  so an overriding mod is mounted last and wins (deterministic order, unlike the old directory scan).
- **Mod = a grouped unit:** `AfInstallLocator.FindMods` groups the `~mods`/`LogicMods` paks by file
  stem into `InstalledMod(Name, Files)` (an IoStore mod's `.pak`+`.utoc` become one entry; `.ucas`
  opens automatically). `GameAssetProvider.LoadedMods` now reports mounted mod NAMES, not file names.
- **Per-mod persistence:** `ModLoadStore` gains `DisabledMods` (a `mods-disabled.txt` set next to the
  master flag), `IsModEnabled`, `SetModEnabled`. Effective mount = master `ModsEnabled` AND
  `IsModEnabled(name)`. Default is enabled (absent name = on).
- **App:** mod controls moved out of the Game Data card into a dedicated **Mods card** (Game Data
  tab): a master "Load installed mods" switch plus one toggle per installed mod, with a
  "{loaded} of {total}" status line. Per-mod switches lock when the master is off or `ABIOTIC_NO_MODS`
  is set; any toggle reloads game data in place. `GameDataServices` exposes `InstalledMods`,
  `IsModEnabled`, `SetModEnabled`. New `Settings_Mods`/`ModsSettings_*` resx keys across en/de/es/fr.
- Tests: `ModSupportTests` adds `FindMods` grouping and a hermetic `SetModEnabled` round-trip (saves
  and restores the real file); the `LoadedMods` live test now expects enabled mod names. Full suite
  green (586 assertion tests).

## Round-38: fixtures regrouped by platform + minimal-region factory + warning hygiene (2026-06-19)
- **Fixtures regrouped by platform** under `tests/fixtures/` (see the Fixtures bullet far below for
  the full layout): `SteamSaves/` (`Config/Windows` + `SaveGames/<steamid>/Worlds` mirror the real
  `Saved/` install; `Legacy/Cascade` is the older standalone world = `CascadeDir`), `GamePassSaves/`,
  `DedicatedServerSaves/`. **Backups dropped entirely** (~847 MB -> ~205 MB); discovery already skips
  `Backups/` by name, so no test relies on them. `Fixtures.cs` locators rewritten (with pre-regroup
  fallbacks); stale path comments across tests/docs updated. Full suite green (573).
- **`WorldSaveFactory.CreateMinimalRegion(worldDir, region)`**: crafts a minimal valid
  `WorldSave_<region>.sav` for a region a save hasn't visited yet, so story / quest-flag edits that
  reference it have a real world save to target. Templated from an **embedded** near-empty region
  save (`Core/Resources/blank-region-template.sav`, a copy of the game's smallest region save), with
  `SaveIdentifier` stamped to the region; no fixture/installed game needed. Refuses overwrite,
  normalizes/validates the region token. CLI: `world add-region <world-dir> <region>`. Tests:
  `WorldSaveFactoryTests` (round-trips through the reader, normalization, overwrite/unsafe-token).
- **Entitlements review**: `ServerEntitlements` (EarlyAccess, SupportersEdition) is complete vs the
  fixtures and already round-trips unknowns dynamically. **Follow-up - `UserEntitlements`**: the
  metadata save carries a sibling map keyed by SteamID64 holding the player's recipe entitlements
  (hundreds of `recipe_*` tokens); it is NOT surfaced by any feature yet. A `UserEntitlementsFeature`
  (or generalizing `ServerEntitlementsFeature` over both maps) would close the gap - design how it
  relates to existing `GlobalRecipes` / player recipe-unlock editing before building.
- **Build hygiene**: silenced vendored-submodule warnings (CUE4Parse `CS8602/CS0169`, transitive
  `NU1903` from Microsoft.Bcl.Memory) via `submodules/Directory.Build.targets` + `NuGetAuditMode=direct`
  at the repo root, so `dotnet test` output is clean. Our `src/`/`tests/` keep full warnings + WoE.

## Round-37: mod support (mount mod paks + struct-based table discovery) (2026-06-19)
- **The blocker was one line:** `GameAssetProvider.CreateForPaks` mounted with
  `SearchOption.TopDirectoryOnly`, so mod paks in `Content/Paks/~mods` and `LogicMods` never
  mounted. Added an `includeMods` flag (threaded through `CreateForLocalInstall`): true ->
  `AllDirectories` (mounts mods), false -> base-only. New `GameAssetProvider.LoadedMods` lists the
  mounted mod paks (enumerated by `AfInstallLocator.FindModPaks`, "a pak in a subfolder is a mod").
- **Auto-load with a toggle:** new `Core/Assets/ModLoadStore` (boolean marker next to `gamepath.txt`,
  default on) + `ABIOTIC_NO_MODS=1` env override (mirrors `ABIOTIC_NO_PLUGINS`).
  `CreateForLocalInstall(includeMods: null)` honors the store.
- **Generic mod-table discovery:** new `Core/Assets/ModTableDiscovery.DiscoverTablesByRowStruct`
  finds mod/patch DataTables by matching `UDataTable.RowStructName` (mods must reuse the game's row
  struct), across ANY content root - not just `AbioticFactor/Content`. Candidate set gated by
  datatable-name shape (`DT_`/`CDT_`/`ItemTable_`/...); index built once per provider (cached).
- **Applied to every table-backed catalog** (Item, Recipe, Map, ItemUpgrade, Trait, Trader,
  Codex emails/journals/compendium/fish, SectorMap): load base table by name -> discover by its row
  struct -> merge non-conflicting rows (base wins). `SkillCatalog` deliberately NOT merged
  (positional; mod skills come via DT_Skills override). `NpcStateCatalog` is a UEnum (override-only).
  `PetCatalog` NPC-class root conversion fixed to preserve mod mount points (`/<Mod>/...` not `/Game`).
- **Mod content already degraded gracefully before this** (unknown ids show the raw id, editable,
  byte-perfect round-trip); this upgrades it to real names/icons/stats.
- **App:** `GameDataServices.ModsEnabled`/`LoadedMods`/`ModsDisabledByEnv`; Settings > Game Data card
  shows a "Load installed mods" toggle (locked off when env-disabled) + the mounted-mods line.
  New `GameDataSettings_*` resx keys across en/de/es/fr.
- **CLI:** `dump-registry` now mounts base-only (`includeMods: false`) so the bundled registry stays
  clean; other commands honor the toggle/env var via the default.
- Tests: new `ModSupportTests` (FindModPaks, `LooksLikeDataTable` gate, env override, base-only =
  no mods). Full suite green (573 assertion tests).
- Follow-ups: mod.io cache-dir mounting, per-mod enable/disable UI, `DoorClassCatalog` (curated list).

## Round-36: non-Steam identity + full Game Pass (Xbox container) support (2026-06-19)
- **Opaque player identity** (commit 149b9cc): generalized the player id from a numeric `ulong`
  SteamID64 to an opaque string across Core/CLI/App so Game Pass / Epic / non-Steam saves are
  first-class. New `Core/PlayerSaves/PlayerIdentifier` (`IsSteamId` = `^7656119\d{10}$`,
  `IsSafeFileToken`, `TryParseFromPlayerFileName`, `TryParseSteamId`). `PlayerSaveIdentity`/
  factories take `string` (ulong overloads delegate). Bed claims: `WorldDeployable.OwnerId`
  (string) primary, `OwnerSteamId` a numeric convenience; `WorldSteamIdPatcher` exact-string match,
  refuses different-length swaps. `SteamPersonaIndex` string-keyed + `ResolveDisplayName`;
  `SteamAchievements.LoadFor(string)` gates on `IsSteamId`. App add-player / change-id /
  CreateWorld accept any safe token; Achievements/customization stay Steam-only.
- **Game Pass save format fully reverse-engineered** (see memory `gamepass-save-format.md`): GP/MS
  Store saves are NOT loose `Player_<id>.sav` files - they are Xbox **wgs** (Connected Storage)
  containers (`containers.index` + GUID folders + `container.N` + blob) holding an
  **`ABF_SAVE_VERSION`** bundle (TOC + one **Oodle**-compressed stream of every world/player member;
  members are headerless GVAS bodies). Player ids inside are 16-19 digit XUIDs. Validated on a real
  dump: a GP→Steam→GP→Steam round-trip is byte-identical for all 69 saves.
- **Core `GamePass/`**: `OodleCodec` (P/Invoke compress+decompress; DLL via `ABIOTIC_OODLE_DLL` /
  game install / CUE4Parse download), `AbfSaveBundle`, `GamePassMemberCodec` + `GvasHeaderTemplates`
  (3 class headers; `ToMemberBody` locates the body by class-name marker + custom-header size so it
  works on any save), `WgsContainerStore` (read/rewrite index/container.N/blob, new generation;
  `WriteNewContainer`; `PackageFamilyName`/`IsAbioticContainerFolder`), `GamePassSaveSet`
  (open/list/read/write/`ExtractWorld`/`ApplyWorld`, folder `.bak`, zip-slip-guarded paths),
  `GamePassConverter` (Steam<->GP lossless, optional player re-home), `GamePassDiscovery` (scans both
  `%LOCALAPPDATA%\Packages\…\wgs` AND `<drive>:\XboxGames\GameSave\wgs`).
- **`AfInstallLocator.FindGamePassInstallRoot`**: auto-detects a GP install at
  `<drive>:\XboxGames\<Game>\Content`, wired into `FindInstallRoot` so game data loads for GP users.
- **CLI** `gamepass` (`list`/`extract`/`import`/`discover`/`to-steam`/`to-gamepass` with
  `--player-id`). **App**: discovery tags worlds STEAM/GAME PASS/SERVER/UNKNOWN and lists wgs worlds
  (with their folder location); opening a GP folder (Open Folder/drag-drop/discovery) extracts a temp
  working copy and the normal **SAVE** packs it straight back into the container (editors raise a
  `Saved` event; no banner/working-copy concept exposed); sidebar shows a persistent platform badge;
  Settings **CONVERT** card (Steam<->GP, optional account id, inline results); Create World writes a
  GP copy too. Player General tab shows the real owner id and locks it for non-Steam.
- **Docs**: `docs/guide/game-pass.md` (how it works, opening, locations + auto-detect, conversion,
  CLI, internals). **Fixtures**: sanitized real GP container at `tests/fixtures/GamePassSaves/` (synthetic
  XUID/ids, no PII). Tests cover identity, codec/container/bundle round-trips, the real fixture,
  Steam<->GP conversion + re-home, and platform classification. **557 tests green**. App GUI not
  screenshot-verified; the in-game accept of a written container still needs on-console confirmation.

## Round-35: custom game-install folder + Traders empty-state (2026-06-19)
- User feedback: the TRADERS tab on the metadata save "seems blank". Root cause is NOT the
  save or mods: the trader roster AND the per-trade gating flags (e.g. Dr. Carson's
  "Gears for Murkweed") come 100% from the installed game's paks (`DT_NPC_Traders` +
  `DT_NPC_TraderItems`), never from the save. Auto-detection was Steam-registry-only
  (`AfInstallLocator`), so Game Pass / Epic / moved-library / non-Steam users got an empty,
  unexplained tab. The per-trade unlock feature itself already exists
  (`WorldEditorViewModel.UnlockTraderFlagsAsync` writes the gating flag to
  `WorldSave_Facility.sav`); it was just unreachable with no roster.
- Core: `AfInstallLocator` gains `OverrideInstallRoot` (in-process), honors the
  `ABIOTIC_GAME_DIR` env var, and a tolerant `ResolvePaksDirectory(path)` that accepts the
  install root, the inner `AbioticFactor` folder, or the `Paks` folder itself (rejects a folder
  with no `*.pak`/`*.utoc`). New `GamePathStore` persists the chosen folder to
  `%LOCALAPPDATA%/AbioticEditor/gamepath.txt` so the CLI and App share one config without an env
  var. `FindPaksDirectory` resolution order: override -> env var -> GamePathStore -> Steam; a
  stale source falls through instead of disabling detection.
- App: `GameDataServices` gains a `GameDataStatus` (Ready / InstallNotFound / MappingsMissing /
  LoadFailed) + `StatusMessage`, a `ReloadAsync` (live reload of all catalogs without relaunch -
  `LoadCore`/`ResetState`), `CustomInstallPath` (backed by `GamePathStore`) and `IsGameDataLoaded`.
  Settings GAME DATA card rebuilt (`SettingsPage.BuildGameDataCard`): a status line, LOCATE GAME
  FOLDER (FolderPicker -> `ResolvePaksDirectory` validate -> persist -> live `ReloadAsync` ->
  "reopen your save"), USE AUTO-DETECT (clear + reload), plus the existing IMPORT USMAP. The
  TRADERS tab shows a localized "Game data not loaded" panel (`WorldTraders_NoData*`, en/de/es/fr)
  via `WorldEditorViewModel.HasTraderCards`/`HasNoTraderData` when the roster is empty.
- Tests: `AfInstallLocatorTests` (9) cover the three accepted layouts, blank/garbage rejection,
  and override precedence/fall-through. Core + App (net10.0-windows) build clean; NOT
  screenshot-verified.
- Static trader fallback (so the editor never NEEDS the game for trader info): `TraderCatalog`
  is now `partial`; `TraderCatalogFallback.cs` ships a generated snapshot of DT_NPC_Traders +
  DT_NPC_TraderItems (8 traders + Fili, exact ids + gating flags, no pak portraits).
  `TraderCatalog.LoadFrom` returns `Fallback` when paks/mappings are absent or the parse fails
  (was `Array.Empty`); `GameDataServices.Traders => _traders ?? TraderCatalog.Fallback`. The
  TRADERS tab now always populates; the former "no data" panel became an
  `IsTraderDataFromSnapshot` info note (`WorldTraders_Snapshot*`, en/de/es/fr) saying the trades +
  unlock flags are accurate but item names/icons need the game. Regenerate the snapshot after a
  game patch with `AbioticEditor.Probes TraderFallbackProbe.GenerateFallback` (writes
  `%TEMP%/abiotic-editor-schema/TraderCatalogFallback.cs`). Tests: `TraderCatalogFallbackTests` (4).
- Unified game-data clarity (never silently present fallback/empty data as complete): shared
  `Services/GameDataPrompt.PickAndSaveFolderAsync` (pick -> `ResolvePaksDirectory` validate ->
  persist) reused by the new surfaces. `MainViewModel` gains `GameDataMissing`, `GameDataNotice`
  (= `GameDataServices.StatusMessage`) and `LocateGameFolderCommand` (pick -> `ReloadGameDataAsync`
  -> alert); `RaiseGameDataStatusChanged` now also raises those. A global banner in `MainPage.xaml`
  (header banner stack, orange) shows whenever `GameDataMissing` with the differentiated reason +
  a LOCATE GAME FOLDER button (`GameData_BannerTitle`/`GameData_LocateButton`, en/de/es/fr).
  First-run one-time prompt in `MainPage.StartupAsync.MaybePromptForGameDataAsync` (Preference
  `gamedata_prompt_seen`, sequenced after the language chooser) offers to locate the folder. Recipe/
  Codex empty states already surface `StatusMessage`; Settings GAME DATA card already routes through
  `_vm.ReloadGameDataAsync` (left as-is). App builds clean; localization parity green.

## Round-34: world-state feature maps moved from the Settings modal into world-editor tabs (2026-06-14)
- The world-state maps (power sockets, resource nodes, NPC spawns, triggers, elevators, buttons,
  portals, trams, server entitlements, teleporter pads, ...) used to be editable only through the
  generic `WorldMapsPage` modal launched from Settings -> EDIT WORLD MAPS. They are now first-class
  world-editor tabs that look and feel like the Fish/Vehicles master-detail editors.
- App-only change (Core `Features` framework unchanged): a new generic
  `WorldFeatureTabViewModel` (+ entry/field VMs) wraps any `IWorldMapFeature`, and a single shared
  `Views/World/WorldFeatureTab.xaml` renders the master entry list + the selected entry's typed
  fields (read-only Label, Switch for bool, Picker for choice, Entry for numeric/text). Free-text/
  numeric fields commit on unfocus/return (code-behind reads the field VM off `BindingContext`);
  bools/choices apply immediately. Edits patch the live save tree through the feature and stage
  until the world editor's SAVE (keeps a .bak); REVERT restores each field.
- `WorldEditorViewModel` builds `FeatureTabs` from `WorldMapFeatures.ApplicableTo(_data.Raw)` at
  load, adds `WorldTab.Feature` + `IsFeatureTab` + `SelectedFeatureTab` + `SelectFeatureTab`, folds
  feature dirtiness into `IsDirty`, and accepts/reverts feature baselines in Save/Revert.
  `WorldEditorView.xaml` renders one dynamic tab button per applicable feature (a `BindableLayout`
  over `FeatureTabs`, each button bound to the tab VM's `SelectCommand`/`IsActive`) plus a
  `LazyView` host. The Settings WORLD MAPS card and `WorldMapsPage.cs` are removed; the CLI `world`
  group is unchanged. Builds clean (net10.0-windows); NOT screenshot-verified.

## Round-33: enforce hotbar-only pets in the inventory editor (2026-06-14)
- Bug: the player inventory editor let pests and weapon-form pets be dropped/given into the
  Main backpack, which the game forbids (pets live in the hotbar or the Companion slot only).
- Probed ItemTable_Global: every `Item.Pet` row (all 22 in `PetItemCatalog`, incl. `biocannon`
  / `Skink_Magma_Crafted` weapon forms and every `Pest_*`/`Peccary`) carries
  `EquipSlot = 21` (EquipmentSlot_Companion) and the `Item.Pet` tag. **No item carries
  EquipSlot 0 (Hotbar)** - the hotbar/backpack split is NOT encoded generically; the pet rule
  is keyed on the Companion EquipSlot. Ordinary items carry EquipSlot 1 (InventoryBackpack).
- Core: `EquipSlotTypes.Companion` (21) + `IsHotbarOnly(entry)` (== Companion EquipSlot).
- App: `InventorySlotViewModel.ValidateForSlot(kind, role, entry)` layers the role-fit check
  with "no hotbar-only item in a Main slot"; `ValidationWarning` now flags such a slot.
  Wired into `SlotInteractions` (palette drop, slot swap both ways, double-tap quick-give) and
  `MainViewModel.PickUpGroundItem` - quick-give and ground-pickup now route pets to the hotbar
  (never the backpack). The existing role check already blocked pets from non-Companion
  equipment slots; the gap was only the role-less Hotbar/Main slots. CLI `pet` was already
  correct (only places Companion/Hotbar). World-storage containers (also `InventoryKind.Main`)
  share the block.
- Tests: `EquipSlotValidationTests` gains `IsHotbarOnly_*` theory + a real-catalog assertion
  that every pet row is Companion-slot + `Item.Pet`-tagged.

## Round-32: cross-save pet movement (world PetNPC <-> player hotbar) (2026-06-14)
- A carried pet is an ordinary `Item.Pet` inventory item (not a PetNPC): in the Companion slot
  (`EquipmentInventory_[12]`), a hotbar slot, or backpack. Item rows like `Skink_Magma_Crafted`
  ("Magma Skink (Weapon)" BioCannon), `Pest_Leyak`, `Sow`. Health = `CurrentItemDurability_`;
  name = `PlayerMadeString_`; XP/`MutationProgress`/`PetMutation` in the slot
  `ChangeableData_ -> DynamicProperties_`. The item table has no NPC-class field, so the
  item<->creature bridge is by shared friendly name (`PetItemCatalog` <-> `PetCatalog`, 22 rows).
- Core: `CarriedPet` + `PlayerSaveReader.ReadCarriedPets` + `PlayerSaveData.CarriedPets`;
  `PlayerSaveWriter.ApplyCarriedPet/AddCarriedPetToSlot/RemoveCarriedPet`; `WorldSaveWriter.AddPet`
  (clone+rekey a PetNPC); `PetTransfer.WorldToPlayer/PlayerToWorld`; `PetSaveLocator` (sibling
  saves). Moved pets keep variant/name/XP, arrive at full health (per-limb<->durability is lossy).
- **Fabrication gotcha:** UE5.4 stores the enum type in the property tag's complete-type-name
  parameters AND struct arrays carry an internal prototype, so a `DynamicProperties` element can't
  be built blind. `PetDynamicProperties` reuses an existing element's tag `Type` and, for a slot
  with no array, grafts a detached clone of one (prototype intact). Verified round-trip.
- CLI: `pet hotbar` (list carried), `pet send <world> <pet> --to <player> [--companion|--hotbar]`,
  `pet grab <player> <#i> --to <world> [--x --y --z]`. App: player COMPANIONS tab
  (`PlayerPetsTab` + `CarriedPetViewModel`) lists + edits carried pets (name/variant/level/health).
- In-GUI cross-save move: world PETS tab "Send to player" (Companion/hotbar) + player COMPANIONS
  "Send to world" (placed at a pet bed when the world has one). Immediate both-file write with a
  `.bak` each, guarded on unsaved changes; sibling saves resolved via `PetSaveLocator`.
- Best-effort 1:1 health: durability = sum of world per-limb HP; on return it's distributed across
  the (same-class-preferred) clone template's limbs proportionally. Not exact (no world max-HP /
  differing limb sets) but preserves the HP total.
- **410 tests green** (+4 transfer/catalog tests).

## Round-31: comprehensive Vehicle system (2026-06-14)
- VehicleMap (region saves) was previously only the limited generic `VehicleMapFeature`
  (driveable/destroyed bools). Now a first-class system. Probed the struct: `Class_`,
  `VehicleID_` (= spawn actor path = map key), `Transform_` (Rotation quat / Translation /
  Scale3D), `ContainerInventories_` (standard slot arrays), `VehicleDriveable_`,
  `VehicleDestroyed_`. No "lock" field - "unlocked" maps to drivable.
- Core: `WorldVehicle` + `WorldSaveReader.ReadVehicles`; on-board storage surfaced as
  `WorldContainerSource.Vehicle` containers so it reuses the full slot editor +
  `ApplyContainers`. `WorldSaveWriter.ApplyVehicles` patches driveable/destroyed/transform
  (translation + rotation). `VehicleCatalog` (friendly names + wiki-image candidates, pak
  enumeration). **Reset-to-spawn**: `GameAssetProvider.TryGetActorTransform` resolves the
  `VehicleSpawn_*` actor's world transform from the cooked level via CUE4Parse (verified it
  returns sensible coords differing from the driven position); degrades when no install.
- CLI: `vehicle list/info/unlock/lock/repair/wreck/move/reset`. App: VEHICLES tab,
  Fish-style master-detail (`WorldVehiclesTab` + `WorldVehicleViewModel`) - appearance via
  wiki image, region + location, drivable/wrecked toggles, X/Y/Z move, reset-to-spawn,
  "open inventory" jumps to the CONTAINERS tab filtered to the vehicle.
- **406 tests green** (+3 vehicle round-trip/inventory tests).

## Round-30: first-class Pet system, split from NPCs (2026-06-14)
- Pets were mixed into the NPCS tab (rename + revive only). Now their own PETS tab and
  editor. PetNPC struct: `IsDead_` (only life flag), `CurrentHealthMap_` (per-limb
  `EBodyLimbs::*` -> double; no "downed" field, derived from health), `NPCClass_` (mutation
  target), `DynamicProperties_::XP` (level 0-20), `CustomName_`.
- Core: `WorldPet` + `ReadPets`/`ApplyPets`/`RemovePet`; `PetCatalog` (4 families
  Pest/Peccary/Skink/Other; curated table merged with pak-enumerated `NPC_*`; new families =
  one token); `PetHealth` (status/heal/down/revive). CLI: `pet` group. App: PETS tab,
  master-detail `WorldPetViewModel` (name, health/heal/down/revive, dead, level/XP slider,
  upgrade/downgrade variant picker, delete). Exor/Mystagogue summons shown read-only.
- NPCS tab is now narrative-NPCs-only (pet columns dropped). Assign-to-bed/companion is
  Phase 2 (needs a probe of a stored/carried pet - format not in the repo yet).

## Round-29: reader/writer validation tests (reversibility + isolation) (2026-06-14)
- User wanted explicit proof the readers/writers round-trip safely. New
  `tests/.../SaveReaderWriterValidationTests.cs` (+8 tests) asserts two properties the
  existing "mutation persists" tests don't:
  - REVERSIBILITY (byte identity): load -> change a few values -> change them back -> the
    re-serialized bytes equal the original file. Covers player skills + traits and world
    flags + a security-door open flag. Deliberately uses only patch-in-place fields (no
    ApplyStats/ApplyInventory, which materialize delta-omitted siblings on write and so are
    not reversible by design).
  - ISOLATION (surgical edit): one change -> write -> reload from disk -> diff against the
    original with `SaveComparer.CompareFiles`. Player skill XP and world flag/door edits each
    produce EXACTLY one leaf diff (1 Changed / 1 Added / 0 else) at the expected path; the
    container-slot edit asserts every diff is confined to that one container's subtree
    (ApplyContainers can materialize sparse slot fields, so it's a subtree-confinement check,
    not a single-leaf check).
- **390 tests green** (was 382). No production code changed.

## Round-28: Teleporter Pad tag editor (the real teleporter "tags") (2026-06-13)
- User pointed out the Teleporter Pad has **134 selectable tags** (wiki). Found where they live:
  a placed pad is a `Deployed_TeleporterPad_C` entry in **DeployedObjectMap**, and its tag is an
  integer `TeleporterFrequency` inside `ChangableData_` (sic) → `DynamicProperties_` (array of
  `DynamicProperty{Key:EDynamicProperty enum, Value:int}`). 0 = unassigned, 1..133 = named tags.
- **Recovered the tag order** from the wiki table read **column-major** (its columns are coherent
  groups: NATO A-M / N-Z / region names / damage types / …), then **verified against real save
  data**: every tagged pad in the ClientSaved Facility fixture resolves to a sensible region tag
  (27→Facility, 33→Power Services, 34→The Reactors, 122→Far Garden, 124→Some Distant Shore [a
  linked pair], 133→Voussoir). `TeleporterTagCatalog` holds the 133 names (134 choices incl.
  "(none)") with `Label(freq)`/`Frequency(name)`; out-of-range round-trips as `Tag #N`.
- **`TeleporterPadFeature`** (implements `IWorldMapFeature` directly - filters DeployedObjectMap to
  the pad class, digs into the nested dynamic-property array): editable `tag` (Choice over the 134)
  + `frequency` (raw int 0..133). Lossless; pads sharing a tag link. Auto-discovered, so it appears
  in CLI `world` (`world show … teleporter-pads`, `world set … tag "The Reactors"`) and the App
  WorldMapsPage with no extra wiring. CLI `show` summarises big choice lists ("134 choices, see
  --json").
- This supersedes Round-27's PortalMap note: PortalMap = fixed world teleporters (active only); the
  player-facing teleporter **tags** are the Teleporter Pad frequencies handled here.
- **Forward-compatible (for the upcoming DLC)**: a frequency beyond the known 133 is not rejected -
  it's displayed as `Unknown #N`, preserved, and the `tag` picker appends that value so it stays
  selectable (`ChoicesFor(freq)`); the raw `frequency` field accepts any non-negative int. When a
  DLC adds tags we can't name from saves, they show as Unknown rather than breaking; adding the real
  names later is a one-line edit to `TeleporterTagCatalog`.
- **+8 tests** (`TeleporterPadFeatureTests`: catalog 134/mapping, unknown-tag forward-compat, read 22
  pads, tag↔frequency edit + round-trip, future-frequency accepted, rejects). **382 tests green**;
  Core/CLI build clean.

## Round-27: editable world-state maps (Features framework + 10 maps) (2026-06-13)
- Made every previously-unmodeled world-save map editable. New **`Core/WorldSaves/Features/`**
  framework: `IWorldMapFeature` (typed `WorldMapEntry`/`WorldMapField` rows; field factories
  ReadOnly/Bool/Integer/Number/Choice; `WorldEditResult`), `WorldMapFeatureBase` (implement
  `ReadFields`+`ApplyField`; `ShortLabel`, `ResolveChoice`), `WorldMapAccessor` (public
  read/write helpers mirroring the reader/writer idiom: `Entries/FindEntry/HasMap`, `SetBool/
  SetInt/SetDouble/SetFloat/SetString/SetName/SetEnumByte/SetVector`, parse helpers), and
  `WorldMapFeatures` (**reflection-discovered registry** - drop in a class, it's auto-registered;
  `IsKnownMap` makes `WorldSaveReader.LogUnmodeledKeys` stop flagging the map as unknown). Edits
  are lossless (patch existing leaves only).
- **10 feature modules** (one per map, built by parallel agents over a verified template):
  `ElevatorMapFeature` (topOpen), `ButtonMapFeature` (pressedOnce/enabled/activated/noReset),
  `ResourceNodeMapFeature` (harvested/dayPickedUp - un-harvest to refill a node), `NpcSpawnMapFeature`
  (cooldownRemaining/spawnCount/spawnedOnce/…), `TriggerMapFeature` (timesTriggered),
  `VehicleMapFeature` (driveable/destroyed + read-only class/inventory count),
  `PowerSocketMapFeature` (hasTimer editable; timerMode read-only - only one enumerator observed,
  full E_PowerTimerModes set unknown), `PortalMapFeature` (active), `TramMapFeature` (read-only
  viewer: station + inventory count), `ServerEntitlementsFeature` (metadata; comma-separated
  per-SteamID entitlement list, array-replace round-trip).
- **PortalMap "tag" finding (user asked to edit teleporter tags from a built-in list)**: the saved
  `SaveData_PortalStruct` has ONLY `ActorPath_` + `PortalActive_` - **no tag/channel leaf**, verified
  across Facility/Salem. Teleporter linking is NOT in PortalMap: the handheld Personal Teleporter
  syncs via the item's `PlayerMadeString_` = target bench's DeployedObjectMap GUID (see
  `TeleporterLinkTests`); placed teleporter labels are `Deployed_Sign_C` `PlayerMadeString_` text;
  deployables carry an (empty in saves) `GameplayTags_` container. No built-in allowed-tag vocabulary
  exists in saves/tables. So portals ship `active` only; the tag editor is deferred until a real
  DeployedObjectMap teleporter-tag vocabulary is sourced (documented in `PortalMapFeature` XML).
- **CLI** `world` group (`Cli/Commands/WorldCommands.cs`, generic over the registry): `world list
  <save>` (features present + entry counts), `world show <save> <feature> [--json] [--limit]`
  (entries + fields), `world set <save> <feature> <#index|key|substring> <field> <value> [--dry-run]`
  (writes with .bak). Verified live on a Facility copy (list 9 features, show elevators, dry-run,
  real button edit + .bak).
- **App**: `WorldMapsPage` modal (Settings → EDIT WORLD MAPS) - generic over the registry: pick
  world save → pick feature → virtualized entry list → tap an entry → typed field editors (Switch/
  Picker/Entry) → SAVE (writes .bak). Builds clean (net10.0-windows); NOT screenshot-verified.
- **+47 tests** (`tests/.../Features/*` - read/edit/round-trip/reject per feature). **374 tests
  green**; Core/CLI/App build clean (new files 0-warning).

## Round-26: self-updater (new AbioticEditor.Updater project) + log fix (2026-06-13)
- **New project `src/AbioticEditor.Updater`** (net10.0, zero deps - no MAUI/Core/CUE4Parse) so
  both the CLI and the app can reference and bundle it. Talks to the GitHub Releases API,
  downloads the matching asset, and replaces the running install IN PURE MANAGED CODE.
  - Pieces: `UpdaterOptions` (defaults to the real repo coords `ChristopherVR`/`AbioticEditor`;
    blanking/sentinel owner re-flags it unconfigured; `ForCli()`/`ForApp()` presets pick assets by
    keyword `cli|app` + `win-x64`); `GitHubReleaseClient` (releases/latest or list, System.Text.Json,
    User-Agent required); `ReleaseVersion` (lenient semver parse + compare, pre-release aware);
    `AssetSelector` (all-keywords-match, installable-ext preferred); `UpdateChecker`/
    `UpdateCheckResult` (status: UpdateAvailable/UpToDate/NoReleases/NoMatchingAsset);
    `UpdateInstaller` (download w/ IProgress, zip extract + single-root flatten); `AppUpdater`
    (the one facade hosts use). `IUpdaterLog` bridges diagnostics to each host.
  - **Self-replace is script-free** (user asked: no .cmd/.sh, avoid admin/exec-policy). Uses the
    Windows rename-in-place trick - a loaded exe/DLL can be RENAMED (not overwritten), so
    `InPlaceReplacer` moves each in-use file aside to `*.old-update`, drops the new file in,
    relaunches in managed code, and the host exits. `UpdateCleanup.Run` (called at CLI `Main` /
    App ctor startup) sweeps `*.old-update` and finishes any `*.pending-update` deferred when a
    target was locked. Bare installers (.msi/.exe) are just launched instead.
- **CLI** `update` command (`Commands/UpdateCommand.cs`, registered in CommandTree): `update`/
  `update check [--json] [--pre]` report; `update install [-y] [--pre] [--relaunch]` downloads +
  applies (CLI defaults to no relaunch). Honours `GITHUB_TOKEN`. With the placeholder repo it
  exits 1 with a clear "not configured yet" message (verified live).
- **App** `Services/UpdateService` (bridges to EditorLog, MAUI-thread apply+`Quit`) + an **UPDATES
  card** in SettingsPage (CHECK FOR UPDATES -> status; DOWNLOAD & INSTALL appears when newer;
  confirm dialog -> progress -> restart). App ctor calls `UpdateService.RunStartupCleanup()`.
- Wired into slnx, CLI/App/Tests csproj. **16 new offline tests** (`UpdaterTests`: version parse/
  order, asset selection, in-place replace + cleanup, placeholder detection). **327 tests green**;
  Updater builds 0-warning; CLI + App(win) build clean.
- **Log fix (user-reported)**: the Edit diagnostic logged the whole `ItemCatalogEntry` via the
  record's auto `ToString`, which renders collection members as type names (`Tags = System.String[]`,
  `AllowedLiquids = List`1[System.Int32]`). Added a concise `ItemCatalogEntry.ToString()` override
  (`shelf_m (Medium Shelf)`) - the general fix for any log that prints an entry.
- **World-save "unknown" review (user-requested)**: `WorldSaveReader.LogUnmodeledKeys` logs every
  top-level world property not in `ConsumedPrefixes` as "unmodeled ... not editable" (still
  preserved verbatim on save). Enumerated the real unmodeled keys from fixtures: metadata =
  `ServerEntitlements`; Facility/region = `ResourceNodeMap` (169 harvest nodes,
  SaveData_Resource_Struct), `PowerSocketMap` (223, SaveData_PowerSockets), `ButtonMap`,
  `NPCSpawnMap`, `PortalMap` (BP_Teleporter actors, SaveData_PortalStruct), `VehicleMap`
  (SaveData_Vehicle_Struct - fuel/inventory/pos), `ElevatorMap`, `TramMap`, `TriggerMap`. All are
  per-actor state maps keyed by actor path/GUID. Candidates worth surfacing as editable later
  (highest value first): VehicleMap (fuel/inventory), ResourceNodeMap (reset/refill nodes),
  PowerSocketMap (power on), ButtonMap (toggle), ServerEntitlements (admin list). Not implemented
  this round - flagged for follow-up.

## Round-25: Compare - domain-aware summary-first diff + segmented mode (2026-06-13)
- User wanted Compare to read like the rest of the app ("save 1 has Fish A with its image, save 2
  doesn't"), summary-first then deep-dive, with raw still available, and the mode picker a proper
  toggle.
- **`Views/SaveSemanticDiff.cs`** (`PlayerSemanticDiff`): builds a human-readable diff of two
  PLAYER saves reusing the editor catalogs. Sections: PROGRESSION (money + per-skill level as
  `A → B` rows), and set-difference categories Recipes / Fish / Traits / Items discovered / Items
  crafted / Maps / Journals / Emails - each as ONLY-IN-A (red) / ONLY-IN-B (green) chips. Chips
  resolve display name + icon: items via `ItemCatalog`, recipes via `RecipeInfo.CreatesItemId`,
  fish via `FishDefinition.ItemId`, traits via `TraitDetails`; icons extracted lazily off-thread
  (`provider.ExtractTextureByGameRef` + `IconColorizer`, same path as inventory slots).
- **ComparePage** rewired: a leading **WHAT'S DIFFERENT** overview card (one line per changed
  category) → per-category cards → a collapsed **RAW PROPERTY DIFF** card (the old leaf list +
  noise switch) for the deep-dive. Two player saves get the semantic view; any other pairing (or
  non-player) falls back to raw expanded. Catalogs are ensured loaded before comparing so
  names/icons resolve. Mode picker is now a real **segmented toggle** (`ModalChrome.Segmented`)
  instead of two buttons.
- Build clean to temp output (0 errors). NOT screenshot-verified.
- **World semantic diff (follow-up, same session)**: `WorldSemanticDiff.Build(WorldSaveData a, b)`
  mirrors the world editor tabs - PROGRESSION (story chapter + time-played scalars), GLOBAL RECIPES
  (set-diff w/ item icons), QUEST FLAGS (set-diff via `QuestFlagCatalog.Lookup` friendly names),
  DOORS (lock/open-state changes matched by id → scalars), GROUND ITEMS (dropped-item set-diff w/
  icons), NPCS (state / alive→dead changes), WORLD CONTENTS (container/object/ground/NPC count
  scalars). Reuses the shared `SemanticSection`/`RenderSection`. ComparePage's `TryBuildSemantic`
  now resolves two player saves → PLAYER sections, else two world saves
  (`WorldSaveReader.ReadFromFile`) → WORLD sections, else raw-only; `ShowFileResult` takes a
  `(Kind, Sections)?` and labels the summary card. Build clean (0 errors). NOTE: a concurrent
  UPDATES feature (`AbioticEditor.Updater` project + `Services/UpdateService` + Settings UPDATES
  card) is being wired by the user - left untouched.

## Round-24: Settings + Compare sheets restyled to the game look (2026-06-13)
- User: the Settings/Compare modals "don't look anything like the main game" UI. Both were bare
  code-built stacks (default buttons, plain section labels on the page bg). New shared
  `Views/ModalChrome.cs` gives the code-built sheets the facility look: `Scaffold(eyebrow, title,
  cards, footer)` = branded header (AF badge + amber eyebrow + `AfH1` title) + hazard stripe +
  centred scroll column of `Card`s + a sticky `AfChrome` footer bar; `Card(header, hint, body…)`
  = `AfPanel` border with an amber `AfFieldLabel` + `AfMuted` hint; `Button(text, primary)` =
  primary fill or `AfGhostButton`.
- **SettingsPage** rebuilt: THEME / DIAGNOSTICS / SPOILERS / GAME DATA / PLUGINS / ABOUT each a
  panel card; theme accent buttons act as a segmented control (active = filled + ✓, inactive =
  ghost); switch rows use `AfFieldValue`; CLOSE in the footer. **ComparePage** rebuilt: MODE /
  SOURCES (A/B sub-labels) / RESULTS cards; mode buttons segmented; `DiffDetailPage` drill-down
  uses the same scaffold. Logic (theme apply, plugin toggles, compare/diff, folder drill-down)
  unchanged - only the chrome.
- App builds clean to temp output (0 errors). NOT screenshot-verified (user holds the app).
  PluginsPage still uses the old plain style - left for a follow-up unless asked.

## Round-24: switch-regression fix + localization + self-contained publish + drop fixes (2026-06-13)
- **Save-switch regression fixed**: the 150ms selection debounce (round-23-era) made single
  player-save clicks feel laggy. Replaced with serialize-and-coalesce (`RequestSwitchAsync`):
  load IMMEDIATELY when idle (instant single click), and while a load runs just update
  `_pendingSelection` (no concurrent parses, only the settled save loads). Removed the fixed delay.
- **Localization (multi-language)**: `Localization/AppResources.resx` (en neutral) + es/fr/de
  satellite resx (build confirmed es/fr/de/AbioticEditor.App.resources.dll produced).
  `Services/LocalizationResourceManager` (observable indexer; raises "Item[]" on culture change →
  live re-localize), `Controls/LocalizeExtension` (`{loc:Localize Key}` markup), `Services/
  LocalizationService` (OS-default via CurrentUICulture→shipped code, persist Preferences
  "AppLanguage", ApplyStartup/SetLanguage). `LanguagePage` (code-built, rebuilds live on pick) shown
  first-run from `MainPage.StartupAsync` when `!HasChosenLanguage`; reachable from a new Settings
  LANGUAGE card. `App` ctor calls `LocalizationService.ApplyStartup()`. Wired the Settings LANGUAGE
  card + LanguagePage strings; rest of the UI is incremental (mechanism + keys in place).
- **Self-contained publish**: `Properties/PublishProfiles/win-x64.pubxml` - SelfContained +
  **WindowsAppSDKSelfContained** + WindowsPackageType=None + ReadyToRun → install-free contained
  folder. (Literal single .exe isn't reliable for WinUI; documented in the pubxml.)
- **Drop-item fixes** (user): (1) dropped item now appears in NEARBY GROUND ITEMS immediately -
  `DropActiveItemAsync` inserts a staged `GroundItemOption { IsStaged=true }` (was only visible
  after SAVE); SAVE replaces it with the real disk entry, REVERT clears it; picking up a staged
  entry cancels the pending drop (matched by the shared slot reference) instead of staging a
  removal. (2) DROP button now `IsEnabled="{Binding ActiveSlot.IsEmpty, Converter=BoolNegate,
  FallbackValue=False}"` - disabled when no occupied slot is selected.
- Build: Release verified via temp `-o` output (the user's running instance locked bin). NOT
  runtime-verified (user mid-game). To pick up these changes: close the app, rebuild Release,
  relaunch. First-run language prompt only shows when no language has been chosen yet.

## Round-23: editor-host restructure - bounded viewport + per-tab scroll (2026-06-13)
- Continuing the perf work ("clunky on every tab click, no smoothness, even light tabs"). Root
  cause beyond lazy tabs: the ENTIRE editor lived in one page-level `ScrollView` (MainPage col 2),
  and tabs toggled `IsVisible` inside it - so every switch re-measured a giant scroll content, and
  every `CollectionView` inside that scroll got infinite height (virtualization dead → realizes all
  rows). User chose "do A" (the structural fix).
- **MainPage**: col-2 `ScrollView>VSL` → `Grid RowDefinitions="Auto,*"`: row 0 = fixed header
  (title, SAVE/REVERT, loading/error/compat banners, no longer scrolls); row 1 = bounded editor
  region. The 4 editors overlap there gated by IsVisible. Player/World are tabbed; IniEditor +
  EmptyState wrapped in their own `ScrollView` (gated HasIniEditor / HasNoEditor).
- **PlayerEditorView**: `VSL` → `Grid RowDefinitions="Auto,*"`: row 0 = horizontal tab bar; row 1 =
  tab host (Grid; the 11 LazyViews overlap, only active visible, fill the cell). Each LazyView's
  ContentTemplate now wraps the tab in its OWN `ScrollView`. Height chain `*→*→Fill→*` propagates
  from the window so each tab's ScrollView is a bounded viewport - switching re-lays-out only the
  open tab, not the whole editor. `VerticalOptions=Fill` on the ContentView.
- Build clean (Debug + Release, net10.0-windows). **NOT runtime-verified** - user was mid-game
  (fullscreen); could not screenshot the app. MUST verify: player editor still renders (header top,
  tab bar, tab content fills + scrolls), tabs show content, layout not clipped. Risk: structural
  layout change; revert path = restore the single outer ScrollView.
- FOLLOW-UP (not done): heavy tabs (Recipes ~600 rows, Codex, Skills/Character/Achievements) still
  wrap their CollectionViews in the per-tab ScrollView → still realize all rows. To fully virtualize
  those, their lists must move into a bounded `*` row (not inside a ScrollView). World editor wrapped
  whole in a ScrollView (bounded region) but not yet per-tab.

## Round-22: add-player, sidebar search, Material nav header (2026-06-13)
- **"+" add-player on the PLAYERS group header** (`FileSidebarView`): asks new-blank vs copy
  the selected player, prompts a 17-digit SteamID64, writes a fresh `Player_<id>.sav` into the
  world's PlayerData and selects it. Flow in `MainViewModel.AddPlayerAsync` /
  `ResolvePlayerDataDir` / `PromptForNewSteamIdAsync` / `LoadBlankPlayerTemplateAsync`. Button is
  in the group-header DataTemplate, so it fires via a code-behind `Clicked` (BindingContext VM)
  not an x:Reference across the template namescope.
- **Core player-creation** (`Core/PlayerSaves`): `PlayerSaveFactory` (`ResetToBlank` reuses the
  PlayerSaveWriter Apply* methods to zero money/skills/health-full and empty every unlock/
  compendium/inventory list; `BuildBlankTemplate`; `CreateFromTemplate`). `PlayerSaveWriter.
  ClearAllInventory` clears slots to the `Empty` sentinel. `PlayerSaveIdentity` refactored:
  `CloneToNewId` (copy keeping source, for "copy existing"), shared `WriteAs`, public
  `StampIdentifier`. **Blank template bundled** as `Resources/Raw/blank-player-template.sav`
  (MauiAsset, generated once from a Cascade fixture via PlayerSaveFactory). New-from-template
  is structure-from-the-bundled-blank; copy-existing keeps progress.
- **Sidebar search filter** (`MainViewModel.SidebarFilter` + `MatchesFilter`): one box filters
  both save rows (display/owner/kind/filename) and config files, case-insensitive. Filtered
  config via `VisibleConfigFiles`; PLAYERS group stays visible once a folder is loaded so its
  "+" is always reachable. Search box + clear "×" added to `FileSidebarView` header.
- **Dialog text input**: `DialogViewModel.PromptAsync` + `ShowInput/InputText/InputPlaceholder`;
  Entry added to `DialogHostView` (used for the SteamID prompt).
- **Material nav header** (`HeaderBarView`): top-right reworked to Material conventions - round
  icon buttons (home/folder/build glyphs) for HOME + the two pane toggles with hover states, a
  divider, then a filled primary "OPEN FOLDER" (folder_open glyph + label). Bundled **Material
  Symbols Outlined** font (`Resources/Fonts/MaterialSymbolsOutlined.ttf`, registered as
  `MaterialSymbols`). OPEN FOLDER is now a tapped Border (`OnOpenFolderTapped`).
- **Verified**: Core builds; App builds clean to temp output (0 errors, only pre-existing
  warnings); **311 tests green** (+3 `PlayerSaveFactoryTests`: blank wipes progress + reparses,
  create-from-template writes an owned player + refuses overwrite, clone keeps source + copies
  progress). NOT yet screenshot-verified (user holds the running app) - the in-game validity of
  a fabricated blank player should be confirmed in-game (a `.bak` is kept on every write).
- **UI refinement pass (same round, user feedback)**: (a) the "+" is now a clean circular
  `Border` with a Material `add` glyph (was an odd-shaped Button); (b) search box restyled -
  Material search/clear glyphs, fixed 38px height, and on Windows the native TextBox chrome
  (border + pale fill + hover/focus brushes) is stripped via `OnSearchEntryHandlerChanged` so it
  reads as part of the dark container; (c) footer COMPARE/SETTINGS are Material icon+label tonal
  buttons (hover state) instead of tiny ghost buttons; (d) **pane toggles moved out of the nav
  bar onto edge rails** - the header keeps only HOME + OPEN FOLDER; each side pane has a 22px
  vertical rail (`FileToggleRail`/`SlotToggleRail`) with a chevron that flips to point "collapse"
  vs "expand" (`ResponsivePaneController.UpdateRailGlyphs`, kept in sync across toggle/resize/
  drawer transitions). HeaderBarView's FilesToggleRequested/ToolsToggleRequested events removed.
- **2nd refinement pass (more feedback)**: rails were too subtle and not resizable -> replaced
  with **visible draggable splitters** (`FileSplitter`/`SlotSplitter`, 16px, `AfPanelElevated`
  strip with a centred grip + the chevron toggle on top). Drag the grip to resize: `PanUpdated`
  -> `ResponsivePaneController.Begin/UpdateFileResize` / `Begin/UpdateSlotResize` clamp the
  pane's `WidthRequest` ([220,600] file / [260,680] slot); the editor is the `*` column so the
  main pane resizes with them ("resizable stacks"). Grips use an opaque bg (this MAUI build
  doesn't hit-test `Transparent` - same reason the drawer scrim uses #000+Opacity0). Drawer-exit
  now restores the user's chosen widths. Footer COMPARE/SETTINGS restyled again into **outlined
  pills** (1px border, accent-orange Material icon, hover fills + accent border) - the flat tonal
  look read as "basic".

## Round-21: UI performance pass - lazy tabs + lighter render (2026-06-13)
- User: switching tabs / scrolling / typing / resizing all feel slower than a browser; fix perf
  WITHOUT removing features. (Data/parse hot paths already optimized in research-perf-review.)
- **Root cause**: ALL tabs were realized into the visual tree at once - `PlayerEditorView`
  instantiated all 11 `<player:*Tab/>` and `WorldEditorView` all 10 world tabs, toggling only
  `IsVisible`. Every layout pass (scroll/resize/keystroke remeasure) + every binding update kept
  the whole tree for every tab live (incl. 388-line Inventory, 299-line Codex).
- **Fix `Controls/LazyView.cs`**: a ContentView that builds its `ContentTemplate` (a DataTemplate
  wrapping the real tab) only on first `IsActive=true`, then keeps it; collapsed until activated.
  Wrapped every player + world tab in `LazyView IsActive="{Binding ...IsXTab}"`. Now only opened
  tabs join the live tree. Tabs are self-contained compiled ContentViews (own namescope) so the 4
  using `x:Reference` (Character/Codex/Recipes/WorldFlags) are unaffected by the template boundary;
  BindingContext flows by inheritance.
- Render: reveals now a quick ~120ms fade+rise (dropped the relayout-forcing scale tween in Fx.cs,
  210→120ms); panel drop-shadows removed (19 GPU composition shadows → 0); global hover-lift
  removed (scale-on-hover forced per-row relayout). Snappy > smooth.
- Build clean (net10.0-windows). **Runtime click-through NOT yet verified** (user mid-game,
  fullscreen). Low risk (XAMLC validated; standard lazy-template pattern); one-line revert if a
  tab shows blank. Biggest USER-controlled lever: run a **Release** build (Debug WinUI is far slower).

## Round-20: quest-flags tab simplification + dialog fix (2026-06-13)
- **Quest flags tab (`WorldFlagsTab.xaml`) now mirrors the story-aspects pattern**: shows ONLY
  the flags actually stored in THIS save (the world's reached quest flags), grouped by story
  region. Removed (per user): the ACTIVE/MISSING counts + SHOW MISSING checkbox, the category
  filter chips (ALL/TUTORIAL/QUEST/…), and the "ADD STORY FLAGS UP TO" picker row. Kept the
  text filter, ADD FLAG, the HOW QUESTS WORK help, and per-row category badge; per-row button
  relabelled TOGGLE→CLEAR (every shown flag is active) and the redundant ACTIVE status badge
  dropped. Header now just shows an IN THIS FILE count.
- **VM cleanup (`WorldEditorViewModel`)**: removed `ShowInactiveFlags`, `CategoryFilter`/
  `SetCategoryFilter`/`ClearCategoryFilterCommand`/`AllCategories`, `MissingFlagCount`, and the
  whole story-flag batch-add block (`StoryFlagTarget(s)`/`AddStoryFlagsCommand`/
  `AddStoryFlagsUpToTarget`). `UnfilteredFlagItems` now builds from `Flags` only (no catalog/
  inactive branch); `ApplyFlagFilter` dropped the category predicate. Prereq cascade still
  enforced on toggle and via `EnablePrerequisitesForSelectedFlag` (sidebar detail). Code-behind
  trimmed to just InitializeComponent.
- **Unsaved-changes dialog (`DialogHostView.xaml`)**: the 3 buttons (Cancel / Discard changes /
  Save and continue) overflowed the 460px card and wrapped, stranding the affirmative on its own
  right-aligned line. Widened the card to 520 so they sit cleanly on one right-aligned row.
- App builds clean (net10.0-windows, 0 errors; warnings all pre-existing). Not screenshot-
  verified (user holds the running app).

## Round-19: UI smoothness/refinement pass (keep game theme) (2026-06-13)
- User wanted it to feel smoother/more fluid (ShadCN-ish) but KEEP the Abiotic theme. Global
  stylesheet + motion only (low risk; user holds the running app so not screenshot-verified yet).
- **Motion** (`Controls/Fx.cs`): new `Fx.HoverLift` attached property animates a subtle scale
  (1.0↔1.015, 130ms CubicOut) on pointer hover - MAUI VSM hover snaps instantly, this eases it;
  applied app-wide via the `AfSidebarItem` style (all list/sidebar rows). Tracks its recognizer
  via a private attached BindableProperty (PointerGestureRecognizer is sealed; no CommandParameter).
  Desktop-only (no-op on touch). `Fx.Reveal` refined: fade + rise + settle with a subtle scale
  (0.99→1) over 210ms.
- **Buttons** (`AbioticStyles.xaml`): primary hover no longer flips orange→hazard-yellow (jarring
  hue jump) - now a gentle grow (scale 1.02) + slight brighten; press gentler (0.97); dropped the
  dark 1px border; radius 4→6. Ghost/tab/chip radius→6, press scales softened.
- **Visual**: panel shadow softened (opacity 0.35→0.18, radius 14→20, offset 0,3) for a calm
  modern elevation; consistent 6px control radius.
- **Typography**: tightened dated letter-spacing (H1 6→2, H2 4→1.5, field labels 4→1.5, status
  2→1); digital-7 readouts + wordmark (brand) left untouched.
- App builds clean (net10.0-windows). Needs a quick visual pass when the desktop is free.

## Round-18: user-reported bug fixes (2026-06-13)
- **Sidebar listed Backups saves**: `SaveFolderScanner.Scan` recursed into `Backups/` (AllDirectories).
  Now excludes any path with a `Backups` segment (helper `IsUnderBackups`); `SaveDiscovery.AddIfWorld`
  also ignores Backups when counting saves / computing LastPlayed. Test: `SaveFolderScannerTests`.
- **Trader "available from the start" was wrong** (Jimmy Sanders is post-game): most traders carry no
  `RequiredWorldFlags` in DT_NPC_Traders, so the editor can't infer gating from flags. Added a curated
  `Unlock` field to `TraderLore.Entry` (e.g. Jimmy: met in Botanical Garden, only trades AFTER beating
  the game at the Taco Mine) and `TraderCardViewModel.AvailabilityText` now shows it instead of
  "Available from the start". Added `Unlock`/`HasUnlock` to the card VM.
- **Trader barter clarity**: trader detail card now has explicit "WHAT THEY ACCEPT AS PAYMENT" (was a
  small muted line) + a barter note + "WHAT THEY SELL" header over the stock list (`SlotSidebarView.xaml`).
- **Drop item now reaches the world ground**: `WorldSaveWriter.AddDroppedItem` clones an existing
  `DroppedItemMap` entry (whole-save round-trip → independent copy), re-keys it with a fresh GUID
  (format-matched), swaps in the item slot + player location + NoDespawn, and appends it. Returns null
  when there's no entry to clone (never fabricates from scratch). `PlayerEditorViewModel.PendingGroundDrops`
  + `CommitGroundDropsAsync` (mirrors pickup) commit on player SAVE; `MainViewModel.DropActiveItemAsync`
  picks the region (else Facility) save that has a clonable entry off-thread, stages the drop, clears the
  slot; `GroundDropsCommitted` refreshes NEARBY GROUND ITEMS. DROP button/tooltip restored.
  Test: `DroppedItemWriterTests` (clone + write + re-read round-trips with correct id/location/slot).
  NOTE: structurally round-trip-verified; user should confirm in-game (a .bak is kept on every write).

## Round-17: plugin web tools (HTML/React) + host-UI bridge + Vite sample (2026-06-13)
- **`IWebTool` capability** (SDK `Ui/IWebTool` + `WebToolContent` + `IWebToolContext`; registry
  `AddWebTool`; `webTool` token): a plugin renders an HTML page (incl. React) in a MAUI WebView.
  Wired through PluginRegistry/Descriptor/Manager like the other capabilities.
- **WebView host + bridge** (`App/WebToolHostPage`): renders inline HTML (bridge prepended) or a
  directory-served bundle (relative `rootDirectory` resolved against the plugin folder; bridge
  injected on Navigated). Bridge = custom-scheme nav (`abiotic://request?...`) intercepted in
  `Navigating`, routed to `IWebTool.HandleMessageAsync`, Promise resolved via EvaluateJavaScript.
  Page gets `abiotic.request()/log()/onEvent()`. Surfaced in PluginsPage WEB TOOLS section.
- **JS `abiotic.registerWebTool`** (`JsWebTool` + `JsWebToolContext.playerSummaryJson()`).
- **Host-UI bridge** (`IHostUi` + `NullHostUi` in SDK; `IPluginHost.Ui`; Core
  `PluginHostEnvironment.HostUi`; App `AppHostUi` marshals to UI thread; JS `abiotic.ui`):
  plugins drive the app via `showAlert/confirm/toast`, `runSaveOperation(id)` (runs through the
  backup/write path + reloads), `reloadSave`, `openSettings/openPlugins`. Installed in App ctor
  via `PluginService.InstallHostUi`. CLI/tests get the no-op NullHostUi.
- **3 web JS samples**: `ReactDashboard` (inline React from CDN), `WebStats` (offline HTML in a
  bundled `web/` folder), and **`ReactAppDashboard`**, a real Vite+React project (`app/`:
  package.json, vite.config base:'./' + vite-plugin-singlefile, src/*) built to a single
  self-contained `dist/index.html`; its React UI reads the save AND drives the app (Max-skills
  button → `abiotic.ui.runSaveOperation`, toast button → `abiotic.ui.toast`). `npm run build`
  verified (147KB inlined, no external scripts → file:// safe).
- `WebToolContext` re-reads the save per request (live dashboard sees edits). NOTE: the user
  concurrently added an `ISaveUpgrader` capability (saveUpgrader token), integrated alongside.
- **Verified**: Core/CLI build clean; App builds clean to temp output; Vite app builds; CLI loads
  all web samples (ReactAppDashboard registers its save op). **306 tests green** (+ web-tool
  registration/bridge round-trip, JS→app-UI bridge: showAlert/toast/runSaveOperation reach host).

## Round-16: plugin events + menu actions + JavaScript runtime (2026-06-13)
- **SDK additions** (`Plugins.Abstractions`): `Events/PluginEvent` + `PluginEvents` constants
  (`app.started`/`save.opened`/`save.closed`/`save.written`); `Ui/IMenuAction` (+context,
  `NotifyAsync`); `IPluginRegistry.AddMenuAction` + `AddEventHandler(name, Action<PluginEvent>)`.
  Manifest gained `runtime` (`dotnet`|`javascript`) + `entryScript`; `PluginRuntimes` +
  MenuAction/EventHandler capability tokens.
- **Core event hub**: `PluginRegistry`/`PluginDescriptor` carry MenuActions + EventHandlers
  (`PluginEventSubscription`); `PluginManager` aggregates `MenuActions` and adds
  `RaiseEvent(name,data)` (snapshots matching handlers, invokes each isolated in try/catch).
  Hosts raise events: CLI `plugins run` raises `save.written`; App raises `save.opened`/
  `save.closed` in `LoadEditorForAsync` and `app.started` in PluginService.Initialize.
- **JavaScript runtime** (`Core/Plugins/Scripting/`, pkg **Jint 4.4.1**, pure-managed → all
  TFMs): `JavaScriptPlugin : IAbioticPlugin` runs the `.js` on a bounded engine (recursion/
  timeout/statement caps, case-insensitive member access so JS uses camelCase) and exposes the
  `abiotic` API (log, registerSaveOperation/Command/MenuAction, on(event)). `JsCapabilities.cs`:
  JS-backed `ISaveOperation`/`IConsoleCommand`/`IMenuAction` + context facades + `JsPlayerSave`
  (money get/set, setAllSkillLevels). `PluginManager.CreatePlugin` dispatches on runtime. JS
  plugins need NO build step. `JsRuntime` serializes engine access (Jint is single-threaded).
- **App**: SettingsPage PLUGINS section gained an inline enable/disable switch per plugin (plus
  MANAGE PLUGINS); `PluginsPage` gained a MENU ACTIONS section; `MainPage.BuildPluginMenu` adds
  a real "Plugins" MenuBarItem of menu actions; `PluginService` exposes MenuActions +
  `CreateMenuActionContext` (NotifyAsync via dialog) + raises app.started.
- **JS sample `plugins/HelloScript/`** (plugin.js + plugin.json, no csproj): `rich-player` save
  op (uses `ctx.player`), `js-greet` command, `say-hi` menu action, `save.written` handler.
- **Docs/README**: full README plugins section (managed + JS usage, events, menu, settings);
  `docs/plugins.md` + `plugin-authoring.md` extended (events table, menu, JavaScript).
- **Verified**: Core/CLI build clean; App builds clean to temp output. CLI proven on the JS
  plugin: `plugins list/info`, `js-greet`, `rich-player` write+`.bak`+idempotent. **295 tests
  green** (+4: JS load registers all caps, JS save op edits+persists money on disk, RaiseEvent
  dispatches to a JS handler, throwing handler isolated).

## Round-15: PLUGIN SYSTEM (Core + CLI + App) + 4 samples (2026-06-13)
- **New SDK project `src/AbioticEditor.Plugins.Abstractions`** (net10.0, no MAUI / no
  System.CommandLine; refs only UeSaveGame). Host-agnostic contracts plugin authors compile
  against: `IAbioticPlugin` (single entry, `Configure(registry, host)`), `IPluginHost`/
  `IPluginLog`/`IPluginRegistry`, `PluginManifest` (+`PluginCapabilities` tokens), and three
  capability interfaces: `ISaveOperation` (+context/result/params, `SaveKind`), `IConsoleCommand`
  (+neutral arg/option/context), and `IWebTool` (HTML/React UI). CA1716 on `Error` suppressed
  (GlobalSuppressions, justified).
- **Hosting in `Core/Plugins/`**: `PluginPaths` (user `%LOCALAPPDATA%\AbioticEditor\plugins`
  + bundled `<exe>\plugins`; `ABIOTIC_PLUGINS_DIR` override; per-plugin data dir),
  `PluginManifestIo` (parse/validate/persist plugin.json; never loads code; strict on id +
  bare-filename entryAssembly), `PluginLoadContext` (collectible ALC; unifies the editor
  contracts AND anything already loaded in Default),
  `PluginManager` (two-phase discover→load, `Shared` singleton, aggregates capabilities,
  `EnsureLoaded(hostKind, shouldLoad?)`), `PluginDescriptor`/`PluginLoadState`,
  `SaveKindDetector` (header-only class→`SaveKind`), `SaveOperationRunner` (load→kind-check→
  required-params→execute→ backup+write ONLY if `MarkChanged()` and not `--dry-run`; the one
  dangerous path, kept out of plugins). Added `PlayerSaveReader.ReadFrom(SaveGame)` so ops/UI
  build typed data over the already-loaded save (data.Raw IS the instance the host persists).
- **CLI**: `plugins list/info/run` (`PluginsCommands`) + `PluginCliBridge` adapts
  `IConsoleCommand`→System.CommandLine; `CommandTree.RegisterPluginCommands` grafts plugin
  verbs at root (collision-guarded, `ABIOTIC_NO_PLUGINS=1` to skip; CLI skips UI-only plugins).
- **App**: `Services/PluginService` (static, loads on startup in App ctor; runs ops via
  runner; builds web-tool contexts from the selected save path),
  `PluginsPage` (modal: installed list w/ enable toggle, SAVE OPERATIONS run against the open
  save then `MainViewModel.ReloadSelectedSaveAsync()`), entry from SettingsPage "MANAGE PLUGINS".
- **4 samples in `plugins/`** (shared-assembly rule: `Private=false ExcludeAssets=runtime`, so
  output = own DLL + plugin.json): `MaxSkills` (ISaveOperation, player, `--param level`),
  `SaveStats` (IConsoleCommand `save-stats <save> [--json]`) and web-tool samples.
- **Docs**: `docs/plugins.md` (architecture + justification + security/trust),
  `docs/plugin-authoring.md` (how-to + checklist); README + slnx updated.
- **Verified**: Core/CLI/Abstractions/samples build clean (0 new warnings); App builds clean
  to temp output. CLI end-to-end proven: discover→load→`plugins list/info`, `save-stats` on
  player+world, `max-skills` dry-run / real write+`.bak` / idempotent re-run / wrong-kind
  guard. **290 assertion tests green** (21 new `PluginTests`: manifest IO, discovery+dedup,
  kind detection, runner write/backup/dry-run/no-change/required-params).

## Round-14: fish journal detail (unlocks + catch requirements) (2026-06-13)
- **DT_Fish fully modelled** (`CodexCatalog.FishDefinition` + `BuildFish`): besides item/rare,
  now carries `Location` (FishName FText = water/biome), `UnlockRecipeId` (RecipeToUnlock),
  `RequiredWorldFlag`, `RequiredDlcId`, `RequiredBaitTag` (first `Fishing.Bait.*` tag in the
  CatchRequirement GameplayTagQuery's TagDictionary), and the four time-of-day catch
  multipliers (`MidnightMult`/`DawnMult`/`NoonMult`/`DuskMult`; 0 = never then, >1 = best).
  `HasTimePreference`/`RequiresSpecialCatch` are computed. Probe: FishSchemaProbeTests.
- **Fish reading pane (PlayerCodexTab)** shows two new sections via `FishBaitResolver`
  (`App/ViewModels/FishBaitResolver.cs`):
  - WHEN YOU CATCH IT: the unlocked bait (icon + name, tappable) + "+N XP on first catch".
    Bait resolves by RecipeToUnlock→recipe→item, with a **family-tag fallback** (group fish by
    base name stripping `_rare\d*`/`_AllDay`/`_torii`; map the family's `Fishing.Bait.X` tag to
    its bait item) so fish without a RecipeToUnlock (Gem Crab, etc.) still show their bait.
  - TO CATCH IT: location ("cast where there's …"), story-flag gate, a **specific** time-of-day
    sentence computed from the multipliers (e.g. "Only bites at night", "Bites best at dawn,
    midday and dusk"), DLC, plus an EQUIP-THIS-BAIT row naming the exact required bait
    (rare variants; resolved from RequiredBaitTag), also tappable.
  - Tapping either bait calls `MainViewModel.ShowItemEncyclopedia(baitId)`; `ShowItemPalette`
    now also surfaces on the codex tab once an item is selected, so the bait opens in the slot
    editor sidebar (same path as the dropped-item encyclopedia).
- Note: a few fish (Fogfish, Reaper/Inkfish) have no craftable bait item for their tag; the
  bait row is simply omitted (null-safe). Tests: CodexTests.Fish_TimeOfDayAndBaitTagsParse +
  Fish_CarryUnlockAndCatchRequirements (269 assertion tests green).

## Round-13b: save comparison feature (2026-06-13)
- **New Core engine `AbioticEditor.Core/Compare/`** - generic property-level diff over the
  raw `IList<FPropertyTag>` tree (NOT per-type models), so it works for any save (player,
  world, metadata):
  - `SavePropertyFlattener`: walks the property tree into ordered `path -> value` leaves.
    `PropertiesStruct` recurses; arrays index `[i]`; maps key `{key}`; specialized structs
    (Vector/Guid/Color/DateTime/gameplay-tags) compare via ToString. Blueprint hash suffixes
    (`_<idx>_<hex>`) are stripped via `Normalize` so the same logical property lines up across
    saves/builds. Leaf cap (default 4M) guards the 16MB Facility save; sets `Truncated`.
  - `SaveComparer.Compare(left,right)` / `CompareFiles(a,b)` -> `SaveDiff` (Changed/Added/
    Removed leaves, left order preserved, additions appended). `SaveDiff.Summary` = "N changed,
    N added, N removed" or "identical".
  - `SaveFolderComparer.Compare(dirA,dirB)` -> `FolderDiff`: pairs `*.sav` by path relative to
    each root; per-file Identical/Differs/OnlyLeft/OnlyRight/Error + the full `SaveDiff`.
- **CLI**: `abioticeditor compare <a> <b>` (file-vs-file or folder-vs-folder; auto-detects),
  `--json`, `--limit` (text cap, default 200), `--full` (expand every folder file's diffs).
  Registered in `CommandTree`. Verified live: two player saves -> 1191 changed incl. readable
  paths like `EquipmentInventory[2].ItemDataTable.RowName: armor_legs_groupe -> armor_legs_bionic`;
  backup1-vs-backup5 folder -> "17 differing, 45 identical, 0 only A, 1 only B" and correctly
  flagged `WorldSave_V_ISLAND.sav` as B-only.
- **App**: COMPARE button added to the StatusBarView (next to SETTINGS) -> `CompareRequested`
  event -> MainPage pushes `ComparePage` (code-built modal, mirrors SettingsPage). Page has a
  FILE-vs-FILE / FOLDER-vs-FOLDER mode switch, quick-picker of currently-loaded saves + BROWSE
  (FilePicker/FolderPicker), runs the compare off the UI thread w/ busy indicator, and renders
  a virtualized diff list (+ green / - red / ~ yellow). Folder mode lists per-file status; tap a
  differing file -> `DiffDetailPage` with that file's diffs.
- **Tests**: `SaveComparerTests` (6, green) over Server/Backups/Cascade/1..5 + PlayerData:
  hash-strip, same-file identical, backup snapshots differ (with side-population invariants),
  two players differ on SaveIdentifier, folder pairing flags V_ISLAND as only-on-right, self
  vs self all-identical.
- **Difference classification (noise folding)** - a raw leaf diff is noisy: comparing two
  different players, the SteamID, every item AssetID, playtime and positions all "differ" but
  aren't real changes. `SaveDiffClassifier.Classify(path,type,left,right)` tags each
  `SaveLeafDiff` with a `SaveDiffCategory` (Gameplay / Identity / Playtime / Timestamp /
  InstanceId / Position). Heuristics: leaf-name hints (SaveIdentifier, MinutesPassed/
  PlayTime/CurrentDay, LastPlayed/DateTime, AssetID/*GUID, *Location/Rotation/Translation)
  PLUS value-shape detection (32-hex/dashed GUID -> InstanceId; 3-4 space-separated floats ->
  Position) which catches the bulk of the AssetID noise. `SaveDiff` gained MeaningfulCount /
  NoiseCount / AreMeaningfullyIdentical / MeaningfulSummary ("N gameplay difference(s) (+ M
  identity/clock/instance/position)"). CLI defaults to gameplay-only with a hidden-noise
  footer + `--all` (tags noise lines `[category]`); folder rows show "X gameplay, Y total".
  App ComparePage leads with the meaningful summary + a switch to fold the noise back in
  (noise rows tagged with their category). Verified live: two players -> "1442 gameplay (+ 49
  identity/clock/instance/position)" with AssetID handles correctly folded out. Test
  `Classify_FoldsIdentityInstanceAndPositionOutOfMeaningful` (7 comparer tests green).
- Build: Core + CLI + App (net10.0-windows) all 0 compile errors (App verified via temp output
  dir while the live app instance held bin DLLs). NOT yet screenshot-verified.

## Plugin system note (in progress, user-authored)
A separate plugin architecture is being built concurrently (`AbioticEditor.Plugins.Abstractions`
SDK, `Core/Plugins/*`, CLI `PluginsCommands`, App `PluginService`/`PluginsPage`, sample plugins
under `plugins/`, Jint-backed `Scripting/JavaScriptPlugin` + `JsCapabilities`). The original
build blocker (missing `Scripting.JavaScriptPlugin`) was resolved by the user's new Scripting
files; Core compiles. The comparison work above deliberately stays out of the `Plugins/` files
to avoid clobbering live edits.

**Fix-up contribution (new files only, no edits to in-flight Plugins/ code):**
- Two managed sample "fix-up" plugins under `plugins/`: `RepairNeeds` (`repair-needs`, player -
  tops every survival need to 100 via PlayerSaveReader/Writer, also repairs needs that read 0
  from a missing tag) and `GrantFlag` (`grant-flag`, world - adds a named entry to `WorldFlags`
  by editing `context.Save.Properties` directly, so it handles flags Core doesn't model;
  required `flag` param, idempotent). Both build clean; added to `AbioticEditor.slnx`.
- `tests/AbioticEditor.Tests/PluginFixupTests.cs` (5 green) drives the REAL sample operations
  through `SaveOperationRunner` against throwaway fixture copies: needs restored on reload +
  `.bak` only on real write, dry-run leaves bytes untouched, wrong-kind rejected, grant-flag
  add→idempotent, missing-required-param fails. Tests.csproj now references the two samples.
- `docs/plugin-fixups.md`: a fix-up cookbook (typed-writer repair, raw-property edit, backpack/
  journal/version notes, testing pattern).
- **Version fix-up hook (`ISaveUpgrader`)** - implemented end-to-end (user approved "build it
  now"). SDK: `Saves/ISaveUpgrader.cs` (+ `SaveUpgradeProbe`/`ISaveUpgradeContext`/
  `SaveUpgradeResult`), `IPluginRegistry.AddSaveUpgrader`, `saveUpgrader` capability token.
  Core plumbing mirrors the other capabilities: `PluginRegistry.SaveUpgraders` (dedup by id),
  `PluginDescriptor.SaveUpgraders` (+ HasCapabilities/summary), `PluginManager.SaveUpgraders`
  aggregate + copy in LoadOne. `Core/Plugins/SaveUpgradeService.LoadAsync(path, upgraders, log,
  persist)`: tries `SaveGame.LoadFrom`; on NotSupported/Format/InvalidData builds a header-only
  probe (magic+SaveGameVersion+UE4/UE5 read from bytes; save-class/ABF via
  `SaveFolderScanner.ReadHeaderInfo`) and offers it to each upgrader's `CanUpgrade`; the first
  to return corrected bytes wins (host loads them, optionally writes after a `.preupgrade.bak`);
  rethrows the real load error when none handle it. Sample `plugins/VersionShim/`
  (`FixSaveVersionUpgrader`) rewrites an unsupported `SaveGameVersion` field to 3. Tests
  `SaveUpgradeServiceTests` (3): valid save loads w/o upgrade, version-corrupted save recovered
  + persisted + `.preupgrade.bak` + reloads clean, no-upgrader rethrows. Only `PluginRegistry`
  implements `IPluginRegistry` (no other implementer broken); CLI + App rebuild clean against
  the extended SDK. NOT yet wired into the App/CLI open-save path (host integration left to the
  user, who owns MainViewModel/PluginService). **15 new tests this session** (7 comparison +
  5 fix-up + 3 upgrade).

## Round-13: skill milestone detail + hidden-until-unlock (2026-06-13)
- **Tap a milestone chip → detail card in the right slot panel** (parity with door/chapter/
  flag/trader detail). `SkillMilestoneViewModel` gained detail members (SkillName,
  SkillIconPath, LevelText, StatusText, RequirementText = levels/XP to go, perk + effect).
  `PlayerEditorViewModel.SelectedMilestone`/`HasSelectedMilestone`; `MainViewModel.ShowMilestoneDetail`
  (added to `RaiseSidebarContextChanged` + the `OnEditorContextChanged` name filter -
  PlayerEditor is already subscribed). New milestone card in `SlotSidebarView.xaml`
  (gated on ShowMilestoneDetail, absolute x:Reference Root bindings, ✕ → `OnCloseMilestoneDetail`).
  Chip tap handled by `PlayerSkillsTab.OnMilestoneTapped` (toggles selection; closes on re-tap).
- **Hidden-until-unlocked perks** (user note: the game hides milestone perks until reached):
  mirrored via the Round-10 SpoilerService. A LOCKED milestone is `IsConcealed` (future =
  `!IsUnlocked`); chip masks the perk name (level stays visible) + effect shows "Hidden until
  unlocked". Tapping a sealed chip prompts OVERRIDE CLEARANCE, then opens the detail; raising
  the skill to the level auto-reveals it (RefreshUnlockState now re-notifies all masked/derived
  members). Spoiler protection OFF → every perk shows as before. New `SpoilerService.Skill` ns.
- **Milestone data verified COMPLETE**: `SkillMilestoneCatalog` matches docs/research-wiki-round10.md
  exactly (all 15 skills; irregular counts 4-8 are correct; Fishing has no level-20). The
  "missing milestones" perception was the real per-skill irregularity + the in-game
  hidden-until-unlock behavior now reflected by concealment.
- Build: App compiles clean for net10.0-windows (0 errors; live instance locks bin DLLs, so
  verified via a temp `-o` output). NOT yet screenshot-verified (user holds the running app).

## Round-12: right-click "Open in Explorer/Finder" (2026-06-13)
- **`FileRevealer`** (partial class, mirrors `FolderDropHandler`): shared `Views/FileRevealer.cs`
  exposes `Reveal(path)` (safe/no-throw, logs failures) + a static `RevealLabel` ("Open in
  Explorer" on Windows, "Open in Finder" on macCatalyst, "Open File Location" elsewhere).
  Platform impls of `static partial void PlatformReveal`: Windows `explorer /select,"path"`,
  macOS `open -R "path"`. Android/iOS provide no impl, so the partial no-ops there.
- **Context menu**: `FileSidebarView` save rows AND config rows got a `FlyoutBase.ContextFlyout`
  → `MenuFlyout` with one `MenuFlyoutItem Text="{x:Static views:FileRevealer.RevealLabel}"`.
  Handler `OnRevealFileClicked` reads the row's BindingContext (SaveFileSummary.FullPath or
  ConfigFileOption.File.FullPath) and calls `FileRevealer.Reveal`. (Row style AfSidebarItem
  already has BackgroundColor=Transparent, so the Grid is hit-testable for right-click.)
- Build: 0 errors. **Screenshot-verified**: right-clicking a save row shows "Open in Explorer";
  clicking it opened a File Explorer window at the save's folder.

## Round-11: in-app dialog (replaces native popups) (2026-06-13)
- **`DialogViewModel` (ViewModels) + `DialogHostView` (Views)**: one app-global, animated,
  themed modal that replaces every `DisplayAlert`/`DisplayActionSheet`. `DialogViewModel.Current`
  is a singleton the always-present overlay binds to; callers `await` `ShowAsync(title, message,
  params (text, DialogTone)[])` (returns chosen index, -1 if scrim-dismissed), or the
  `ConfirmAsync`/`AlertAsync` convenience wrappers. `DialogTone` = Primary/Danger/Neutral →
  button fill resolved from theme resources at show time.
- **Overlay**: `DialogHostView` added to MainPage as the top-most child (`Grid.RowSpan=4`),
  hidden until opened. Code-behind animates enter (scrim fade-in + card scale 0.92→1 SpringOut)
  and exit (reverse, then hide) via `FadeToAsync`/`ScaleToAsync` (the non-Async `FadeTo`/`ScaleTo`
  are obsolete in .NET 10 MAUI - using them was the CS0618 source, now fixed). Scrim tap = cancel.
- **Routed every former native dialog through it:** `ViewUtils.ConfirmAsync`/`AlertAsync`
  (host param kept for call-site compat, now ignored) → so `ConfirmBulkAsync`/`ConfirmRevealAsync`
  and all their callers ride along; `SpoilerPrompt.RevealAsync`; and the three direct
  `MainViewModel` popups - the leave-gate (now 3 toned buttons: Cancel/Discard[Danger]/Save),
  the mappings-installed alert, and the bed-reassign confirm (Danger).
- Build: 0 errors; CS0618 cleared. NOT screenshot-verified open (coordinate automation +
  concurrent live use of the app made a clean capture impractical) - but it's exercised by the
  Round-10 spoiler reveal prompts, which now route through it.

## Round-10: spoiler protection (2026-06-13)
- **App-wide SPOILER PROTECTION** (default ON): seals content the player hasn't reached
  behind an in-universe CLASSIFIED / CLEARANCE-REQUIRED stamp; tapping a sealed item
  prompts an OVERRIDE CLEARANCE confirm and reveals just that item, permanently (per-item
  reveals persist across sessions). Scope = future/locked content only.
  - `Services/SpoilerService` (static, Preferences-backed like ThemeService): `Enabled`
    (key `SpoilerProtectionEnabled`, default true), persisted revealed-key set (key
    `SpoilerRevealedKeys`, `\n`-joined), `Key(ns,id)` / `IsRevealed` / `ShouldConceal(key,
    isFuture)` / `Reveal` / `ResetReveals` / `RevealedCount`, `Changed` event, mask copy
    constants (`ClassifiedTitle`/`ClassifiedShort`/`Redacted`/`ClassifiedHint`) + `Mask()`.
    Namespaces: flag/trader/recipe/ach/codex/containment.
  - `Services/SpoilerPrompt.RevealAsync(what,key)` routes through the in-app
    `DialogViewModel` (no page ref needed) so any row VM offers tap-to-reveal.
  - SETTINGS gained a SPOILERS section: master toggle + RE-SEAL REVEALED ITEMS + count
    hint. Toggling/reseal sets `_spoilerChanged`, so CloseAsync rebuilds the editor host
    (same path as theme) and every open surface re-evaluates concealment.
  - Per-surface masking (Shown* display props + IsConcealed + tap-to-reveal; sealed rows
    can't open their detail pane - the selection setter / tap handler redirects to a
    reveal prompt, and acting controls like checkboxes/TOGGLE are disabled while sealed):
    - **Achievements** (`AchievementRowViewModel`): generalized the old per-tab
      `ShowSpoilers` into the global service; future = `Hidden && !Unlocked`. The SHOW
      SPOILERS checkbox now mirrors the app-wide setting. Row tap = reveal.
    - **Recipes** (`RecipeRowViewModel`): future = `!IsUnlocked`; masks name/status/
      tooltip/icon, disables the unlock checkbox, guards `SelectedRecipe`.
    - **Flags** (`FlagItemViewModel`): future = `IsLocked` (gated); masks friendly/raw
      name, description, STORY chip; disables TOGGLE; guards `SelectedFlag` (rebuilds the
      grouped list on reveal since the VM is immutable/no INPC).
    - **Traders** (`TraderCardViewModel`): future = `!IsAvailableHere`; masks name/where/
      blurb/sells/availability/portrait; `OnTraderCardTapped` redirects to reveal.
    - **Codex** (`CodexItemViewModel`): future = not-known AND region-gated
      (`!ProgressContext.CanUnlockRow`); masks title/subtitle/body/icon, disables the read
      checkbox, guards `Selected`.
    - **Containment** (`LeyakContainmentViewModel`): every contained anomaly is a
      candidate; masks creature name + flips the tap hint; `OnContainmentTapped` redirects
      to reveal (detail keeps appearance/location sealed until revealed).
  - **CLI**: reviewed - the only story-content command is `flags list`, which prints
    flags ALREADY SET (achieved progress), not future/unreached vocabulary, so nothing to
    conceal under the future-only scope. No CLI change (an unused `--show-spoilers` flag
    would be noise). The CLI runs in a separate process with no shared Preferences anyway.
  - Build: Core+CLI clean; App compiles clean for net10.0-windows (0 errors; the live app
    instance locks bin DLLs so the in-place copy step fails - compiled to a temp output to
    verify). Pre-existing CS0618 (DialogHostView) / CA1305 (MainViewModel) warnings remain.
  - NOT yet screenshot-verified (user holds the running instance, PID seen locking DLLs).

## Round-9: domain content → Core + Traders UI rework (2026-06-13)
Goal: shrink the UI to presentation only; move game *facts* into Core catalogs so a CLI / future
frontend can reuse them. Move data (records/strings), not behaviour - no description-service.
- **5 domain-content moves (App ViewModels → Core):**
  1. Door lock prose: `WorldDoorViewModel.AboutText` → `DoorClassCatalog.LockExplanation(lockKind)`.
  2. `App/ViewModels/TraderLore.cs` → `Core/Codex/TraderLore.cs` (namespace `AbioticEditor.Core.Codex`).
     Added `using AbioticEditor.Core.Codex;` to WorldEditorViewModel, ItemPaletteViewModel,
     RecipeListViewModel (TraderCardViewModel already had it).
  3. Containment creature `DisplayName`/`Lore` (Leyak/Krasue) → `Core/WorldSaves/ContainmentCreatureCatalog`.
  4. NPC identity hints → `Core/WorldSaves/NpcIdentityCatalog.LabelFor(id, actorName)`
     (the `IsPet` short-circuit stays in the VM as presentation).
  5. Ini per-kind `KindLabel`/`Description` → `AbioticIniCatalog.LabelFor`/`DescriptionFor`.
  Left in the UI on purpose: LockChip, KindLabel mappings used purely for display, coordinate
  formatting in ContextText/LocationText, tooltips that just compose existing Core fields.
- **Traders UI moved into the right-hand slot panel** (parity with door/chapter/flag detail):
  - Inline detail Border removed from `WorldTradersTab.xaml`; roster + tap-to-open kept. Tab
    handlers trimmed to just `OnTraderCardTapped` (selecting a card sets `SelectedTrader`,
    which drives `ShowTraderDetail` → the sidebar card; tapping the open card closes it).
  - New trader detail card in `SlotSidebarView.xaml` (gated `ShowTraderDetail`): portrait, blurb,
    availability, barter terms, stock list, unlock buttons. Handlers `OnCloseTraderDetail`,
    `OnUnlockSelectedStock`, `OnUnlockTraderFull`, `OnTraderOfferTapped` in `SlotSidebarView.xaml.cs`.
  - **Item icons + inspection:** `TraderOfferRowViewModel` now carries `ItemId` + a
    `PaletteItemViewModel Item` (reuses the palette VM's icon extraction AND encyclopedia detail).
    Each stock row shows the real item icon; tapping a row/icon calls `TraderCardViewModel.SelectOffer`
    → `SelectedOfferItem` → an encyclopedia sub-card (icon, stats, description, crafted-by, used-in)
    inside the trader panel. `RefreshAvailability` clears `SelectedOfferItem` (rows are rebuilt).
- Build: 0 errors (Core + App). Visual pass: confirmed metadata save loads + the world tab strip
  renders; full Traders click-through was NOT completed on-screen (live desktop had the app in use
  + window-focus contention) - needs a quick visual confirm when the desktop is free.

## Round-8: phantom-dirty fix + nav/ini polish (2026-06-13)
- **Phantom discard dialog on tab→save switch FIXED**: two binding write-backs were
  dirtying clean player saves. (1) SKILLS XP slider clamp-wrote real XP, so `MaxXp` now
  accommodates over-cap end-game XP, `XpSliderValue` rejects the platform slider's
  default-range (0..1) init-clamp (`value <= 1 && _xp > 1`) and tolerates sub-0.5 drift;
  (2) SPAWN region/terminal pickers replayed stored values during binding churn, so snaps
  now guard against the save's own baseline. Diagnostics: `PlayerEditorViewModel.DescribeDirty()`
  lists every dirty contributor; the leave-gate logs `Leave-gate for <file>: …` when a
  player save is dirty (App channel). Fixture tab-walk + save-switch now clean.
- **HOME button**: new header button → `MainViewModel.GoHomeAsync()` runs the leave-gate,
  tears editors down directly (no double-gate), re-scans detected worlds, keeps the folder
  + save list loaded. Returns to the landing page; verified on fixture.
- **Folder-picker cancel no longer errors**: `FolderPicking.PickAndLoadAsync` treats a
  dismissed dialog (toolkit reports cancel as an exception-bearing failure) as a no-op
  via `IsCancellation` (OperationCanceledException or "cancel" in message).
- **SandboxSettings.ini key trimming fixed**: ini key column 140→320px, MiddleTruncation→
  TailTruncation + full-key tooltip; long keys (RefrigerationEffectivenessMultiplier) now
  render whole. Verified on fixture.
- **Ini SAVE/REVERT buttons resized**: were inheriting default button metrics (huge); now
  FontSize 11 / Padding 16,6 / VerticalOptions Center to match the panel-header scale.
- Trader (UnlockTrader*/OfferRow/SelectedTrader) + leyak containment detail (EnsureDetail)
  members were completed by the user concurrently; build green (0 errors).

## Round-7: UX fixes + in-app Steam sign-in + visual pass (2026-06-13)
- **STORY tab SET consolidated**: the ADD FLAGS UP TO HERE / CLEAR FORWARD FLAGS
  buttons are gone; `WorldEditorViewModel.SetChapterAsync(row)` (wired to every
  chapter's SET + the sidebar card) moves the pointer AND runs SyncFacilityFlags +
  ClearForwardFlags in one go (Facility file written immediately w/ .bak; pointer
  stages until SAVE).
- **Transmog visibility toggles limited to visual gear**: VisibleTransmogToggles =
  indices 0-5 (chest/head/legs/backpack/arms/suit); headlamp/trinket/watch/hacker
  toggles were no-ops (no body visuals). All 12 flags still round-trip.
- **Spawn coords snap**: RespawnTerminalCatalog gained the 10 terminals' world
  positions (from research-respawn-terminals.md); picking a REGION or RESPAWN
  TERMINAL snaps X/Y/Z to the matching terminal anchor. Guarded against the save's
  own stored value - Picker binding churn during load replays it (caught live:
  unguarded version overwrote coords + dirtied on load).
- **Skills XP slider** (replaces the read-only progress bar): 0..MaxXp where MaxXp =
  max(level-20 threshold, the save's own XP) - END-GAME SAVES EXCEED THE CAP (e.g.
  97,079 XP) and a threshold-only Maximum clamp-wrote them away (caught via the
  edit-trace log); XpSliderValue has 0.5-XP write-back tolerance (F0-entry gotcha).
- **In-app Steam sign-in** (subagent): SteamLoginPage (MAUI WebView -> WinUI WebView2
  CookieManager captures sessionid+steamLoginSecure; #if WINDOWS, browser fallback
  elsewhere), Services/SteamSession (memory + SecureStorage "SteamSessionCookie",
  SIGN OUT clears), SteamWebAchievements.FetchAsync(cookieHeader) sends the Cookie
  header; achievements tab: SIGN IN (IN-APP) primary in the gated panel, SIGNED IN ·
  SIGN OUT cluster in the header; comparisons also use the session.
- **Doors**: REGION WIKI button removed (ONLINE MAP stays); keycard AboutText
  corrected - keycards are NOT looted from corpses (placed in world; keypad hacking
  is the common path).
- **Visual pass done (fixture tree)**: split UI, vitals, skills 3-col grid +
  sliders, transmog 6 toggles, bases inline container list, door detail card, NPCs
  incl. PET rows all verified by screenshot; dirty-on-load regressions fixed and
  re-verified clean. LESSON: interactive click-driving must use
  ABIOTIC_EDITOR_FOLDER=tests/fixtures/... - stray clicks on the live tree staged
  real edits (leave-gate discarded them).
- 259 tests green; only upstream NU1903 warnings remain. User is concurrently adding
  pet renaming (PetName entry in WorldNpcsTab + WorldNpcViewModel; writer-side
  CustomName persistence still needed at the time of writing).

## Round-6: more UNKWN candidates + platform drop + Core layout + Steam prompt (2026-06-13)
- **PetNPC merged into the NPCs tab**: `WorldNpc` gained IsPet/CustomName/NpcClass
  (ActorName prefers the given name, then the pet's class tail); reader walks
  NarrativeNPCMap + PetNPC (same SaveData_NPCState_Struct), writer patches both maps;
  rows show a PET chip; named pets display as e.g. "Rex".
- **World unlocks (GlobalUnlocks struct, STORY tab)**: counts + additive staged bulk
  unlocks for GlobalItemsPickedUp/GlobalEmailsRead/GlobalJournalEntries/
  GlobalCompendium{Email,Narrative,Exploration} (vocab: item catalog + CodexCatalog;
  compendium rows land per SectionTypes, existing placements never moved).
  `ReadGlobalUnlockArray`/`ApplyGlobalUnlockArray` in reader/writer.
  `LastPlayed` shown next to MINUTES PLAYED. PetNPC/GlobalUnlocks/LastPlayed added to
  ConsumedPrefixes.
- **Folder drop is now per-platform** (subagent): `Views/FolderDropHandler` partial
  class; Windows = previous behavior, MacCatalyst = real UIDropSession file-url drop
  with security-scoped access (compile-verified only), Android/iOS = deliberate no-op
  (picker covers them). MainPage ctor just calls FolderDropHandler.Attach. All four
  TFMs build.
- **Core layout pass**: folders already matched namespaces except four misplaced
  files - moved AbioticSaveClasses.cs (root -> SaveClasses/), SaveJsonBridge.cs
  (root -> Saves/), SteamPersonaIndex.cs (Saves/ -> Steam/), SaveCompatibility.cs
  (SaveClasses/ -> Compatibility/), namespaces re-aligned, usings fixed repo-wide.
  Deliberately NOT merged: Saves/ (file plumbing) vs SaveClasses/ (UeSaveGame
  [SaveClass] impls + JSON serializers) - distinct concerns, both well-named.
- **Steam achievements gated-profile prompt**: AchievementsViewModel.ProfileGated +
  SignInAndViewCommand (opens steamcommunity login that redirects to the profile's
  achievements page - signed-in sessions can see own/friends' stats) +
  OpenPrivacySettingsCommand; hazard prompt panel in PlayerAchievementsTab appears on
  SteamGameDetailsPrivateException.
- 255 tests green; app/CLI build clean (pre-existing CA1859 only). Visual verify still
  pending (user instance holds the exe).

## Round-5: theme fix + MainPage split + bases/skills/selection + UNKWN modeling (2026-06-12 night)
- **Theme staleness FIXED**: MAUI Style objects are created lazily and capture
  StaticResource color VALUES once app-wide, so ThemeService's resource overwrites
  never reached already-created styles (buttons/panels/labels stayed on the old accent
  until restart). AbioticStyles.xaml now uses DynamicResource for every color (incl.
  VSM states; Brush-typed Stroke setters point at the *Brush keys because
  DynamicResource skips the Color->Brush converter). Page rebuild on switch stays (it
  covers inline StaticResources + converter output). LIVE SWITCH NOT YET SCREENSHOT-
  VERIFIED - the user's app instance held the exe all session.
- **MainPage.xaml SPLIT** (was ~3.7k lines + 800 code-behind; now ~190 + ~110):
  - `Views/`: HeaderBarView (FILES/TOOLS raise events; SetCompact), FileSidebarView,
    SlotSidebarView (all detail cards + palette; x:Name="Root" preserved so the
    absolute-binding pattern is untouched), IniEditorView, EmptyStateView,
    StatusBarView (SettingsRequested event).
  - `Views/Player/`: PlayerEditorView (tab strip) + General/Vitals/Character/Transmog/
    Spawn/Inventory/Skills/Recipes/Codex/Achievements/Raw tabs. Multi-panel tabs
    (vitals, character) wrap their panels in a VSL with the root ContentView's
    IsVisible bound to the tab flag.
  - `Views/World/`: WorldEditorView (summary + strip) + Containers/Flags/Doors/
    Dropped/Npcs/Bases/Story/Raw tabs.
  - Shared plumbing: `Views/ViewUtils` (FindBoundContext/ParentPage/Confirm helpers),
    `Views/SlotInteractions` (all slot/palette/container gesture logic; views keep
    thin instance wrappers - XamlC needs handlers on the x:Class type),
    `Views/FolderPicking`, `Views/ResponsivePaneController` (ALL breakpoint/drawer
    logic moved out of the page; subscribes to vm.SelectedSave to close the file
    drawer and vm.ActiveSlot to surface the slot pane - drawer on phones, un-collapse
    inline on desktops).
  - Pure logic moved to MainViewModel: SelectSlot, SortBackpack, DropActiveItem,
    BeginDismantlePreview/ConfirmDismantle/CancelDismantle + FindSlotCollection.
- **Skills tab large-screen fix**: FlexLayout (lone last card stretched full row) ->
  CollectionView + ResponsiveLayout.ItemWidth=430/MaxSpan=3 uniform adaptive grid.
- **Selected-slot highlight**: InventorySlotViewModel.IsSelected (maintained by
  MainViewModel.ActiveSlot setter) -> hazard-yellow 2px ring + hover bg via
  DataTrigger in pockets/hotbar/transmog/container/base slot templates + an overlay
  ring in App.xaml's EquipmentSlotTemplate (TemplateBinding IsSelected).
- **Bases tab overhaul**: container rows in "CONTAINERS IN THIS BASE" are now
  selection rows (EDIT-jump button removed) bound to NEW
  WorldEditorViewModel.SelectedBaseContainer; the selected container's slot grid
  opens IN PLACE of the base map (ShowBaseMap=false) with a "✕ MAP" close button,
  full slot gestures and the slot-editor sidebar (auto-surfaced via the controller).
  Cleared when SelectedBase changes.
- **UNKWN log review (editor-20260612.log) -> newly modeled** (probe:
  tests/AbioticEditor.Probes/UnmodeledWorldPropsProbe.cs):
  - `TimeOfDay` struct (Facility save): TimeOfDaySeconds (double 0..86400) +
    CurrentDay + LastAssaultDay/LastWeatherDay/LastPowerLeechDay ints. Editor: WORLD
    DAY entry + TIME OF DAY slider in the world editor header
    (Reader.ReadWorldClock / Writer.ApplyWorldClock).
  - `DayDiscovered` int (region saves): editable entry
    (ReadDayDiscovered/ApplyDayDiscovered).
  - `LeyakContainmentIDs` Map<Name,Str> (metadata save): creature row name (Leyak,
    Krasue) -> containment unit's DeployedObjectMap GUID (teleporter-style link).
    Editor: CONTAINMENT tab (metadata-only) with per-creature RELEASE
    (stages map-entry removal; ReadLeyakContainments/RemoveLeyakContainment) and a
    tap-to-detail card (compendium texture T_Compendium_<creature> + sector name from
    RespawnTerminalCatalog.NearestTo against the containment unit's facility deployable).
  - All three added to WorldSaveReader.ConsumedPrefixes (UNKWN noise gone).
- **Traders rework (metadata TRADERS tab)**: trader roster moved off the world NPCs
  tab onto a metadata-only TRADERS tab. Fili excluded (TraderLore.NonTraders - she is
  an Anteverse NPC, not a trader); unknown future rows still render (row-id fallback,
  sorted after known lore). Per-item stock unlocking: each locked offer row has a
  checkbox; APPLY writes the chosen RequiredFlags (plus any trader-gating flags) via
  StoryFlagSync.AddFacilityFlags into WorldSave_Facility.sav with a confirmation dialog
  listing the exact flags. Availability now reads HasWorldFlag (sibling Facility flags
  on the metadata save). TraderOfferRowViewModel replaces the OfferRow record;
  OfferDetails is cached (WinUI re-seed guard).
  - Still unmodeled (documented): GlobalUnlocks struct (world-wide pickups/emails/
    journal/compendium/distilled arrays - candidate for world bulk unlocks),
    LastPlayed DateTime, ServerEntitlements, PetNPC (same struct as NarrativeNPCMap -
    candidate to merge into the NPCs tab), Destructible/Elevator/ResourceNode/Button/
    NPCSpawn/PowerSocket/Trigger/Portal/Tram/Vehicle/Corpse/Decal maps; player-side
    CompletedIntro, LastControlRotation, CurrentBuffDebuffs, unread/favorites arrays.
- 255 assertion tests green; app compiles with zero NEW warnings (CA1859 in
  DoorLocationResolver pre-exists from the door-locator work). VISUAL VERIFY PENDING
  for: theme live-switch, all split views render, bases inline editing, skills grid,
  slot highlight - blocked because the user's app instance held bin\...\exe.

## Round-4: UI rework (2026-06-12 evening) - game style + fluidity + mobile
- **Theme**: default palette is now the game-accurate blue-teal facility look
  ("FACILITY BLUE" = ThemeAccent.Cascade, colors lifted from the shipped inventory UI:
  panes 306481/5292B7, headers 71C5F6, cyan readouts ~8CFFFB, CTA orange F89A4F,
  caution yellow FFE563). Pref key bumped to `ThemeAccentV2` so existing installs
  re-default; HAZARD ORANGE (old amber-CRT) stays as the alternate. Colors.xaml static
  values mirror the new default.
- **Motion**: `Controls/Fx.Reveal` attached property fades+rises panels when IsVisible
  flips (all player/world tab panels carry it); Button styles gained Pressed states
  (scale dip); drawers slide with eased TranslateToAsync + scrim fade.
- **Responsive system** (all in `src/AbioticEditor.App/Controls/`):
  - `AdaptiveGrid`: stacks its cells vertically below CompactWidth (own measured
    width); lifts child WidthRequests while stacked; if the grid had a fixed height it
    moves StackedChildHeight onto height-less children. Used on codex/recipes/
    containers/bases master-details, inventory 3-col, body health, spawn coords,
    steamid row, background row, story-flag batch row.
  - `ResponsiveLayout.ItemWidth/MaxSpan`: recomputes GridItemsLayout.Span from the
    CollectionView's width (pockets/hotbar/container slots 96px, palette 110, traders
    340). Hotbar becomes a multi-column grid when the inventory stacks.
  - Tab bars (player 11 tabs, world 8, codex apps) are horizontal ScrollViews of
    `AfTabButton` chips - no more crushed star columns.
- **Drawer mode** (MainPage.xaml.cs): below 800px width both side panes re-home from
  the inline Auto columns into overlay ContentView hosts (`*InlineHost`/`*OverlayHost`)
  and slide in over the editor with a tap-to-close scrim; FILES/TOOLS toggle them,
  tapping a slot auto-opens the tool drawer, picking a save auto-closes the file
  drawer (MainViewModel.SelectedSave PropertyChanged hook). Header drops breadcrumb +
  version below 900px. Inline auto-collapse below 1150px unchanged. Pane re-homing
  back to desktop verified by screenshot both directions.
- **Crash logging**: App.xaml.cs writes unhandled/unobserved exceptions to
  `%TEMP%\AbioticEditor-crash.log` (WinUI otherwise exits silently). Added because one
  launch died with exit -1 before logging existed; not reproduced since - if the app
  vanishes again, read that file.
- Verified by screenshots (tools/shots/rework-*, step*-*.png): desktop empty/player/
  skills/inventory + slot sidebar, phone (440px) vitals/skills/world containers,
  FILES + slot drawers, 700px inventory, desktop restore after drawer mode.
- Known cosmetic leftover: transmog slot row keeps Span=6 on phones (6 slots shrink
  rather than wrap - its CollectionView has a fixed 104px height).

## Round-3 additions (2026-06-12 late)
- **Repo flattened**: the old `dotnet/` wrapper is gone; `src/ tests/ docs/ assets/
  tools/` plus slnx/props sit at the repo root. `Saved/` deleted; `Saved/` +
  `*.upipelinecache` ignored. README rewritten for the dotnet product.
- **Projects**: `src/AbioticEditor.Cli` (abioticeditor: scan/info/export-json/
  import-json/flags/steamid/ini/version, exit codes 0/1/2, --json), probes split into
  `tests/AbioticEditor.Probes` (dotnet test on Tests = assertions only). Central
  package management (`Directory.Packages.props`, transitive pin Microsoft.Bcl.Memory
  9.0.14); submodules shielded via `submodules/Directory.*.props`. Analyzers
  latest-recommended, ZERO warnings in our projects (probes exempt by design; CA1707
  off in test projects; XAML-bound members carry justified suppressions).
- **slnx lists all submodule projects** (CUE4Parse, CUE4Parse-Conversion,
  UeSaveGame.Json added) - required for IDE IntelliSense; reload the C# language
  server after pulling this change.
- **Core**: `Saves/PropertyTagExtensions` (shared FindByPrefix/Get*/TryGet* used by
  both readers+writers), `Saves/SaveDiscovery` (client tree + Steam-library dedicated
  server scan, Backups skipped), `Ini/IniFile`+`AbioticIniCatalog` (order/comment
  preserving; Admin.ini, SandboxSettings.ini, client config),
  `Compatibility/SaveVersionRegistry`+`CompatibilityAnalyzer`+`CompatibilityReport`
  (severities Exact/NewerMinor/NewerVersion/Unknown; bump versions in the registry on
  a game update), `BackpackSpecialSlotCatalog` (DYNAMIC from DT_ItemCosmetics ->
  data-asset slot arrays; unknown *Slots kinds get derived badges + UNKWN log;
  verified table as fallback).
- **App**: ini editor (CONFIG FILES sidebar section -> section/key/value editor,
  .bak on save); SETTINGS modal (`SettingsPage` + `Services/ThemeService`: Hazard
  Orange / Cascade Blue accents x dark/light, persisted, applied in App ctor, page
  tree rebuilt on switch); per-save dirty gate (`ConfirmLeaveCurrentEditorAsync`:
  Save and continue / Discard / Cancel on every navigation incl. theme rebuild,
  folder drop, discovery load); startup world discovery list on the empty state
  ("WORLDS FOUND ON THIS MACHINE" + LOAD); folder drag-and-drop onto the window
  (#if WINDOWS, accepts a folder or any file inside one). Status bar slimmed to
  status + logging text + SETTINGS.
- **Standalone saves**: folders with only player saves or only the metadata save
  load with no world context (gating off, pickers empty) - pinned by
  StandaloneSaveTests.
- **Style scrub done**: em-dashes/ellipses/arrows and telltale phrasing removed from
  comments and docs repo-wide (129 files); functional UI glyphs kept.
- Verified by screenshot: startup discovery list shows Cascade + Chrissie (CLIENT)
  with LOAD buttons; app launches clean.

## Older layout notes (round 2)
- **Rust product REMOVED**: `uesave/`, `uesave_cli/`, `uesave_wasm/`, `web/`, Cargo files,
  `.github/` rust/web workflows - all git-rm'd (uncommitted).
- `src/AbioticEditor.App` (MAUI, net10 win), `src/AbioticEditor.Core` (net10),
  `tests/AbioticEditor.Tests`.
- **Fixtures (grouped by platform under `tests/fixtures/`, Backups removed, ~205 MB total)**:
  - `SteamSaves/` - `Legacy/Cascade/` (the older standalone single-player world, ~51 MB, the
    canonical `CascadeDir`), `Config/Windows/*.ini` (client config) and `SaveGames/<steamid>/Worlds/`
    (the newer client tree + account files Admin.ini/Unlocks.sav/PlayerStatsSave.sav/etc., the
    `ClientSavedDir`). `Config/` + `SaveGames/` mirror the game's real `Saved/` install.
  - `GamePassSaves/<account>/` - sanitized Xbox "wgs" container (`GamePassWgsDir`).
  - `DedicatedServerSaves/` - `Admin.ini` + `Worlds/Cascade/` (complete story, NAMED benches;
    `ServerWorldsDir = .../Worlds/Cascade`).
  - `Fixtures.cs` exposes `CascadeDir`, `ClientSavedDir`, `ServerWorldsDir`, `GamePassWgsDir`
    (walk-up + pre-regroup fallbacks). `.bak` and `Backups/` are not committed (`*.bak` is in
    `.gitignore`); discovery already excludes `Backups/` by name.
- Submodules at repo-root `submodules/CUE4Parse` (@1125f5bc) + `submodules/UeSaveGame`.
- usmap at `assets/Mappings.usmap` (bundled via csproj link); user override at
  `%LOCALAPPDATA%\AbioticEditor\mappings\Mappings.usmap` - installable in-app via the
  status-bar IMPORT USMAP button (`GameAssetProvider.InstallUserMappings`, validates the
  0xC4 0x30 magic; restart required). App icon = game's ABF logo.

## Feature inventory (all shipped + tested)
- **Editors**: player (stats/inventory/equipment/skills/traits/recipes/codex/fish/kills/
  maps/transmog/respawn/steamid), world (containers/flags/doors/dropped/NPCs/bases/story),
  metadata (story chapter + world research recipes - other tabs hidden there),
  ScientistCustomization (13 appearance fields, per-account), JSON export/import.
- **Player tabs**: GENERAL (SteamID change - renames file AND rewrites internal
  `SaveIdentifier`; bulk unlocks w/ confirmations) · VITALS (stats+health) · CHARACTER
  (background/traits/appearance w/ preview icons+swatches) · TRANSMOG (6 slots with
  CHEST/HEAD/LEGS/BACK/ARMS/SUIT roles + 12 visibility toggles) · SPAWN (XYZ + region
  picker by friendly name, respawn-terminal picker (10 known terminals), bed picker) ·
  INVENTORY (3-col game-like layout, POCKETS titled by equipped pack + capacity,
  special-slot tags COLD/FREEZER/SHIELDED/WARM, money/SORT/DROP ITEM footer, vertical
  hotbar) · SKILLS (2-up cards, milestone chips w/ visible effects) · RECIPES (icons +
  wiki-style detail pane) · GATEPAL (PDA-look chrome) · ACHIEVEMENTS · DATA.
- **Slot editor**: enum-strict equip validation (`EquipSlot`, wildcard 2), upgrade/
  downgrade via DT_ItemUpgrades + dynamic special chains (keypad_hacker t2..t9 probed
  from catalog), ▲ badge, dismantle (preview+confirm, hidden when no recipe), teleporter
  ↔ bench sync, LIQUID section (type picker limited to the item's `AllowedLiquids`,
  level capped at `MaxLiquid`).
- **World**: quest flags grouped in story order (01 OFFICE ... 11 FINALE, anomalies last)
  with per-flag descriptions, lock chips, sidebar detail (region card art, prereqs ✓/✗,
  gated TOGGLE + SET PREREQUISITES); chapter list w/ DONE/READY/LOCKED (facility flags
  read cross-file) + sidebar quest cards (map_* art, all 37 summaries); trader detail in
  sidebar w/ UNLOCK TRADER+STOCK (warning lists flags); NPCs (identities, REVIVE-only,
  script-phase note); bases (per-bench list w/ custom names, reworked map: glyph legend,
  bench labels, selected-base ring); doors tab shows sector card banner (game has NO
  per-door art - verified).
- **Gating**: FlagGate (story-linear prereqs + area->chapter); ProgressContext gates
  codex rows (area-prefix) and recipes (email-attachment link) against world progress
  loaded from sibling facility save; ungated when no world context.
- **Compat/diagnostics**: ABF_SAVE_VERSION JSON header serializers (was silent
  corruption); version warnings (world v3 / character v1 known-good); unknown recipe
  tables -> "Misc"; unknown skills/fish/rows preserved + labeled; writers create missing
  delta-serialized tags (survival stats, slot ChangeableData - exact full names
  hardcoded); `EditorLog` (opt-in toggle in status bar, 7-day rotation, edit-trace of
  every staged change, `UNKWN` channel for unmodeled save properties, dedup per folder).

## Research docs (all under docs/)
`player-save-schema.md`, `world-save-schema.md`, `research-customization.md`,
`research-wiki-round10.md` (skill milestones/appearance/item-infobox),
`research-respawn-terminals.md` (GUID->location table),
`research-transmog-appearance.md` (slot indices, EquipSlot enum, customization icons),
`research-narrative-npcs.md`, `research-backpack-traits.md` (capacity/special slots,
cut traits), `research-new-save-gaps.md` + `research-server-saves.md` (round-trip
audits; DayDiscovered/CorpseMap unmodeled), `research-slot-types.md` (EquipSlot map,
bench-name verdict, SaveIdentifier), `research-gatepal-quests.md` (PDA spec, inventory
spec, 37-chapter table), `reference-inventory-ui.png` (user's target screenshot).
Liquids: enum + LiquidData findings live in `tests/LiquidDoorProbeTests.cs` output
(E_LiquidType displaymap hardcoded in `Core/Items/LiquidTypes.cs`).

## 2026-06-12 round-2 additions
- **Steam achievements fix**: CHECK STEAM failures were misblamed on private profiles.
  Live-probed: profile `privacyState: public` is NOT enough - Steam's separate
  "Game details" dropdown gates `.../stats/<appid>/achievements?xml=1`, and denials come
  back as an HTML error page served with a `text/xml` content type (verified: 1 of the
  4 co-op accounts works anonymously, the others are denied). `SteamWebAchievements`
  now detects the HTML page, extracts Steam's real message, throws typed
  `SteamGameDetailsPrivateException`; the VM shows precise guidance (Game details ->
  Public) and stops blaming privacy for unrelated failures. `ParseResponse`/`ExtractHtmlError`
  exposed + unit-tested (`SteamWebAchievementTests`).
- **Usmap import**: status-bar IMPORT USMAP button -> file picker -> magic-validated copy
  to the `%LOCALAPPDATA%` override; alert explains restart + how to revert. Tests in
  `UsmapInstallTests`.
- **Forward-compat (unknown data)**: unknown door classes/states, equip-slot and liquid
  enumerators now log on the UNKWN channel (deduped); door-state Picker appends an
  unknown current state (e.g. "State 7") so future-version saves display it and
  re-selecting it is a no-op instead of data loss (label-based mapping, not positional).
  Already-graceful paths confirmed: `EquipSlotTypes.NameOf` -> "slot type N",
  `LiquidTypes.NameFor` -> "Liquid #N", unknown flag areas -> "OTHER · ANOMALIES & META"
  group, `DoorClassCatalog.Lookup` -> echo + Unknown lock kind.
- **Perf/memory review COMPLETE** (`docs/research-perf-review.md`). Fixed:
  `WorldLevelIndex` streams instead of LOH `ReadAllBytes`; flag VMs cached (filter
  keystrokes no longer rebuild all `FlagItemViewModel`s; bulk ops batched via
  `RunFlagBatch`); `StoryProgressionCatalog`/`FlagGate` lookups memoized; recipe/skill
  icon extraction batched off the UI thread; hot-path `EditorLog.Info` interpolations
  guarded by `Enabled`. MainViewModel follow-ups applied separately: bench/world-flag
  caches are instance fields cleared per folder load, `ProgressContext.WorldFlags`
  reset on folder switch, editor-setter event detach moved inside the `Set` branch.
  Audited-clean: editor subscriptions, texture disk cache, `SeenUnknown` reset,
  virtualization (big lists all CollectionView).

## 2026-07-02: story revert left the endgame region "stuck finished"

Investigated a report that reverting story flags to an earlier chapter (e.g. back to the
start of the Reactor Sector) still left the game reading the save as finished. Root cause,
confirmed against a real completed-game save (`tests/fixtures/DedicatedServerSaves/Worlds/Cascade`,
`StoryProgressionRow == EndGame`, 214 flags): `StoryFlagSync.ClearForwardFlags` (the revert half of
the STORY tab's SET action) only ever removed the 37 curated `DT_StoryProgression` trigger flags.
Everything past Dams (Reactors/Praetorium/Residence/Fracture/finale) has dozens of granular
non-trigger flags - including `End_MainStoryComplete` itself, which isn't a chapter trigger at all -
that nothing ever cleared.

Fix: extended `QuestFlagDependencies.Direct` with the full main-story granular chain from Power
Services through the finale (sourced from the real completed save's flag set, cross-checked against
`StoryProgressionCatalog`'s chapter summaries), then added `FlagGate.DependentsOf` - the same curated
graph walked in reverse - so `ClearForwardFlags` now clears every granular flag built on top of a
forward trigger, not just the trigger itself. Also wired into `WorldEditorViewModel.ToggleFlag` so
manually clearing one flag in the Flags tab cascades to its dependents too. Tests:
`StoryProgressionTests` (`DependentsOf_*`, `ClearForwardFlags_OnARealCompletedSave_*` - the latter
copies the real completed fixture to a temp dir and asserts the whole finale chain and granular
Reactor/Residence/Fracture flags disappear on a rewind to `ReactorsEntry`, while Office through Dams
survive). 613/613 tests green.

**Deliberately out of scope** (side content, not main-story completion signals): the portal-world
vignettes (V_Signal, VWinter, Salem, NightRealm, MirrorWorld, H_Japan, Snowglobe, Rise), ambient
MapReveal/Tram/Weather flags, and NPC "met" metas outside the main spine.

**Still not reverted by anything** (raised by the user, not yet built):
- ~~Player transform/location~~ and ~~journal/codex/email unlocks~~ - built same day, see below.
- `QuestFlagDependencies.Consequences` (doors opened, NPCs killed) is seeded with one example
  (Office cafeteria/Jager) and is not applied automatically anywhere; a real revert tool would need
  to walk the forward-cleared flag set and undo consequences too. Still open.

## 2026-07-02 (same day, follow-up): codex/email/journal revert + move-players-on-revert

Picked up the two items left open above.

**Codex/email/journal revert** (`CodexRevert.cs`, new): filters `GlobalEmailsRead_`/
`GlobalJournalEntries_`/`GlobalCompendium{Email,Narrative,Exploration}_` (metadata save) and the
per-player `EmailsRead_`/`JournalEntries_`/`Compendium_*Sections_` arrays down to what the
post-revert flag set actually allows, using the same `FlagGate.RegionChapterForRowId` gate the
forward-unlock path (`ProgressContext.CanUnlockRow`) already used. Wired into
`WorldEditorViewModel.SetChapterAsync`: metadata arrays stage into the existing
`_stagedWorldUnlocks` dictionary (commit on SAVE, like every other world-wide unlock edit); player
files are patched immediately with the standard `.bak`, same cross-file pattern as
`StoryFlagSync`.

**Found and fixed a real, pre-existing bug while building this**: `FlagGate.RegionChapterForRowId`
assumed email ids were ordered `Region_Email_Name` (that's what its old doc comment and
`FlagGateTests` claimed). Real save data (all 4 fixture players, 160 confirmed instances) shows
the actual order is `Email_Region_Name` (`Email_Labs_Kizz`, `email_labs_creepingcrystal`) - the
reverse. Under the old code this meant the area extracted was always `"Email"`, which never
matches anything in `AreaToChapterRow`, so email rows were silently never gated - not just by my
new revert, but by the already-shipped forward gate (`ProgressContext.CanUnlockRow`) too. Fixed by
stripping a leading `Email_`/`email_` marker before area extraction; `FlagGateTests` rewritten to
assert the verified real convention instead of the fabricated one.

**Player location revert** (`RespawnTerminalCatalog.ForChapter`, `PlayerRespawnRevert.cs`, new):
reuses the existing 10-terminal `RespawnTerminalCatalog` (already backing the PLAYER > SPAWN tab)
plus a small chapter-row -> sector-terminal table, falling back to the nearest earlier chapter's
terminal for portal-world/vignette chapters that have none of their own (Flathill, Voussoir,
Anteverse C, Fracture, Botanical, DarkLens, SouthIsland, EndGame). Deliberately **opt-in**, not
automatic: relocating a player is more consequential than clearing flags, so it's gated behind a
new `MovePlayersOnChapterSet` checkbox on the STORY tab (off by default). Only X/Y/Z and
`TerminalRespawnID_` are written - `LastSafeWorldGUID_` is left untouched, matching what the
existing SPAWN tab already does when you pick a terminal without also picking a different
streamed sub-level (verified: that tab's `SelectedTerminal` setter never touches the level GUID
either).

Tests: `CodexRevertTests` (unit + a real-completed-save integration test copying the fixture to a
temp dir and confirming Reactors/Residence email+journal rows disappear on a rewind to `MF`),
`RespawnEditTests` (`ForChapter_*`, `MoveToChapterTerminal_OnARealSave_*`), `FlagGateTests`
(rewritten email-id tests). 621/621 tests green.

**Still not built**: `QuestFlagDependencies.Consequences` (door/NPC-death undo) - the last item
from the original ask.

## Known open items / verify-next
0. NEW (2026-06-12 late): story-timeline checklist REMOVED from region flags tab
   (redundant with grouped list; checklist remains on metadata STORY tab). Door rows
   are now click-selectable -> sidebar door card (sub-level card art, lock kind +
   required-key name, LocationText = sub-level + actor - saves store NO door
   coordinates, stated in UI; state picker + OPEN/ONE-WAY toggles editable there).
   Both built + 285 tests green; screenshot verification pending.
1. **Grouped flag list after ctor fix**: ApplyFlagFilter now runs in the world editor
   ctor (was: groups empty until a filter changed). Built, not yet screenshot-verified
   (user had the screen). Check QUEST FLAGS tab shows grouped rows.
2. **Flag row click -> sidebar detail**: hit-test fix applied (explicit row background);
   verify a row click opens the detail card.
3. Transmog durability retention fix (sparse ChangeableData) is tested at Core level;
   user-flow verification pending.
4. NU1903 advisory: Microsoft.Bcl.Memory 9.0.0 transitive inside CUE4Parse upstream.
5. Nothing is committed - entire working tree awaits user review/commit (submodule
   moves are staged by necessity of git mv semantics).

## Conventions / gotchas (see also memory: abiotic-save-schema-facts)
- Property names hash-suffixed -> always prefix-match; delta-serialization omits
  default-valued tags -> writers must FindOrCreate (full names in PlayerSaveWriter.FullNames).
- Gameplay-tag containers render comma-separated (split on ',' AND '|').
- WinUI: Grid w/o background is hit-test transparent; Border>ScrollView never measures;
  hoist records for x:DataType; stored command instances; F0-format two-way Entry
  bindings write rounded values back (use tolerant dirty thresholds).
- Visual verification loop: `tools/capture.ps1` + env `ABIOTIC_EDITOR_FOLDER` /
  `ABIOTIC_EDITOR_AUTOSELECT`; always check foreground window first - the user may be
  using the machine; never Stop-Process the app while the user has it open.
