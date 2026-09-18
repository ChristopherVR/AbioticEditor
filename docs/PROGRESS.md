# Abiotic Editor - Session history

## Round-125: live ELEVATORS retry storm, and a stuck TRAMS refusal against a real, working recall (2026-09-18)

Two live-mode bugs from the same editor log window (`editor-20260918.log`, 14:47-14:48), both on
`WorldFeaturesTab.razor` (the shared master-detail editor every live/file world-feature area uses):
the owner toggled an elevator's "At top stop" and the tab spammed the same "elevator is not powered"
error toast roughly every two seconds until they left the tab; separately, a tram recall the owner
confirmed was actually working in-game was refused with "could not confirm the tram started moving
toward that station".

**A. Elevator retry storm - root cause.** `LiveElevatorsFeatureSession.SetMapFeatureField`
(`Web.Shared/Models/LiveElevatorsFeatureSession.cs`) called `_channel.SetTopOpenAsync(...)` with no
try/catch around it, so the Lua handler's own `error("elevator is not powered: ...")` reached it as
an uncaught `LiveAgentException` instead of a returned `WorldEditResult.Failure` - every OTHER
settable live area (buttons/npcspawns/triggers/destructibles/resourcenodes/trams) already wraps its
own channel call in `catch (LiveAgentException ex) { return WorldEditResult.Failure(ex.Message); }`;
elevators was the one area that never picked up that pattern (and, checked while investigating,
`LivePortalsFeatureSession.SetMapFeatureField` had the identical gap, just not yet observed live).
The escaped exception skipped `WorldFeaturesTab.SetFieldAsync`'s (`Web.Shared/Components/World/
WorldFeaturesTab.razor:610`, pre-round-125 line number) own error handling AND its
`RefreshSnapshot()` call at the end - the one call that snaps a checkbox/select back to the
confirmed value. Left un-reverted, the control kept showing the click the game never accepted, and
the live world's own 2-second periodic refresh (`LiveConnect.razor`'s `LiveRefreshLoopAsync` ->
`RefreshActiveAreaAsync`, plus the elevator session's own `Changed` event firing a second,
near-simultaneous `StateHasChanged` from `RefreshAsync`) re-rendered that same stale, unreverted
field on every tick - matching the log's exact cadence (two `SetFieldAsync` calls a couple of
milliseconds apart, repeating every ~2 seconds, for as long as the tab stayed open).

**Fix.** Two layers, so this class of bug cannot recur silently for a future live area either:
- `LiveElevatorsFeatureSession.SetMapFeatureField` and `LivePortalsFeatureSession.SetMapFeatureField`
  both gained the same `catch (LiveAgentException ex) -> WorldEditResult.Failure(ex.Message)` every
  sibling area already had.
- `WorldFeaturesTab.SetFieldAsync` is now defensive on top of that, not just reactive to it: a
  try/catch treats ANY exception from `Session.SetMapFeatureField` as a Failure result (so a future
  session with the same gap fails once instead of hanging the whole render), `RefreshSnapshot()`
  now runs in a `finally` (so a throw can never skip the revert), and a new `_pendingFieldSends` set
  refuses a second send for the same entry+field while one is already in flight (so even a genuine
  double-dispatch of one UI event can only ever reach the game once). Refusal reasons are also
  translated to plain language for the toast/inline error now (`FriendlyReason`, matching "not
  powered"/"currently moving"/"not controllable" substrings to new resx strings
  `WorldFeature_ReasonNotPowered`/`_ReasonMoving`/`_ReasonNotControllable`) - the raw agent text
  still reaches the log via `EditorLog.Warn("WorldFeatures", ...)`, and an unrecognised reason still
  shows verbatim rather than being swallowed.
- `LiveElevatorsFeatureSession`/`LiveElevatorsChannel`/`elevators.lua` gained a new read-only
  `powered` row field (off the same confirmed `IsPowered()` function `elevators.set` already gates
  a press on), so the player can see an elevator has no power before clicking, not only from the
  refusal afterward.

**B. Trams: a real recall being refused - root cause.** `trams.lua`'s `recallTramToStation` pressed
the linked `TramSystem_RecallStation_C`'s own `TramRecallPressed(true)` and then immediately
re-read `Moving`/`PreviousStation`, refusing ("could not confirm...") when neither had visibly
changed yet. That is the expected case for a real, working recall, not evidence of failure:
`TramRecallPressed` hands off to the recall station's own multi-hop pathfinding
(`FindNextStation`/`GetDirectionFromStation`/`GetNextStopPoint`/`IsStationLocked`, a counted loop -
see the module's own header comment) before it ever touches the tram, and every station stop is a
confirmed full stop (`TramReachedLocation`'s own bytecode) - a synchronous read immediately after
the call can easily land before any of that has moved anything yet.

**Fix.** `recallTramToStation` still refuses up front for the same two concrete, pre-press reasons
as before (the tram's moving state cannot be read at all, or it is already moving) and still refuses
when no recall station links the requested tram/station pair - but a press that changes none of
`Moving`/`TargetStation`/the recall station's own `TramRecallStatus` synchronously is now accepted
(`nil`, no error) instead of refused; a changed value on any of the three is still read back as a
same-tick confirmation when the game happens to be fast enough to show one, but is no longer
required. The next `trams.list` poll (or the live editor's own periodic refresh) shows whatever the
tram and recall station end up actually reporting - the same "the write already reached the game" 
acceptance `elevators.set` uses for a move that has not yet arrived, extended to a press that has
not yet even started moving.

**Power property: not found.** Re-checked the earlier tram dump
(`Tram_ParentBP.json`/`TramSystem_Station.json`/`Button_Tram.json`/`Button_TramRecall.json`/
`layouts.txt`) for a `power`/`Powered`/`IsPowered` property on any of
`Tram_ParentBP_C`/`TramSystem_Station_C`/`TramSystem_RecallStation_C`/`Button_Tram_C`/
`Button_TramRecall_C` - there is none; a case-insensitive "power" search across every file in that
dump comes back empty. Trams have no analogous "not powered" refusal to add. The dump does show a
real `TramSystem_Station_C:IsStationLocked()` function (checks a world flag,
`CallFunc_HasWorldFlag_ReturnValue`) - a story/progress gate, not electrical power - and
`TramRecallPressed`'s own local-variable list includes `CallFunc_IsStationLocked_Locked`, strong
evidence the button itself already checks it internally. **Still not independently confirmed**:
`TramSystem_RecallStation_C.json`'s own `ScriptBytecode` was never part of any dump (only its
properties/function list), so whether `TramRecallPressed` surfaces a locked-station refusal in any
observable way (as opposed to silently no-op'ing) is unproven - a future round could close this with
that one file's bytecode. No new "station locked" refusal was added to `trams.set` without that
evidence.

**Tests.** `python tools/run-lua-tests.py`: 1327 checks passing (up from 1314 before this round) -
new elevator `powered`-field cases (a normal fixture reporting `true`, an explicitly-unpowered
fixture reporting `false`, and an unfamiliar/no-`IsPowered()` fixture reporting absent rather than a
guessed `false`), plus the trams "no synchronous effect is now accepted, not refused" cases
(including one that only changes `TramRecallStatus`, proving the read-back actually checks the
recall station's own status and not only the tram's `Moving`/`TargetStation`). C#: new
`WorldLiveElevatorsAreaTests.cs` (this area had none before - the same convention every other
settable live area already has one of; its absence is plausibly why the round-123-class gap went
unnoticed here for as long as it did) and a new cross-area `WorldLiveEditFailureContractTests.cs`
that (1) enumerates every `Live*FeatureSession.cs` under `Models/` and asserts any one that calls
into its channel from `SetMapFeatureField` also catches `LiveAgentException` there, and (2) asserts
`WorldFeaturesTab.SetFieldAsync` still has its catch/finally/revert/pending-guard shape - so a
future area or a future edit to the shared tab that reintroduces either half of this bug fails a
test instead of waiting for another live retry storm to surface it. Both are source-text contract
tests (this repo's existing style for these live-area checks; there is no bUnit/component-render
harness here to drive an actual click), not `dotnet test`-verified this round (the desktop app was
running live for the owner's own testing, so the build was left untouched - identifiers were
re-checked by hand against each file's actual current contents, including the concurrently-landing
round-122/round-124-adjacent dedupe and `@key` work in the same files).

**Docs.** `docs/reference/live-editing-protocol.md`: the elevators section gained the `powered`
field and a note on the round-125 exception-handling fix; the trams section's confirmation
paragraph was rewritten to describe the tolerant read-back and the "no power property found"
finding. English resx only (`WorldFeature_ReasonNotPowered`/`_ReasonMoving`/`_ReasonNotControllable`);
de/es/fr left for a follow-up localization pass, matching how other rounds have handled new
English-only strings.

**Risk.** Low for the C# fix (adds a catch/finally/guard around an existing call path; no change to
what a successful edit does). Low-moderate for the trams tolerance change: a recall that the game
genuinely, silently refuses (not observed, but not disprovable without `TramSystem_RecallStation_C`'s
bytecode - see above) would now report success instead of an honest error, with the real state only
surfacing on the next list/refresh instead of immediately - an explicit, documented trade accepted
per this round's instructions, matching what the owner asked for. Not verified against the running
game this round (see the Tests note); the owner is running live and can confirm both on the next
session.

## Round-124: NPCS tab - the Dead checkbox let you "kill" a hologram the game never actually kills (2026-09-18)

Owner report, NPCS tab (story-character section): the hint text already says "Story hologram
(scripted scene, cannot die)" and "Static trader stand", yet the Dead checkbox next to those rows
was fully interactive - a dishonest control, since toggling it did nothing the game would honour.

**Decision, from evidence.** `docs/reference/research/research-narrative-npcs.md` shows all 62
fixture holograms were alive across 27 fixture saves; every observed `IsDead = true` entry was a
`Human_ParentBP` or `Ela` row, never a hologram, and the two `Human_TRADER` rows are static stand
actors, not combatants. Nothing in that research shows the game visually honouring `IsDead`/
`IsCorpse` for a hologram (no "the actor disappears" evidence) - the opposite: holograms are
described as non-interactive scripted scenes players cannot kill in-game at all. So the fix is to
make the control honest by disabling it, not to relabel it as a working kill switch.

**Fix.** `NpcIdentityCatalog` (`Core/Catalogs/World/NpcIdentityCatalog.cs`) gained a
`CanBeKilled(id, actorName)` lookup on the same curated hint table the labels already use:
`Human_Hologram` and `Human_TRADER` are `false`; `Human_Killable`, `Human_ParentBP`, `Ela_`,
`HastaTria`, `Larva_`, `MGT_CKCore`, and every unrecognised class default to `true` (the table only
ever turns the control off on positive evidence, never as a default-deny for an unknown class).
`WorldNpcsTab.razor`'s Dead checkbox now adds `!CanKillSelected(selected)` to its existing disabled
condition and shows a `title` tooltip when disabled, built from `WorldNpcs_CannotDieTooltipFormat`
- the same `title="@(cond ? null : ...)"` pattern `PlayerCharacterTab.razor` already uses for its
own catalog-gated controls. This is one component for both file and live mode (round 77/92), so
both hosts get the fix for free. The checkbox still shows whatever `IsDead`/`IsCorpse` a row already
carries (a hologram or trader row that legitimately has story-scripted data keeps displaying it) -
only the toggle is gated, not the underlying data.

**Follow-up, same round: "hologram" is jargon (owner).** The owner pointed out players do not know
the word "hologram", so `NpcIdentityCatalog`'s labels were rewritten as plain one-line explanations
instead of game-internal class names: `Human_Hologram` is now "Recorded projection that plays a
scene - not a living character" and `Human_TRADER` is "Trading stall fixed in one spot - not a
character you can fight" (the other labels got a lighter pass for the same clarity). These labels
are what the tab already shows as the row's primary name (when no real character name resolved
from the round-99 registry) or its secondary line under a resolved name like "Dr. Manse" (see
`WorldNpcsTab.CharacterName`/`CharacterSecondary`), so the plain-language fix reaches the tab with
no template change. The Dead checkbox's tooltip now reuses the same label text
(`WorldNpcs_CannotDieTooltipFormat`, a localized "Can't be killed here: {0}." wrapper around
`NpcIdentityCatalog.LabelFor`) instead of a separate generic sentence, so a hologram's tooltip and
its row both say the same "recorded projection, not a living character" thing - one source of
truth. `NpcIdentityCatalog`'s labels themselves stay English-only curated game-content data (same
as before this round; they were never run through the resx pipeline), matching how the rest of
`Catalogs/` is documented in CLAUDE.md.

**Live write path, investigated, not changed.** `narrative.lua`'s `narrativenpcs.set` writes
`IsCorpse` directly with no confirmed setter (already documented there as "genuinely unproven
whether this alone updates the NPC's ragdoll/visual state live without a game restart") - that
caveat applies uniformly to every narrative-NPC class, not specifically to holograms, so there is
no evidence basis for turning the write into a per-class refusal. In practice no row reaches this
handler from the tab any more for the disabled classes, since the UI never sends the request. Added
a comment on the handler explaining the reasoning so a future probe that does find a class-specific
difference knows to add a named refusal there instead of leaving a silent no-op.

**Tests.** `tests/AbioticEditor.Tests/NpcIdentityCatalogTests.cs` (new): hologram/trader-stand
`false`, killable/named/generic-ParentBP `true`, and an explicit unknown-class-defaults-to-`true`
case. `tests/AbioticEditor.Tests/WorldLiveAreaParityContractTests.cs` gained a source-text check
that the tab's disabled condition and tooltip resource key are actually wired up, plus the new
resource key in the existing `Merged_npcs_tab_resource_keys_exist_in_AppResources` check.
`python tools/run-lua-tests.py`: 1314 checks passing, unchanged (the Lua edit is comment-only).
English resx only (`WorldNpcs_CannotDieTooltipFormat`); de/es/fr/ru left for a follow-up
localization pass.

**Risk.** Low - additive data table plus a UI gate on an existing control; no writer/reader/GVAS
format touched, no change to what the checkbox does when it is enabled. The app was running live
during this change (owner testing), so it was not rebuilt or restarted to verify in the UI; the
source-text test pins the wiring instead.

## Round-122: a duplicate `@key` crashes the whole page, made structurally impossible, not just fixed (2026-09-18)

Second crash of this exact class in one day (`editor-20260918.log`, 14:48:12): `CircuitHost:
Unhandled exception ... More than one sibling of element 'button' has the same key value,
'CA_PunchCard_TutorialPanelTrigger'`, thrown by Blazor's `RenderTreeDiffBuilder` on the live
TRIGGERS tab (`WorldFeaturesTab.razor`, shared by every world-feature area). The game places
several trigger volumes that share one game-authored `UniqueTriggerID`; the row list keyed
straight off that id, so two loaded volumes with the same id produced two sibling rows with the
same `@key`. A renderer diff exception like this cannot be caught by any `ErrorBoundary` - it kills
the whole page circuit, which is what "clicking between the tabs breaks the app again" was: the
first instance of this class was the Bases tab, fixed in Round-118 by namespacing its keys, but
that fix only prevented one specific collision, not the underlying pattern.

**Fix, three layers so a future tab cannot reintroduce this:**

1. **`RenderKeys` (`Web.Shared/Services/RenderKeys.cs`, new, static, pure)** turns any sequence
   into `(item, uniqueKey)` pairs: the first item with a given id keeps that id as its key, the
   2nd/3rd/... item sharing the same id gets `"{id}#2"`, `"{id}#3"`, ... so a key can never repeat.
   Audited every `@key=` under `Web.Shared/Components` (48 sites) and routed every loop keyed by
   data this app does not fully control - live-agent rows, save-file entries, catalog/game-registry
   ids - through it: `WorldFeaturesTab` (entries via a new `FeatureRow.RenderKey`, and fields),
   `WorldNpcsTab` (both the story-character and live-creature lists), `WorldBasesTab` (benches,
   painted objects, base containers - on top of Round-118's own "bench:"/"paint:" namespacing, which
   only prevented cross-list collisions, not same-list ones), `WorldContainersTab`,
   `WorldContainmentTab` (units, and orphaned assignments keyed by creature name, which can repeat),
   `WorldDoorsTab`, `WorldDroppedItemsTab`, `WorldFlagsTab`, `WorldPetsTab`, `WorldStoryTab` (both
   recipe lists), `WorldTradersTab` (traders, and an item a trader can legitimately sell under two
   different unlock flags), `WorldVehiclesTab` (regrouped so ids are deduped across the WHOLE list,
   not just within one region group), `LiveChemistryBenchTab` (benches, flasks, recipes), and the
   Player tabs (Achievements, Codex, Inventory ground items, Recipes list + research queue,
   ReleaseNotesDialog). Left alone, with a comment recorded at each site, wherever the key is
   provably safe: a real `Dictionary` key (`WorldPetsTab`'s limb health), a hardcoded C# catalog
   array (`BenchUpgradeCatalog.All`, `StoryProgressionCatalog.Chapters`,
   `SkillLocalization.MilestonesFor`), a counter this app owns (`BaseDetector`'s "Base 1"/"Base 2"
   naming), a `GroupBy` key (inherently distinct), or a reference-typed object used as its own key
   (`IniEditor`'s section/entry drafts). `LiveConnect.razor`/`SaveEditorSurface.razor`'s
   `AppErrorBoundary @key="_worldTab"` is a single element, not a loop, so it was left as-is.

2. **Data-layer model fix for triggers.** `areas/triggers.lua`'s `triggerRows()` now merges every
   placed volume sharing a `UniqueTriggerID` into ONE row - matching the save's own `TriggerMap`,
   which only ever has one entry per id: `timesTriggered` takes the highest count seen across the
   volumes, `hasBeenTriggeredOnce` is true if any of them is. `triggers.set` now finds and edits
   EVERY volume sharing an id (`findTriggersById`, replacing the old first-match-only
   `findTrigger`), so a fire-count write or reset stays in lockstep across all of them. New harness
   case in `tests/cases/triggers.lua` places two volumes under one id and asserts exactly one merged
   row plus both volumes reset. Checked the other new live areas (buttons, elevators,
   resourcenodes, destructibles, corpses, npcspawns, powersockets, trams, portals) for the same
   possibility: all nine already key by `ctx.fullName(actor)` (`GetFullName()`, engine-guaranteed
   unique per loaded actor), confirmed by reading each module's own `id = name` assignment -
   triggers is the one documented exception. Added a shared `LiveFeatureRows.DistinctById`
   (`Web.Shared/Models/LiveFeatureRows.cs`) as a second, independent backstop in all ten
   `Live*FeatureSession` classes anyway (including triggers, on top of the Lua-side merge), so even
   an engine edge case or a future area that forgets this convention cannot reach the render tree.

3. **Regression guard.** `RenderKeySafetyContractTests` (new) scans every `.razor` file under
   `Components` in both front-end projects for a bare `loopVariable.Id`/`.Key`/`.Row`/`.Name` `@key`
   expression - exactly the shape that crashed - and fails unless that exact site is on an explicit,
   commented allowlist (the four provably-safe catalog/Dictionary/counter sites above); it also
   fails if an allowlist entry goes stale (the site it names no longer exists). `RenderKeysTests`
   covers the helper directly: unique ids pass through unchanged, repeats get `#2`/`#3`/... in
   order, a null id selector result is treated as `""`, and `KeyLookup` gives distinct dictionary
   keys to distinct row instances that happen to share an id.

**Risk left on the table:** the UI-layer fix accepts a cosmetic multi-select edge case as the price
of never crashing - if two rows still end up sharing the same *domain* id (not just render key) in
some area nobody has audited yet, clicking one can visually select both, since `WorldFeaturesTab`'s
selection still compares the real domain key (`row.Key`), not the render key. That is a UI glitch,
not a crash, and is the intended trade-off; the actual fix for a *known* offender (triggers) is the
Lua-side merge in step 2, which removes the domain-level duplicate entirely.

## Round-121: BASES tab - benches renamed in-game showed no name, and "nearest to me" was missing (2026-09-18)

Owner report, live mode, BASES tab: the tab showed bases for places unrelated to the region being
viewed, crafting benches renamed in-game showed no name here, and there was no way to tell which
bench was actually closest to the player.

**Name - root cause.** `bases.lua` read/wrote `AbioticDeployed_ParentBP_C`'s `AlternativeObjectName`
(`FTextProperty`, "Edit | BlueprintVisible | DisableEditOnInstance" - confirmed against the class
dump to carry no `Net` flag at all). `main.lua`'s own `containers.rename` had already investigated
and rejected that exact field for the identical reason, in favour of `PlayerMadeString` (a
replicated `Net | RepNotify` `StrProperty` on `AbioticDeployed_Furniture_ParentBP_C`, which benches
and containers both derive from) - `bases.lua` never picked up that finding. A write with no `Net`
flag is only ever seen by whichever machine made it, and reads back as whatever the game's own
systems last put there (nothing, for a bench that has never gone through the game's own in-world
rename prompt) - exactly the "no name shows" symptom reported.

**Name - fix.** `bases.lua` now reads/writes `PlayerMadeString` with the same mark-dirty +
`NewPlayerMadeString()` refresh `containers.rename` already proved live, matching the save file's
own `CustomTextDisplay_` leaf (`WorldSaveWriter.ApplyDeployableCustomText`/`ApplyContainerCustomName`,
confirmed against `WorldSaveReader`'s matching read). A deployable class with no `PlayerMadeString`
at all (not Furniture-derived) still reads `AlternativeObjectName` as a read-only fallback so it
does not regress to showing nothing, but the fallback is never written to for a class that has the
real field.

**Scope - investigated, not changed.** `bases.lua`'s `deployableRows()` sweeps
`AbioticDeployed_ParentBP_C` with `FindAllOf` and no per-actor map-path filter - exactly like every
other region-scoped live area (`doors.list`/`containers.list` in `main.lua`, `destructibles.lua`,
`triggers.lua`; none of them filter by the actor's map path either). The only scoping this protocol
has anywhere is `LiveConnect.razor`'s `ResetRegionScopedWorldSessions`, which drops the cached
BASES session when `world.info`'s `levelToken` changes, so the next tab visit re-sweeps whatever
region is loaded now - BASES was already wired into that reset list. No working precedent exists in
this mod for filtering `FindAllOf` by map path, and there was no running game available this round
to test one against, so adding an unproven filter risked hiding real, loaded bases instead of
fixing anything. Given the Facility region alone streams several named sub-levels at once (per
CLAUDE.md, it is the ~16 MB region), a base reported "for an unrelated place" may simply be far away
within the same region rather than genuinely a different world save.

**Mitigation, per the task brief's own fallback instruction.** Since scoping could not be verified
either way, the desktop app now shows each row's own sub-level and lets the player sort by distance
instead of guessing at a filter:
- `WorldDeployable.SubLevel` (`Core/Domain/World/WorldBase.cs`) parses a deployable's own `Id` with
  the same `DoorIdParser` the DOORS tab already uses on `WorldDoor.Id` (both ids are the identical
  UE actor-path shape, live or file) - no protocol change needed, since `id` already carried this.
- `WorldDeployable.DistanceTo` (same file) mirrors `WorldContainer`/`WorldDroppedItem`'s existing
  method of the same name.
- `WorldBasesTab.razor` gained a `PlayerPosition` parameter (same shape/convention as
  `WorldContainersTab`/`WorldDroppedItemsTab`), a "Nearest first" sort toggle, and a per-row
  distance + sub-level line on both the base list and each crafting bench. `LiveConnect.razor` wires
  it to `_spawn?.LivePosition` (the connected player's own read position); `SaveEditorSurface.razor`
  wires it to the workspace's selected player's saved respawn point (`Workspace.TransferPlayerSession`),
  when one is open - offline had no position source for this tab before.

**Tests.** `live-agent/AbioticEditorLiveAgentLua/tests/cases/bases.lua`: rewritten name-read/write
cases (`PlayerMadeString` primary, `AlternativeObjectName` read-only fallback, mark-dirty +
`NewPlayerMadeString` call counts), an x/y/z-present case backing the new client-side distance math,
and a case proving a deployable from a different sub-level still appears in `bases.list` (locks in
the "not region-scoped" design). `python tools/run-lua-tests.py`: 1314 checks passing (up from 1304
last round). `tests/AbioticEditor.Tests/LiveBasesSessionTests.cs` gained
`WorldDeployable_SubLevel_parses_both_the_live_and_file_id_shapes` (live-GetFullName-shaped and
file-map-key-shaped ids) and `WorldDeployable_DistanceTo_computes_straight_line_distance`. Could not
run `dotnet build`/`dotnet test` this round (desktop app was running live against the game); the new
C# tests should be run once the app is closed.

## Round-120: live DELETE/remove buttons no longer leave the item on screen for several seconds (2026-09-18)

Owner report, live mode: clicking DELETE on an inventory/container slot (and removing a dropped
item, pet, or corpse) did not clear the row right away - it stayed visible for a few seconds
before disappearing.

**Root cause.** Every live delete/remove path (`LiveInventorySession.PushSlotAsync`/
`TryDropSlotLiveAsync`, `LiveContainersSession`'s `ApplyAsync` behind
`TrySetContainerSlotAsync`/`SetContainerSlotCountAsync`, `LiveDroppedItemsSession.RemoveDroppedItemsAsync`,
`LivePetsSession.RemovePetAsync`, `LiveCorpsesFeatureSession.RemoveMapFeatureEntry`) sent the
write, then AWAITED a second, full re-read (a four-inventory `inventory.list`, a `containers.get`,
or a world-scanning `dropped.list`/`pets.list`/`corpses.list`) before returning - and the owning
tab's own `StateHasChanged()` only fires once that whole chain completes. The wire itself is fast
(the Lua agent polls its ipc folder every 50 ms), but stacking a second full-mailbox round trip
behind the first is what showed up as "a few seconds", worst for the player inventory since its
re-read lists all four inventories' slots every time.

**Fix.** Each of those five methods now applies the write's own already-known result to the
session's local model and raises `Changed` the instant the write is confirmed, then kicks off the
old full re-read in the background as reconciliation only (`_ = ReconcileAsync(...)`, never
awaited by the caller). A background reconciliation failure is swallowed rather than surfaced,
since the edit already succeeded by the time it runs. File-mode sessions are untouched (this is
all inside the `Live*Session` classes only).

**Tests**: one fake-channel test per touched session (`LiveInventorySessionTests`,
`LiveContainersSessionTests`, the new `LiveDroppedItemsSessionTests` case, and two new files
`LivePetsSessionTests`/`LiveCorpsesFeatureSessionTests`) arms a gate that blocks the reconciling
read and proves the session's collection already reflects the delete right after the awaited call
returns, before that read is ever allowed to complete. No Lua changed, so
`python tools/run-lua-tests.py` was not re-run. Could not run `dotnet build`/`dotnet test` this
round (desktop app was running live against the game); the new tests should be run once the app is
closed.

## Round-119: fixed MoonFish_AllDay/MoonFish_rare1_AllDay never sticking in the FISH codex (2026-09-18)

Owner report: in the player CODEX tab's FISH list, ticking `MoonFish_AllDay` or
`MoonFish_rare1_AllDay` never took (live mode most likely, but it never worked offline either),
while the plain `MoonFish`/`MoonFish_rare1` rows worked fine.

**Root cause.** `DT_Fish` carries a second row for Moon Fish and Pelagic Moon Fish suffixed
`_AllDay` (see `assets/registry/registry.en.json`): same item/recipe/bait tag/XP as the plain row,
only the time-of-day catch-chance multipliers differ (an "always bites" schedule vs. the plain
row's midnight-only one - almost certainly swapped in by a world setting). Real save fixtures
confirm the game only ever records the base id in `Compendium_Fish_`/`FishCaughtArray`, never the
`_AllDay` one, no matter which schedule was active when the fish was caught. So the `_AllDay` row
was a dead-end GATEPal checkbox: live, marking it known wrote to an id the game's own
`FishCaughtArray` never reads back, so the next periodic refresh reverted the tick; offline, the id
staged into `Compendium_Fish_` but the native game's own journal, which tracks the same base id,
never showed it caught either.

**Fix.** `Core/Catalogs/Codex/CodexCatalog.cs`: `LoadFish` now runs its rows through a new
`CollapseAllDayVariants` step that drops an `_AllDay` row when its plain sibling is present, kept
public so it can be unit-tested with synthetic rows and no game install. Both the live and file
codex sessions read fish through this same `CodexCatalog.LoadFish`/`CodexVocabularyService` path,
so the fix applies to both modes from one place; no changes needed in `LivePlayerCodexSession`,
`PlayerCodexEdit` or `codex.lua`.

**Tests** (`tests/AbioticEditor.Tests/CodexTests.cs`): `CollapseAllDayVariants_...` proves the real
`MoonFish`/`MoonFish_rare1`/`MoonFish_AllDay`/`MoonFish_rare1_AllDay` ids collapse to the two real
fish (and that an "_AllDay" row with no plain sibling is kept rather than dropped);
`PlayerCodexEdit_TogglesEachCollapsedFishRowIndependently` proves the two surviving fish toggle
known/unknown independently of each other through the session-facing model. Ran
`python tools/run-lua-tests.py` (1304 checks, all passing, unchanged) since `codex.lua` was not
touched. Could not run `dotnet build`/`dotnet test` this round (desktop app was running live against
the game); the new tests should be run once the app is closed.

## Round-118: the bench-upgrade crash that killed every button, and the app can no longer go silently dead (2026-09-18)

A live report: upgrading a bench crashed the app so hard nothing could be clicked afterwards. The
editor log pinned the actual throw to `RenderTreeDiffBuilder`: "More than one sibling of element
'div' has the same key value" for a `Deployed_CraftingBench_Default_C` actor's own id.

**Root cause.** Not duplicated live-agent data. `WorldBasesTab.razor` renders two separate
`@foreach` loops as direct siblings under one shared `wt-card` - "Crafting Benches", then a little
further down the same base's "Painted Objects" - each producing a `<div class="bench-row">` keyed
on the bare deployable id. `Deployed_CraftingBench_Default_C` is both a crafting bench
(`WorldDeployable.IsCraftingBench`) and paintable (`DeployablePaintCatalog`), so it rendered in
*both* lists with the identical `@key`, and Blazor's diff algorithm requires unique keys among
every sibling one render pass produces, even across two unrelated loops, once they land in the
same parent. Fixed by namespacing each loop's key (`"bench:" + bench.Id` / `"paint:" + deployable.Id`).
That duplicate-key error is raised by the renderer's own diff pass while applying a render batch,
*not* as a normal per-component render exception, so it can escape even an `ErrorBoundary`
wrapping the whole page - the keys themselves had to be the fix, not a boundary.

Belt-and-braces on the data side too, since a genuinely duplicated id from the live agent is still
a real possibility independent of this bug: `bases.lua`'s `deployableRows()` was the one
single-class `findAll()` sweep in the whole live-agent that had no `seen`-table dedupe (every other
one - corpses, destructibles, buttons, etc. - already defends against `FindAllOf` itself returning
a duplicate, even with only one root class); added the same guard there. `LiveBasesSession.Apply`
built its id dictionary with a raw `ToDictionary`, which throws uncaught on any duplicate key with
nothing on the calling side watching for it; it now dedupes (first entry wins) before building
anything. Audited every other `@key=` in `Components/` for the same "two loops, one parent, one id
field" shape (containers, doors, dropped items, pets, vehicles, npcs, features, story) - all clear
project-wide: the story/NPC tabs that reuse `npc.Id`/`row.Id` twice are always mutually exclusive
branches, and every other repeated-field list lives in its own separate parent element.

**The app must not go silently dead again, whatever throws next.** MainLayout already wraps the
whole routed page in one `AppErrorBoundary`, but that only ever showed one page-wide failure card
and (per the finding above) cannot even guarantee that for a duplicate-key error specifically. Gave
both the file editor's world/player tab strips (`SaveEditorSurface.razor`, `PlayerEditor.razor`)
and the live one (`LiveConnect.razor`) their own `<AppErrorBoundary @key="<active tab>">` around
just the active tab's content: an ordinary exception in one tab now shows an inline retry card
without taking the sidebar, tab strip or any other tab down with it, and switching tabs remounts a
fresh boundary on its own.

Separately, `App.razor` never defined the `components-reconnect-modal` element `blazor.web.js`
expects - it only ever toggles CSS classes (`components-reconnect-show`/`-failed`/`-rejected`) on
an element with that id; it never creates the element or styles it. So a dead circuit (this crash,
a lost connection, the process restarting) rendered as a frozen window with literally nothing on
screen to explain why. Added the element, its reload button, and CSS for all three states, so a
lost connection now shows "Connection lost" with a working Reload button inside the Photino window.

Also found and fixed the two other crash-log entries from the same session: `ModalHost.razor`'s
`Dispose()` fired an unawaited, unguarded `abiotic.modal.deactivate` JS call - unlike every other
modal shell in the editor (`ModeSelectDialog`, `Settings`, `ReleaseNotesDialog`), which already use
`async ValueTask DisposeAsync()` with a `try/catch (JSDisconnectedException)`. Worse, ModalHost is
mounted unconditionally in MainLayout, so its very first instance is disposed on *every single
launch* when the framework replaces the static-prerendered tree with the interactive one, long
before any modal was ever shown - that is exactly the "JavaScript interop calls cannot be issued at
this time... the component is being statically rendered" unobserved-task-exception pair the log
showed at 13:20. Fixed to match the established `DisposeAsync` pattern and to skip the call
entirely when nothing was ever activated. `CrashLog`'s `TaskScheduler.UnobservedTaskException`
handler logged every one of these as `"Crash"`, burying the one entry that actually mattered under
expected disconnect noise; it now recognizes a fault that is *entirely* `JSDisconnectedException`
and quietly marks it observed instead.

New tests: `LiveBasesSessionTests.ConnectAsync_dedupes_a_deployable_the_agent_reports_twice`,
`WorldBasesTabKeyUniquenessTests`, `PerTabErrorBoundaryTests`, `ReconnectionUiWiringTests`.

## Round-117: player-facing live-editing docs caught up to rounds 92-113 (2026-09-18)

The player-facing live-editing documentation had fallen well behind the last several weeks of
live-editing work (rounds 92-113: buttons, elevators, resource nodes, breakables, corpses, NPC
spawners, triggers, power sockets, trams, a vehicle's on-board storage, tamed-pet species change,
garden plots/Power Chairs/chemistry benches, traits, appearance, bench-upgrade paint and the
tag-write rewrite, world-wide seen lists, and the Linux/Steam Play work). Read `docs/PROGRESS.md`
rounds 92-113 and `docs/reference/live-editing-protocol.md` end to end and rewrote the player-facing
material to match.

**`docs/guide/live-editing.md`.** The old "What you can change" section was a short prose list that
predated most of the above. Replaced it with a Player table and a World table (area, what works
live, whether it needs host authority, and a plain-language reason for anything read-only or
impossible), covering every area named in the coordinator's brief: on the player side vitals,
skills, inventory/transmog/companions, recipes, codex/GatePal (including the now-settable
kill-tracked compendium sections), traits, appearance, and spawn; on the world side flags, story,
clock/weather, doors, containers, dropped items, bases and bench upgrades, vehicles (including
on-board storage), pets (including the Peccary/Lamogi limitation and the now-live species change for
matched pets), story characters and creatures, containment, traders, portals, elevators, buttons,
resource nodes, breakables, corpses, NPC spawners, triggers, power sockets (genuinely fully
read-only, and why), trams, garden plots/Power Chairs/chemistry benches, and the world-wide seen
lists browser. Added the honest note that most of the newer world-object areas were built by reading
the game's own code but have not yet been tried against a running game, and the "try a pet species
change on one you can afford to lose first" caveat. The Linux/Steam Play warning box now reflects
round 113: tested through Wine on a non-Proton stand-in (three real bugs found and fixed there), with
a real Proton game session and the desktop app's own window (needs glibc 2.38, roughly Ubuntu 24.04+)
both still unconfirmed.

**Other stale claims fixed.** `docs/guide/desktop-app.md`, `docs/guide/index.md` and `README.md`
each still said live editing was Windows-only; all three now also mention the Linux/Steam Play path
round 98 added. `docs/nexus-mod-page.bbcode` has the same stale Windows-only claim plus a supported-
areas list that predates most of rounds 92-113, but the file has the owner's own uncommitted edits in
progress, so it was deliberately left alone; the owner should update it directly (see this round's
handback report for the exact lines).

**`AppResources.resx` (English only).** Fixed four keys whose live-limitation claims were plainly
wrong: `WorldPets_LiveSpeciesChangeUnavailable` (used to say species change "isn't available while
editing live" full stop; it now is, for matched Pest/Skink-family pets on an up-to-date agent),
`PlayerCodex_LiveCompendiumReadOnlyHelp` (used to say kill-tracked compendium entries "stay read-only
live" unconditionally; round 106 made them settable on a supporting agent),
`WorldStory_LiveGlobalRecipesReadOnlyHelp` (used to say "the running game has no button anywhere to
unlock [a recipe] world-wide"; it does now, this text was only ever the generic fallback for an
unmapped reason), and `PlayerGeneral_ItemsCraftedLiveReadOnlyHelp` (used to say the running game "has
no function to mark [an item] crafted on demand"; it does, an older agent just doesn't expose it).
Did not touch the General tab's traits-readout key, per the coordinator's note that another agent
owns it this round.

Not run: `dotnet build`/`dotnet test` (docs and resx text only, coordinator builds centrally after
concurrent sessions land).

## Round-115: a genuinely fresh install asks language, then mode, before anything loads (2026-09-18)

A brand-new install used to land straight on the "choose a world to edit" screen, which on the
desktop host kicked off its Steam/Game Pass save-folder scan in the background before the player
had even picked a display language or said whether they meant to edit a save file or connect to a
running game. This closes that gap by giving the existing "what do you want to do" prompt
(`ModeSelectDialog`) a first step of its own, and by making sure nothing else starts until it is
answered.

**First-run detection.** No separate marker file: "first run" is exactly "nothing has been written
to `HostPreferenceStore`'s language key yet" (`HostLanguageService.HasChosenLanguage`), the same
signal the original MAUI app's own first-run `LanguagePage` used before the Razor port
(`LocalizationService.HasChosenLanguage`, found in the initial commit's `AbioticEditor.App`). It
holds on both hosts unchanged - the browser keeps the same key in `localStorage` via
`HostPreferenceStore.UseStore`, wired up in `AbioticEditor.Web.Wasm/Program.cs`'s
`UseBrowserStorageForPreferences` before any screen renders.

**The flow.** `ModeSelectDialog` gets a new first step, `Step.Language`, entered only when
`MainLayout` passes it `ShowLanguageStep="true"` (on `_firstRunPending`, computed once in
`OnInitialized` from `!Languages.HasChosenLanguage`). It reuses three resx strings that have sat
translated into all four other shipped languages since the initial commit and were never wired up
anywhere (`Language_Title`/`Language_Subtitle`/`Language_Continue`/`Language_SystemDefault`) - a
leftover from the exact same first-run prompt in the old app. Picking a language calls
`HostLanguageService.SetLanguage` immediately (the same setter the dedicated `/language` page
uses), so CONTINUE and the Choose step right after it already render in the chosen language.
CONTINUE moves to `Step.Choose`, the existing offline/live picker, completely unchanged.

**Nothing loads in the background until then.** `MainLayout` no longer renders `<WorkspaceShell>
<AppErrorBoundary>@Body</AppErrorBoundary></WorkspaceShell>` unconditionally - it sits behind
`@if (!_firstRunPending)`. Because `@Body` is a `RenderFragment` the router hands down, skipping
its invocation means the routed page (`Home.razor`, whose `OnInitializedAsync` runs
`SaveLibraryService.DiscoverAsync` - the Steam/Game Pass folder scan - plus its own recently-opened
list and the `ABIOTIC_EDITOR_FOLDER` auto-open test hook) is never instantiated at all, not merely
hidden behind the dialog the way it already was for every later mode-select reopen. The browser
host previously never auto-opened `ModeSelectDialog` at all (only `_liveEditingAvailable`, which is
always false there, used to gate it); a first run now opens it there too
(`if (_liveEditingAvailable || _firstRun) _modeSelectOpen = true;`), so the same gate applies on
both hosts. `_firstRunPending` is cleared for good the moment `CloseModeSelect` runs (whichever way
the player finished: offline or live), so a later reopen within the same session - the header
button, a dropped live connection bouncing back to the dialog - never blocks `@Body` or shows the
language step again; only the very first prompt does either. Subsequent launches are unchanged
(language remembered, mode select behaves exactly as before this round).

**Tests** (`tests/AbioticEditor.Tests/FirstRunTests.cs`, new): `HostLanguageService.HasChosenLanguage`
round-tripped against the real per-user file (backed up and restored the same way
`ReleaseNotesTests` already handles `ReleaseNotesStore.ConfigPath`) - a fresh profile is a first
run, saving a language ends it for that instance and for a fresh one reading the same file - plus
source-text checks, in the style of `LiveEditingBrowserHintTests`/`OpenGuardTests`, that
`MainLayout` computes `_firstRun`/`_firstRunPending` from `HasChosenLanguage`, gates `@Body` behind
it, opens the dialog on a first run on every host, passes `ShowLanguageStep` through, and clears
`_firstRunPending` in `CloseModeSelect`; and that `ModeSelectDialog` accepts `ShowLanguageStep`,
lists every language by its own name via `L.Available`/`language.NativeName`, applies a pick
immediately through `HostLanguageService.SetLanguage`, and only ever continues into the unchanged
Choose step. Not built or run this round (the desktop app was running from this checkout's own
`bin` folder while this work happened, and a build rewrites the scoped-CSS bundle it serves) -
every identifier was cross-checked against the current source by hand instead.

**Risks / what's unverified:** untested in a running app or browser tab - the reasoning above is
grounded in how Blazor's `RenderFragment`/`RouteView` model actually skips instantiating a
component whose containing fragment is never invoked, not in an observed run. Worth an explicit
first-run click-through (ideally with the language config file removed first) before release.

## Round-114: player GENERAL tab drops its stale traits readout, always shows ACCOUNT (2026-09-18)

The GENERAL tab's ACCOUNT block was hidden behind a "Account and background" disclosure toggle
that also carried a read-only TRAITS list underneath, with a footnote claiming traits could only
be added or removed on the file-based CHARACTER tab while connected to a running game. Neither
half of that was true any more: the background field had already moved to the CHARACTER tab in an
earlier round (leaving the disclosure's own label half-orphaned), and live trait add/remove had
since shipped on the CHARACTER tab itself (`Session.CanEditTraits`, gated on the host agent's
`general.trait.set` support). `PlayerGeneralTab.razor` now shows the ACCOUNT block plainly, with
no collapsible label and no traits section; traits stay exactly where they already fully work, on
the CHARACTER tab, for both file and live sessions. Removed the now-dead
`Editing_AccountDetails`/`PlayerGeneral_Traits`/`PlayerGeneral_TraitsHelp`/`PlayerGeneral_TraitsNone`
resource keys (English-only; no other locale resx had translated them).

## Round-113: Linux live editing verified in WSL as far as WSL allows - three real bugs fixed (2026-09-18)

A Sonnet agent ran the Linux host and the real Windows helper under Wine 6 inside this PC's WSL2
Ubuntu 22.04 (fake Steam library + `wineboot` prefix; reusable as
`tools/verify-linux-live-setup.sh`). `LiveAgentSetup.EnsureReadyAsync` reached `Ready`, the helper
launched through Wine and listened on TCP, and the Proton/save-discovery tests passed on Linux
(44/44). Three bugs surfaced and are fixed:

- **The Linux publish shipped no live agent at all.** `AbioticEditor.Web.csproj`'s live-agent
  bundle item group was gated on `win*` runtime identifiers, so a `linux-x64` publish had no
  `live-agent/` folder and `EnsureReadyAsync` could only ever answer `HelperUnavailable`. Linux
  now bundles the same package (a Steam Play player runs the same Windows game binary).
- **The helper's token/port files were never found.** Wine 6 wrote them under the legacy
  `Local Settings\Application Data` layout (and a plain Wine prefix names its user after the
  Linux account, not `steamuser`). `ProtonLiveAgentEnvironment.LocalAppDataCandidates` now lists
  the expected folder first and then every prefix user in both layouts; the capability and the
  Lua log bridge probe all of them.
- **`IsHelperRunning` never matched under Wine.** The kernel truncates the process name to 15
  bytes (`AbioticEditorLi`), so the exact-name match found nothing and every connect launched
  another helper. It matches on the prefix now.

Still untestable here: the native GTK window needs glibc 2.38 (Ubuntu 24.04+; this WSL is
22.04), and whether UE4SS itself loads under a real Proton prefix. WSL cannot host the game under
Proton, so the in-game mailbox path remains unexercised.


## Round-112: world-wide seen/read/found lists get a browsable UI on the shared STORY tab (2026-09-18)

Round 107 built the live backend for the six world-wide (`Abiotic_Survival_GameState_C`)
discovery lists - items picked up, emails read, journal entries, and the three compendium
unlock-type arrays - but deliberately kept the new session members (`LiveStorySession.
CanEditGlobalLists`, `GlobalItemsPickedUpIds`, etc.) **outside** `IWorldStorySession`, because the
offline file session had nothing to mirror: only the world recipes browser existed on the STORY
tab, and the offline `WorldSaveSession` only staged these six lists for the CONTAINMENT tab's
add-only "unlock everything" sweeps (`EnableWorldItemsSeen`/`EnableWorldEmailsRead`/
`EnableWorldJournalsFound`/`EnableWorldCompendium`, still there, unchanged). This round gives them
a real per-row browser and moves the members onto the shared interface.

**Session boundary.** `IWorldStorySession` gains `SupportsGlobalLists`, six read-only id
collections (`GlobalItemsPickedUpIds`, `GlobalEmailsReadIds`, `GlobalJournalEntryIds`,
`GlobalCompendiumEmailIds`/`GlobalCompendiumNarrativeIds`/`GlobalCompendiumExplorationIds`),
`CanEditGlobalLists`, `GlobalListEditsUnavailableReason` (defaults to null), and
`SetGlobalListAsync(list, ids, present, ct)` - shaped exactly like the existing world-recipes
members, and `SetGlobalListAsync`'s wire-name parameter (`"itemsPickedUp"`, `"emailsRead"`,
`"journalEntries"`, `"compendiumEmail"`, `"compendiumNarrative"`, `"compendiumExploration"`)
matches `LiveWorldUnlocksChannel.SetGlobalListAsync`'s existing wire names exactly, so one method
serves both session kinds with no per-kind branching in the tab. `LiveStorySession` already had
every member except `SupportsGlobalLists` (added, mirroring `SupportsRecipes`) - round 107's work
satisfies the rest of the interface unchanged.

**Offline (`WorldSaveSession`).** New members reuse the existing `_stagedWorldUnlocks` storage
(the same dictionary the CONTAINMENT tab's bulk sweeps already stage into) rather than a second
staging path. `CanEditGlobalLists` mirrors `CanEditGlobalRecipes` exactly (`FindByPrefix
("GlobalUnlocks") is not null`, the same "no unlock ever recorded yet" limitation the recipe gate
already has). A new `StageWorldUnlockEdit(prefix, ids, present)` sits beside the old add-only
`StageWorldUnlock` (kept unchanged - the CONTAINMENT sweeps only ever add) and supports removal
too; `SetGlobalListAsync` maps the wire list name to a `GlobalUnlockPrefix` constant through a new
`GlobalUnlockPrefixByWireName` dictionary and stages the edit. Save still goes through the
existing per-prefix `WorldSaveWriter.ApplyGlobalUnlockArray` loop, unchanged - it already creates
a missing `GlobalUnlocks` struct and array using the exact hash-suffixed `FullNames` (verified
against the `DedicatedServerSaves/Worlds/Cascade` fixture, which already carries
`GlobalItemsPickedUp_32_0D99146044C3330A30A4C4AB8980DAF4` etc. byte-for-byte), so no writer changes
were needed at all this round - only the session/UI layer had no way to reach it per-row.

**UI (`WorldStoryTab.razor`, both hosts).** One "WORLD-WIDE SEEN" section, not six copies of the
recipes browser: a dropdown picks which of the six lists is on screen, reusing the recipes
browser's own `ws-recipe-list`/`ws-recipe-row`/`ws-recipes-head` CSS classes verbatim (no new
styles). Friendly names come from the same services the player CODEX tab uses to name its rows -
`ItemCatalogService` for items, `CodexVocabularyService` (loaded the same on-demand,
off-render-thread way `PlayerCodexTab` already loads it) for emails/journals/compendium. Each row
list is "every id the installed game's data knows for that kind, plus any id already present that
game data doesn't recognise" - the same rule the recipes browser's `AllWorldRecipeRows` already
uses. Read-only help text mirrors the recipes browser's pattern (a specific reason when the live
agent reports one, a generic fallback otherwise).

**New resx keys (English only, matching this repo's convention for brand-new keys - the
adjacent `WorldStory_LiveGlobalRecipesNotHost`/`ReadOnlyHelp` keys from round 77 are English-only
for the same reason despite sitting next to translated recipe-browser keys):**
`WorldStory_WorldWideSeen`, `WorldStory_SearchWorldWideSeen`, `WorldStory_NoWorldWideSeen`,
`WorldStory_WorldWideSeenCountFormat`, `WorldStory_WorldItemsPickedUp`,
`WorldStory_WorldEmailsRead`, `WorldStory_WorldJournalEntries`, `WorldStory_WorldCompendiumEmail`,
`WorldStory_WorldCompendiumNarrative`, `WorldStory_WorldCompendiumExploration`,
`WorldStory_LiveGlobalListsNotHost`, `WorldStory_LiveGlobalListsReadOnlyHelp`.

**Tests** (`tests/AbioticEditor.Tests/WorldStoryGlobalListsBrowserTests.cs`, new): an offline
add-then-save-then-reread round trip and a remove-then-save-then-reread round trip against the
`ServerWorldsDir` fixture (asserting the `.bak` matches the pre-edit bytes exactly, the established
"nothing else changed" idiom this repo's other session round-trip tests already use), an
unknown-list-name refusal, live `SupportsGlobalLists` true/false coverage (a stubbed agent that
fails `worldunlocks.get` entirely, simulating a pre-round-107 build) plus a live add via the
shared interface, and source-text checks that `WorldStoryTab.razor` renders the section off
`Session.SupportsGlobalLists`/`CanEditGlobalLists`/`SetGlobalListAsync` and that both
`WorldSaveSession.cs` and `LiveStorySession.cs` implement the new members. Not built or run this
round (coordinator builds centrally, per instruction); every C# identifier was cross-checked
against the existing source instead.

**Risks / what's unverified:** the UI has not been opened in a running app or against a live game
- everything above is grounded in the round-107 backend (already covered by
`LiveCodexKillSectionsAndWorldGlobalListsTests`) and the existing writer coverage
(`GlobalUnlockWriterTests`), plus the new tests above, but nobody has clicked the new "WORLD-WIDE
SEEN" section in the actual app or against a live game. Only English strings exist for the new
keys, matching the repo's brand-new-key convention; no other locale is aware of the section yet.

## Round-111: dropped-item positioning live, and a stale/missing explanation on the BASES tab fixed (2026-09-18)

Closed the two remaining live-editing parity gaps the coordinator scoped for this round: dropped
items always landed wherever the game chose (no caller-chosen position, unlike the file editor),
and the BASES tab's bench-upgrade gate had no honest explanation for the player. **Not yet
exercised in the running game** - proven against the Lua stub harness only.

**A. Dropped-item positioning (`dropped.add`).** `dropped.add` still routes through a scratch
inventory slot plus the character's own `Request_DropInventorySlot` RPC (round 77, unchanged), but
now accepts an optional `x`/`y`/`z` (all three or none) and moves the item there afterwards. The
game mode's own `SpawnItem(InTransform, ItemRow, StackSize, Durability, NoPhysics, NoCollision,
ConnectToComponent, ConnectToBone, ...)` (evidence: coordinator's `gapprobe/pass3/layouts.txt`
~line 5441) was considered and rejected: its `ItemRow` parameter is a `DataTableRowHandle` struct
that has to be built and passed ACROSS a function-call boundary - exactly the class of
struct-marshaling that crashed the whole game outright for bench upgrades (see gap B and
`bases.lua`'s header comment). The only proven precedent in this codebase for passing a
`DataTableRowHandle`-shaped table as a function *argument* (`weatherRowHandleToTable`, `world.set`)
only works because its fields are copied from a handle the engine itself already enumerated
(`GetAllWeatherEventRowHandles`); there is no equivalent enumeration function for the item table,
so building `SpawnItem`'s `ItemRow` here would repeat the fabricated-handle situation that already
crashed the bridge once, on a function with six more parameters than the one that already crashed
(several of them object/component references). `writeSlot`'s own proven-live `DataTable`/`RowName`
writes (round 74) are only proven as PROPERTY assignments onto an existing slot struct, never as a
function argument, so they cannot ground `SpawnItem` either.

Instead `dropped.add` moves the actor the drop RPC itself already created, with
`K2_TeleportTo(Location, Rotation)` - a real `AActor` function, used verbatim by the reference
mod's own `BaseUtils.TeleportActorToActor`, and already proven live for `vehicles.set`/`spawn.set`
with exactly the plain `{X=,Y=,Z=}` table this reuses (no new struct shape, no new call). The new
dropped-item actor is told apart from every item already on the ground by snapshotting
`Abiotic_Item_Dropped_C` actors just before the drop RPC and diffing after it - a full-world scan
only paid when a position was actually requested. If that diff finds no new actor (the drop may
have merged into an existing ground stack) or more than one (another drop landed in the same
instant), the whole call fails with an honest reason instead of silently leaving the item wherever
it actually landed; on success the moved actor's position is read back with `K2_GetActorLocation`
and checked against the request before reporting success - the same "no confirmed effect = an
honest error" rule `resourcenodes.set`/`elevators.set` already established. No position given
behaves exactly as before this round. `LiveDroppedItemsChannel.AddAsync`/
`IWorldDroppedItemsSession.AddDroppedItemLiveAsync` gained optional `x`/`y`/`z` parameters (default
null, every existing call site unaffected); `WorldDroppedItemsTab` gained a "move it to my exact
position after dropping" checkbox next to SPAWN NEAR ME, shown only when a live position is known
(there is no world-map position picker in this tab, so reusing the player's own known position is
the one caller-chosen spot this can honestly offer without inventing a raw x/y/z form with no way
to judge a sensible value).

**B. Bench-upgrade gate (`canEditUpgrades`) re-examined, and the BASES tab's own explanation
fixed.** Evidence: coordinator's `classprobe.txt` (`Deployed_Bench`/`BenchUpgrade`/
`Deployed_CraftingBench` fragments). Two findings, neither changing the write path itself.
First, new grounding for the existing tag-write approach: `AddUpgrade`'s own disassembly
(`AbioticDeployed_CraftingBench_ParentBP_C`) ends its success path in a local
`CallFunc_AddTagToChangeableData_ReturnValue` call - the native function's own internal
implementation IS a tag write into `ChangeableData`, so `bench_tags.lua`'s direct write (round 79)
is not an outside approximation of `AddUpgrade`, it is the same operation reached without
marshaling the crash-prone row-handle struct across a function-call boundary. Second, the gate
itself (`benchSupportsUpgrades(obj) and benchTags.available(obj)` in `bases.lua`) was checked for
being wider than it needs to be and is not: it already requires exactly the two things a tag write
needs (replication support, and this specific instance's `UpgradeTagContainer` and `ChangeableData`
tag struct both being readable), not a loose class-level check, so it was left unchanged.
`supportsBenchUpgradeRemoval` reporting the identical value to `supportsBenchUpgrades` is
intentional (not a leftover): since round 79/80 removal already uses the exact same tag-write path
as install, removal genuinely needs nothing install does not already have.

What the C# side got wrong, now fixed: `WorldBasesTab` showed an unconditional "Bench upgrades:
offline editor only" line even in a live session where several benches usually could be edited -
stale since round 77 added live installs, actively misleading since round 79/80 grounded live
removal too - and had no "you are not the host" banner at all (every other live world tab has
one). Replaced with an accurate caveat (`WorldBases_LiveUpgradeCaveat`) and the missing
`LiveBases_NotHostWarning` banner. A bench that genuinely has upgrade slots but cannot be edited on
this connection right now (`canEditUpgrades` false) used to just have its whole upgrades section
vanish with no trace it was ever there; `IWorldBasesSession` gained `BenchHasUpgradeSlot` (the
class-level "has slots at all" question, kept apart from `BenchSupportsUpgrades`'s "can edit them
right now") so the tab can show `WorldBases_UpgradesUnavailableLive` instead of silence. The stale
`SetBenchUpgradeAsync`/`SetWorldFlag`-era XML doc comments claiming installs go through `AddUpgrade`
and removal has no game-side call were also fixed to describe the actual round-79/80 tag-write path
for both directions.

Harness: `live-agent/AbioticEditorLiveAgentLua/tests/cases/world_gaps.lua` extended with
`dropped.add` position cases (moved-and-verified, no-new-actor refusal, ambiguous-new-actor
refusal, no-position-unchanged). `python tools/run-lua-tests.py` passes for every case this round
touched (world_gaps, bases, dropped, item_tables); the small number of failures seen mid-session
belonged to other agents' concurrent in-flight areas (pets species change, buttons), not this
round's changes.

Risks / what's unverified: everything above is proven against the Lua stub harness only.
`K2_TeleportTo` on a fresh `Abiotic_Item_Dropped_C` actor (versus the vehicle/player actors it has
actually moved before) and the snapshot/diff actor-identification technique are genuinely unproven
against the running game; a `SpawnItem`-based rewrite remains a real option for a future round IF a
DataTableRowHandle enumeration function for the item table is ever found (removing the fabricated-
handle risk this round declined to take). Gap B changed no write path, only its own gate's
grounding-in-evidence and the app's explanation of it, so its live-verification risk is unchanged
from round 79/80.

## Round-110: live Buttons' "pressed once" made settable, live NPC spawners' cooldown made an exact editable value (2026-09-18)

Closed the two remaining live-editing parity gaps against buttons/NPC spawners, both grounded in
evidence already gathered by earlier rounds (`ebprobe/Button_Generic.json`, `classprobe.txt`
~line 40011, `gapprobe/pass3/Abiotic_Survival_GameMode.json`, `gapprobe/pass2/
Abiotic_NPCSpawn_ParentBP.json`), re-traced further this round rather than re-probed fresh.

**A. Buttons `pressedOnce` (was read-only, now settable, honestly).** Round-96 found
`Button_Generic_C:UpdateButtonSaveData(Force)` unconditionally sets
`ButtonSaveData.ButtonHasBeenPressedOnce_110_C4AE20D34162FCD3FA3323907300CB1F = true` on every
call and concluded there was no live write path at all. Re-examined: that forced-`true` behavior
belongs to `UpdateButtonSaveData` specifically, not to the underlying save data, so the fix is to
never call that wrapper for a `pressedOnce` write. `GameMode:UpdateActorToWorldSave(Self, false,
4)` - the actual "persist this now" call `UpdateButtonSaveData` already ends with - is a real,
independently callable function, confirmed from `Abiotic_Survival_GameMode_C`'s own
`ChildProperties`/`FunctionFlags` in `gapprobe/pass3/Abiotic_Survival_GameMode.json`:
`FUNC_Public | FUNC_BlueprintCallable | FUNC_BlueprintEvent`, signature `(Actor, RemoveFromSave:
bool, SaveType: E_SaveType byte)`. `buttons.lua` now writes the struct leaf directly (the same
struct-instance-by-hash-suffixed-name precedent `main.lua`'s `SKILL_XP_FIELD` already established
for reads, now used for a write too - no other write of this shape existed in the codebase before
this round, so it is new territory backed by the confirmed leaf name plus UE4SS's uniform
FProperty-indexed accessor behavior, not a guess) then calls `UpdateActorToWorldSave` itself,
deliberately bypassing `UpdateButtonSaveData` - reads the leaf back afterward and only reports
success once it matches. **Confirmed the caveat is real, not hypothetical**: checked every one of
`UpdateButtonSaveData`'s five call sites in the bytecode and all five fall inside
`ExecuteUbergraph_Button_Generic` (the shared interaction graph a player's press, or
`TriggerButtonWithoutUser()`, runs) - no other caller exists anywhere in the class. So a
`pressedOnce: false` write is real and applies immediately, but only lasts until this exact button
is next actually interacted with in-game, which re-runs `UpdateButtonSaveData` and forces it back
to `true`; `pressedOnce: true` has no such caveat. Exposed as a normal, honest, settable field
(never framed as fully persistent) rather than kept read-only. Files: `live-agent/
AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua` (header + `buttons.set`),
`live-agent/AbioticEditorLiveAgentLua/tests/harness.lua` (added `UpdateActorToWorldSave` to the
shared `H.gameMode` fixture, recording its arguments), `live-agent/AbioticEditorLiveAgentLua/tests/
cases/buttons.lua`, `Core/LiveEditing/World/LiveButtonsChannel.cs`, `Web.Shared/Models/
LiveButtonsFeatureSession.cs` (`pressedOnce` now `WorldMapField.Bool`, not `ReadOnly`), and this
doc's `buttons.set` section.

**B. NPC spawners `cooldownRemainingSeconds` (was read-only info, now an editable exact value).**
Round-102 confirmed `SetSpawnOnCooldown(TimeRemaining: double, InCurrentDay: int)` accepts
arbitrary values in principle but only ever called it with `(0.0, 0)` for `resetCooldown`. Traced
the function's full bytecode this round (`gapprobe/pass2/Abiotic_NPCSpawn_ParentBP.json`,
`SetSpawnOnCooldown`): `CooldownDay` is set to `InCurrentDay` unconditionally, then - only when
`InCurrentDay==0` and `AI Director`/`AI Director.DayNightManager` are both valid - overwritten with
today's real day from the `DayNightManager`; the function then calls
`AIDirectorSubsystem:SetCooldownForSpawner(spawner, TimeRemaining, CooldownDay,
spawner.OnlySpawnOnce)` passing `TimeRemaining` through with no clamping or zeroing anywhere in the
traced bytecode - confirming an arbitrary seconds value really does reach the subsystem unchanged.
`npcspawns.set` now accepts `cooldownRemainingSeconds: number` per row and calls
`spawner:SetSpawnOnCooldown(wanted, 0)` (day left at `0` so it resolves to "today", changing only
the seconds figure). A `resetCooldown` sent in the same row is applied second and wins, matching
`triggers.lua`'s own "the more complete reset action wins over an arbitrary value sent in the same
row" precedent. **`MinutesPassedCooldownStarted_` re-checked and still not exposed**: the day
argument is whole-day granularity only (fed straight from `DayNightManager.CurrentDay`, itself a
whole-day counter), with no minutes-within-the-day component to derive or set this leaf from - the
conclusion from round 102 stands, now doubly confirmed rather than just not found. Files:
`live-agent/AbioticEditorLiveAgentLua/Scripts/areas/npcspawns.lua` (header + `npcspawns.set`),
`live-agent/AbioticEditorLiveAgentLua/tests/cases/npcspawns.lua` (fixture's `SetSpawnOnCooldown`
mock now tracks the requested seconds value instead of only ever zeroing it),
`Core/LiveEditing/World/LiveNpcSpawnsChannel.cs` (new `SetCooldownRemainingAsync`,
`EditWire.CooldownRemainingSeconds`), `Web.Shared/Models/LiveNpcSpawnsFeatureSession.cs`
(`cooldownRemainingSeconds` now an editable `WorldMapField.Number` when the spawner is
controllable and a value is known, not always `ReadOnly`), and this doc's `npcspawns.set` section.
The `resetCooldown`/`forceSpawn` momentary actions are unchanged.

Neither offline feature's own doc comment (`Core/WorldSaves/Features/ButtonMapFeature.cs`,
`NpcSpawnMapFeature.cs`) made any claim about live editing, so neither needed correcting.

**Lua harness**: 1273 checks passed, 0 failed (`python tools/run-lua-tests.py`), up from 1270
before this round (two transient failures seen mid-run belonged to a concurrent pets-area change,
not this round's own areas, and were gone by the final run). C# not built or tested this round
(three other agents were editing concurrently; every identifier above was hand-checked against the
real files it references - `WorldMapField.Bool/Number/ReadOnly`, `WorldMapAccessor.TryParseDouble`,
`ILiveGameChannel.RequestAsync`, `IWorldFeaturesSession` - since a build could not safely be run
mid-edit). **Not yet exercised in the running game.**

## Round-109: live species change for tamed pets, for matched (Pest/Skink-family) rows only (2026-09-18)

Closed the live-pets species-change gap round 76/77/105 all re-examined and left refused: the
game's own `Abiotic_Survival_GameMode_C.SpawnPet(Class, SpawnTransform, Guid, Name, Owner,
DynamicProperties, Tamed)` needs an `FTransform` for `SpawnTransform`, and this project had no
working construction precedent for one - guessing an unverified struct shape for a native-bridged
call is exactly what caused the BASES tab's fatal, non-catchable crash in round 79, so every prior
round stopped there rather than guess. **Not yet exercised in the running game** - proven only
against the Lua stub harness.

**The re-examination, not a re-guess.** The round-79 crash came from a *hand-fabricated Lua table*
standing in for a struct nobody had ever seen a real instance of. That is a categorically different
risk from what round 76 already separately proved: an engine-*returned* struct (an `FVector`/
`FRotator` handed back by `K2_GetActorLocation`/`K2_GetActorRotation`) can be passed straight into
another native call's matching struct parameter, unchanged or with individual leaf fields
overwritten (`spawn.lua`'s `TeleportPlayer` path, `vehicles.lua`'s `K2_TeleportTo` path - both
confirmed live). `AActor` also exposes a standard, zero-argument, `BlueprintPure` function for the
*combined* transform - `K2_GetActorTransform` - in exactly the same category as those two calls,
just for `FTransform` instead of two flat structs. `pets.lua`'s new `trySpeciesChange` reads the
OLD pet actor's own current transform fresh via that call and passes it to `SpawnPet` completely
UNCHANGED - no field on it is ever read, guessed, or written - which is a strictly *smaller* risk
than the already-proven vector/rotator round trip (that one also mutates individual leaf fields
before passing it back). Every other `SpawnPet` argument is likewise read straight off the OLD
actor, never fabricated: `Guid`/`Tamed` are plain scalars already proven writable elsewhere in this
file; `Name` is the OLD pet's own `PetName` `FText` userdata, passed through unchanged (never
re-encoded via a Lua string); `Owner` is its own `FollowingOwner` object reference (confirmed real
and `pcall`-readable on this exact family by `companions.lua`, round 78/79); `DynamicProperties` is
its own live array, passed by reference unchanged - which is *why* XP/mutation progress survive a
species change even though nothing here touches them directly. Health/limb state is **not** part of
`SpawnPet`'s signature, so a species change does not carry the pet's current health over - the new
actor spawns with its class's normal health.

**Evidence, from a fresh pak dump of `Abiotic_Survival_GameMode_C`** (full bytecode, not just the
signature round 105 already had): `SpawnPet`'s `FunctionFlags` is `FUNC_Public | FUNC_HasOutParms |
FUNC_HasDefaults | FUNC_BlueprintCallable | FUNC_BlueprintEvent` - the exact same combination as
every other function this project already calls live (`TeleportPlayer`, `TrySpawnNPCNew`,
`SetSpawnOnCooldown`, ...), not some special/unreachable kind of function. `SpawnTransform`'s own
struct type is confirmed `Class'Transform'` from `/Script/CoreUObject` with `ElementSize: 96` bytes
(matching the engine's real double-precision `FTransform`), not a guess.

**Safety ordering, never destroy-then-verify.** `trySpeciesChange` calls `SpawnPet` first; the
returned actor must be valid **and** report back the *same* `Guid` that was passed in before the
OLD actor is destroyed. Any failure at any step (an unresolved/not-yet-loaded target class, an
unreadable transform, the call itself erroring, an invalid or mismatched-identity result) leaves
the OLD pet completely untouched and comes back as a non-fatal warning (the same round-78 "apply
every field independently, never abort the whole call" shape `pets.set` already uses for XP), never
a thrown error and never a destroy without a confirmed replacement. The one acknowledged rough edge:
a spawn that "worked" but reports the wrong identity is the one case that can leave a stray,
unmatched extra actor behind in the world even though the edit itself is reported as failed - a
later `pets.list` sweep will list it via the round-105 generic tamed-marker path, never silently
lost track of.

**Species change is refused for unmatched (Peccary/Lamogi-shaped) pets**, with a named warning:
there is no Guid to hand the game to preserve identity with, and nothing to verify a "same pet"
result against.

**THE ONE ASSUMPTION STILL NEEDING REAL-GAME CONFIRMATION**, more loudly flagged here than almost
anything else in this project: a wrong-shaped argument to a native `UFunction` call is the one
class of failure `pcall` cannot be trusted to catch - that is exactly what made round 79's BASES
crash non-catchable rather than an ordinary Lua error. Every reasoning step above argues why THIS
call should not hit that failure mode (nothing in it is a fabricated struct table, unlike the BASES
case), but `K2_GetActorTransform` and `SpawnPet` have never actually run against the real game -
this round is proven only against the Lua stub harness (`python tools/run-lua-tests.py`, new
success/no-op/unknown-species/spawn-invalid/mismatched-identity/unmatched-refusal cases in
`tests/cases/pets.lua`, a new `GameMode.SpawnPet` fake in `tests/harness.lua`). Treat the first
real-game use of `pets.set{npcClass=...}` as a genuine test, ideally on a low-stakes/replaceable
pet in a singleplayer or otherwise easily-restartable session, before trusting it broadly.

**Capability negotiation.** `pets.list` now reports `supportsSpeciesChange: true` (reusing the
existing wire field, not a redundant new one - it already meant exactly this); an older agent build
that omits the field is read as `false` by `LivePetsSession` (which previously hardcoded `false`
regardless of what the agent said - now it actually relays `LivePetDirectory.SupportsSpeciesChange`),
so the shared `WorldPetsTab`'s creature-type control stays hidden against an older agent
automatically, and `LivePetsChannel.SetAsync`/the `pets.set` wire's `SetWire` record gained a
trailing `npcClass` so a species-change request can actually reach the live agent (previously
accepted by the shared `IWorldPetsSession` interface but silently dropped by the live session).

**Files changed:** `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/pets.lua` (header comment
rewritten with the round-109 findings; new `gameMode`/`resolveClass`/`shortClassTag`/
`trySpeciesChange` helpers; `pets.list`/`pets.set` wired up), `live-agent/AbioticEditorLiveAgentLua/
tests/harness.lua` (new `GameMode.SpawnPet` fake), `live-agent/AbioticEditorLiveAgentLua/tests/
cases/pets.lua` (new species-change section) and `tests/cases/world_gaps.lua` (updated capability
assertion), `src/AbioticEditor.Core/LiveEditing/World/LivePetsChannel.cs` (doc comments, `SetAsync`/
`SetWire` gained `npcClass`), `src/AbioticEditor.Web.Shared/Models/LivePetsSession.cs`
(`SupportsSpeciesChange` now relays the agent's own report; `SetPetAsync` forwards `npcClass`),
`src/AbioticEditor.Web.Shared/Models/WorldPetsSession.cs` (interface doc comment),
`docs/reference/live-editing-protocol.md`'s `pets.list`/`pets.set` section, and a new
`tests/AbioticEditor.Tests/WorldLivePetsSpeciesChangeContractTests.cs` (source-text contract tests,
mirroring `WorldLivePetsGapContractTests`'s own style - deliberately a new file; that existing
file's own stale `supportsSpeciesChange = false` assertion was also updated so it does not now
fail against this round's change). `WorldPetsTab.razor` was deliberately **not** changed - its
existing `Session.SupportsSpeciesChange`/`VariantOptions()` gating already does the right thing
generically; no leaf class list was added anywhere in this round (the target species class is
resolved from whatever path the C#/UI side already sends, via `StaticFindObject`/`LoadAsset`, the
same technique `main.lua`'s own data-table lookup already uses for a not-yet-loaded asset).

**Lua harness**: `python tools/run-lua-tests.py` - 1304 checks passed, 0 failed. C# not built or
tested this round (coordinator builds centrally, per instruction) - every C# assertion above was
verified by direct text search against the edited files instead, since a build was out of scope
for this round.

## Round-107: three partial live-editing gaps closed as far as the game allows - recipe relock re-grounded, kill-tracked compendium sections, world-wide item/codex lists (2026-09-18)

Closed the three partial gaps the coordinator's fresh pak dump (`pass2\Abiotic_CharacterProgressionComponent.json`/`layouts.txt`,
`Abiotic_Survival_GameState.json`) was scoped for. **Not yet exercised in the running game** - proven
against the Lua stub harness only.

**A. Recipe relock (`recipes.get`/`recipes.set`).** Re-checked, not changed: `canLock` was already
`ctx.isHost() and replication.available()`, and the fresh dump confirms `RecipesUnlockedArray` is a
plain `FArrayProperty`, `OnRep_RecipesUnlockedArray` is real, and no dedicated relock/forget RPC
exists anywhere on `Abiotic_CharacterProgressionComponent_C` - array-replace-then-RepNotify is the
only path the game exposes, and a non-host client's own write to a replicated property never
persists, so `canLock` is already as wide as honestly possible. The real gap was test coverage: no
`tests/cases/recipes.lua` existed, and the harness's progression fixture had no
`OnRep_RecipesUnlockedArray` method for the host-relock branch to call - a real regression there
would not have failed any test. Added both (`live-agent/AbioticEditorLiveAgentLua/tests/cases/recipes.lua`,
new; `tests/harness.lua`, one added fixture method) and registered it in `tests/cases/manifest.lua`.

**B. Compendium kill-tracked entries (`codex.get`/`codex.set`).** Real gap, now closed. Round 77
assumed `ECompendiumUnlockType::KilLRequirement` (enum value 3) was never reachable through
`Request_UnlockCompendiumSection` because no installed mod calls it that way - but the RPC's own
private target function, `Server Try Unlock Compendium Section`, disassembles (full bytecode in the
coordinator's dump) to a 4-case switch on the unlock type, and case 3 adds the row to
`Compendium_KillSections` (an `FArrayProperty` with the same `Net | RepNotify` shape as the other
three section arrays) unconditionally - gated only by the same already-unlocked check the other
three share, with no read of `Compendium_KillCount`/`AllowedCompendiumKills` anywhere in that path.
`codex.lua` now maps `sectionType: "KillRequirement"` to value 3, reads/clears
`Compendium_KillSections` alongside the other three section arrays, and reports a new
`canUnlockKillSections` capability (older agents omit it, kept read-only). On the app side,
`LivePlayerCodexChannel`/`LiveCodexDirectory` carry the new capability, and
`LivePlayerCodexSession.BuildCompendiumRows` adds a live-only `"KillRequirement"` section type for
a row with `CompendiumEntry.KillRequired` set - the shared `CodexCatalog` model is untouched, so
offline editing (which unlocks a kill-only row through its own kill-count field) is unaffected.

**C. World-wide item/codex lists (`worldunlocks.get`/`worldunlocks.set`).** Real gap, now closed at
the backend/session layer (no UI yet - see below). `GlobalItemsPickedUp`/`GlobalEmailsRead`/
`GlobalJournalEntries`/`GlobalCompendiumEmail`/`GlobalCompendiumNarrative`/`GlobalCompendiumExploration`
on `Abiotic_Survival_GameState_C` are confirmed plain `FArrayProperty` (matching what was already
read), and - like the two recipe `TSet`s beside them - carry no `Net`/`RepNotify` flag in the pak
dump and no `OnRep_Global*` function exists anywhere on the class. `worldunlocks.set` now
add/removes rows in any of the six lists via the same array-replace technique `codex.lua`'s
per-player `clear` already uses, gated by a new `canEditGlobalLists` capability (host authority +
replication support only - no `TSet` capability check needed, so these six lists are editable even
on a runtime too old for `canEditRecipes`). The handler was restructured so a recipe-only edit still
needs `canEditRecipes` but a list-only edit does not, and an empty `worldunlocks.set{}` is now a
no-op success instead of an unconditional error. `LiveWorldUnlocksChannel` gained
`SetGlobalListAsync`/`LiveWorldListEdit` and `LiveWorldUnlocks` gained `CanEditGlobalLists`/
`GlobalListEditsUnavailableReason`; `LiveStorySession` gained matching read/write members
(`GlobalItemsPickedUpIds`, `SetGlobalListAsync`, etc.) **outside** `IWorldStorySession` on purpose -
the offline file session has no UI for these six lists either (only world recipes get an offline
browser), so widening the shared interface would force an offline implementation with nothing to
mirror. No `WorldStoryTab` UI wiring yet; the channel/session plumbing is ready for it.

Harness coverage: `tests/cases/recipes.lua` (new, ~20 checks), `tests/cases/codex.lua` (extended:
KillRequirement accepted/still-ignored-when-unmapped, `canUnlockKillSections` reported),
`tests/cases/worldunlocks.lua` (extended: list edits on a TSet-unsupported runtime, combined
recipe+list request, validation errors, non-host denial for lists too). `python
tools/run-lua-tests.py` passes in full (1150+ checks; the small number of unrelated failures seen
mid-session belonged to other agents' concurrent in-flight areas, not these three).

Risks / what's unverified: everything above is proven against the Lua stub harness only, not the
real game - `Compendium_KillSections`/`OnRep_Compendium_KillSections` and the six
`Global*` array writes have never run against a live server. The bytecode-switch evidence for gap B
is strong (a direct disassembly of the exact function the existing, working three-type unlock
already calls) but still unconfirmed in-game. Gap C ships with no UI, so it is exercised only by
the harness and by any future direct API caller.

## Round-106: live RESOURCE NODES - harvest/respawn via the game's own functions (2026-09-18)

Resource nodes (`Core/WorldSaves/Features/ResourceNodeMapFeature.cs`, the save's
`ResourceNodeMap`) had no live path at all before this round, and are the largest world-map feature
in the game: 1413 entries in the Facility region fixture alone, across dozens of concrete classes
(`ResourceNode_WoodCrate_Manufacturing`, `ResourceNode_GlassPane`, `Resource_MicroNode_DuctTape`,
`ResourceNode_MetalVent`, `Resource_Micronode_LeyakEssence_TWO`, `ResourceNode_Hydropanel`,
`ResourceNode_Turbine`, `ResourceNode_AnalysisMachine`, and more). Owner rules for this round: never
hardcode a leaf blueprint class name (discover through the parent class, `FindAllOf` is
hierarchy-inclusive), feature-detect every property per instance with `pcall`, and never guess a
live property name from the save's own leaf name - read it from the coordinator's real CUE4Parse
class+bytecode probe (`ResourceNode_ParentBP.json`, `Resource_MicroNode_ParentBP.json`,
`ResourceNode_WoodCrate_Office.json`).

**Discovery**: a single `FindAllOf("ResourceNode_ParentBP_C")` sweep (super `AbioticActor_C`)
covers every concrete node class AND the `Resource_MicroNode_ParentBP_C` family, since that class
itself declares `super=ResourceNode_ParentBP_C` in the dump - confirmed, not assumed, so nothing in
`resourcenodes.lua` names a single concrete class.

**Field mapping, read from `ResourceNode_ParentBP_C`'s own `ChildProperties` and
`SaveNodeToWorldSave`'s own bytecode** (not the save leaf names):
- offline `harvested` (`HasBeenPickedUp_`) = live `IsDepleted` (bool, replicated,
  `OnRep_IsDepleted`) - `SaveNodeToWorldSave`'s bytecode reads exactly this property before
  persisting a node, grounding the mapping directly.
- offline `dayPickedUp` (`DayPickedUp_`) = live `DayWasDepleted` (int, **not replicated** - no
  `Net` flag, no `OnRep_`) - `SaveNodeToWorldSave`'s bytecode sets it to
  `DayNightManager.CurrentDay` at the moment it persists a depleted node.
- offline `position` (`CurrentPosition_`) = the live actor transform (no separate position
  property on the class), the same `K2_GetActorLocation` read every other fixed-actor feature uses.

**`harvested` is never a bare property write** - `ResourceNode_ParentBP_C` exposes two real,
zero-parameter `FUNC_BlueprintCallable | FUNC_BlueprintEvent` functions. Traced through the shared
ubergraph both jump into (not guessed): `RespawnResourceNode()` calls `FlushNetDormancy()`, sets
`IsDepleted=false`, re-places the node on the ground (`PlaceOnGround`, gated on the streaming
location being loaded), then calls `OnRep_IsDepleted()` and
`NetPushModelHelpers.MarkPropertyDirtyFromRepIndex(self, IsDepleted)` - the game's own "make this
node visibly reappear" path, preferred over a direct write per the owner's own instruction.
`Force_DepleteNode()` is the exact mirror (confirmed via its own unconditional jump straight into
that same tail). A documented, honestly-flagged quirk: a node carrying a valid, currently-set
`ContinualRespawnFlag` world flag takes a different branch inside `RespawnResourceNode` (plays a
portal-vanish effect and falls into the depleting tail instead of respawning) - there is no exposed
way to detect this ahead of time from Lua, so `resourcenodes.set` always re-reads `IsDepleted`
after calling either function and only reports success once it actually matches what was asked,
the same "no confirmed effect = an honest error, not a false success" rule `elevators.set`
established. `dayPickedUp` is a direct write (not replicated, no setter function needed), the same
`NoVignetteReset` precedent `buttons.lua` already documents.

**Performance**: this is the largest sweep any live area performs, so it is deliberately excluded
from `LiveConnect.razor`'s periodic `ActiveLiveSessions` refresh loop (same reasoning as
containers/npcs/bases) - only an explicit tab visit or a region change re-fetches it.
`resourcenodes.list` also accepts an optional `classFilter` (case-insensitive substring against the
class name) for a caller that wants to narrow the sweep to one harvestable type; the desktop app's
own tab does not use it today (it lists everything, like the file editor, and relies on
`WorldFeaturesTab`'s existing virtualized/filterable row list - already built for exactly this
scale, confirmed by reading that component, so no UI change was needed there).

**Files added**: `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/resourcenodes.lua` (registered
in `areas/manifest.lua`), `live-agent/AbioticEditorLiveAgentLua/tests/cases/resourcenodes.lua`
(registered in `tests/cases/manifest.lua`), `Core/LiveEditing/World/LiveResourceNodesChannel.cs`,
`Web.Shared/Models/LiveResourceNodesFeatureSession.cs` (implements `IWorldFeaturesSession` scoped
to the `resource-nodes` feature id only, so the existing offline `WorldFeaturesTab`/
`ResourceNodeNaming.FriendlyType` render it live with no new UI), wiring in `LiveConnect.razor`
(nav tab, field, `EnsureAreaConnectedAsync` case, render branch, region-scoped reset/disconnect
cleanup - deliberately no `ActiveLiveSessions` case), `Live_TabResourceNodes` resx key (English
only, matching this repo's convention for brand-new keys), the `resourcenodes.list`/
`resourcenodes.set` section in `docs/reference/live-editing-protocol.md`, and
`tests/AbioticEditor.Tests/WorldLiveResourceNodesAreaTests.cs` (standalone, mirrors
`WorldLiveButtonsAreaTests.cs`; also asserts there is no `case "resourcenodes":` inside
`ActiveLiveSessions` specifically, not just that the string is absent from the whole file, since
`EnsureAreaConnectedAsync` legitimately has its own case with that same name). Removal is refused
live (offline "remove" drops the whole map entry and lets the game recreate the actor at its
blueprint default; `RespawnResourceNode` does not reset position or anything beyond the harvested
flag, so mapping "remove" onto it would overstate what actually happens).

**Lua harness**: 1036 checks passed, 0 failed via `python tools/run-lua-tests.py` (a couple of
transient, unrelated failures in `codex`/`parity` were observed in an intermediate run - sibling
areas under concurrent, unrelated edits at the same time - and gone by the final clean run this
entry reports). C# not built or tested this round (coordinator builds centrally, per instruction).
**Not yet exercised in the running game** - everything above is grounded in the real class dump and
bytecode, but nobody has confirmed a `resourcenodes.set` call visibly un-depletes/depletes a node
in the actual game, or that the `ContinualRespawnFlag` quirk behaves exactly as traced.

## Round-105: Peccary/Lamogi pets are listed live now, not silently omitted (2026-09-18)

Closed the last real live-pets gap round 77/79 had re-confirmed but not closed: Peccary and
Lamogi family pets carry none of `Guid`/`PetName`/`DynamicProperties`/`FollowingOwner`, so
`pets.list` never listed them at all - "renamed, healed and levelled up in the save file" was the
best it offered. Owner rules for this round: never hardcode a leaf blueprint class name (discover
through the parent class, `FindAllOf` is hierarchy-inclusive), feature-detect per instance with
`pcall`, and never guess a live property name - read it from the game's own class dump
(`tests/AbioticEditor.Probes/LiveGapProbe.cs`, `NPC_Monster_Peccary`/`NPC_Monster_WinterSprite`
fragments with function bytecode).

**A real, generic tamed marker exists.** `NPC_Monster_WinterSprite_C`'s own compiled graph calls a
static library function, `AbioticFunctionLibrary::IsTamedPet(Actor)` (bool, one parameter), from
three of its own overridden functions (`IsInvincible`, `TargetBlockedAttack`,
`UpdateHealthTextureIndex`). It is a general-purpose actor query, not Pest/Skink-specific, so
`pets.list` now also sweeps `NPC_Base_ParentBP_C` - the same hierarchy-inclusive parent class
`npcs.list` already sweeps for the whole CREATURES tab, so no Peccary/Lamogi/future-family class
list is needed at all - and feature-detects per instance: skip anything with its own `Guid`
(already covered by the existing Pest/Skink path), keep anything `IsTamedPet` reports true for.
Those rows come back `matched:false` with `id` set to the live actor's own full path (the same id
scheme `npcs.list` already uses), since there is still no stable id they could share with a save's
`PetNPC` record - never silently reused as if it were one. `isDead`/per-limb health are real,
universal `AbioticCharacter` fields on these actors too (the same fields already proven for the
player and for Pest/Skink pets), so those stay editable; `customName`/`xp` are refused with a
non-fatal warning (the round-78 pattern) since the class genuinely has neither field, not because
of a policy choice.

**Species change re-examined with sharper evidence, still refused.** The game's own
`Abiotic_Survival_GameMode_C.SpawnPet(Class, SpawnTransform, Guid, Name, Owner, DynamicProperties,
Tamed)` is a real function shaped exactly like a "respawn as a different class" edit would need -
but `SpawnTransform` is an `FTransform`, a nested struct this project has no working construction
precedent for anywhere over UE4SS Lua reflection (unlike the flat `FVector`/`FRotator` tables round
76 proved out for `spawn.set`). Guessing an unverified struct shape for a native-bridged call is
exactly what caused the BASES tab's fatal, non-catchable crash in round 79, so `SupportsSpeciesChange`
stays `false` - now backed by a concrete blocker instead of the older, vaguer "no confirmed
despawn/respawn round trip" reasoning.

**Threaded a new `Matched` flag through the whole live-pets stack** rather than inventing a
parallel "unmatched pet" type: `WorldPet` (`Core/Domain/World/WorldPet.cs`) gained a trailing
`bool Matched = true` parameter (every existing constructor call - the save reader, `PetTransfer`,
`WorldSaveSession`'s pending-pet staging - keeps compiling unchanged and stays `true`);
`LivePetsChannel`'s wire records and `LivePet` carry it through from `pets.lua`; `LivePetsSession`
passes it into `WorldPet`. The shared `WorldPetsTab.razor` (used by both the file and live
sessions) now computes `identityEditable = editable && pet.Matched` and gates the Name and
Level/XP controls on it, showing a new `WorldPets_UnmatchedLiveNotice` notice instead when a row
is live-editable but unmatched; the Health section stays gated by `editable` alone, unchanged,
since it is real for every tamed pet regardless of match state.

**Tests**: new `tests/AbioticEditor.Tests/WorldLivePetsGapContractTests.cs` (source-text contract
tests, the same style `WorldLiveAreaParityContractTests` already uses - deliberately a new file,
not an addition to that one) pinning the `Matched` flag's plumbing end to end, the generic
(not-hardcoded) tamed sweep in `pets.lua`, the still-refused species change with its new evidence,
the new resource key across all five shipped locales (en/de/es/fr/ru), and the protocol doc's own
description of this round. `docs/reference/live-editing-protocol.md`'s `pets.list`/`pets.set`/
`pets.remove` section rewritten with the round-105 findings. Lua harness cases added to
`tests/cases/pets.lua` (see that file's own notes) and `python tools/run-lua-tests.py` run clean.
**Not yet exercised in the running game** - calling `IsTamedPet` on a Peccary/Lamogi actor is new
and genuinely unverified until tested live, same as every other first-use call in this project;
the C# side only got `dotnet build`/`dotnet test` run by the coordinator, not by this round itself.

## Round-104: a vehicle's on-board storage is now editable live, no new UI needed (2026-09-18)

Closed a gap the round-76/77/79 write-ups all flagged: offline, the VEHICLES tab exposes a
vehicle's on-board storage fully (a link into the CONTAINERS tab's own slot editor), but live
`LiveVehiclesSession` hardcoded `HasInventory: false`/`InventoryItemCount: 0` for every vehicle, so
the button never appeared. Owner rules for this round: never hardcode a leaf blueprint class name
(discover through the parent class, `FindAllOf` is hierarchy-inclusive), and never guess a live
property/component name from a save leaf name - read it from the game's own class dump.

**The evidence, not a guess.** Re-probed the installed game's own class layout (fragment
`ABF_Vehicle_ParentBP`, full bytecode JSON). `ABF_Vehicle_ParentBP_C` carries a plain, unsuffixed
`StorageContainer` property - a `ChildActorComponent` - and a BlueprintPure `GetVehicleContainers()`
function whose own bytecode dynamic-casts `StorageContainer.ChildActor` to
`Deployed_Container_ParentBP_C` and reads its `ContainerInventory`. The forklift's own placed
child-actor default (`ChildActorTemplate`) is `Deployed_Container_ForkliftCargo_C`, which chains
`Deployed_Container_ForkliftCargo_C` -> `Deployed_Container_Cargo_C` -> `Deployed_Container_ParentBP_C`
(the security cart's `Deployed_Container_SecurityCartCargo_C` chains the same way) - the exact
class `containers.lua`'s own `CONTAINER_CLASSES` sweep already scans for. **A vehicle's on-board
cargo is therefore a genuine, independently-loaded `Deployed_Container_ParentBP_C` actor, not a
bespoke vehicle-only structure.**

**Approach chosen, and why.** That finding collapses the two options the task offered into one:
since the cargo actor is already discoverable and editable by the existing
`containers.list`/`containers.get`/`containers.set` handlers with zero changes there, the fix is
to expose it through `vehicles.list` with a `containerId` those handlers already accept, rather
than teaching `containers.list` a vehicle-specific "kind" field. This keeps exactly one slot-edit
code path in the whole stack - the same one every placed crate, locker and bench already uses -
instead of adding a second one for vehicles alone.

**Changes.** `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/vehicles.lua`: resolves
`StorageContainer.ChildActor` per vehicle (pcall-guarded throughout - a vehicle type with no cargo
child actor resolved reports no storage instead of erroring the whole list), reports its own
`fullName()` as `containerId`, and counts non-empty slots the same "" / `Empty` / `None` sentinel
way `containers.lua`'s own `slotRow` does (via the shared `ctx.containerInventory`/
`ctx.slotRowName` helpers - no new container-reading logic). `Core/LiveEditing/World/
LiveVehiclesChannel.cs`: `LiveVehicle`/`VehicleWire` gained `ContainerId`/`HasInventory`/
`InventoryItemCount` (trailing, defaulted, so an older live-agent build that never sends them still
deserializes to "no storage" instead of throwing). `Core/Domain/World/WorldVehicle.cs`: gained a
trailing `ContainerId` (default null - every existing positional constructor call still compiles,
the established pattern this repo uses for this exact situation, see round 79's `VariantRowName`).
Offline, `ContainerId` stays null and `WorldVehiclesTab`'s "open storage" button falls back to the
vehicle's own `Id` (unchanged behavior - the file format embeds the container in the same
`VehicleMap` entry, so the vehicle's own id already is the container's id there).
`Web.Shared/Models/LiveVehiclesSession.cs`: maps the wire's real `HasInventory`/
`InventoryItemCount`/`ContainerId` instead of the old hardcoded values.
`Web.Shared/Components/World/WorldVehiclesTab.razor`: `OpenContainer` now opens
`vehicle.ContainerId ?? vehicle.Id`. `Web.Shared/Components/Pages/LiveConnect.razor`: the live
VEHICLES tab render gained `OnOpenContainer="OpenCareContainerAsync"` - reusing the exact same
"open this container id in the CONTAINERS tab, connecting that area first if needed" method the
garden-plot/power-chair/chemistry-bench live features already call, rather than adding a second,
near-identical method.

**Tests.** Lua harness: extended `tests/cases/vehicles.lua` (already registered in
`tests/cases/manifest.lua`, no manifest edit needed) with a no-storage vehicle (the existing
forklift fake, which never set `StorageContainer`, degrading cleanly), a real cargo child actor
with one non-empty and one empty slot, and proof that `containers.list`/`containers.set` already
see and can write that exact same container id with no vehicle-specific handling at all - **999
checks passed, 0 failed** (`python tools/run-lua-tests.py`, up from 996 before this round's
addition). C# (not run this round; coordinator builds centrally): new
`tests/AbioticEditor.Tests/WorldLiveVehicleStorageTests.cs` (kept separate from
`WorldLiveAreaParityContractTests.cs` on purpose, matching round 94's `WorldLiveButtonsAreaTests`
precedent) - a fake-channel test proving `LiveVehiclesChannel` parses the new fields and degrades
to "no storage" when an older build omits them entirely, a `LiveVehiclesSession` test proving the
container id reaches `WorldVehicle` and is never confused with the vehicle's own id, a
`WorldVehicle` construction test proving the new field defaults to null, and source-text checks
that `WorldVehiclesTab.razor` and `LiveConnect.razor` actually wire the fallback/callback described
above. Updated `docs/reference/live-editing-protocol.md`'s vehicles section with the same evidence.

**Not yet exercised in the running game.** Everything above is grounded in the game's own class
dump and confirmed bytecode (the same standard of evidence `elevators`/`buttons` shipped under),
and reuses `containers.set`'s already-live-proven write path rather than any new native call - but
nobody has yet opened a real vehicle's cargo hold through this exact `containerId` hookup against
the actual running game to confirm the button appears and the slot editor round-trips a real item.

## Round-103: live POWER SOCKETS and TRAMS - both fully read-only live, with hard evidence for why (2026-09-18)

Added the live twins of two offline world-map features that had none: `power-sockets`
(`Core/WorldSaves/Services/WorldMapFeatures/PowerSocketMapFeature.cs`, the save's
`PowerSocketMap`) and `trams` (`TramMapFeature.cs`, the save's `TramMap`, Facility only).
Followed the buttons/elevators template end to end: `powersockets.lua`/`trams.lua` (new area
modules under `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/`, registered in
`areas/manifest.lua`), `LivePowerSocketsChannel`/`LiveTramsChannel`
(`Core/LiveEditing/World/`), `LivePowerSocketsFeatureSession`/`LiveTramsFeatureSession`
(`Web.Shared/Models/`, each scoped to its one feature id so the existing offline
`WorldFeaturesTab` renders it live with zero new UI), and wiring in `LiveConnect.razor` at
every touch point the portals/buttons/elevators sessions have (nav tab, render branch, field
declarations, `EnsureAreaConnectedAsync`, the periodic-refresh switch, `AllKnownLiveSessions()`,
`ResetRegionScopedWorldSessions()`, and the full disconnect reset). New `Live_TabPowerSockets`/
`Live_TabTrams` resx keys (English only, matching this repo's existing convention for brand-new
keys). Owner rule followed throughout: discovery is a hierarchy sweep on the parent class alone
(`PowerSocket_ParentBP_C`, `Tram_ParentBP_C` - both `FindAllOf` calls are hierarchy-inclusive, no
leaf class list), every property/function read is per-instance `pcall`-feature-detected so an
unfamiliar subclass still lists with its real class name, and no property/function name below was
guessed - every one is copied verbatim from the coordinator's CUE4Parse class dump plus the full
blueprint bytecode (`ScriptBytecode` JSON) of the relevant functions.

**A. Power sockets - both offline fields' live equivalents are read-only, and now have hard proof
why.** `socketId` reads the actor's own `GetPowerSocketID()`, whose bytecode is exactly
`BreakSoftObjectPath(MakeSavedObjectPath(Self)).PathString` - the same id space the save's
`PowerSocket_<hash>` leaf stores (`PowerSocketMapFeature`'s own doc comment already said this
"matches the map entry key"). `pluggedInDevice` reads the live `PluggedInDevice` object reference
directly and reports the plugged actor's own real class name - needs no game-data catalog, unlike
resolving the save's asset-id string. `hasTimer`/`timerMode` are read-only: an exhaustive grep of
every hash-suffixed struct-member write in `PowerSocket_ParentBP_C`'s own bytecode finds exactly
one write site for each, `Update_SaveData` (called from `SavePowerSocketToWorldSave`, the actor's
own "persist me now" entry point) - and in BOTH branches of that function's own attach/detach
if-else, traced statement by statement, `LatestSaveData.HasTimer_` and `LatestSaveData.TimerMode_`
are set unconditionally to `false`/`0`. There is no other write site, no gate, and no read of
either leaf anywhere in the class. This means the very next time anything triggers a save on a
socket (plugging/unplugging a device, or any other save-triggering event), both fields are forced
back to false/0 regardless of what a player or this module wrote - so nothing live could ever make
`hasTimer` stick. The offline `PowerSocketMapFeature.cs` doc comment was updated (comment only, no
behavior change) to record this: the coordinator's own request to consider exposing `timerMode` as
a real offline choice, now that the full `E_PowerTimerModes` enum dump is available, was
deliberately **not** taken - the dump shows 9 real enumerator values (indices 0-8) plus `E_MAX`,
but every one is still an auto-generated `NewEnumeratorN` name with no meaningful English label
anywhere in the asset, so a fuller list would only replace "cannot be determined" with "determined,
and meaningless" - not a safe improvement. `powered` is a bonus read-only field off the confirmed
`IsPowered()` function.

**B. Trams - `previousStation` is confirmed by tracing the actual arrival bytecode, not inferred by
symmetry, but no live write path exists.** `Tram_ParentBP_C`'s own ubergraph, on reaching a stop,
sets `PreviousStation = TargetStation` (a plain instance-to-instance property copy), marks it
dirty, calls `OnRep_PreviousStation()`, then immediately calls
`GameMode:UpdateActorToWorldSave(Self, false, 14)` - the real "persist this tram now" call, run
right after `PreviousStation` updates. That is exactly the save-time mapping `TramMapFeature.cs`'s
own doc comment describes for `LastStation_`, traced end to end. `targetStation`/`moving`/
`positiveDirection`/`isAtStation`/`hasPassengers` are bonus read-only fields off confirmed
properties; `inventories` (on-board container count) reads the confirmed `GetTramContainers()`.
**No live write path for `lastStation` exists or was attempted.** `Tram_ParentBP_C`'s own functions
(`PositiveButtonPressed`/`NegativeButtonPressed`, `SetNextStopPoint`/`FindNextStation`) only toggle
or continue travel along the current rail - none takes an arbitrary destination station. The game
does ship a `Button_TramRecall` actor (the owner's own tip) that a player uses to summon a tram to
a specific platform, but its class was **not** part of this round's probe dump (no
`Button_TramRecall.uasset`/`.json` was produced by the coordinator's sweep), so its actual
mechanism is completely unconfirmed - per the owner's own rule against guessing a live function
from a hunch, `trams.set` refuses every request with a named, evidenced reason instead of
attempting a call nobody has verified. A future round with a real `Button_TramRecall` dump can
revisit this.

**Lua harness**: **1156 checks passed, 0 failed** via `python tools/run-lua-tests.py` (both new
area's fakes cover subclass-only discovery via `__bases`, an "unfamiliar" instance missing every
expected property/function still listing with fields absent rather than erroring or being dropped,
and the refusal path for both `.set` handlers). C# not built or tested this round (out of scope;
the coordinator builds centrally) - reviewed every new type/using by hand against the exact
existing buttons/elevators/resourcenodes files instead. New contract tests
`tests/AbioticEditor.Tests/WorldLivePowerSocketsAreaTests.cs` /
`WorldLiveTramsAreaTests.cs` (mirroring `WorldLiveButtonsAreaTests.cs`, not touching the shared
`WorldLiveAreaParityContractTests.cs` per the coordinator's own instruction that other agents own
that file this round).

**Not yet exercised in the running game.** Both areas are grounded in the class dump and bytecode
(the strongest evidence tier this project has for an unverified area), but nobody has yet run
`powersockets.list`/`trams.list` against the actual live game to confirm the read side reports
real values, or confirmed in-game that no plugin/mod ever manages to arm a socket's timer or send a
tram somewhere through a path this round could not see.

### Round-103 follow-up: trams gained a real recall write path (2026-09-18)

Same day. The coordinator dumped the tram recall chain (`gapprobe/tram/`: `Button_TramRecall.json`,
`Button_Tram.json`, `TramSystem_Station.json`, `Tram_ParentBP.json`, plus `layouts.txt` property/
function signatures - not bytecode - for `TramSystem_RecallStation_C` and `TramSystem_Rail_C`), so
the "no evidence at all" verdict above is superseded for trams (power sockets are unaffected and
stay exactly as described above).

**Traced**: `Button_TramRecall_C` (super `Button_Tram_C`, itself super `Button_Generic_C` with no
properties/functions of its own) overrides only `GetInteractText` - confirmed from its own bytecode,
which calls `TramReference:FindNextStation(Positive)` purely to build display text and touches
nothing else; `Tram_ParentBP_C`'s own bytecode never references the recall system at all, ruling out
both classes as the actual trigger. That trigger is `TramSystem_RecallStation_C` - a leaf class (no
parent of its own beyond `Actor`, so discovery is a plain `FindAllOf`, no hierarchy sweep) with
`LinkedTram`/`LinkedStation` object properties and a `TramRecallPressed(Activated: bool)` function.
Its own `ExecuteUbergraph` (properties-only view) casts something to `Button_Tram_Recall` and
creates delegates right next to its own `RecallButton` property - the same "cast the button, bind
its Activated delegate" shape `Tram_ParentBP_C` itself uses for its own Positive/NegativeButton,
confirmed by direct comparison, not assumed. `TramRecallPressed`'s own local-variable list (not its
bytecode, which was not part of this dump) - `FindNextStation`/`GetDirectionFromStation`/
`GetNextStopPoint`/`IsStationLocked` calls inside a counted loop - proves it performs real multi-hop
pathfinding, not a vestigial stub. Separately, this round fully traced
`TramSystem_Station_C:TramReachedLocation`'s bytecode: a short, unbranching function whose only
meaningful statement sets `ContinueMoving = false` unconditionally - every station stop is a real,
full stop, confirming a distant recall must be an asynchronous, multi-step journey rather than a
single atomic teleport. Also fully traced this round: `Tram_ParentBP_C:SetNextStopPoint(Positive,
CurrentPoint)` calls the rail's `GetNextStopPoint`, writes `TargetStation`, and calls
`SetMoving(true)` - a genuine, working one-hop "start heading this way" call, though not itself
enough to reach a distant station.

**What shipped**: `trams.list` rows gained `recallStations` (the friendly station labels a real
`TramSystem_RecallStation_C` links to that specific tram, empty when none do).
`trams.set{id,targetStation}` finds the recall station whose `LinkedTram`/`LinkedStation` match the
request and calls its own `TramRecallPressed(true)` - the game's own function, never reimplemented.
Refuses up front if the tram's moving state cannot be confirmed or it is already moving, and if no
recall station links that exact tram/station pair (an honest, narrower reachable set than offline's
"any station the save has ever referenced"). After pressing, re-reads `Moving`/`PreviousStation` and
accepts either the tram now moving or already at the requested station as success (the `elevators.set`
"a press with no confirmed effect is an error, not a false success" discipline) - it does not assert
anything about `TramRecallPressed`'s own internals, since those were not independently confirmed.
`LiveTramsFeatureSession`'s `lastStation` field is now an editable `Choice` (options =
`recallStations`) when a tram has at least one linked recall station, and stays read-only (per-tram,
not per-area) otherwise - the same per-instance-degradation idiom `LiveResourceNodesFeatureSession`
already uses. Wire shape changed, so `LiveTramsChannel`/`LiveTramsFeatureSession` were updated (new
`SetTargetStationAsync`, `LiveTram.RecallStations`); `LivePowerSocketsChannel`/
`LivePowerSocketsFeatureSession` are untouched.

**Still not fully bytecode-confirmed, flagged loudly rather than asserted as certain**:
`TramRecallPressed`'s own `ScriptBytecode` (exactly what it calls on `LinkedTram`, and whether it
gates on host/`IsServer()` itself) and `TramSystem_Rail_C`'s `GetNextStopPoint`/
`GetDirectionFromStation` bytecode were not part of this dump - the coordinator can supply
`TramSystem_RecallStation.json`/`TramSystem_Rail.json` to close this with full certainty in a future
round. Until then this write path is evidenced by real, dumped property/function names and shapes,
not by a hunch, but is a step below the byte-level certainty `elevators.set`/`buttons.set` reached
after their own follow-up rounds.

**Lua harness**: **1252 checks passed, 0 failed** via `python tools/run-lua-tests.py` (new
`trams.lua` coverage: successful recall through the correctly-matched linked recall station,
already-there no-op that never presses any recall station, currently-moving refusal that runs before
any recall lookup, unlinked tram/station pair refusal, a press-with-no-effect honesty refusal, and
an unfamiliar tram with an unreadable `Moving` state refusing rather than guessing). Caught and fixed
a real bug in the first draft during this pass: checking the `pcall` "ok" flag alone for `Moving`
does not detect a genuinely missing property (the fake, and per `buttons.lua`'s own documented trap,
possibly real UE4SS too, returns `nil` without erroring) - fixed to use the same `boolOrNil`
type-check idiom every other area in this mod already relies on for exactly this reason. C# not
built or tested this round (out of scope; the coordinator builds centrally) - the two changed C#
files were reviewed by hand against `LiveButtonsChannel.cs`/`LiveButtonsFeatureSession.cs` (the one
other area with a real settable field and a `LiveAgentException` catch) line for line. Not yet
exercised in the running game.

## Round-102: live NPC spawners and world triggers - cooldown/count state confirmed on a native subsystem, trigger rows keyed by a game-authored id, not an actor path (2026-09-18)

Closed two of the remaining offline-only world-map features from a real CUE4Parse class+bytecode
probe (`tests/AbioticEditor.Probes/LiveGapProbe.cs` output, `pass2\Abiotic_NPCSpawn_ParentBP.json`/
`NPCSpawn_Narrative.json`/`Abiotic_TriggerVolume_ParentBP.json` plus `layouts.txt`), following the
buttons/elevators template end to end: Lua area modules
(`live-agent/AbioticEditorLiveAgentLua/Scripts/areas/npcspawns.lua`,
`.../areas/triggers.lua`, registered in `areas/manifest.lua`), C# channels
(`Core/LiveEditing/World/LiveNpcSpawnsChannel.cs`, `.../LiveTriggersChannel.cs`), C# sessions
(`Web.Shared/Models/LiveNpcSpawnsFeatureSession.cs`, `.../LiveTriggersFeatureSession.cs`, both
implementing `IWorldFeaturesSession` scoped to one feature id each so the existing offline
`WorldFeaturesTab` renders them live with zero new UI), wiring in `LiveConnect.razor` (nav buttons,
render branches, `EnsureAreaConnectedAsync`, session field list, region-scoped reset, DISCONNECT
reset), `Live_TabNpcSpawns`/`Live_TabTriggers` resx keys (English only, matching the existing
"no back-fill for brand-new keys" convention), and two new protocol doc sections. C# contract
tests `tests/AbioticEditor.Tests/WorldLiveNpcSpawnsAreaTests.cs` /
`WorldLiveTriggersAreaTests.cs` (standalone files, mirroring `WorldLiveButtonsAreaTests.cs`, not
touching the shared `WorldLiveAreaParityContractTests.cs`).

**NPC spawners (`npcspawns.list`/`npcspawns.set`, feature id `npc-spawns`).** **Class discovery
needed three roots, correcting the task brief's own assumption of one hierarchy**: the dump shows
`NPCSpawn_Entity_C` and `NPCSpawn_Narrative_C` both declare `super=Actor` directly, not
`super=Abiotic_NPCSpawn_ParentBP_C` - two genuinely separate roots (confirmed subclasses:
`NPCSpawn_Trader_Chef_C`/`NPCSpawn_Trader_Marion_C` from the narrative one,
`NPCSpawn_VOTV_UFO_C`/`NPCSpawn_VOTV_Wisp_C` from the entity one), added as
`ADDITIONAL_ROOT_CLASSES` (data, not logic - same idiom buttons.lua already uses). The
overwhelming majority of real spawner classes (every zombie/pest/gatekeeper/order/pillager/
darklens/security-bot/peccary/winter-sprite/single-grunt family, checked one by one against the
dump's own `super=` chain) do chain to `Abiotic_NPCSpawn_ParentBP_C`, so one `FindAllOf` on that
root covers them; the two additional roots never expose any of the cooldown/count system, so their
rows always report `controllable:false`.

**Cooldown/count state is split between the spawner actor and a native world subsystem,
`AIDirectorSubsystem`** (`/Script/AbioticFactor`) - confirmed from the spawner's own
`ExecuteUbergraph`/`CheckSpawnProximity*` bytecode, which fetches it via
`SubsystemBlueprintLibrary::GetWorldSubsystem` and calls
`GetCurrentCooldownRemainingFromSpawner`/`GetCooldownDaysRemainingFromSpawner`/
`GetHasBeenEncounteredOnceForSpawner` on it, passing itself as the spawner argument. `onCooldown`
is instead the spawner's own real `IsOnCooldown()` function (bytecode confirms it is exactly
`remaining > 0 OR daysRemaining > 0`); `hasSpawnedOnce` is a direct actor property;
`spawnCount` is the spawner's own `GetCurrentSpawnedCount(false)`. The offline leaf
`MinutesPassedCooldownStarted_` has **no confirmed live counterpart** anywhere in the dump and is
not exposed.

**Both writes are momentary "do it now" toggles.** `resetCooldown` calls the spawner's own real,
actor-level `SetSpawnOnCooldown(TimeRemaining: double, InCurrentDay: int)` with `(0.0, 0)` -
traced its full bytecode: passing `InCurrentDay=0` makes the function look up "today" itself off
a feature-detected `AI Director.DayNightManager.CurrentDay` rather than requiring the caller to
know it, then it calls `AIDirectorSubsystem:SetCooldownForSpawner` internally, so this module
never calls that subsystem function directly. `forceSpawn` calls the spawner's own
`TrySpawnNPCNew(false, true, false)`, falling back to the older `TrySpawnNPC` with identical
arguments when the newer one is absent - `ForceSuccessByTrigger=true` is confirmed (traced
multiple `JumpIfNot` branches gated on it) to bypass individual spawn-check jumps, matching what a
`Trigger_*` volume would pass. **Genuinely unverified against the running game**: a call that does
not error is reported as requested, not a confirmed spawn - no live capture confirms an NPC
actually appears. Deliberately excluded from the desktop app's periodic live-tab refresh loop
(`ActiveLiveSessions` in `LiveConnect.razor`), matching `containers`/`resourcenodes`/
`destructibles` - the Facility fixture alone carries 918 `NPCSpawn_*` entries.

**World triggers (`triggers.list`/`triggers.set`, feature id `triggers`).** **Rows are keyed by
the trigger's own `UniqueTriggerID` string** (e.g. `WF_NewGameStarted`), not an actor path - the
one deliberate exception to this project's own convention, confirmed directly off
`Abiotic_TriggerVolume_ParentBP_C`'s declared, unsuffixed `UniqueTriggerID` property and matching
the save file's `TriggerMap` key exactly. The task brief's own guess that live counts might live
in a single map on the game mode/game state was checked and is wrong: `Abiotic_WorldSave_C` does
carry a `TriggerMap` (confirmed in the dump), but each placed trigger actor keeps and persists its
own entry directly - there is no separate live map object to go through. Class discovery sweeps
one confirmed root, `Abiotic_TriggerVolume_ParentBP_C` (the dump's own example subclass,
`Trigger_CompendiumExploration_C`, chains to it directly); the probe's package set did not happen
to include the other ~16 `Trigger_*` blueprints, so only that one subclass relationship is
independently confirmed, though the hierarchy sweep is expected to cover the rest with no code
change.

`timesTriggered`/`hasBeenTriggeredOnce`/`triggerLimit` are all direct, unsuffixed instance
properties (only `timesTriggered` is written). Writing an arbitrary count patches
`TimesTriggered` directly then calls the trigger's own real, no-argument, actor-level
`SaveTriggerData()` - confirmed `FUNC_Public|FUNC_BlueprintCallable|FUNC_BlueprintEvent`. `reset`
instead calls the trigger's own real, no-argument `ResetTriggerState()`, whose bytecode was traced
in full: it sets `TimesTriggered=0`, `HasBeenTriggeredOnce=false`, re-enables the trigger volume's
collision, re-allows overlap on its linked trigger arrays, and calls `SaveTriggerData()` itself -
strictly more complete than a bare count write, so it is preferred whenever a full reset (not an
arbitrary count) is wanted, and wins over a `timesTriggered` value sent on the same row.

**Lua harness**: 1217 checks passed, 0 failed (`python tools/run-lua-tests.py`, up from 1186
before this round). Both new test-case files use fakes with an unfamiliar subclass declared only
via `__bases` (proving the hierarchy sweep finds it and it is fully listable/settable) and a fake
lacking the expected properties/functions entirely (proving it still lists, with every
unsupported field reported absent rather than erroring or being dropped, per the owner's standing
rule). Not run: `dotnet build`/`dotnet test` (coordinator builds centrally) - every C# identifier
above was hand-checked against the real files it references (`ILiveGameChannel.RequestAsync`,
`WorldMapAccessor.TryParseBool/TryParseInt`, `WorldMapField.Bool/Integer/ReadOnly`,
`IWorldFeaturesSession`) since this round could not build to verify. **Not yet exercised in the
running game.**

## Round-100: live Breakable Objects and Corpses (2026-09-18)

Two new live world-map areas, both simple bool-state actor sweeps mirroring buttons/elevators'
established shape exactly. Owner rules for this round: never hardcode a leaf blueprint class name
(discover through the parent class, `FindAllOf` is hierarchy-inclusive, with a small data-only
table for any additional root classes), feature-detect every property per instance with `pcall`
(unsupported fields report as not available live, never dropped), report the real class name in
each row, and never guess a live property name from a save leaf name - read it from the probe dump.

**DESTRUCTIBLES** (`Core/WorldSaves/Features/DestructibleMapFeature.cs`, id `destructibles`, one
editable leaf `Broken_`). Confirmed against the coordinator's own CUE4Parse class dump and
`Abiotic_GenericDestructible_BP_C`'s own blueprint bytecode: every fixture-confirmed class
(`Destructible_CeilingTile_C`, `Webbing_BP_C`, `IceWall_BP_C`, `Destructible_Fracture_MageEye_C`,
`Destructible_InvisibleWall_C`, `XRayField_BP_C`, `Destructible_CafeteriaDoor_C`,
`Destructible_PortablePortal_C`, `Destructible_SyncrotronHole_C`,
`Destructible_SecurityContainerDoor_C`, `Destructible_BookCartStack_C`,
`Destructible_ContainmentShield_C`) declares `super=Abiotic_GenericDestructible_BP_C` either
directly or one step removed (`Webbing_Marshmallow_BP_C` etc. chain through `Webbing_BP_C`), so a
single `FindAllOf("Abiotic_GenericDestructible_BP_C")` sweep finds every one with no class name
hardcoded anywhere - `ADDITIONAL_ROOT_CLASSES` stays empty this round, same as `buttons.lua`'s.
`broken` maps to `actor.Broken` (direct, replicated BoolProperty, RepNotify `OnRep_Broken`) -
**settable, one-way only**: `broken: true` writes `Broken = true` then calls the real
`OnRep_Broken()`, which itself calls `SetStateBroken(NoFX)` exactly the way the class's own break
path (world-flag trigger or damage reaching zero health) already does, so a live break gets the
real mesh swap, collision change, and FX/SFX. `broken: false` ("repair") is refused with a named,
player-safe error, **confirmed impossible, not assumed**: `OnRep_Broken`'s own bytecode is `if not
Broken then return` with no other branch - nothing else in the class (every function in the dump
was checked) ever restores the intact mesh's collision/visibility once `SetStateBroken` has run, so
writing `Broken = false` would silently desync the save flag from what the player still sees.
Deliberately excluded from the desktop app's periodic live refresh loop, for the same "a region can
carry a great many of these" reason `resourcenodes` already documents. Removal is not offered live
(matches the offline feature, which disables it for the same reason: an entry only exists once
broken, and un-breaking is refused anyway).

**CORPSES** (`CorpseMapFeature.cs`, id `corpses`, leaves `IsGibbed_`/`IsLooted_` read-only offline,
offline action is remove). Confirmed against the coordinator's own CUE4Parse class dump of
`CharacterCorpse_ParentBP_C`: every fixture-confirmed class (`CharacterCorpse_Human_BP_C`,
`CharacterCorpse_MonsterGeneric_C`, `CharacterCorpse_OrderGrunt_C`, `CharacterCorpse_OrderSniper_C`)
declares `super=CharacterCorpse_ParentBP_C` either directly (`MonsterGeneric`) or through
`CharacterCorpse_Human_BP_C` (every other named human-shaped variant, including
`CharacterCorpse_OrderBreacher_C`/`OrderCaptain_C`/`LabRat_C`/`Human_GATESecurity_C`), so one
`FindAllOf("CharacterCorpse_ParentBP_C")` sweep finds every one, `ADDITIONAL_ROOT_CLASSES` empty
again. `gibbed`/`looted` map to `actor.IsGibbed` (replicated, RepNotify `OnRep_IsGibbed`) and
`actor.HasBeenLooted` (replicated, no RepNotify) - one field-name step from the save's own
`IsGibbed_`/`IsLooted_` leaves, same meaning, both stay **read-only live too**, matching the file
editor's own "no in-game reason to flip either by hand". **Removal is real and live** - checked
every function `CharacterCorpse_ParentBP_C` declares against the dump and, matching round 78's
identical finding for tamed pets, none of them cleanly despawns an already-placed corpse (no
"DespawnCorpse" or equivalent), so `corpses.remove` uses the same `K2_DestroyActor()` technique
`pets.remove` already established (the reference CheatConsoleCommands mod's own "deleteobject"
path) - no undo. `LiveCorpsesFeatureSession` is the first live `IWorldFeaturesSession` where
`SupportsRemoval` is genuinely `true` rather than every offline removal having no live equivalent.
Corpses stay on the periodic live refresh loop (a region typically holds few of them, unlike
resource nodes/destructibles).

**Files**: `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/{destructibles,corpses}.lua` (+
`areas/manifest.lua`), `live-agent/AbioticEditorLiveAgentLua/tests/cases/{destructibles,corpses}.lua`
(+ `tests/cases/manifest.lua`), `Core/LiveEditing/World/Live{Destructibles,Corpses}Channel.cs`,
`Web.Shared/Models/Live{Destructibles,Corpses}FeatureSession.cs`, `LiveConnect.razor` (tab
buttons, render blocks, fields, connect switch, periodic-refresh switch - corpses only),
`Live_TabDestructibles`/`Live_TabCorpses` in `AppResources.resx`,
`docs/reference/live-editing-protocol.md`, `WorldLiveDestructiblesAreaTests.cs`/
`WorldLiveCorpsesAreaTests.cs` (standalone contract tests, same shape as
`WorldLiveButtonsAreaTests.cs`).

**Verified**: both new `tests/cases/*.lua` harness cases pass in isolation (`python
tools/run-lua-tests.py`, 42 checks: hierarchy-sweep discovery including an unfamiliar future
subclass and a non-matching actor, per-field feature-detection, the repair refusal - both alone and
mixed with a real break in the same batch, missing-id handling, non-host refusal, and - for corpses -
real removal plus a destroy-call failure path). **Not yet exercised in the running game** (no
UE4SS/live-game verification this round, matching every other live area's own "not yet exercised"
caveat until an owner with the game running confirms it - see `live-world-editing.md` memory for
the harness's own limits: it proves the module resolves the fields/functions it expects and
respects host gating, never that a name is correct against the real running game beyond what the
CUE4Parse dump already confirms).

## Round-99: Characters and Creatures merged into one NPCS tab, real names for creatures (2026-09-18)

Round-92 compared the offline "Characters" tab (`WorldNpcsTab.razor`, the save's `NarrativeNPCMap`
- story NPCs/traders) against the live-only "Creatures" tab (`LiveNpcsTab.razor`, every
`NPC_Base_ParentBP_C` actor currently loaded - wildlife, monsters, robots, humanoid NPCs) and left
them as two tabs: the data is genuinely disjoint (the save never persists what is loaded in the
world). The owner disagreed with two tabs for one concept and separately flagged that neither
section names who occupies a slot. This round merges the UI (the data split from Round-92 still
stands and is unaffected) and adds a real name source for creatures.

**Merge**: `WorldNpcsTab.razor` now renders both rosters behind a `world-tabs`-styled chip pair
("Story characters" / "Creatures and NPCs nearby") in one section, instead of two separate tabs.
Story characters keep the exact Round-92 behavior (one Dead checkbox doing kill/revive, story
stage picker) unchanged. The creatures chip is the old `LiveNpcsTab.razor` body, renamed into the
same component: REFRESH button, host warning, Disabled/Invincible/Faction fields, wiki picture via
`CreatureWikiImages`, and the same Round-92 Dead checkbox. `LiveNpcsTab.razor` is deleted; its
session (`LiveNpcSession`, `main.lua`'s `npcs.list`/`npcs.set`) and every Lua/C# wire type are
unchanged - only the razor component and its wiring moved. Offline, the creatures chip shows a
plain note ("connect to your running game...") instead of existing as a dead separate tab, the
same capability-gated idiom `WorldTradersTab`/`WorldContainmentTab` already use for a live-only
half of a shared tab; `Creatures` is a new optional `LiveNpcSession?` parameter (null offline, null
live until `LiveConnect.razor` finishes connecting it) alongside the existing `IWorldNpcsSession
Session` parameter for the story half. `LiveConnect.razor`: the dedicated "npcs"
(`Live_TabWildlife`) nav button and render branch are gone; the "narrativenpcs" branch now passes
`Creatures="_npcs"` alongside `Session="_narrativeNpcs"`, and `EnsureAreaConnectedAsync`'s
`"narrativenpcs"` case also connects `_npcs` (moved out of the deleted `"npcs"` case) so the
creatures chip has data the moment a player switches to it. Deliberately did **not** add `_npcs` to
`ActiveLiveSessions()`'s periodic-refresh switch - that switch already documents skipping it
("backs onto a full world scan ... the same freeze/timeout story containers had"); the REFRESH
button and the Round-92 revive path are the only ways it re-fetches, unchanged from before the
merge. `SaveEditorSurface.razor` needed no change: `Creatures` defaults to null, so passing only
`Session="@world"` there still renders correctly with the offline note.

**Real names (corrected same day, see follow-up below)**: this entry originally said
story-character names were already as good as the data allows, citing
`docs/reference/research/research-narrative-npcs.md`'s "anonymous slot" verdict. That verdict was
wrong - it reasoned from the actor class alone and never checked what the level file itself sets
per placed instance. The coordinator's own level probe proved every placed actor DOES carry its
own conversation row. See the follow-up write-up below for what actually shipped.

The creatures section got a genuine upgrade: new `NpcDisplayNameCatalog`
(`Core/Catalogs/World/NpcDisplayNameCatalog.cs`) reads `DT_NPCList` in full (row -> `DisplayName_`
+ `NPCSpawnClass_`), generalizing what `PetGameData.cs` already does for pets alone into every
spawnable NPC class. This is the game's own name, not a guess: `NPC_Robot_Defense_C` naively
derives to "Robot Defense" (the old `LiveNpcsTab.DisplayName` heuristic, kept as `HeuristicName` -
still the `CreatureWikiImages` lookup key, that catalog is curated against exactly this
derivation, and the last-resort fallback), but `DT_NPCList` itself calls the row "Defense Robot"
(see `CreatureWikiImages`'s own header comment, which already documented this exact mismatch from
Round 91's wiki-art pass without fixing the tab's display name). Bundled into the game-data
registry as `GameDataRegistry.NpcDisplayNames` (`string -> string`, no schema bump - the existing
"nullable, absent means not dumped" convention) so a browser build with no game install resolves
the same names; `ItemCatalogService.GetNpcDisplayNameAsync` prefers a mounted install (freshest,
picks up mods/DLC) and falls back to the bundled dictionary, mirroring `GetCharacterNameAsync`'s
existing shape. **Update, later same round: the coordinator re-ran `dump-registry --all-cultures`**
- `assets/registry/registry.json` now carries 117 `NpcDisplayNames` entries (confirmed by reading
the committed file directly), so the browser build already has real creature names with no further
action needed for this part. `DumpRegistryCommand` itself needed no change either way (it already
calls `GameDataRegistry.BuildFromInstall` for every culture, which picked up the new catalog
automatically).

Added `tests/AbioticEditor.Tests/NpcDisplayNameCatalogTests.cs`: pure `Resolve` matching (short
class, full soft-object path, case-insensitivity, null/empty handling - no game install needed), a
live-table cross-check against `PetGameDataTests`' own already-asserted `NPC_Monster_LamogiSpeedy`
-> "Speedogi" fact (skips without an install), and a `GameDataRegistry.NpcDisplayNames` save/load
round-trip. Extended `WorldLiveAreaParityContractTests.cs` with three facts: `WorldNpcsTab.razor`
binds to `IWorldNpcsSession Session` plus optional `LiveNpcSession? Creatures` and
`LiveNpcsTab.razor` no longer exists, `LiveConnect.razor` wires `Creatures="_npcs"` on the merged
tab with no leftover `<LiveNpcsTab`, and the three new resource keys exist while
`Live_TabWildlife` does not.

**Resources**: added `WorldNpcs_SectionStory`, `WorldNpcs_SectionCreatures`,
`WorldNpcs_CreaturesNeedLiveConnection` (English only, matching this repo's existing convention of
not back-filling de/es/fr/ru for brand-new keys - confirmed none of the keys touched this round
existed in those four files to begin with, so nothing there needed editing). Changed
`WorldEditor_TabNpcs`'s English value from "Characters" to "NPCs" (same key, not renamed).
Removed the now-dead `Live_TabWildlife` key. Every other key from both source tabs
(`WorldNpcs_*`, `LiveNpcs_*`, `Editing_SelectNpc`) is still referenced by the merged component
and was left alone - `LiveNpcs_SelectToPreview` in particular had zero usages before this round
(a pre-existing orphan) and is now actually wired to the creatures chip's select-hint.

Not run: `dotnet build`/`dotnet test` (coordinator builds centrally after concurrent sessions
land) and `python tools/run-lua-tests.py` (no Lua touched this round - the wire protocol and
`main.lua` are unchanged). Not exercised in a running game.

### Round-99 follow-up: story-character names ARE resolvable - the "anonymous slot" verdict was wrong

Same day, same round. The coordinator ran `tests/AbioticEditor.Probes/NarrativeNpcLevelProbe.cs`
against a real install (all 77 `.umap` level packages, ~85s) and found every placed
`NarrativeNPC_*` actor carries its own **instance-level** `NarrativeNPC_ConversationRow` - not
just inherited from the class default. E.g. in `Facility_Pens`: `NarrativeNPC_Ela_C_1` -> row
`Labs_Ela_Pest` -> `DT_NPC_Conversations`'s `NPCName` "Ela"; `NarrativeNPC_Human_ParentBP_C_2` ->
row `LABS_Abe` -> "Abe"; the generic-looking `NarrativeNPC_Human_Hologram_C_0` -> row `Manse_DL_03`
-> "Dr. Manse". The class being generic (`Human_ParentBP`/`Human_Hologram`) never meant the *slot*
was anonymous - the level file always knew exactly who was there; the save file just never
repeated it. Confirmed independently against the real fixture: grepping the raw bytes of
`tests/fixtures/SteamSaves/Legacy/Cascade/WorldSave_Facility_Pens.sav` for `NarrativeNPC` finds the
literal key `/Game/Maps/Facility_Pens.Facility_Pens:PersistentLevel.NarrativeNPC_Ela_C_1` - the
exact (map, actor) pair the probe evidence names.

Added `AbioticEditor.Core.WorldSaves.NarrativeNpcNameCatalog`
(`Core/Catalogs/World/NarrativeNpcNameCatalog.cs`), `DoorLocationResolver`'s counterpart for names
instead of positions: `BuildFrom(provider)` walks every `.umap` under
`AbioticFactor/Content/Maps`, reads each `NarrativeNPC_*` export's own
`NarrativeNPC_ConversationRow.RowName`, and resolves it against `DT_NPC_Conversations`'s `NPCName`
(localized). Keyed by `"<LevelFileName>:<ActorInstanceName>"` (e.g.
`Facility_Pens:NarrativeNPC_Ela_C_1`) via `KeyFor`; `KeyForActorPath` parses a `WorldNpc.Id` into
the same key using **`DoorIdParser.Parse`, reused unchanged** - the exact same generic UE
actor-path parser `WorldDoorsTab` already relies on for `WorldDoor.Id`, since `WorldNpc.Id` is the
identical actor-path shape (`/Game/Maps/X.X:PersistentLevel.Actor_C_N`, also accepts the live
`GetFullName()` form). This is deliberately **dump-time only** - `BuildFrom` is slow (~85s, walks
77 level packages) and must never run inside the live app; it is called exactly once, from
`GameDataRegistry.BuildFromInstall` (i.e. only by the maintainer `dump-registry` CLI command).

New nullable `GameDataRegistry.NarrativeNpcNames` (`string -> string`, no schema bump, same
"absent means not dumped" convention as `NpcDisplayNames`). At runtime **both** hosts read only
the bundled registry for this field - `ItemCatalogService.GetNarrativeNpcName` is a plain
synchronous dictionary lookup with **no live-provider fallback at all** (unlike
`GetCharacterNameAsync`/`GetNpcDisplayNameAsync`), because there is no fast per-actor path: the
registry itself only exists because building it means loading every level once, and neither the
desktop app nor the browser build may repeat that at runtime. `WorldNpcsTab`'s story rows now call
`Items.GetNarrativeNpcName(npc.Id)` first; when it resolves, the bold primary label becomes the
real name and the small secondary line becomes the `NpcIdentityCatalog` hint/class context that
used to be the primary label, with the full raw actor id moved to the row/detail's `title`
tooltip. When it does not resolve (older bundled registry, or an actor the probe's own sweep
couldn't reach), everything falls back to the exact pre-follow-up chain (live desktop lookup, then
the curated hint, raw id visible inline) - zero behavior change until the registry is re-dumped.

Updated `docs/reference/research/research-narrative-npcs.md`'s "NPC identity" section: replaced
the wrong "anonymous slots" verdict with the corrected finding and a caveat that a wandering
trader's conversation row may not track which trader currently occupies that spawn point (that
identity is `NarrativeNPCDirectorComponent`'s own runtime state, not this map or the conversation
row) - the resolved name is the *placed actor's* identity, not necessarily "whoever is standing
there today" for the handful of roaming trader slots.

Added `tests/AbioticEditor.Tests/NarrativeNpcNameCatalogTests.cs`: `KeyFor`/`KeyForActorPath`
against the literal id grepped out of the real Cascade fixture's `WorldSave_Facility_Pens.sav` raw
bytes (both the file-form and live `GetFullName()`-form ids), `Resolve` against an inline sample
dictionary, a `GameDataRegistry.NarrativeNpcNames` save/load round-trip, and a fixture test that
reads `WorldSave_Facility_Pens.sav` through the real `WorldSaveReader`, confirms the real
`WorldNpc.Id` values for the Ela and Abe entries normalize to the expected composite key, resolves
them against an inline sample dictionary (proving the shape end to end right now), and separately
checks the real bundled registry's `NarrativeNpcNames` only when present (skipped until the
registry is re-dumped).

`NarrativeNpcNameCatalog` is brand new this follow-up, so the registry re-dump the coordinator
already ran for `NpcDisplayNames` (see the update above) predates this field - confirmed by reading
`assets/registry/registry.json` directly: it has `NpcDisplayNames` but no `NarrativeNpcNames` key
at all yet. **Coordinator: re-run `dump-registry --all-cultures` once more and commit the refresh**
so story-character rows actually show real names; until then every row keeps showing today's
hint-based label (`NarrativeNpcNameCatalogTests`' bundled-registry check is written to skip
gracefully in exactly this situation, not fail). Not run: `dotnet build`/`dotnet test` (coordinator
builds centrally). Not exercised in a running game.

## Round-98: live editing for Linux players (Steam Play/Proton) (2026-09-18)

Live editing's in-game side (UE4SS + the bundled Lua mod + the native
`AbioticEditorLiveAgentHelper.exe`) was Windows-only even on the Linux desktop app, which already
supports offline editing and already detects Unix Steam libraries and Proton `compatdata` saves
(`AbioticEditor.Core.Saves.SaveDiscovery.DiscoverProtonClientWorlds`). Abiotic Factor has no
native Linux build, so a Linux copy of the game is always the same Windows binary running under
Steam Play - the same bundled UE4SS package and the same Lua mod install into the same
`Binaries/Win64` folder as on Windows, no code changes needed there (`Ue4ssBundledRuntime` was
already pure `System.IO`, no platform checks).

**Blockers found**: `LiveAgentSetup.EnsureReadyAsync` hard-returned `NotSupportedOnThisPlatform`
for any non-Windows OS (`src/AbioticEditor.Web.Shared/Services/LiveAgentSetup.cs`, was line 112).
Deeper down, the native helper resolves its IPC mailbox and token/port files from
`%LOCALAPPDATA%` via a raw Win32 `GetEnvironmentVariableA` call
(`live-agent/AbioticEditorLiveAgentHelper/src/TokenStore.h`), and the in-game Lua mod resolves the
same folder via its own `os.getenv("LOCALAPPDATA")` - both only meaningful from *inside* the
game's own Proton (Wine) prefix, which is a different filesystem location from this editor's own
native Linux `%LOCALAPPDATA%` (`~/.local/share` by .NET convention). `DesktopLiveEditingCapability`
and `LiveAgentLogBridgeService` both read that native path unconditionally, so even if the helper
somehow ran, the editor would never find its token/port/log files on Linux.

**What changed**: new `AbioticEditor.Core.LiveEditing.ProtonLiveAgentEnvironment` resolves a Steam
library root from any install path (`FindSteamLibraryRoot`, walking for a `steamapps/common`
segment pair - robust to whichever install shape the player picked, mirroring
`GameInstallLocator.InferKind`'s technique) and that library's Proton prefix for Abiotic Factor's
fixed Steam app id (`SteamAchievements.AppId` = 427410; a beta/demo build with a different app id
is not handled - a known gap, matching `SaveDiscovery`'s own compatdata-scanning code not existing
yet for this specific lookup). `LiveAgentSetup.EnsureReadyAsync` now allows Linux past the
platform gate (macOS stays blocked - no bundled helper/UE4SS build and no Proton-equivalent path),
resolves that prefix before launching the helper, and launches it through `wine` (or a binary named
by the new `ABIOTIC_LIVE_WINE` env var) with `WINEPREFIX` and an explicit `LOCALAPPDATA` set to the
prefix's own `C:\users\steamuser\AppData\Local`, so the helper's token/port/mailbox files land
exactly where the Lua mod (running inside that same prefix) looks for them. A missing Proton
profile or missing Wine binary now returns a plain-language `HelperUnavailable` message instead of
a generic "not supported" state. `DesktopLiveEditingCapability.TryReadLocalToken/Port` and
`LiveAgentLogBridgeService`'s `lua.log` tail now check the same Proton-mapped folder first on
Linux (falling back to the native path); `helper.log` stays at the native path since this editor's
own process writes that file directly, not through Wine.

**Not verified against a real Linux/Proton box** (no such machine available this round - flagged
loudly in the code and in `docs/guide/live-editing.md`'s new Linux/Proton section): whether
`Process.GetProcesses()`/`GetProcessesByName` actually see the Wine-wrapped game and helper
process under their expected names (`IsGameRunning`/`IsHelperRunning` in `LiveAgentSetup.cs` -
Wine's own process-naming and the Linux kernel's 15-byte `comm` limit could mean neither ever
matches, silently defeating the "already running" fast paths without breaking a first launch);
whether Wine's console stdout/stderr redirection through .NET's `Process` class behaves the same
as a native Windows child; and whether Steam's Proton always uses the `steamuser`/`pfx` layout
this assumes (matches `SaveDiscovery`'s already-relied-upon `ProtonSaveGamesSubPath`, so treated as
established, not a fresh guess). Tests added in
`tests/AbioticEditor.Tests/ProtonLiveAgentEnvironmentTests.cs` cover the pure path-resolution logic
against fake `steamapps/common/.../compatdata/<appid>/pfx` fixture trees only, not the wine launch
or process-detection code, which needs a real box.

## Round-97: web-only safeguards, containment scan in the browser, bundled data refresh (2026-09-18)

Coordinator round that landed alongside rounds 92-96 (all seven workstreams ran as parallel
subagents; the coordinator built and tested centrally: 1473 tests green, Lua harness 985 checks).

- **Mod disclaimer (browser build only)**: `IModDisclaimerGate` (`DesktopModDisclaimerGate` is a
  passthrough, `BrowserModDisclaimerGate` shows a modal) wraps both "open a save" call sites in
  `WorkspaceShell.razor`, ahead of the unsaved-changes guard. It fires on every open, with no
  "don't show again". `OpenAsync` only scans a folder and never selects a save, so the first save
  opened after picking files also goes through it.
- **Move items between worlds is gone from the browser build**: `IBrowserHostMarker` (registered
  only by `Web.Wasm/Program.cs`) hides the link in `WorldContainersTab.razor` and makes
  `TransferItems.razor` redirect to `browse`.
- **CONTAINMENT said "no units built" in the browser**: `ContainmentDirectory.Survey` walks the
  disk, and a browser save "path" is an opaque handle, so the survey came back empty without an
  error. `WorldSaveSession` now takes the sibling region-save identifiers from the workspace and,
  when `ISaveFileSystem.HasLocalPaths` is false, reads them through the file-system seam and joins
  with the new disk-free `ContainmentDirectory.Assemble`. With no sibling saves to read it reports
  `ContainmentScanUnavailable` and the tab says it could not check, instead of claiming none exist.
- **Bundled browser data**: audit found icons (1622), art (351) and wiki pictures (126) in sync,
  and the registry stale (no `CraftDurationSeconds` on recipes). Regenerated with
  `dump-registry --all-cultures`. `registry.zh-Hant.json` carried 17 skills because the DONOTUSE
  marker is string-table text and that translation rewords it; `SkillCatalog.RetiredRowIds` now
  names the two retired rows by id, and `BundledGameDataTests` asserts every language lists the
  same skill ids.
- **Live property names come from a probe, not from save leaf names**: the first elevator cut
  guessed a `TopOpen` property that does not exist. `tests/AbioticEditor.Probes/ElevatorButtonProbe.cs`
  dumps the elevator and button blueprints with bytecode (set `ELEVATOR_BUTTON_PROBE_OUT`). Owner
  rule recorded this round: discover actors through the parent class with fallbacks, never a list
  of leaf blueprint names.
- **Not yet exercised in the running game**: live buttons, live elevators, the shared TRADERS tab
  when connected live, and the Dead toggle on live creatures.


## Round-96: live Buttons - property/function names confirmed from the coordinator's class dump (2026-09-18)

Round-94's live Buttons area shipped with every state-field property name a pcall-probed guess.
The coordinator then ran `LiveClassPropsProbe`/a new `tests/AbioticEditor.Probes/
ElevatorButtonProbe.cs` against the installed game and handed over the full CUE4Parse class dump
(properties + functions) and, for `Button_Generic.uasset`/`Button_Keypad.uasset`/
`Button_LightSwitch.uasset`, the complete blueprint bytecode as JSON. Replaced every guess with the
confirmed names.

**Class discovery switched from a hardcoded leaf list to a hierarchy sweep (owner request, same
round).** The first pass of this confirmation still enumerated 15 concrete `Button_Generic_C`
subclasses by name (down from 17 once the two non-buttons below were excluded) - correct today,
but silently stale the moment the game ships a new button type. Checked how other areas in this
mod already handle this: `pets.lua`'s own header comment documents "FindAllOf is
hierarchy-inclusive - confirmed already by `bases.lua`/`containers.list` scanning this same way".
`buttons.lua` now calls `FindAllOf("Button_Generic_C")` alone (`BUTTON_ROOT_CLASS`) - every current
subclass (`Button_DFWarReactor_C`, `Button_Keypad_C` and its own subclasses, `Button_LightSwitch_C`
and its subclass, `Button_ORDER_C`, `Button_Torii_Lantern_C[_Hanging]`, `Button_Tram_C` and its
subclass, `Button_ValveWheel_C`, `Button_VehicleRecall_C`, `Button_WeatherEnd_C`) comes back
automatically, and so will any future one, with no code change here. A second, currently-empty
`ADDITIONAL_ROOT_CLASSES` table (data, not logic - one line to extend) exists for a button-shaped
class that does NOT chain up to `Button_Generic_C`; nothing qualifies for it today. Every
property/function read or write is per-instance `pcall`-feature-detected rather than assumed
present, so a class this module has never heard of (through either root) still lists with whatever
it actually has and reports the rest unavailable rather than erroring or being dropped, and its
real runtime class name always comes through as the row's `label` (`ctx.classLabel`, off
`GetFullName()`) so an unfamiliar type stays visible to the player and in logs. Two names that
looked like buttons are excluded because they genuinely are not part of this system, confirmed
from their own `super=` chain, not because of the leaf list: `Button_SpecialImageButton_C` is a UMG
widget (`WidgetBlueprintGeneratedClass`, `super=AbioticWidget`), never a placed level actor, so it
can never appear in a `FindAllOf` sweep of the world at all; `CartRecallButton_C` derives from
`VehicleRecallStation_C`, not `Button_Generic_C`, and its own dump has no
`Activated`/`ButtonDisabled`/`ButtonSaveData` at all - it is not part of `ButtonMap` and does not
belong under "buttons" even as an id/position-only row. The C# side (`LiveButtonsChannel`/
`LiveButtonsFeatureSession`) never had a class-name switch/whitelist of its own - the wire only
ever carried id/label/state, so nothing there needed to change for this.

**Field mapping, read straight off `Button_Generic_C`'s own `UpdateButtonSaveData(Force)`
bytecode** (gated on `Force OR CanButtonSave()`; `CanButtonSave` checks per-actor
`ShouldSave`/`ToggleSwitch` design flags, so `Force=true` - the same idea `portals.lua`'s
`SavePortalState(true)` already uses - bypasses that gate entirely, and is what this module always
passes):
- `ButtonSaveData.ButtonIsEnabled_ = NOT ButtonDisabled` - offline "enabled" is the INVERSE of the
  live top-level `ButtonDisabled` bool (replicated, `OnRep_ButtonDisabled`).
- `ButtonSaveData.ButtonActivated_ = Activated` - direct copy of the live top-level `Activated`
  bool (replicated, `OnRep_Activated`).
- `ButtonSaveData.NoReset_ = NoVignetteReset` - direct copy of a live top-level bool that is NOT
  replicated (no `OnRep_` exists for it at all) and, notably, is not named anything close to
  "NoReset" live - this could not have been guessed.
- `ButtonSaveData.ButtonHasBeenPressedOnce_ = true` - UNCONDITIONALLY, every single time
  `UpdateButtonSaveData` runs at all, regardless of `Force`/`CanButtonSave()`. There is no other
  place in the bytecode that touches this field. This means "pressed once" cannot be set
  independently live at all, in either direction, and every successful `enabled`/`activated`/
  `noReset` edit on a button unavoidably also forces it `true` as a side effect of the same
  persistence call. `buttons.set` now refuses any request that tries to set `pressedOnce`, with a
  named error, rather than silently ignoring it or pretending it worked; it stays READABLE though
  (`button.ButtonSaveData.ButtonHasBeenPressedOnce_110_C4AE20D34162FCD3FA3323907300CB1F`, the exact
  hash-suffixed leaf name confirmed from `SaveData_ButtonStruct`'s own `ChildProperties`, accessed
  the same struct-nested-leaf way `main.lua`'s `SKILL_XP_FIELD` already documents).

**State-change choice, made deliberately, not by default**: `Button_Generic_C` also has
`TriggerButtonWithoutUser()`, but its bytecode is a single jump into the shared interaction
ubergraph (`ExecuteUbergraph_Button_Generic[6719]`) - the same entry point a player's own
interaction uses, which fires linked-button chains, cooldown checks and whatever else that graph
does. That is not what a state editor wants, so this module instead writes the property directly,
calls the matching `OnRep_` (a server never gets its own `OnRep` for something it just wrote
locally - `transmog.lua`/`portals.lua` already document the same reasoning), then calls
`UpdateButtonSaveData(true)` - exactly the shape the game's own save path already takes.

**Files changed**: `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua` (rewritten twice
this round - confirmed field mapping first, then hardcoded leaf-class list replaced with the
hierarchy sweep + fallback-roots table + per-instance feature detection),
`live-agent/AbioticEditorLiveAgentLua/tests/cases/buttons.lua` (fakes rewritten to the real
property names; added a fake subclass buttons.lua has never heard of, declared only via
`__bases = { "Button_Generic_C" }`, to prove it lists AND is settable, plus a non-button fake with
no such ancestry to prove exclusion, plus new coverage for the forced-`pressedOnce`-true side
effect and the refusal path), `Core/LiveEditing/World/LiveButtonsChannel.cs` and
`Web.Shared/Models/LiveButtonsFeatureSession.cs` (doc comments updated to the confirmed mapping;
`pressedOnce` now always renders read-only in the BUTTONS tab and is rejected locally before any
round trip to the game), `docs/reference/live-editing-protocol.md`'s buttons section, and
`tests/AbioticEditor.Tests/TcpLiveGameChannelTests.cs` (comment wording only - the wire shape did
not change either time). The wire shape (`LiveButton`/`LiveButtonEdit`/JSON field names) is
unchanged from Round-94, so no other C# registration points needed touching, and the C# side never
had a concrete-class switch/whitelist to begin with.

**Lua harness**: **985 checks passed, 0 failed** via `python tools/run-lua-tests.py` (up from 936 at
Round-94; a couple of intermediate runs this round briefly showed 1-2 failures in `elevators.lua`,
a sibling live area under concurrent, unrelated edits at the same time - resolved by the time of
this final run, and never anything in this entry's own buttons coverage). C# not built or tested
this round either (still out of scope; the coordinator builds centrally).

**Still not yet exercised in the running game.** Everything above is grounded in the class dump and
bytecode, which is real evidence the property names and the mapping exist - but nobody has yet
run this against the actual live game to confirm a `buttons.set` call visibly changes a button's
in-game behavior (lights up/unlocks/etc.), confirm `UpdateActorToWorldSave`'s byte-4 argument
really is the ButtonMap enum entry assumed here, or confirm the forced-`pressedOnce`-true side
effect matches what a player would expect to see reflected in the file afterward.

## Round-95: live ELEVATORS - list/set `topOpen` via the game's own buttons, subclass-generic discovery (2026-09-18)

**Before this round, elevators had NO live path at all.** Confirmed by grep: no `elevators.*`
handler existed in any `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/*.lua`, no
`LiveElevators*` type existed under `Core/LiveEditing` or `Web.Shared/Models`, and
`LivePortalsFeatureSession`'s own header comment explicitly listed "elevators" among the feature
ids with no live equivalent. The offline feature (`Core/WorldSaves/Features/ElevatorMapFeature.cs`,
the save's `ElevatorMap`) exposes one field, `topOpen` (bool - parked at the top vs. the bottom).

Added the live twin end to end, following the `portals` area as the template: `elevators.lua`
(new area module, registered in `areas/manifest.lua`), `LiveElevatorsChannel`
(`Core/LiveEditing/World`), `LiveElevatorsFeatureSession` (`Web.Shared/Models`, implementing the
same `IWorldFeaturesSession` boundary `WorldFeaturesTab` already binds to), and wiring in
`LiveConnect.razor` - the offline WORLD > Elevators screen now works unchanged when connected
live. This entry replaces the original write-up: the first pass guessed a live `TopOpen`
property from the saved leaf name alone; the coordinator then ran a real CUE4Parse class+bytecode
probe against the installed game (`tests/AbioticEditor.Probes/ElevatorButtonProbe.cs`) and the
guess was wrong. Corrected below, twice - first the mechanics, then discovery.

**What the probe actually shows.** `Elevator_ParentBP_C` (super `Actor`) has no `TopOpen`
property and no `OnRep_TopOpen`. The real live state is a replicated byte enum,
`ElevatorCurrentMode` (`E_ElevatorMovementTypes`: 0 StoppedAtBottom, 1 StoppedAtTop, 2
MovingToTop, 3 MovingToBottom - read straight from the enum asset's own `DisplayNameMap`/`Names`
tables). Traced from the blueprint's own bytecode (JSON export with `ScriptBytecode`, not
guessed):
- `OnLoadedFromSave(Top: bool)` sets `ElevatorCurrentMode` via a plain `EX_SwitchValue`/Select:
  `Top ? 1 (StoppedAtTop) : 0 (StoppedAtBottom)`. That is the save's own load-time mapping, so the
  saved `TopOpen_` leaf means exactly `ElevatorCurrentMode == StoppedAtTop`. The reverse,
  save-time direction runs through `SaveElevatorStateToWorldSave` -> the game mode's own
  `UpdateActorToWorldSave`, outside this class and not itself traced; the live module relies on
  the load-time mapping by symmetry.
- `TryPressTopButton(Activated: bool)`/`TryPressBottomButton(Activated: bool)` only act when
  `Activated` is true and check neither `IsServer()` nor `IsPowered()` internally - traced the
  full switch-on-`ElevatorCurrentMode` cascade for both. `TryPressTopButton`: mode 0 -> 2 (start
  moving up), mode 3 -> 2 (redirect up), mode 2 -> unchanged (prints "Elevator is already on its
  way up"), mode 1 -> 3 (**a real toggle-AWAY quirk**: pressing the top button while already
  parked at the top sends it back down). `TryPressBottomButton` is the exact mirror. Both set
  `ElevatorCurrentMode` then call `OnRep_ElevatorCurrentMode()`.
- `IsElevatorMoving()` = `(mode == 2) OR (mode == 3)`; `IsPowered()` returns a plain `PowerOn`
  bool - both confirmed present and used as the safety gates the press functions themselves do
  not apply.

`elevators.lua` never presses the button for the side the elevator already occupies (avoiding the
toggle-away quirk when the caller only wanted to confirm it is already there), refuses with a
named reason if the elevator is currently moving or not powered, and - because moving the
platform is asynchronous (real travel time) - accepts a press that starts or continues the
correct direction as success rather than requiring an already-arrived read-back. A press with no
confirmed effect (the real honesty branch a wrong property/function name would hit) is reported
as an error, not a false success.

**Discovery is subclass-generic, not a hardcoded class list** (second coordinator correction, so
a future DLC elevator variant needs no code change here). The asset list shows at least
`Elevator_Office_BP_C` (confirmed `super=Elevator_ParentBP_C` from the class dump) plus
`Elevator_ORD_BP`/`Elevator_VWinter` in the same folder with the same naming pattern (not
independently confirmed as subclasses, never special-cased either way) and
`BucketElevator_Spline_BP` (deliberately excluded - a spline-based "bucket" actor reads as a
different, conveyor-like actor family with no evidence it is part of `ElevatorMap` at all).
`elevators.lua` sweeps only the parent class, `ctx.findAll("Elevator_ParentBP_C")` - UE4SS
`FindAllOf` returns subclass instances too, the same idiom `main.lua`'s `npcs.list` already relies
on for `NPC_Base_ParentBP_C` (every concrete wildlife/monster class from one query). A short,
explicitly-labelled fallback class list (data, not logic) only runs if that parent sweep itself
comes back empty. Every instance, known class or not, is read through `pcall`
feature-detection (does it expose `ElevatorCurrentMode`? does pressing change it?): an elevator
type this module does not recognize still lists - with its real class name as its label, the
existing convention for these unnamed fixed actors - flagged `controllable: false` instead of
erroring or being dropped, and a set attempt against it is refused by name. `LiveElevator` gained
`Controllable`/`Moving` alongside `TopOpen`; `LiveElevatorsFeatureSession` renders an
uncontrollable entry's `topOpen` as a read-only "not controllable" field instead of an editable
one, and refuses `SetMapFeatureField` on it up front.

Added Lua harness coverage (`tests/cases/elevators.lua`, registered in `tests/cases/manifest.lua`):
list across a parent instance and a `__bases`-declared subclass, a press that starts a move is
accepted before arrival, the toggle-away quirk is never triggered by a same-side request, a
currently-moving elevator refuses a new call, an unpowered one refuses to move, a press with no
confirmed effect is an honest error, **a brand-new subclass this module has never heard of is
still fully listed and settable through the parent sweep alone**, **an elevator lacking the
expected capabilities lists as `controllable: false` and refuses a set attempt by name**, the
fallback class list is exercised directly (world reset, only a bare subclass instance present),
an unresolved id fails cleanly without blocking other rows in the same call, and a non-host client
is refused. **985 checks passed, 0 failed** (`python tools/run-lua-tests.py`, includes every other
area's existing cases plus this round's additions and the concurrently-landed buttons area).

Updated C# tests in `WorldLiveAreaParityContractTests` (interface/scoping, `LiveConnect.razor`
wiring, the `Live_TabElevators` resource key, the Lua manifest registration, and the protocol doc
section) and the `elevators.list`/`elevators.set` wire-shape section in
`docs/reference/live-editing-protocol.md`, both now describing the confirmed mechanics instead of
the original guess. Also added an `"Elevator_ParentBP"`/`"Elevator_Office_BP"` fragment to
`tests/AbioticEditor.Probes/LiveClassPropsProbe.cs` before the correction landed; superseded by
the coordinator's own `tests/AbioticEditor.Probes/ElevatorButtonProbe.cs`, which is the citation
for everything confirmed above. **Not run**: `dotnet build`/`dotnet test` (coordinator builds
centrally after concurrent sessions land) and the live game itself was never launched - **none of
this has been exercised against the running game yet**; the save-direction mapping (inferred by
symmetry, not independently traced) and whether a press's effect is felt by players riding the
platform are the two open questions a real in-game run would settle.

## Round-94: live "Buttons" area added - id/position grounded, state fields best-effort (2026-09-18)

World buttons (offline `buttons` world-map feature, `Core/WorldSaves/Features/ButtonMapFeature.cs`,
the save's `ButtonMap`) had **no live equivalent before this round**: no Lua area module, no C#
live channel/session, and `LiveButtonsFeatureSession`/`LiveButtonsChannel` did not exist -
`LivePortalsFeatureSession`'s own header comment explicitly listed "buttons" among the features
with no live path (`MapFeature` returned null for it). Confirmed by searching `live-agent/` and
`src/AbioticEditor.Core/LiveEditing/World/` before starting: only `portals`/`elevators` had a
`Live*Channel` at all.

Added end to end, following the portals/elevators pattern exactly:
- `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua` (new area module, registered in
  `areas/manifest.lua`) - `buttons.list`/`buttons.set`.
- `Core/LiveEditing/World/LiveButtonsChannel.cs`, `Web.Shared/Models/LiveButtonsFeatureSession.cs`
  (implements the same `IWorldFeaturesSession` boundary `WorldFeaturesTab` already binds to - the
  offline BUTTONS tab now shows real data when connected live, no new UI).
- `LiveConnect.razor` wiring (tab button, render branch, field, connect switch, periodic-refresh
  yield, `AllKnownLiveSessions`/`ResetRegionScopedWorldSessions`/disconnect resets),
  `Live_TabButtons` resource key, `docs/reference/live-editing-protocol.md`'s new section.
- Lua harness cases (`tests/cases/buttons.lua`, registered in `tests/cases/manifest.lua`): **936
  checks passed, 0 failed** via `python tools/run-lua-tests.py` (was 692 as of the last recorded
  run; other areas added checks too this round). C# tests added but **not run** (`dotnet test` was
  out of scope this round - shared `obj/` collision risk with concurrent sessions):
  `TcpLiveGameChannelTests.LiveButtonsChannel_GetAsync_reads_buttons`,
  `LiveSessionPeriodicRefreshContractTests` (added `LiveButtonsFeatureSession` to the tracked
  list), and a new `WorldLiveButtonsAreaTests.cs` (kept separate from the shared
  `WorldLiveAreaParityContractTests.cs` to avoid colliding with concurrent live-area work there).

**What is live-settable and what is not, and why - read this before trusting the BUTTONS tab
live**: `id`/`label`/position are a plain `FindAllOf` sweep across the ~17 concrete button
Blueprint classes found in the installed game's own pak listing this round (`Button_Generic`,
`Button_Keypad[_VOTV[_Terminal]]`, `Button_LightSwitch`, `Button_VOTV_Lightswitch`, `Button_Tram`,
`Button_TramRecall`, `Button_ValveWheel`, `Button_VehicleRecall`, `Button_WeatherEnd`,
`Button_DFWarReactor`, `Button_ORDER`, `Button_SpecialImageButton`,
`Button_Torii_Lantern[_Hanging]`, `CartRecallButton` - scanned directly from
`pakchunk0-Windows.utoc` with a small Python script, since running `LiveClassPropsProbe` via
`dotnet test` to add a "Button" fragment and reflect the real class layout was out of scope this
round) - as grounded as any other actor listing in this mod. The four state fields (enabled,
activated, pressed-once, no-reset) are **not confirmed**: unlike portals (`IsTeleporterActive`)
and elevators (`TopOpen`), no CUE4Parse reflection dump and no installed mod cover any button
class at all, and the save's own leaf names (`ButtonIsEnabled_`, etc.) are hash-suffixed Blueprint
variables this mod's own discipline (see `main.lua`'s `SKILL_XP_FIELD` comment) refuses to guess
blind. `buttons.list` instead pcall-probes a short list of plausible clean (non-suffixed) property
names per field at runtime and reports a value only when one actually resolved on that exact
button; a field with none is simply left off that entry (decodes as `null`, never a guessed
`false`), and the BUTTONS tab renders it as a read-only "not available live" row instead of a
toggle. `buttons.set` mirrors this: it writes whichever candidate resolved and best-effort calls
its matching `OnRep_<name>()`, and fails by name (not silently) when nothing resolved for a
requested field. Every button-related file this round says this plainly in its own header comment
so the next person does not mistake "loaded" for "confirmed".

**Not yet exercised in the running game** - this was all built and harness-tested against the fake
UE4SS stub (`tests/harness.lua`) and read-only pak/usmap inspection, with no dotnet build/test and
no game launch this round (both were out of scope for this task). Whether any of the four state
fields' candidate names actually resolve on a real button, and whether world buttons even remain
loaded/interactive after a live edit, is unknown until someone runs this against the game.

## Round-93: TRADERS tab merged into the shared live/offline component (2026-09-18)

The live TRADERS tab (`LiveTradersTab.razor`) looked nothing like the offline one
(`WorldTradersTab.razor`, wrapping reference cards with a right-hand stock detail) - it was a
flat expand-in-place list with no spoiler concealment, its own compact row markup and its own
CSS (`wt-live-*`). Its header comment claimed the offline layout could not be reused because its
detail pane needs `InventorySelectionService`'s master-detail split pane, which the live page's
"flat single-column page does not have." That premise no longer held: `WorldDoorsTab` and
`WorldBasesTab` already prove the split pane is shared, because the pane is `WorkspaceShell`'s
own `InventorySlotEditor` sidebar, rendered around every page (`MainLayout.razor` wraps `@Body`
in `<WorkspaceShell>`, which renders `<InventorySlotEditor />` next to the page content) - not
something local to the offline world-editor screen.

Merged the same way containers/doors/bases/vehicles/pets/narrative-NPCs already are: a new narrow
`IWorldTradersSession` (`AppliesImmediately`, `IsHost`, `Status`, `HasWorldFlag`, `RefreshAsync`),
implemented by `WorldSaveSession` (staged, or a direct sibling-Facility-file write for a metadata
save - unchanged behavior) and by `LiveTradersSession` (immediate, against the running game).
`WorldTradersTab` now takes `IWorldTradersSession Session` instead of the concrete
`WorldSaveSession`; the trader roster itself stays session-independent (static curated game
data, fetched by the tab the same way either way, per the deleted tab's own correct observation).
The three genuinely different unlock-write mechanics (staged file, direct metadata-save write,
live wire command) stay a type-checked branch in the tab, the same pattern `WorldContainersTab`
already uses for its own live/file divergence - forcing them onto the shared interface would have
meant giving `WorldSaveSession` a dependency on host-level services (`StoryFlagSyncService`,
`RecipeProgressGateService`) it doesn't otherwise have.

Live-only additions folded into the shared tab behind `Session.AppliesImmediately`: a REFRESH
button (re-reads the running game's current flags) and a "not hosting" warning that disables the
unlock actions for a joined client. Spoiler concealment (a trader not yet met stays hidden),
localization, icons/art and the confirm-before-unlock flow are now identical in both modes.
Deleted `LiveTradersTab.razor` and its now-dead `wt-live-*` CSS rules and
`LiveTraders_Title`/`LiveTraders_Intro`/`LiveTraders_Unlock` resource keys (`LiveTraders_NotHostWarning`
is reused, not dead). Updated `WorldLiveAreaParityContractTests` (traders is no longer a
"dedicated" live deviation - only chemistry benches remain one) and `LiveConnect.razor`/
`SaveEditorSurface.razor`'s wiring. Not run: `dotnet build`/`dotnet test` (coordinator builds
centrally after concurrent sessions land).

## Round-92: Characters/Creatures tab review, and one Dead toggle instead of a Revive button (2026-09-18)

Asked to compare the offline "Characters" world tab (`WorldNpcsTab.razor`) against the live
"Creatures" tab (`LiveNpcsTab.razor`) and merge them if they are the same concept. They are not:
`WorldNpcsTab` already reads/writes only the save's `NarrativeNPCMap` (named story characters and
traders, `IsDead`/story-stage state) and is already the ONE shared tab for that data in both
modes (round 77's `IWorldNpcsSession`, backed live by `LiveNarrativeNpcsSession` over
`narrativenpcs.list`/`.set` in `areas/narrative.lua`). `LiveNpcsTab` covers a disjoint, live-only
universe: every `NPC_Base_ParentBP_C` actor currently loaded (wildlife, monsters, robots), via
`main.lua`'s `npcs.list`/`.set` - state the save file never persists at all, so there is no
offline counterpart to merge it with. The code already documents this split in-line
(`LiveConnect.razor`'s own comment next to the `narrativenpcs`/`npcs` tab wiring). Left both tabs
as-is per the task's own fallback rule for genuinely different concepts.

Did apply the Dead-toggle cleanup to both, since each had grown a redundant control:
- `WorldNpcsTab` had a `Dead` checkbox AND a separate REVIVE button that called the exact same
  setter with `false`. Removed the button; the checkbox alone now does both directions.
- `LiveNpcsTab` had a single KILL/REVIVE button (not a checkbox) toggling the same `IsDead`
  field. Replaced it with the same `Dead` checkbox idiom the rest of the app uses (WORLD Pets,
  Characters), for one consistent control across every creature-ish tab. Confirmed end to end
  that unchecking it already invokes the full round-91 `reviveNpc` pipeline, not a bare flag
  flip: `LiveNpcsTab` -> `LiveNpcSession.ApplyAsync` -> `LiveNpcChannel.SetAsync` ->
  `npcs.set` in `main.lua`, whose handler already branches `isDead == false` into `reviveNpc()`
  (controller respawn, un-ragdoll, health reinit, fade-delay disarm) rather than a plain
  `IsDead = false` write - no Lua or C# behavior changed, only the UI.

Removed the now-dead `WorldNpcs_Revive`/`WorldNpcs_ReviveTooltip` and `LiveNpcs_Kill`/
`LiveNpcs_Revive` resource keys from every language file, and reworded `WorldNpcs_NpcsIntro` in
de/es/fr/ru (the English copy never named the button) since those translations called the old
REVIVE button out by name.

## Limitation sweep: item stats, badges, Game Pass profiles, crops, corpses (2026-09-17)

v2.13.0 shipped (paint colours, pet progress). A read-only survey of docs, UI hints, the Lua
agent, unexposed domain fields and GitHub issues (none open) produced the work below, run as
six Sonnet packages on disjoint files and committed one per package.

- Item details gained a Stats block from `ItemTable_Global`: `WeaponData_` (damage, time
  between shots, magazine, ammo), `EquipmentData_` (armour, heat/cold resist, set bonus row),
  `ConsumableData_` (hunger/thirst/fatigue/sanity, buffs), `RepairItem_`, and `SalvageData_`
  resolved through `DT_Salvage`. No research-material column exists in this build. All ten
  bundled registries regenerated (roughly 2 MB to 3-4 MB each).
- Recipe, compendium, journal and fish edits now maintain the game's NEW badge arrays
  (`NewestRecipes_`, `*_Unread_`); `RecipesRequiringResearch_`, `CompletedIntro_` and
  `LastControlRotation_` are modelled and editable. An empty research queue never creates a
  missing tag.
- Game Pass conversion carries `ProfileUnlocks`, `ProfilePlayerStatsSave`,
  `ProfileUserSettings` and `ProfileScientistCustomization_<n>` both ways, resolving the Steam
  account folder above `Worlds`, never overwriting a differing existing file (reported as kept).
- Garden planting spots offer the 24 real crops (the eight `Plant_` ammo cartridges belong to
  the digital plot and are excluded by mesh); a crop change resets growth to Sprout/0.
  Planting an empty spot or clearing one is still unsupported: no fixture shows the empty shape.
- Pet mutation target is a picker: `PetMutation` = 1 + index into the owning `DT_Pets` row's
  `Mutations_` list, verified on both fixture pets (crafted lineages own their own list).
- `DestructibleMap` (`ActorPath_`, `Broken_`) and `CorpseMap` (`ActorPath_`, `IsGibbed_`,
  `IsLooted_`) are new world-map features: unbreak objects, remove corpses. Every observed
  destructible entry is Broken=true, so there is no "break" use case.
- Live world-wide recipe controls now say why they are unavailable (not host, no replication,
  or a UE4SS build without TSet support) instead of vanishing.
- Wall-art rows `painting_a_*` resolved from placed instances in real saves to the landscape
  and square-fancy paintings (`_4`/`_5` never appear anywhere).
- Bug fixed: `IniSection.GetValue`/`SetValue` used the first duplicate key while the game reads
  the last; the CLI `ini set` path was affected (the desktop editor already worked around it).

Verification: full suite 1,392 passed, one Lua-wrapper skip; Lua harness 737 checks passed through lupa; host builds clean. Nothing
live above was exercised in a running game. Still open and needing a game session for
mechanism discovery: live pet species change (FTransform construction) and containment
stability level. Browser-edition cross-region transfer needs an architecture spike.

## Release v2.12.0, live coatings, paint colours, more variants, pet progress (2026-09-16)

Pushed the session's commits. The first Release run failed in the Windows build: the
single-file bundler swallowed the bundled UE4SS zip and manifest into the executable and left
`live-agent/ue4ss` empty. Marked those files and the helper `ExcludeFromSingleFile`, made the
release check require the helper too, and v2.12.0 published with all ten assets (the v2.11.0
tag is the failed attempt and has no release). Note for CI watching: `jq` is not installed on
this PC; use `gh --jq`.

Four Sonnet packages closed documented limits, each grounded in game data or real saves:

- Weapon coatings are editable in live player inventories and containers. A sticky
  per-session flag turns on only once the connected agent has returned complete item metadata,
  so an older agent hides the picker instead of dropping the edit.
- Placed objects can be repainted (13 `EPaintColor` values plus Unpainted). The save keeps
  paint as an `EDynamicProperty::PaintColor` entry in the deployable's `ChangableData_`
  dynamic-property array, confirmed on 75 already-painted objects across the fixtures, and
  46 `Deployed_*_C` classes map to `DT_PaintedDeployables` rows via their compiled defaults.
  Live, `bases.set` writes `PaintedColor`, replays its OnRep, and also upserts the saved entry
  and calls `SaveDeployable()` (the bench_tags both-sides shape). Research note:
  `docs/reference/research/research-deployable-paint.md`.
- The item variant picker gained paintings by frame family (desk photo frames resolved to
  `Painting_Desk`), all eleven TV screens, and the office, cafeteria, bed and cot families, with
  pak and save evidence recorded in the variant research note. `painting_a_*` stays unmapped.
- Carried-pet mutation progress is editable offline and live. The setter rejects negatives but
  does not cap: the largest fixture value (3, two pets) is shown as a hint only, after a review
  caught that clamping on load would rewrite a higher saved value. Peccary and Lamogi followers
  carry no owner link in the installed class data, so that despawn limit is now a confirmed one.

Verification: full suite 1,356 passed, one Lua-wrapper skip; Lua harness 720 checks through
lupa; host builds clean; Release run for the fix commit green end to end. Everything live above
is still awaiting an in-game check, as is whether a live repaint survives a real world save.

## Full live parity landed, bundled UE4SS, artifacts ignored (2026-09-16)

Resumed the interrupted parity session. Committed its uncommitted work: live trait editing
through the installed trait buff row (never replaying trait initialization), live appearance
editing with a profile save, bench upgrade install and removal by writing the bench's own
upgrade tags (the native Has Upgrade/AddUpgrade calls crashed the game in Cascade, even with
real handles), complete item instance metadata (dynamic properties, gameplay tags, table
paths) over new `inventory.setcomplete`/`containers.setcomplete` names, one-request player
to container transfers, processing benches listed with containers, garden/Power Chair/
chemistry care tabs, and live play-time editing. An audit found every layer wired and one
real bug: list-backed record equality made metadata-bearing slots read as dirty after each
live refresh, causing needless re-sends. Fixed with sequence equality plus tests. Care tab
names are now translatable. Protocol reference, live guide and the parity review document
the new commands; all are marked as implemented but awaiting in-game verification.

The Windows release now bundles a pinned UE4SS package (`live-agent/ue4ss/runtime.json`,
exact asset name, size and SHA-256; `tools/fetch-ue4ss.ps1` fetches it in release CI). When
UE4SS is missing, This PC shows a consent screen naming the version and folder, installs
only the runtime plus shared files with an empty mods list, then deploys the agent. Existing
loader traces are never overwritten; builds without the bundle keep the manual guide. UE4SS
(MIT) is listed in the third-party notices. Caveat: the pinned build v3.0.1-1135-gf6d5f942
has not been run against the game; the install every live check used is v3.0.1 Beta, Git SHA
01e0a584, which upstream no longer publishes. The experimental-latest tag is rolling, so the
fetch fails loudly when upstream moves and a maintainer must re-verify and bump the pin.

Housekeeping: `artifacts/` (7 GB of screenshots, scratch builds and save backups) and
`__pycache__/` are ignored; `AGENTS.md` now points at `CLAUDE.md`; `tools/run-lua-tests.py`
runs the Lua harness through the lupa runtime when no Lua executable is installed.

Verification: full suite 1,338 passed, one Lua-wrapper skip; Lua harness 692 checks passed
through lupa; host builds with no warnings. Not done: browser walk-through of the new UE4SS
consent screen (needs a game folder without UE4SS), in-game runs of the new live tools,
multiplayer propagation and save/reload persistence. No game files or saves were changed.

## Cascade live verification (2026-09-15)

With permission to launch Cascade, backed up all 75 save files and verified their SHA256
hashes before testing. Native testing found a timed-out base scan's late reply could be
mistaken for the next command. The mailbox now correlates requests and responses, ignores
stale replies, and rejects an older Lua agent without correlation support. Native regression
coverage exercises timeout, late reply, matching reply, and legacy agent rejection.

The base scan decoded full item metadata just to count occupied slots. Reading only item
names reduced Cascade's 1,861-deployable scan from over five seconds to 229 ms. The next
world-state request returned the correct payload. Recipe removal succeeded but the existing
unlock RPC silently failed to restore it. Hosts now update the authoritative array for both
directions; the removed recipe was restored and verified through the running agent.

World recipe removal/restoration also passed, returning both global recipe sets exactly to
their initial contents. The native buff-handle factory and character buff component resolved
successfully; trait effect mutations were not tested or enabled. Lua regression coverage
passes 563 checks, and the native mailbox regression passes.

Closed the game and test helper, removed the temporary installed development hook, and
restored all 75 Cascade files from artifacts/cascade-live-backup-20260915-073257 with zero
SHA256 mismatches. Full parity remains unfinished. Multiplayer propagation and complete
save/reload coverage are still pending.

## Documentation screenshots (15 September 2026)

Added a 28-image screenshot tour and refreshed the handbook illustrations for player saves, world containers and tools, INI editing, settings, Game Pass conversion, and experimental live setup. Technical references link to the relevant gallery sections; the agent README also shows the local helper step. Maintainer instructions cover future capture updates. Captures use copied saves in the Windows local host, frame out account IDs and personal save paths, and leave the remote token empty. Platform-specific installation screens and connected-game behavior are not represented.

Verification: the documentation site builds and its generated links and image paths pass validation. Browser checks at 1440px and 390px found no broken gallery, live-guide or new-feature images and no page overflow. Click-to-enlarge opened successfully. No game files, saves, UE4SS installation or connection settings were changed for these captures.


Dated implementation and verification notes. Older entries describe the application at that
time; they are not a current feature list or test-count guarantee. The maintained entry points
are `README.md`, `docs/guide/index.md`, and `docs/reference/architecture.md`.

## Full live parity follow-up (2026-09-15)

Full parity is the requested target, still unfinished. Implemented host recipe relocking,
crafted-item discovery, clearing supported GatePal entries, and world recipe editing when the
runtime supports TSet mutation. Added liquid contents, custom text, asset IDs, and visual
variants to player/container slot round trips, including the live liquid-type control. New
rich commands reject older agents instead of silently dropping these fields. Direct writes
notify push-model replication. Container swaps use one request/refresh; unchanged GatePal
polls keep their existing rows.

Full tests: 1,307 passed, one standalone-Lua skip. Ran Lua 5.4 separately: 553 checks passed.
Nine focused .NET tests passed after final coverage additions. Host builds. Native gameplay,
multiplayer propagation, and save/reload verification remain pending a disposable test world.
No existing game saves were changed. Traits, appearance, bench upgrades, full item metadata,
and the other remaining areas are listed in docs/reference/research/live-parity-performance-review.md.
The opt-in LiveParityClassProbe corrects earlier research: InitializeTraits uses trait buffs
but also grants rewards, so incremental trait edits must not replay initialization.

## Review feature implementation (2026-09-15)

Implemented coating fields and an installed-table picker for offline player/container slots;
garden water/fertilizer/growth controls with one-spot-at-a-time details; chemistry flask
summaries and contents links; Power Chair battery controls; pet food/mutation guidance;
conversation-derived character names and a stage picker; and installed-table sandbox
setting discovery. See docs/guide/review-features.md for the scope and remaining runtime
limits. No real game session or deployed Power Chair fixture was available. Original saves
were not written. Browser tests used copied saves at 1440px and 390px, including an INI save.
The 171-check targeted suite passed, followed by seven focused checks after adding chair
coverage and the coating-replacement regression. The host builds without warnings.

## Player/world detail audit and wiki comparison (2026-09-15)

Opened all 12 player tabs, 18 populated Facility tabs and five metadata tabs at desktop
and 390px widths. Selected representative entries, including inventory/equipment slots,
container contents, pet/vehicle/base controls, skill perks, recipes, all four GATEPal
sections, story chapters, traders and containment units. Original saves were not written.

Fixed clipped carried-pet fields, added keyboard selection and accessible labels, and
made selected ARIA states explicit. GATEPal's mobile header and four section buttons now
fit visibly. Detail close buttons stay compact; reference cards use the heading Details.
The appearance panel shows loading feedback before its save is discovered.

Wiki comparison found the pet XP curve was an estimate. Replaced it with documented
thresholds and tested all 20 level boundaries. Existing stored XP remains unchanged.
The feature report at `docs/reference/research/research-ui-and-feature-audit.md` prioritizes
coatings, garden care, chemistry production, pet feeding/mutation progress and deployed
Power Chair state. These larger additions require save-schema and in-game verification.

Verification: 111 focused pet/localization/UI tests passed. Final host build had no warnings
or errors. Browser rechecks confirmed keyboard selection, level 3 at 20 XP, unclipped pet
fields, all four mobile GATEPal sections and compact recipe details. Live game behavior,
Steam achievement writes and appearance-save editing were not tested in this pass.

## Live parity and performance review, first implementation pass (2026-09-15)

Compared the shared tabs, live sessions, channels, and agent handlers. Recorded the remaining
feature gaps and performance candidates in `docs/reference/research/live-parity-performance-review.md`.
Player magazine ammo now reads/writes through the live protocol and follows moved weapons.
The exact field comes from the offline save writer; installed-agent updates and in-game
verification are still required. Lua cases cover validation before batch mutation and clearing.

Fixed recipe Unlock All losing batching through the shared player facade. Added batched GatePal
Mark All with one success notification, no writes for already-known rows, and unchanged local
state on request failure. Recipe batch updates now use indexed lookup. Item and game-art caches
start only one extraction per key under contention; unknown item URLs no longer grow the cache.

Verification: host and tests built successfully. Full suite: 1,292 passed, three AppData sandbox
failures, one missing-Lua skip. All three blocked tests passed in permission-approved reruns;
21 focused live/progression/inventory tests passed. Existing saves and installed game files were
not edited; conversion tests created and cleaned up temporary worlds. No live game was exercised.
Concurrent UI/theme changes were preserved separately.


## GATE Teal app theme (2026-09-15)

Added GATE Teal beside Facility Blue and Hazard Orange in Settings. Its aqua controls,
teal panels, and warning colors follow the documentation's online game references. Dark,
light, and system palettes use the shared app color tokens; the existing default is kept.
The preference uses the existing desktop/browser storage, and the selector wraps on narrow
screens. The player guide lists the new choice.

Verification: host build passed without warnings/errors; 23 focused localization, visual
contract, and shell preference checks passed. Browser verified selecting the theme, light
mode, persistence across a host restart, and returning to Hazard Orange. Settings fits at
390px without horizontal overflow. Documentation checked 1,680 links/images across 43 pages.
Restored the original dark/Hazard preference after verification. No saves were edited.

## World controls and INI editing refinement (2026-09-15)

INI files now use readable setting labels, On/Off controls for literal booleans, a visible
unsaved count, per-setting Undo, and responsive save controls. Technical details reveal raw
keys, paths, and earlier repeated sandbox entries. The default sandbox view shows the latest
value of each key; moderator lists retain every entry and their Add/Remove controls.
Switching configuration files from the sidebar asks before discarding unsaved changes.
The unrelated world save toolbar and inventory details no longer occupy the INI workspace.

World doors and quests use compact, expandable help instead of repeated instructions.
Door and quest names are keyboard-operable selection buttons. Raw quest-event entry is
available on demand, and feature summaries leave more room for the editing controls.

Verification: 50 focused INI, localization, UI parity, and open-guard tests passed. The final
host build passed with no warnings or errors. Browser checks at desktop and 390px covered
world doors, quests, buttons and elevators, INI editing/Undo, save and backup, technical
entries, moderator lists, and cancelling or confirming a configuration-file switch. Save
checks used a copied world under artifacts; original game saves were untouched. A new
regression test verifies repeated-key ordering, comments, unchanged values, and backups.

## Online game-theme reference correction (2026-09-15)

Reviewed the official Abiotic Factor website and publisher-provided Steam inventory
screenshot directly online. Replaced the editor-derived navy/orange treatment with
greener translucent teal panels, aqua primary buttons, rounded outlines, and heavier
headings. Recorded reference links and the distinction between observed visual cues
and chosen CSS approximations in the theme's visual-reference.md.

Verification: build passed with 1,680 local links/images across 43 pages. Inspected the
rendered desktop and mobile homepages; no horizontal overflow at 390px. No application
palette or save data was changed.

## Documentation theme aligned with game inventory (2026-09-15)

Replaced the green terminal palette with teal-blue panels and cyan headings using the
repository's in-game inventory reference and the editor's Facility Blue colors. Orange
primary actions, thin panel outlines, subtle inventory grids, and a restrained teal glow
bring the handbook closer to the game. Updated light mode and consolidated theme styles.

Verification: documentation build checked 1,680 links/images across 43 pages. Inspected
desktop and mobile homepages and the light-mode guide. Homepage and getting-started guide
have no horizontal overflow at 390px. Application code and save files were not changed.

## Player handbook and facility-style documentation (2026-09-15)

Reworked the GitHub README, wiki entry points, and twelve player guides with three Terra
agents. Instructions now lead with player goals, plain-language steps, exact button names,
and clear browser Save/Export handling. Game Pass recovery commands moved to the technical
reference while the player guide leads with desktop repair and help routes.

The Pages homepage now uses facility signage, green terminal colors, hazard-strip details,
an inventory preview, and routes into first edits and item transfers. Dark mode is the default;
the paper-colored light theme remains available. Main navigation favors the player handbook,
with command-line material grouped under experienced users.

Verification: documentation build passed with 1,680 local links/images across 43 pages.
Browser checks covered desktop and 390px homepage/guide layouts, light/dark switching, and
the production search index returning the transfer guide first. No horizontal overflow was
found on the checked mobile pages. No application code or saves were changed for this work.

## Open-world tab refinement (2026-09-15)

Reviewed Cascade in the browser: all 12 player tabs, 18 populated Facility tabs, five
metadata/story tabs, the four GATEPal sections, and six configuration files. Non-inventory
tabs no longer reserve an empty right-hand panel. Item palettes and selected detail cards
still open there. This gives ordinary player/world forms about 390px more room at 1440px.

Player and world sections now have named panels, one keyboard tab stop and arrow/Home/End
navigation. World tabs use a section selector when the editing area is narrow. World overview
and clock controls open on demand. Shared tab cards have more readable labels, spacing and
instructions. Removed duplicate Transmog headings and trait IDs from chips, distinguished
live appearance instructions, labelled vital sliders, corrected the NPC empty prompt, and
made container rows keyboard-operable. Feature tab names no longer force capitals.

All configuration files gain a setting/section filter and no-results feedback. Browser checks
found and fixed narrow-screen overflow in Achievements, Ground items, and long configuration
section headings. Populated player, region and story tabs were swept at 390px; region tabs
were also swept at 1440px. Tested tab keyboard navigation and selecting a container with Enter.
No save values or game files were written. Gameplay/live behavior was not changed or tested.

Verification: full suite passed 1,270 tests with one missing-Lua-executable skip. Final focused
localization, visual-contract, live-inventory UI and shell checks passed 27 tests. Final host
build passed without warnings or errors. New labels use the standard translation fallback.

## Start screen and Settings refinement (2026-09-15)

The empty workspace now hides the unused save sidebar, splitter and selection-status row.
The folder heading stays quiet until a world is loaded. World discovery has a name/platform
search, clear no-results feedback, descriptive Open button labels and quieter world rows.
Long folder paths truncate visually and remain available in their title text. The normal
sidebar and save controls return when a world/player is opened.

Settings tabs now link to their named panel, expose lowercase ARIA selected values, and use
one tab stop with arrow-key, Home and End navigation. Tab moves from the selected section
into its content, and the existing modal focus trap respects negative tab indices.

Verification: host build passed with no warnings/errors; 23 localization, visual-contract
and shell-preference checks passed. Browser verified search/no-results/clear, Settings
keyboard navigation, no horizontal overflow at 390px, and opening a world/player restores
the sidebar and Save/Revert controls. No saves or game files were changed during testing.

## Live item DataTable repair (2026-09-14)

Confirmed that player, container, ground-item and carried-pet writes changed RowName while
retaining the previous DataTable reference. Empty or unrelated tables can leave the item
visible to the editor's row-name reader but unresolved in the game. All four Core channels
now send the catalog table; the Lua agent validates the row and writes both handle fields.
Same-item edits preserve valid existing tables, including mod overrides. Missing tables are
loaded on the game thread before resolving names. Inventory/container batches preflight every
slot and table before mutation. None is accepted as an empty-slot value alongside Empty.

Added wire tests and Lua regressions for all four player inventories, wrong-table repair,
valid overrides, rejected batches, late-loaded names, containers, carried pets and ground
item staging. Inspected installed game Blueprint bytecode with an opt-in research probe:
OnRep_CurrentInventory leads through delayed inventory updates and equipment callbacks.
Kept that existing refresh path. Runtime multiplayer propagation, backpack capacity and
character appearance still require a real-game check; no gameplay or game-file writes were
performed. Direct array writes and network dirty-state handling remain a review follow-up
if symptoms persist for remote players. Existing invisible items require an updated agent
and reapplying the intended item; the guide explains this and the protocol documents the field.

Verification: 1,270 .NET tests passed, one Lua-executable wrapper skipped. The Lua 5.4
harness ran separately through a dev-only Lupa runtime: 498 checks passed. Host compiled
as part of the suite. Both installed-game inspection probes passed. Documentation built
and checked 1,740 links/images across 43 pages. Browser checks of the rendered live guide
at 390px and 1280px confirmed the repair section and no horizontal page overflow.

## Guided separate UE4SS installation (2026-09-14)

Supersedes the automatic runtime download described in the previous entry. At the player's
request, removed the UE4SS downloader and replaced it with a read-only prerequisite check.
UE4SS is not bundled, downloaded, or installed by the app. When missing, live setup shows
the official release link, installation help, exact detected Win64 folder, an Open game folder
button, and Check again. After detection, the editor still offers to deploy its own agent and
start its bundled helper. Both nested and flat standard UE4SS layouts are recognized.
Updated the live guide and README introductions to explain the separate installation and
remove automatic-runtime claims. Existing live feature limits and experimental notices remain.

Verification: host build passed with no warnings or errors; 65 focused detection, host,
localization and visual-contract tests passed. Documentation checked 1,737 links/images across
43 pages. Browser checks at 1280px and 390px confirmed the path, official links, repeat check,
and no horizontal page overflow. Missing-runtime checks left an empty scratch game folder
untouched. Adding inert fixture files let Check again reach the editor-helper consent step.
No real game files were changed and no helper was launched during those checks.

## Simpler editing UI and automatic live setup (2026-09-14)

Shortened the in-app mode comparison and replaced the long live guide panel with a compact
experimental notice and documentation link. Settings uses readable cards, a mobile tab grid,
and keyboard focus containment. Player sections use a selector on narrow screens; skill XP,
perks, and account/background controls open on demand. World story summaries no longer show
irrelevant zero container counts, and tab labels use normal capitalization.

Windows local live setup now downloads a missing official UE4SS runtime after consent, checks
the published digest and archive paths, excludes example mods, and preserves existing mod
installations. It waits through short Windows file locks while activating the verified files.
All bundled agent modules are checked for updates. Setup failures stop with recovery guidance,
and Windows releases now require the native helper build to succeed. Bench upgrades advertise
their actual unsupported status and the UI refuses the unavailable operation.

Expanded the player live-editing guide, README, and agent introduction with automatic setup,
reconnect/update steps, mode comparison, backup behavior, feature limits, remote setup,
troubleshooting, file locations, and disabling the agent. Unverified gameplay operations were
not enabled as part of this UI/setup work.

Verification: full .NET suite passed 1,273 tests with one Lua harness skip (interpreter absent).
After the installer lock fix and UI changes, 73 focused installer, live-base, host, localization,
and visual-contract tests passed. Host builds succeeded with no warnings or errors. The docs
build checked 1,734 local links/images across 43 pages. Browser checks covered all six Settings
tabs, language dialog, local live setup consent, mode choice, player skills/general/inventory,
and world story at desktop and phone widths. Mobile Settings and skills had no page overflow.
A real official UE4SS package downloaded, verified, and extracted into a scratch folder; no
runtime was loaded into the game and no player saves were written during this verification.
Running-game compatibility of the current upstream experimental runtime remains unverified.

## Documentation and GitHub Pages refresh (2026-09-14)

Reviewed the README, player guides, reference entry points, plugin/sample links, and Pages
configuration against the current shared Razor UI, host services, and live-agent protocol.
Replaced the stale session-log summary with a historical-context notice. The README now points
to maintained guides; new directory pages expose all player documentation and technical tracks.
Added item-transfer instructions, a current architecture guide, and the missing plugin sample
catalog. Corrected live setup, immediate live edits, bundled catalogs, browser exports, macOS
CLI availability, localization paths, and links from the plugin samples and wiki. The old Linux
host URL remains as a pointer to the maintained guide; the MAUI audit is labeled historical.

Redesigned Pages with a responsive field-manual homepage, light/dark palettes, an editor preview,
and numbered routes into the guides. Kept standard search, keyboard navigation, and screenshot
zoom. Markdown browser-editor links retain their repository base and full-navigation target in
both rendered HTML and client-side transitions. Removed root image.png and containment-tab.png.

Verification: npm run docs:build passes, including 1,728 local links/images across 43 rendered
pages. The new rendered-link checker also validates anchors and base paths; it caught four old
plugin section links. Browser checks covered homepage and guide layouts at desktop and 390px,
light/dark switching, search results for the transfer guide, and guide navigation. Only the
existing large-bundle advisory remains. The Pages preview serves documentation, not the separately
built WebAssembly app. No application code was changed for this refresh and no push was made.

## Round-80: item visual-variant game-data probe (2026-09-14)

Probed the installed game data to determine which inventory items appear to support instance-level
visual variants, prompted by poster art and differently colored helmets being indistinguishable in
the editor. `DT_TextureVariants` has 575 rows, but it is a flat appearance table with no base-item
or compatibility field. `ItemTable_Global` points each item at no more than one fixed appearance row;
383 variant rows are consumed this way, including most weapon and armor upgrade appearances. Those
are distinct item definitions and must not automatically become skin choices for their base item.

The remaining 192 rows are plausible instance variants. Real Cascade saves confirm 12 non-default
item/variant pairs across colored hats, the Hydroplant Hat, Basic Backpack, office furniture, a
locker-room bench, and rare fish. Strong additional families in the game data include eight Poster
art rows, 26 painting artworks, 24 photo-frame images, Hard Hat/Karate Helmet/Lab Mask color sets,
Puffy Coat colors, arcade cabinets, TV screens, and more furniture variants. Paint is a separate
system: `DT_PaintedDeployables` has 38 placed-object profiles with Default plus 13 paint-color
columns and does not cover helmets or weapons. Full findings and the conservative catalog rule are
in `docs/reference/research/research-item-visual-variants.md`; repeatable pak/save probes are in
`tests/AbioticEditor.Probes/ItemVariantProbeTests.cs`.

## Round-81: item visual-variant catalog and editor (2026-09-14)

Added a game-data-backed catalog for all 575 `DT_TextureVariants` rows and regenerated every
bundled locale registry, so variant names, descriptions, and icons remain available without a
local game install. The normal item picker uses a conservative item-to-row mapping for confirmed
or strongly reviewed families: Poster artwork, colored Hard Hats, Karate Helmets, Hydroplant
Hats, Lab Masks, Puffy Coats, Basic Backpacks, selected hats, arcade cabinets, confirmed office
furniture, and rare fish. Fixed weapon and armor upgrade appearances remain separate item IDs and
are intentionally not presented as skins.

Every non-empty slot also has a collapsed manual variant editor. It can choose any known row or
accept a free-typed row name from a future game update, warns about incompatible rows, and keeps an
unknown row already found in a newer save. Player and world-container writers now construct a
missing `TextureVariantRow_` DataTable row handle when an item receives its first override; clearing
the manual value selects the default without needlessly growing a save. Fixture tests cover the
new player and world write paths, while catalog, registry round-trip, and bundled-data tests cover
offline availability.

## UI review: clearer offline and experimental live editing (2026-09-14)

Reworked the startup chooser into a readable comparison: offline editing is recommended,
changes wait for Save, and every save keeps a backup. Live editing is explicitly experimental,
has fewer tools, applies changes to the running game, and has no automatic backup or undo.
A shared, expandable guide explains persistence, disconnect behavior, loaded-object limits,
host requirements, unsupported traits/counters/bench upgrades/world-wide unlocks, and setup.
It opens expanded before connection and stays available inside the live editor. Offline player
files opened within a live session now get their own accurate staged-save message.

The header has a labeled mode switch. Home explains the workflow, puts technical folder paths
behind help, uses readable text, and trims wasted space. Side panels reserve more room for the
actual editor on laptop screens. Mode dialogs keep keyboard focus, make background controls
inert, and sit above mobile drawer controls. The existing landmark test permits the added inert
attribute. New English copy is resource-backed; other locales use the normal English fallback.

Browser verification used the local Razor host in Chromium at 1440px, 960px and 390px widths:
mode choice, live guide, server form/back navigation, keyboard wrapping, world discovery and
opening a real player save. No save writes or live game changes were made. Screenshots are local
artifacts under artifacts/ui-*.png. The connected live-game branch was build-checked but could
not be exercised without a running game. The final host build passed with zero warnings/errors.
Full net10.0 suite: 1257 passed, 1 Lua-harness skip (interpreter unavailable), 0 failed.
The initial test attempt hit the test host's locked build output; verification used isolated output.

## Round-79: live-editing bug review - a real fatal-crash root cause, two N-round-trip perf bugs, and the Wildlife tab redesigned (2026-09-11)

A player reported four live-editing problems in one pass: the app using 3GB+ of memory
"especially when unlocking all recipes", the ground-items DELETE/DELETE ALL SHOWN buttons not
working, the WILDLIFE tab being misleadingly named with no way to identify or preview an NPC in
it (just a dense control-per-row list), and clicking the BASES tab crashing the game with a fatal
error. All four were investigated and addressed without a live game session (static review of the
Lua/C# live-editing paths plus the offline Lua harness); the crash fix in particular should be
re-verified against a real game before being trusted long-term.

**BASES tab crash - root cause found and fixed.** `bases.list` used to call the bench class's
`"Has Upgrade"` function for every one of the 11 known upgrade rows, for every bench, on every
single list/refresh (including the 2-second background poll) - and that call's row-handle
argument was a hand-fabricated struct round 77 had already flagged as "genuinely unverified
against the running game" (no live enumeration function exists for this table, unlike every other
row-handle this project uses). A native UFunction call through UE4SS's reflection bridge with a
struct that does not match the engine's real parameter shape does not raise a Lua error `pcall`
can catch - it is memory corruption or a bad pointer read on the C++ side, which is exactly what a
"Fatal error" (not a Lua stack trace) looks like, and matches the report being 100% reproducible
on simply opening the tab. Both that probe and `bases.set`'s `AddUpgrade` install path (the same
fabricated handle, just user-triggered instead of automatic) are now disabled: `installedUpgrades`
always reports `[]`, and installing an upgrade live now fails with a clear message instead of
attempting the call. `supportsUpgrades` (a plain, proven boolean property read) is unaffected.
Lua harness (`tests/cases/world_gaps.lua`) updated to assert the refusal instead of a successful
install; 461 checks still green. See `areas/bases.lua`'s header comment and
`docs/reference/live-editing-protocol.md` for the full account.

**Two "one network round trip per item" perf bugs, the likely source of "especially when
unlocking all recipes."** UNLOCK ALL called `SetUnlockedAsync` once per recipe even though
`recipes.set` already accepted a batch of ids - a few hundred sequential round trips through the
file-mailbox/game-thread relay for a fresh character. `IPlayerRecipesSession` gained
`SetUnlockedManyAsync` (default: loop, for the cheap in-memory file session; overridden live to
send one batched `recipes.set`) and the tab now calls it once instead of looping. The ground-items
DELETE ALL SHOWN tab had the identical shape (one `dropped.remove` plus one full `dropped.list`
refresh *per item*, up to 400 round trips for 200 shown items) - `IWorldDroppedItemsSession`
gained the same `RemoveDroppedItemsAsync` batching pattern. Neither fully explains a 3GB reading
on its own from static review alone; if memory still climbs after this, it needs profiling against
a live session, not further guessing.

**Ground-items DELETE not working - likely cause: one bad item aborted the whole batch, silently.**
`dropped.remove`'s per-item `InitDespawn()`/`OnItemDespawn()` calls had no `pcall` around them
(every other risky call in this file does), so one problematic item (already mid-pickup, an odd
blueprint override) raised a Lua error that aborted the whole request - and since DELETE ALL SHOWN
used to send one item per request, that stopped the row loop dead at whichever item failed, with
only an easy-to-miss error banner as the only sign why the rest never got removed. Now pcall-guarded
per item so the rest of the batch keeps going.

**WILDLIFE tab renamed CREATURES and redesigned.** The tab lists everything deriving from
`NPC_Base_ParentBP_C` - hostile human NPCs included, not just animals - so "Wildlife" undersold
what was actually killable in there; the card header said "NPCS" while the tab button said
"WILDLIFE", not even self-consistent. Rows used to cram a KILL/REVIVE button, two checkboxes and a
number field into a `shell-save-list` row borrowed wholesale from the save-file picker's own
markup. Rows are now just a name (`PlainNames.Thing` on the class name - the only "what does this
look like" data the live protocol exposes, since there is no position/health/appearance field
here, see `LiveNpcChannel`'s remarks) and a status badge; clicking one opens a preview - name,
class, status, and the actual edit controls - in the shared right-hand sidebar
(`InventorySlotEditor`), the same "select a row, see/edit it over there" idiom
`WorldDroppedItemsTab`/`PlayerRecipesTab` already use, kept fresh across the periodic live refresh
via `OnParametersSet` re-publishing the open card.

**Result**: 1257 dotnet tests + 461 Lua harness checks green, `AbioticEditor.Web` builds clean.

Prompted by two pieces of player feedback plus a direct request: "no way to discern individual
object variants such as specific posters or differently colored helmets" and "would it be
possible to transfer items from one worldsave to another."

- **Item texture variants (poster art, armor/helmet color) are now readable and, where the game
  already recorded one, editable.** The mechanism is `TextureVariantRow_<hash>`
  (`TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636` in the current game build), a
  `DataTableRowHandle` inside every inventory slot's `ChangeableData_` struct, independent of the
  slot's `ItemId` - confirmed present in real fixtures (both player and world-container saves).
  `InventoryItemSlot.VariantRowName` (new, trailing optional param so every existing positional
  constructor call kept compiling) is read in `PlayerSaveReader.ReadSlot` /
  `WorldSaveReader.Containers.ReadSlot` and patched in `PlayerSaveWriter.ApplySlot` /
  `WorldSaveWriter.Containers.ApplySlot` - patch-only-if-the-tag-already-exists, mirroring the
  established `ItemDataTable_` pattern, because most items never had a variant chosen and the tag
  is delta-serialized away entirely; there is no curated picker yet (a raw row-name text field,
  shown in the slot editor only when the game already recorded one). New test:
  `PlayerSaveReaderTests.ReadInventories_SomeSlotCarriesAVariantRowName`.
- **New tool: Transfer Items (`/transfer-items`)**, reachable from the world containers tab's
  help line ("Move items to a different world save"). Loads two world saves completely
  independently of the main open workspace (any two - two regions of one world, or two entirely
  separate playthroughs) into their own `WorldSaveSession`s, and moves an item between their
  containers (tap a filled slot to hold it, tap an empty slot on the other side to drop it); each
  side saves independently with its own `.bak`. The underlying move,
  `InventoryTransferService.TryMoveContainerToContainer`, works on any two `WorldSaveSession`
  instances regardless of origin file - proven with two independently-loaded sessions built from
  the same fixture in `InventoryTransferServiceCrossWorldTests`. Desktop-only (gated on
  `Workspace.HasLocalPaths`, same as Compare), since picking an arbitrary second file needs a real
  file system.
- **"Sanity" status confusion**: a player asked why the editor shows a "Sanity" stat when the
  wiki's Statuses page doesn't list one. Checked the wiki directly (Statuses and Buffs-and-Debuffs
  pages) rather than assuming - neither mentions Sanity at all, and no player guide or community
  discussion found one either, so there is currently no confirmed public documentation of what it
  does in-game, even though both the save file (`Sanity_<hash>` inside `CurrentSurvivalStats_`)
  and the live game (`CurrentSanity`, confirmed via the live-editing agent) report a value for it.
  Rather than asserting an unverified explanation, the Player > Vitals tab's Sanity slider now
  carries an (i) tooltip saying plainly that this is tracked by the game but not documented
  anywhere yet, in all five UI languages.
- **Chrome's "contains system files" folder-picker refusal** now tells the player what to actually
  do about it: try the save folder itself (not a whole drive or the user-profile root), and if
  that folder is itself inside a protected location like Program Files, copy it out to somewhere
  normal first, since Chrome blocks everything under a protected root regardless of which
  subfolder is picked (`saveFileSystem.js`).

## Round-77 wrap-up: story chapter, compendium, transmog visibility and PhD now settable live; everything proven without launching the game (2026-09-06)

Product-owner direction for this round: "Continue with unverified without launching the game -
and fill the remaining gaps. The 'read-only' for story progression makes no sense to me, and
ensure compendium, fish unlocks etc. is settable." Five parallel workstreams (story, codex +
world unlocks, player gaps, world gaps, Lua harness) were merged into `main`; the two entries
below this one are the ones their authors wrote themselves. This entry records the other three
and the integration result.

**STORY chapter is settable live** (was read-only in round 76). The chapter is a pure function
of world flags: every `Trigger_WorldFlag_C` in the game advances the quest through
`UWorldFlagSubsystem::SetWorldFlag` (verified live in round 75 as `flags.set`), and the native
`FindCurrentQuest` recomputes the replicated `CurrentQuest` row from the flag set. So
`story.set` reuses `applyWorldFlagRows` (the shared body of `flags.set`) with a plan computed
editor-side by `LiveStorySession.ComputeFlagPlan` from `StoryProgressionCatalog` + `FlagGate`
(set the chapter's trigger flag and every prerequisite, clear every dependent when moving
backwards), then nudges `CurrentQuest.RowName` directly so the HUD refreshes without waiting
for the next trigger. The shared `WorldStoryTab` now offers the same chapter picker live that it
offers for a file. Unverified live this round (no game launch, by instruction); the flag write
underneath it is the one write path in this project with the most live evidence.

**PLAYER gaps** (`areas/transmog.lua`, `areas/general.lua`, harness `cases/player_gaps.lua`):
transmog per-slot visibility (the eye toggle) writes through the transmog component's own
`Request_ChangeTransmogVisibilityFlag(Index, bool)` RPC, found in the pak dump of
`Abiotic_TransmogInventoryComp` (round 76 had only looked at the save-file property name).
PhD/background writes `Abiotic_PlayerState_C.PhD` (a plain unsuffixed `FName`, no OnRep).
Traits stay read-only with evidence: the only functions touching the array are
`SetTraits`/`InitializeTraits`, which re-run character creation, and the native
`Server_AddTraitBuff` is a different (buff) system. Items-crafted counters likewise have no
write RPC. The live INVENTORY/TRANSMOG tabs now publish the sidebar ITEM CATALOG context the way
the file editor does, so quick-give works with no slot selected.

**Lua harness** (`live-agent/AbioticEditorLiveAgentLua/tests/`): a stub UE4SS runtime
(`harness.lua`: fake UObjects with `__fields`/call counters, `FString`/`FText` userdata that
must be `:ToString()`'d, json round-trip through the real reply path) runs every area module
without the game. `run.lua` executes `tests/cases/manifest.lua`; the dotnet wrapper
`LiveAgentLuaHarnessTests` runs it when a Lua 5.4 interpreter is available (`ABIOTIC_LUA_EXE`
or on PATH) and skips otherwise. It found three real bugs before any game launch (containment
`TrapLeyak` arg count, a `"Free Leyak"` call missing its receiver, silent missing-id lookups
in doors/portals), all fixed. Integration of the five branches surfaced three stale assertions
(two "honestly unsupported" checks that round 77 made supported, one FText rename comparison)
and two stale dotnet tests (the spawn teleport call shape from before the round-76 live fix, and
the copy guard catching a file name inside an XML doc comment); all updated.

**Result**: 364 harness checks green, 1244 dotnet tests green (net10.0), Web host builds. Still
read-only live, each with the evidence recorded in its module: world-wide recipe unlocks
(`worldunlocks.set`), traits, items-crafted counters, kill-requirement compendium rows, bench
upgrade removal, pet species change/removal, Peccary/Lamogi pets. What only the real game can
prove: enum-as-integer RPC arguments (compendium section type, narrative state byte), whether
`PendingDestroy` alone updates wreck visuals, the reconstructed bench-upgrade table path, and
`Request_DropInventorySlot` landing position.

## Round-77: grounding the compendium unlock enum, and researching world-wide unlocks (2026-09-06)

The product owner's direction: "ensure compendium, fish unlocks etc. is settable live - read-only
is not acceptable where the game itself changes the value; find the game's own write path."

**COMPENDIUM is now settable live.** Round 76 left it read-only because
`Request_UnlockCompendiumSection(CompendiumRow, UnlockType)` takes an enum this project could not
ground. Extended `LiveClassPropsProbe` to also dump `UEnum` package exports (none turned up for any
Compendium-tagged asset - `ECompendiumUnlockType` is a native C++ enum, never its own Blueprint
asset) and `LiveNativeClassPropsProbe` to dump the usmap's own native enum table, which DOES carry
it: `ECompendiumUnlockType` = `Exploration`(0), `Email`(1), `NarrativeNPC`(2), `KilLRequirement`(3,
auto-unlocked by kill tracking, never this RPC), `ECompendiumUnlockType_MAX`(4, sentinel) - matching
the file format's own `DT_Compendium` `UnlockRequirement` values exactly
(`Core/Catalogs/Codex/CodexCatalog.cs`). `codex.set` now accepts
`compendium:[{row,sectionType}]`; the desktop app's `CodexRowEdit.SectionTypes` (already computed
for the file editor) tells `LivePlayerCodexSession` which section type(s) to send per row, so a row
spanning several section types sends one call per type. Also re-grounded the COMPENDIUM read: the
previous round read `Local_AllCompendiumEntries` (a `TSet`, unconfirmed Lua-array readability);
this round found `Compendium_ExplorationSections`/`Compendium_EmailSections`/
`Compendium_NarrativeNPCSections` are plain `TArray<FName>` (same confirmed technique as
EmailsRead/JournalEntries), so `codex.get` now reads those instead.

**FISH/EMAILS/JOURNALS verified still fully wired**: all three were already `editable:true` in
`LivePlayerCodexSession` and unconditionally shown in `PlayerCodexTab` (no per-section hiding) -
only the compendium gap needed closing.

**WORLD-LEVEL unlocks: researched, read is real, no write path found.** `Abiotic_Survival_GameState_C`
carries `GlobalRecipesUnlocked`/`GlobalRecipesResearched` (`FSetProperty`) and
`GlobalItemsPickedUp`/`GlobalEmailsRead`/`GlobalJournalEntries`/`GlobalCompendiumEmail`/
`GlobalCompendiumNarrative`/`GlobalCompendiumExploration` (`FArrayProperty`) - the world-wide
analogues of the per-player arrays codex/recipes already read. New `worldunlocks.get` reads all
eight (grounded); `GlobalRecipesUnlocked` powers `IWorldStorySession.SupportsRecipes` (now true
live) so the shared `WorldStoryTab`'s world-recipes browser shows real data connected to a running
game, not just a file. **`worldunlocks.set` has no grounded write path and always fails** (same
shape as `story.set`): neither `Abiotic_Survival_GameState_C` nor `_GameMode_C`'s exported function
list contains anything touching these fields (the GameMode's many `ApplyWorldSaveData|*`/
`Update*ToWorldSave` pairs are the per-actor world-save round trip, not a matching "GlobalRecipes"
slice), the PDB grep for native `AAbioticGameState` symbols turns up nothing recipe-related, and no
installed mod anywhere writes directly into a `TSet`/`TArray` property (every real write precedent
in this project is a UFunction call or a scalar/struct field assignment). `IWorldStorySession`
grew `GlobalRecipeIds`/`CanEditGlobalRecipes` (the latter always false live) so `WorldStoryTab`
shows the browser read-only live instead of hiding it, mirroring how COMPENDIUM looked before this
round grounded its own write path.

New: `LiveWorldUnlocksChannel` (`Core/LiveEditing/World`), `areas/worldunlocks.lua`, `tests/cases/
codex.lua` + `worldunlocks.lua` (Lua harness, 102 checks total incl. existing), two new
`TcpLiveGameChannelTests` facts. Protocol doc and resx updated. Not verified against the real game
this round (no live session available) - the enum values and property names are grounded in the
game's own data (usmap + pak dumps), not guessed, but the RPC's actual runtime behavior when passed
a plain Lua integer for an enum parameter is unconfirmed until tested live, same honesty caveat
every other `pcall`-wrapped write in this project already carries on first use.
## Round-77: closing five WORLD live-editing gaps (2026-09-06)

Direction: fill the remaining live-editing gaps on the world side, finding the game's own write
path rather than reporting "not available". `LiveClassPropsProbe` fragments extended
(`BenchUpgrade`); re-checked several classes round 76 had already dumped.

**Pets (partial, not the blanket `available:false` round 76 shipped)**: Pest/Skink family pets
directly expose `PetName`/`Guid`/`DynamicProperties` (no hash suffix) and are matched to a stable
id by their own `Guid`. Health is universal, not pet-specific: `AbioticCharacter` (native base of
every player AND NPC) carries `CurrentHealth_Head/Torso/LeftArm/RightArm/LeftLeg/RightLeg` with
one shared `OnRep_CurrentHealth` - the same fields `vitals.set` already writes for the player,
confirmed live. Peccary/Lamogi families still carry none of those properties (re-confirmed, not
assumed) and stay file-only. No live species change or removal (no despawn/respawn precedent for
a living NPC) - `IWorldPetsSession` gained `SupportsSpeciesChange`/`SupportsRemoval` flags
(mirroring `IWorldVehiclesSession.SupportsWreckedState`) so `WorldPetsTab` hides those controls
live instead of throwing silently.

**Vehicle wrecked**: `ABF_Vehicle_ParentBP_C.PendingDestroy` is a real, unsuffixed bool property
(the save's `Destroyed` flag was fed from a function-local variable, but this is a genuine class
member near it) - read/write both go through it, direct field write (no confirmed OnRep).
Unverified against the running game whether flipping it alone updates the wreck visuals.

**Bench upgrades (install only)**: `AbioticDeployed_CraftingBench_ParentBP_C.AddUpgrade(handle)`
and `"Has Upgrade"(handle)` (note the literal space in that function's own compiled name - called
via `bench["Has Upgrade"](bench, handle)`) are real functions. The row-handle's `DataTablePath`
is reconstructed from the pak's own asset path since no enumeration function exists for this
table (unlike weather/flags) - the one part of this feature unverified against the real game.
Removal is refused outright: no `RemoveUpgrade` exists anywhere in the class.

**Dropped items add + container sort**: no give-item precedent exists in the reference mod at
all, so `dropped.add` chains two already-proven mechanisms - `writeSlot` into a free player
inventory slot, then the character's own `Request_DropInventorySlot(Inventory, Index)` RPC (a
real 2-parameter function). The item lands near the player, not at a caller-chosen position.
Container sort calls the inventory component's own zero-parameter `SortInventory()` - the exact
reorder the in-game button performs; wired into a `SORT` button on `WorldContainersTab` (the
`SortContainerSlotsAsync` interface member existed already but no live implementation or file UI
had ever called it).

**Narrative NPCs**: the offline session already had `WorldSaveSession.Npcs`/`SetNpc` (filtered to
`IsPet == false`) with no tab. New shared `IWorldNpcsSession`/`WorldNpcsTab.razor`
(`NarrativeNPC_ParentBP_C.IsCorpse`/`NarrativeState`, real `SetNewNarrativeState(byte)` setter),
rendered in both `SaveEditorSurface` (new NPCS tab) and `LiveConnect` (new NARRATIVENPCS tab,
kept distinct from the existing creature `LiveNpcsTab` - not folded in, given the time budget).
`NarrativeState` travels as a raw enum byte over the wire, not the file's `NewEnumeratorN` string
(no probe carries that enum's names).

Tests: `live-agent/.../tests/cases/world_gaps.lua` (62 checks, all six gaps plus non-host
gating) registered in `tests/cases/manifest.lua`; `TcpLiveGameChannelTests` extended/updated for
the new wire shapes; `WorldLiveTabParityContractTests`'s stale round-76 "no path" assertions
updated to match. Full solution + targeted `World`/`Live` test filters green (one pre-existing,
unrelated failure in `LiveSpawnCompanionsContractTests` predates this round). Not run against
the real game this round - see the commit and each Lua module's own comments for exactly what
remains unverified.

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
not exist as UE4SS Lua globals (plain `{X,Y,Z}`/`{Pitch,Yaw,Roll}` tables now); and the
blueprint `TeleportPlayer` takes five parameters on the current game build (UE4SS refused the
reference mod's four-argument call with "UFunction expected"), so the player teleport now uses
the native `K2_TeleportTo` that already moved the forklift, with the blueprint call as a
fallback - verified: 1.5 m up (gravity settled her at +19 cm) and back to the exact spot.
The forklift moved +1 m and back the same way. Note UE4SS hot reload is off in this install
(`EnableHotReloadSystem = 0`), so every script fix cost a full game restart (four this round).
Full suite 1236/1236 after one copy-guard fix (a code comment said "Blazor").

**Still open**: the shared tabs were exercised live through the raw protocol and a headless
Playwright pass of the page (recipes, flags, containers, base manager, spawn, companions all
rendered real data), not every button; the live door swing, the containment assign/release
sequence (no units were loaded in the test world) and any non-host client run remain untested;
`LiveNpcsTab`/`LiveTradersTab` are the two remaining live-only screens.

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
6. **Live creature REVIVE (round 91, 2026-09-17)**: reviving a killed creature used to leave a
   standing corpse (no AI, no movement, no attacks) because the game's NPC death despawns the
   AI controller, ragdolls the body, zeroes limb health and arms a 500 s corpse-fade destroy
   with no revive of its own. `reviveNpc` in main.lua now spawns a fresh controller
   (`SpawnDefaultController`), un-ragdolls the mesh and returns it to its rest pose, re-runs
   the game's own health initialisation (plus the exact pre-death limb values when the editor
   did the killing) and re-arms the fade delay to never. Evidence: the blueprint bytecode dumps
   from `tests/AbioticEditor.Probes/NpcDeathProbe.cs`; harness-tested (`tests/cases/npc_revive.lua`),
   **not yet exercised in the running game** (needs a game restart to load the Lua). Creature
   pictures: every DT_NPCList class now has a curated wiki file where the wiki has one
   (`NpcRosterProbe.cs` + the wiki API); ~100 classes resolve, the imageless rest are listed
   in `CreatureWikiImages.cs`.

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
