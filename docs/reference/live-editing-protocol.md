# Live-editing wire protocol

The protocol between the desktop editor (`AbioticEditor.Core.LiveEditing.TcpLiveGameChannel`) and
whichever in-game agent is listening (`live-agent/`, outside the .NET solution - see its own
README for the two implementations: the primary Lua-mod-plus-native-helper hybrid, and the
secondary pure-C++-mod). Both speak the identical protocol described here - the client cannot
tell which one it is talking to, by design. One TCP connection, one request in flight at a time,
one line of JSON per message in either direction.

## Framing

Every message is exactly one line (`\n`-terminated) of compact JSON. No length prefix: the JSON
object itself is the unit, and neither side's payloads ever contain a raw embedded newline.

## Request

```json
{"id":"3","cmd":"vitals.get","token":"…","payload":{…}}
```

- `id`: a string the response echoes back. The client assigns it; the agent does not need to
  interpret it, only return it unchanged.
- `cmd`: the command name, `"hello"` for the initial handshake, otherwise `"<area>.<action>"`
  (e.g. `"vitals.get"`, `"vitals.set"`).
- `token`: only present on `"hello"`. Every later request on the same connection relies on that
  connection already being authenticated - the agent tracks this per-connection, not per-request.
- `payload`: present when the command needs one (e.g. `vitals.set`'s new values); absent (or
  `null`) for a command with no input, like `vitals.get`. Usually a flat object, but a flat JSON
  array of such objects is also valid (e.g. `skills.set`'s per-skill rows) - `result` can be
  either shape too.

## Response

```json
{"id":"3","ok":true,"result":{…}}
{"id":"3","ok":false,"error":"bad token"}
```

- `id` matches the request it answers.
- `ok:true` responses carry `result` (absent/`null` for a command with no return value, like
  `vitals.set`).
- `ok:false` responses carry `error`, a short player-safe message (never a stack trace or
  internal detail - it may reach the editor's UI directly).

A transport-level failure (the agent closes the connection, a malformed line) is NOT an
`ok:false` response - the client reads it as a connection failure (an exception from the read),
distinct from the agent explicitly rejecting a well-formed request.

## `hello`

The first message on every connection. Request payload: `{"token":"…"}`. Response result:
`{"protocolVersion":1,"agentVersion":"…"}`. The client checks `protocolVersion` matches what it
speaks (currently `1`) and treats a mismatch as a hard failure rather than guessing at
compatibility.

## `vitals.get` / `vitals.set`

The Phase-0 slice (see `docs/PROGRESS.md`). `vitals.get` takes no payload and returns a flat
object with all twelve fields below. `vitals.set` takes the same shape as its payload and returns
no result.

| Field | Type | Matches |
|---|---|---|
| `hunger`, `thirst`, `sanity`, `fatigue`, `continence` | number | `CharacterStats` (`Core/Domain/Player/CharacterStats.cs`) |
| `money` | number | `CharacterStats.Money` |
| `head`, `torso`, `leftArm`, `rightArm`, `leftLeg`, `rightLeg` | number | `LimbHealth` (`Core/Domain/Player/LimbHealth.cs`) |

Deliberately flat (not nested `stats`/`health` objects) so the C++ side only has to populate one
struct from the live PlayerState's properties, mirroring how `LivePlayerVitalsChannel` on the
.NET side flattens the same two domain records for the wire.

## `skills.get` / `skills.set`

`skills.get` takes no payload and returns a flat JSON **array**, one object per skill, ordered by
index. `skills.set` takes the same array shape as its payload (any subset of skills, matched by
`index`; skills it does not mention are left untouched) and returns no result.

| Field | Type | Matches |
|---|---|---|
| `index` | number | `PlayerSkill.Index` (`Core/Domain/Player/PlayerSkill.cs`) - position in the save's `Skills_` array, not a name |
| `xp` | number | `PlayerSkill.Xp` |
| `xpMultiplier` | number | `PlayerSkill.XpMultiplier` |

```json
[{"index":0,"xp":100,"xpMultiplier":1},{"index":1,"xp":200,"xpMultiplier":1.5}]
```

## `players.list`, `npcs.list` / `npcs.set`, `inventory.list` / `inventory.set`

The player directory, NPC and player-inventory areas share the envelope above. `players.list`
returns `{"players":[{"id","name","isLocal"}],"isHost":bool}`. Every player-scoped command
(`vitals.*`, `skills.*`, `inventory.*`) accepts an optional `playerId` in its payload to target a
different connected player; omitted means the local player. `npcs.list` returns
`{"npcs":[{"id","label","isDead","isDisabled","invincible","faction"}],"isHost":bool}` and
`npcs.set` takes `{"npcs":[{"id", ...any of those fields...}]}`. `inventory.list` returns a flat
array of `{"kind":"backpack"|"equip"|"hotbar"|"transmog","slotIndex","itemId","isEmpty","stack",
"durability","maxDurability","ammoInMagazine"}` and `inventory.set` takes `{"edits":[{"kind","slotIndex",
"clear"?,"itemId"?,"stack"?,"durability"?,"maxDurability"?,"ammoInMagazine"?}],"playerId"?}`. `transmog` reads the
same `Abiotic_InventoryComponent_C` slot struct as the other three kinds, over the player's
`TmogInventory` component - the web editor's `LiveInventorySession` sends this kind for a
transmog slot exactly like backpack/equip/hotbar, so no separate command pair exists for it.

Magazine ammo uses the same exact `CurrentAmmoInMagazine_` field as the save writer.
It accepts non-negative 32-bit integers, preserves the field when omitted, and resets it
when clearing a slot. Invalid ammo rejects the batch before mutations. Update the Lua agent
to use this field; older agents omit it and ignore ammo edits. The new write path is covered
by protocol/session tests but still needs verification inside a running game.

An `id` in any world area is the game's own full object name for that exact actor
(`GetFullName()`), re-resolved by a fresh scan on every write: the loaded set of doors, crates,
NPCs and loose items changes constantly, so an index from an earlier list is never trusted.

## `transmog.get` / `transmog.set` - armor-visibility toggles (round 77)

Previously reported as having "no confirmed live property", the six per-slot "hide this armor
piece" eye toggles (`PlayerTransmogTab`'s `TransmogVisibility`) turned out to have a real,
grounded write path once the transmog inventory component's own class layout was checked (not
just the save file's property name): `Abiotic_TransmogInventoryComp_C` (the exact class
`inventory.list`/`.set` already reads/writes for the `transmog` kind, over the player's
`TmogInventory`) declares `Request_ChangeTransmogVisibilityFlag(Index, Item)` as a genuine
client -> server RPC. `transmog.get` takes an optional `{"playerId":"…"}` payload and returns
`{"visibility":[{"index","isVisible"}, ...]}` for the six visual gear roles only (CHEST/HEAD/
LEGS/BACK/ARMS/SUIT - the same subset `PlayerTransmogTab` shows; the remaining stored flags
round-trip untouched). `transmog.set` takes `{"playerId"?, "visibility":[{"index","isVisible"}]}`
and applies each flag via that RPC immediately; an index outside 0-5 is silently ignored rather
than written. Not host-gated, the same "player-owned data" reasoning `inventory.set` already
uses: this component belongs to a specific player's own pawn.

**Bug fix (reported live): the equipment tab didn't reflect a change until the player toggled the
transmog button themselves.** Calling `Request_ChangeTransmogVisibilityFlag` writes the array on
this process's own server-side copy of the component, but a server never receives its own
property's `OnRep` callback the way a remote client does - only the in-game button's own trigger
of that same callback was ever repainting the UI. `transmog.set` now also calls
`OnRep_TransmogVisibility()` itself after every write (the same real function this file's own
class-layout dump already found, just never invoked), forcing the repaint immediately instead of
waiting for the player to press the button.

## `world.get` / `world.set` - clock and weather

`world.get` takes no payload and returns:

| Field | Type | Meaning |
|---|---|---|
| `day` | number | In-game day counter |
| `timeSeconds` | number | Seconds into the current day (0..86400), the world save's `TimeOfDay` unit |
| `isNight`, `paused` | bool | Day/night flag; whether the clock is manually paused |
| `currentWeather` | string | Active weather event row (`None` when clear) |
| `weatherOptions` | string[] | Every weather row the game knows, `None` first |
| `isHost` | bool | Whether this process can change any of it |
| `minutesPassed` | number? | Total world play time in minutes (2026-09-16, see `world.setPlaytime` below) |
| `canSetMinutesPassed` | bool | Whether this process can change `minutesPassed` |

`world.set` takes any subset of `{"timeSeconds","day","weather","nextWeather"}`. `weather`
triggers that event immediately (`None` ends the current one); `nextWeather` queues it for the
next in-game day. Host only.

### `world.setPlaytime` - total world play time (2026-09-16)

**Implemented, awaiting in-game verification.** The counterpart to the file editor's world
playtime field (`WorldSave_MetaData.sav`'s `MinutesPassed`, which `DayNightCycle`/
`GameMode.ApplyWorldSaveData` load into `AbioticGameState.SavedElapsedMinutes`, and
`SetTimeOfDayOnWorldSave` persists back from `GetElapsedMinutes()`). `world.get` wraps its
existing handler (`ctx.worldGetWithoutPlaytime`) to add `minutesPassed`/`canSetMinutesPassed`
without disturbing the rest of the response. `world.setPlaytime` takes
`{"minutesPassed"}` (a nonnegative whole number of minutes), host only: it computes the offset
needed so `GetElapsedMinutes()` reports the requested total, writes it to
`SavedElapsedMinutes`, marks the property for replication, and flushes net dormancy - the
running session clock itself keeps advancing normally from that new base. The write is verified
by an immediate readback; a mismatch (or an offset outside the 32-bit range the field can hold)
is reported as an error rather than silently applied.

## `world.info` - current region (round 78)

`world.info` takes no payload and returns `{"levelToken":string?,"isHost":bool}`. `levelToken` is
the local controller's own `ActiveLevelName` - the exact same evidenced read `spawn.get` already
uses for its own `levelName` field (a display-only streaming level name, e.g. `Facility_MFWest`;
NOT the file's `RespawnLevelGuid`), reused here rather than introducing a new, unverified
`UEHelpers.GetWorld():GetMapName()` call. `levelToken` is absent when there is no local controller
yet (main menu, or between loading screens). This is the live counterpart of picking a
`WorldSave_<Region>.sav` file offline: the desktop app's live sidebar matches this token against
`"WorldSave_" + levelToken + ".sav"` in the current world's own folder (see
`Core/WorldSaves/LiveWorldFolderLocator.cs` for how that folder is found from a connected player's
id) to show every region of this world, enabled only for the one actually loaded right now, and
runs it through `Core/WorldSaves/WorldAreaCatalog.cs` for a friendly display name. Reading this
needs no authority (unlike every other `.set` command in this file) - it is not a write, so a
joined client reads its own accurate value too.

## `flags.list` / `flags.set` - quest and story flags

`flags.list` returns `{"flags":[{"name","isSet"}],"isHost":bool}`: every world-flag row the game
knows (the same names as the world save's `WorldFlags` array and `QuestFlagCatalog`), plus any
set flag the table does not list. `flags.set` takes `{"flags":[{"name","isSet"}]}` and applies
them in order through the game's own world-flag subsystem, so dependent doors, effects and
triggers react exactly as if the flag had been earned in play. Host only.

## `story.get` / `story.set` - main-quest indicator and setter

`story.get` takes no payload and returns `{"currentQuestRow":string,"isHost":bool}`.
`currentQuestRow` is the running game's current-quest row name (`"None"` when it reports no
active quest), read from the replicated `CurrentQuest` field on `Abiotic_Survival_GameState_C`
(confirmed by `tests/AbioticEditor.Probes/LiveClassPropsProbe.cs` dumping
`AbioticFactor/Content/Blueprints/Meta/Abiotic_Survival_GameState.uasset`: a `CurrentQuest`
`FStructProperty` plus an `OnRep_CurrentQuest` client notify). The Razor host feeds this row into
the same `StoryProgressionCatalog` lookup the file editor's chapter checklist uses; a row the
catalog does not recognise renders as "unknown chapter", the existing graceful fallback for an
unfamiliar save value.

**The story chapter is a function of world flags, so it is settable live the same way the file
editor's chapter SET action moves it on disk.** `StoryProgressionCatalog` maps every chapter to
its `TriggerFlag`; `FlagGate` knows the linear prerequisite/dependent closure; and `flags.set`
above (`UWorldFlagSubsystem::SetWorldFlag`, verified against the real game in round 75) is the
game's own mechanism for moving the story - every `Trigger_WorldFlag_C` in the game advances the
quest exactly this way, the native `bool UWorldFlagSubsystem::FindCurrentQuest(FQuestRowHandle&)`
recomputes `CurrentQuest` from the flag set, and `OnRep_CurrentQuest` pushes the change to
clients. `story.set` takes
`{"currentQuestRow":string,"flagsToSet":[string],"flagsToClear":[string]}`: the Razor host's
`LiveStorySession` (which already has the catalogs) computes `flagsToSet` (every chapter trigger
flag from the start of the story through the target, plus the curated
`FlagGate.PrerequisitesFor` closure, excluding anything already set - the same computation as the
file editor's "unlock story through here" action) and `flagsToClear` (for a backward move: every
chapter/quest flag that belongs strictly after the target and is currently set, via
`FlagGate.DependentsOf` + `FlagGate.FlagsPastChapter` - mirroring `StoryFlagSync.PlanClearForwardFlags`)
from the running world's own current flag set (`flags.list`). The mod applies both lists through
the same `applyWorldFlagRows` helper `flags.set` uses (factored out of it for this reuse), then
writes `gameState.CurrentQuest.RowName` directly and calls `OnRep_CurrentQuest()` as a
belt-and-braces nudge - both wrapped in `pcall` since no installed mod writes that struct member
directly; the flags are the real, game-native write, and the game will recompute `CurrentQuest`
from them on its own regardless. Host only - the same authority every other live world write
needs (`CanSetStoryChapter` on `LiveStorySession` mirrors `IsHost`).

The world clock and weather that used to have their own `LiveWorldTab` now render inside the same
shared story tab (`WorldStoryTab`, bound to `IWorldStorySession`) - see `world.get`/`world.set`
above; nothing changed in that wire shape, only which Razor component renders it.

## `doors.list` / `doors.set`

`doors.list` returns `{"doors":[...],"isHost":bool}` with, per loaded door: `id`, `label` (class
name), `kind` (`simple` for hinged doors, `security` for sliding security doors), `state` (the
`E_DoorStates` number the file editor's `DoorStateNames` maps: 0 closed, 1 open, 2 locked, ...),
`isOpen`, `oneWayUnlocked`, `disabled`, and world position `x`/`y`/`z` in centimetres.
`doors.set` takes `{"doors":[{"id","kind","state"?,"isOpen"?,"oneWayUnlocked"?,"disabled"?}]}`
- `state` applies to hinged doors, `isOpen` to security doors. Host only. Every row whose `id`
still resolves is applied even if another row in the same call does not; the reply only turns
into an error (naming the first id that could not be found - unloaded, destroyed, or mistyped)
once every row has had its chance.

## `containers.list` / `containers.set`

`containers.list` returns `{"containers":[{"id","label","x","y","z","slots":[...]}],"isHost":bool}`
where each slot is `{"slotIndex","itemId","isEmpty","stack","durability","maxDurability"}` - the
same slot shape as `inventory.list`, because a container's storage is the same inventory
component class as a player's backpack. `containers.set` takes `{"id","edits":[{"slotIndex",
"clear"?,"itemId"?,"stack"?,"durability"?,"maxDurability"?}],"sort"?:bool}`. `sort:true` (round
77) reorders the container's slots via the inventory component's own zero-parameter
`SortInventory()` function - the same reorder the in-game "sort" button performs; not exercised
by any mod before round 77. Host only.

## `dropped.list` / `dropped.remove` / `dropped.add`

`dropped.list` returns `{"items":[{"id","itemId","stack","x","y","z"}],"isHost":bool}` for every
item lying loose in the loaded world that nobody has picked up. `dropped.remove` takes
`{"ids":[...]}` and returns `{"removed":n}` - the count actually found and despawned. Host only.

`dropped.add` (round 77) takes `{"itemId","stack"?,"durability"?,"maxDurability"?,"playerId"?}`
and spawns a brand-new item on the ground. There is no `SpawnDroppedItem`/"give item" precedent
anywhere in the reference mod (checked: no additem/spawnitem/give-style command exists in it at
all), so this chains two already-proven mechanisms instead of constructing a dropped-item actor
from scratch: it writes the item into a free slot of the target player's own inventory (the same
`writeSlot` `inventory.set` already uses live), then calls the player's own
`Request_DropInventorySlot(Inventory, Index)` RPC - a real function confirmed from the game's own
class layout with exactly the two simple parameters (an object reference and an int) this module
calls it with. Not exercised by any mod, so genuinely unproven end-to-end; the item lands
wherever the game's own `FindBestItemDropLocation` puts it (near the player), not at a
caller-chosen position - unlike the file editor's own explicit-`x`/`y`/`z` add. Host only.

## `bases.list` / `bases.set` - deployables (round 76, bench upgrades round 77, upgrade removal 2026-09-16)

`bases.list` returns `{"deployables":[{"id","className","x","y","z","customName","hasInventory",
"storedItemCount","supportsUpgrades","canEditUpgrades","installedUpgrades":[...]}],"isHost":bool,
"supportsBenchUpgrades":bool,"supportsBenchUpgradeRemoval":bool}` for every deployable currently
loaded (`AbioticDeployed_ParentBP` and every subclass - benches, furniture, defenses,
containers). `supportsUpgrades`/`canEditUpgrades`/`installedUpgrades` are meaningful only for
benches; every other deployable reports `false`/`false`/`[]`. `bases.set` takes `{"id",
"customName"?,"upgradeRow"?,"upgradeInstalled"?}` and renames the object and/or installs or
removes a bench upgrade immediately. Host only, like `containers.set`/`doors.set`.

**Bench-upgrade editing no longer calls the native `AddUpgrade`/`"Has Upgrade"` functions at
all** (implemented, awaiting in-game verification). Round 77 grounded installation in those two
real functions, but the row-handle struct fed to them had to be reconstructed by hand (no live
enumeration function exists for `DT_BenchUpgrades`), and a player reported the BASES tab crashing
the game with a fatal error every time it was opened - `bases.list` used to call `"Has Upgrade"`
with that fabricated handle for every known row, for every bench, on every list/refresh, and an
unconditional native reflection call with a struct whose shape does not match the engine's real
parameter type does not raise a catchable Lua error, it crashes the process. `Scripts/bench_tags.lua`
replaces both calls: a bench upgrade is really just one `BenchUpgrade.<Row>` `GameplayTag` on the
bench's own `UpgradeTagContainer` (plus its saved mirror in `ChangeableData`'s tags struct), so
installing or removing one now means writing that tag container directly - append the tag to
install, filter it out to remove - then marking both properties for replication, flushing net
dormancy, calling `OnRep_UpgradeTagContainer()`, and calling the bench's own `SaveDeployable()`.
See `Scripts/areas/bases.lua`'s header comment: "Never call Has Upgrade/AddUpgrade with Lua-built
row handles: both fabricated and real-enumerated-handle copies crashed the native bridge during
Cascade verification." `installedUpgrades` is read back the same direct way, from the tag
container's `BenchUpgrade.<Row>` entries, instead of probing with `"Has Upgrade"`.

Availability is reported per deployable (`canEditUpgrades`, true only when the bench supports
upgrades AND its tag containers are readable/replication is available) and overall
(`supportsBenchUpgrades`/`supportsBenchUpgradeRemoval` on the directory, both true exactly when at
least one loaded bench reports `canEditUpgrades`) so an older client or runtime degrades to
read-only instead of guessing. `upgradeRow` must be one of the 11 known `DT_BenchUpgrades` rows
(mirrored in Lua as `BENCH_UPGRADE_ROWS`, row-for-row the same as
`AbioticEditor.Core.WorldSaves.BenchUpgradeCatalog.All`); `upgradeInstalled` defaults to `true`
when omitted, so passing `false` removes it.

Opening a bench or crate's contents inline (the file editor's slot grid) is still file-only - it
shares the CONTAINERS tab's staged slot model; use the CONTAINERS tab for live slot editing.

## `vehicles.list` / `vehicles.set` - round 76, wrecked state round 77

`vehicles.list` returns `{"vehicles":[{"id","vehicleId","vehicleClass","driveable","wrecked",
"x","y","z"}],"isHost":bool,"supportsWreckedState":true}` for every vehicle currently loaded
(`ABF_Vehicle_ParentBP` and its subclasses). `vehicles.set` takes `{"id","driveable"?,"wrecked"?,
"x"?,"y"?,"z"?}` - `driveable` is a direct property write (`VehicleDriveable` +
`OnRep_VehicleDriveable`, confirmed on the live class layout); a position takes effect via
`K2_TeleportTo` (confirmed real, used the same way in
`CheatConsoleCommands/AFUtils/BaseUtils/BaseUtils.lua`'s `TeleportActorToActor`), keeping the
vehicle's current rotation. Host only.

`wrecked` (round 77) reads/writes the vehicle's own `PendingDestroy` property - a real,
unsuffixed class member confirmed from the game's own class layout (the save's `Destroyed` flag
is fed from a local variable inside the vehicle's own `UpdateWorldSave` function, and
`PendingDestroy` is the only real class member anywhere near it). There is no confirmed
`OnRep_PendingDestroy`/`OnRep_Destroyed`, so this is a direct field write like `bases.set`'s
rename. **Genuinely unverified against the running game**: no mod anywhere reads or writes this
field, and whether flipping it alone updates the vehicle's wreck visuals live (versus only the
value the save later persists) is unknown without launching the game.
On-board vehicle storage is still not exposed here (`hasInventory`/`inventoryItemCount` are
always `false`/`0` for a live vehicle) - it is a different inventory component than the world
containers this protocol's `containers.*` commands already cover.

## `pets.list` / `pets.set` / `pets.remove` - round 76 (no path), partially closed round 77, removal added round 78

`pets.list` returns `{"pets":[{"id","npcClass","isDead","customName","x","y","z","limbHealth":
{...},"xp"}],"isHost":bool,"available":true,"supportsSpeciesChange":false,
"supportsRemoval":true,"reason":"..."}`. Round 76 found no general live path for tamed pets: the
fields a world save's `PetNPC` record needs are exposed wildly inconsistently between creature
families. Round 77 re-checked the game's own class layout and found a real, **partial** path
instead of guessing a universal one:

- The Pest family (and Skink, which inherits from it) directly exposes, with no hash suffix:
  `PetName` (`FTextProperty`, real `OnRep_PetName`), `Guid` (`FStrProperty` - a stable id matching
  the save's own `PetNPC` key), `DynamicProperties` (the same `{Key,Value}` shape
  `companions.list`'s carried-pet XP already reads/writes), and `FollowingOwner`
  (`FObjectProperty`, a reference to the player it is currently following - see `companions.set`
  below for what this unlocked). `pets.list` only lists actors of this family, matched by `id` =
  their own `Guid` string.
- Per-limb health is **universal**, not pet-specific: `AbioticCharacter` (the native base of
  every player AND every NPC) carries `CurrentHealth_Head/Torso/LeftArm/RightArm/LeftLeg/
  RightLeg` as plain unsuffixed floats with one shared `OnRep_CurrentHealth` - the exact fields
  `vitals.set` already writes for the local player, confirmed live. `pets.set` writes these the
  same way.
- Peccary and Lamogi family pets were re-checked and confirmed to still carry none of
  `Guid`/`PetName`/`DynamicProperties`/`FollowingOwner` as their own properties - there is still no
  stable id for them, so they are never listed; `reason` says so. `supportsSpeciesChange` is always
  `false` - no confirmed despawn/respawn round trip exists for a living NPC with a species change
  in mind.

`pets.set` takes `{"id","isDead"?,"customName"?,"limbHealth"?,"xp"?}` - `npcClass` is never
accepted (no live species change). Host only. It replies `{"warnings":[...]}` rather than failing
outright when one field could not be applied - see the round-78 bug fix below.

`pets.remove` takes `{"id"}` and destroys the pet's actor outright
(`npc:K2_DestroyActor()` - the same standard `AActor` call the reference CheatConsoleCommands
mod's own "deleteobject" console command already uses on an arbitrary world actor). No blueprint
function cleanly "releases" a tamed world pet back into the wild (checked `CreatePetItem`/
`ReleaseFromAIDirector`/`IsFollower` on `NPC_Base_ParentBP_C` - none of them detach-and-vanish an
already-world-placed NPC), so this is the closest evidenced removal there is. Host only, and there
is no undo once it returns.

**Round-78 bug fix (reported live: "pet health and level editing doesn't seem to work").** The
root cause was not that the writes themselves failed live - it was that a combined `pets.set` call
(every field sent together, since the shared `WorldPetsTab` always sends the whole row) used to
raise a hard error the moment ANY one field looked unwritable, most commonly `xp`: a pet that has
never earned real XP yet has no `XP` entry in `DynamicProperties` at all (the identical
delta-omission the save file itself uses, which is why `WorldSaveWriter.ApplyDynamicInt`'s
file-format counterpart had to learn to append a missing entry by cloning an existing one's tag
types - there is still no live-reflection equivalent of that trick, and per `worldunlocks.set`'s
own "no working precedent" refusal, guessing a `TArray`-append technique over UE4SS Lua stays
refused project-wide, so a never-levelled pet's level genuinely still can't be raised live). Because
Lua's `error()` aborts the whole handler, that ONE unrelated field threw away a health/name edit
that had already been written into the live NPC's memory earlier in the same call - the editor saw
the whole request as failed and never refreshed, so it looked like health editing was broken too,
even on requests that never touched level. Fixed by applying every field independently and
returning non-fatal warnings instead of aborting: a request that only changes health can now only
fail for a genuine health-write problem, never because of an unrelated XP echo-back, and the tab
always refreshes to show what actually applied.

## `narrativenpcs.list` / `narrativenpcs.set` - story NPCs and traders (round 77)

`narrativenpcs.list` returns `{"npcs":[{"id","label","isCorpse","narrativeState","x","y","z"}],
"isHost":bool}` for every `NarrativeNPC_ParentBP_C` (and subclass, e.g.
`NarrativeNPC_Human_ParentBP_C`) currently loaded. `IsCorpse`/`NarrativeState` are real,
unsuffixed class members confirmed from the game's own class layout.
`narrativenpcs.set` takes `{"npcs":[{"id","isCorpse"?,"narrativeState"?}]}`. `isCorpse` is a
direct field write (no confirmed `OnRep_IsCorpse` anywhere in this class). `narrativeState` calls
the class's own real one-parameter setter, `SetNewNarrativeState(NarrativeState: byte)` (falls
back to a direct field write if that call fails on a given build) - preferred over a bare write
since it also updates `LastPlayedNarrativeState` and broadcasts `OnNarrativeStateChanged`
internally. `narrativeState` is the enum's own raw integer value, not the file format's own
`E_NarrativeNPCStates::NewEnumeratorN` string encoding - no probe dump anywhere carries that
enum's value names, so this protocol does not attempt to decode/re-encode it; a caller wanting
the file's string form keeps its own mapping. This is the live counterpart to the offline
session's `Npcs`/`SetNpc` (which had no dedicated tab before round 77's shared `WorldNpcsTab`).
Tamed pets are `pets.list`/`pets.set` above, not this command - matching the shared editor's split
between `WorldNpcsTab` (narrative) and `WorldPetsTab` (pets). Host only.

## `containment.list` / `containment.set` - Leyak Containment Units

`containment.list` returns `{"units":[{"id","x","y","z","stability","creature"}],"isHost":bool}`
for every deployed `Deployed_LeyakContainment_C` unit: `stability` is the unit's own 0..100 gauge
(null when it could not be read), `creature` is `"Leyak"`, `"Krasue"`, or `null` when the unit is
empty. `containment.set` takes one action per call:

| `payload.action` | Other fields | Effect |
|---|---|---|
| `"assign"` | `unitId`, `creature` | Traps `creature` into `unitId`, freeing it from any other unit and evicting whoever `unitId` already held |
| `"release"` | `creature` | Frees `creature` from whichever unit currently holds it |
| `"swap"` | `unitIdA`, `unitIdB` | Exchanges the two units' occupants in one step |

Host only. The write path is the reference mod's own trap/free commands
(`AFUtils.TrapLeyak`/`FreeLeyak`/`TrapKrasue`/`FreeKrasue`), so a unit fed to full stability the
same way those commands already do live.

## `traders.list` / `traders.unlock` - trader availability

No mod anywhere touches a trader UObject directly - the barter UI is pure data-table driven -
but trader/stock gating is a set of quest/story world flags, the same ones `flags.list`/
`flags.set` already drive. `traders.list` returns `{"setFlags":[...],"isHost":bool}`: every
quest/story flag currently set (a subset of `flags.list`'s full roster, filtered to just the set
ones, since that is all trader gating needs). `traders.unlock` takes `{"flags":[...]}` and sets
every named flag through the same `UWorldFlagSubsystem` `flags.set` uses. Host only. The trader
roster itself (names, sells/accepts, which flags gate what) is static game data
(`Core/Catalogs/Codex/TraderCatalog.cs`) and needs no live read.

## `portals.list` / `portals.set` - world teleporters ("World Teleporters" pads)

The live twin of the `portals` world-map feature (`Core/WorldSaves/Features/PortalMapFeature.cs`,
the save's `PortalMap`). `portals.list` returns
`{"portals":[{"id","label","active","teleporterId","destinationId","x","y","z"}],"isHost":bool}`
for every loaded `BP_Teleporter_ParentBP_C`; `teleporterId`/`destinationId` are the pad's own
level-baked linking ids (read-only). `portals.set` takes
`{"portals":[{"id","active"?}]}` and flips whether a pad is active/unlocked. Host only. No
installed mod exercises this actor class; this is the first live write to it. Same partial-apply
behavior as `doors.set`: a row with an unresolved `id` does not block the others in the same
call, but the overall reply becomes an error naming it.

## `care.list` / `care.set` - deployed-object care: gardens, Power Chairs, chemistry benches (2026-09-16)

**Implemented, awaiting in-game verification.** The live counterpart of watering/fertilizing a
garden plot, charging a Power Chair, and reading a chemistry bench's flask contents. Unlike most
world areas this one covers three unrelated deployable classes behind one `featureId`:
`"garden-plots"` (`GardenPlot_ParentBP_C`), `"power-chairs"`
(`Deployed_Furniture_Chair_PowerChair_C`), and `"chemistry-benches"` (`Deployed_ChemistryBench_C`).
Each uses only the deployable's own save-aware functions (`Server_ModifyFillState`,
`SetPlantFertilized`/`SetCurrentGrowthProgress`/`SetCurrentGrowthStage`/`SavePlot`,
`RechargeableComponent:Server_ModifyBattery`); no native row handles are constructed.

`care.list` takes `{"featureId"}` and returns `{"entries":[{"id","label","fields":[...],
"containerId"?}],"isHost":bool}` for every loaded object of that feature's class. Each field is
`{"id","label","value","kind","editable","options"?,"maximum"?}` (`kind` is `"integer"`,
`"enum"`, or `"text"`; `editable` is false for host-only or read-only fields on a non-host
connection). Garden plots report a `water` field plus, per planted spot, `fertilizer:<index>`,
`crop:<index>` (read-only class label) and `stage:<index>`/`growth:<index>` for spots that
currently have a plant. Power Chairs report a single `charge` field (0-200). Chemistry benches
report their input/output flask slots (`flask:0`..`flask:3`) as read-only row-name text; a
chemistry bench entry also carries `containerId` equal to its own `id`, since its flask
inventory is the same slot data `containers.list`/`.set` already reads - use that pair, not
`care.set`, to change what is loaded in a flask.

`care.set` takes `{"featureId","id","fieldId","value"}`, host only, and applies exactly one field:
`water` (0..the plot's `Liquid_MaxFill`), `fertilizer:<index>` (0..10000), `growth:<index>`
(0..10000, only on a spot with a plant), `stage:<index>` (one of the eight named growth stages,
only on a spot with a plant), or a Power Chair's `charge` (0..200). Every other field, including
all chemistry-bench fields, is read-only and rejected. After writing, the handler re-reads the
object's fields and errors if the requested value was not retained instead of reporting success
on a write the game silently ignored.

## `spawn.get` / `spawn.set` - player position and respawn point

`spawn.get` takes no payload (or `{"playerId":"…"}`) and returns:

| Field | Type | Meaning |
|---|---|---|
| `x`, `y`, `z` | number | The character's actual current live position (`K2_GetActorLocation`) |
| `levelName` | string? | The controller's `ActiveLevelName` (a display-only streaming level name; NOT the file's `RespawnLevelGuid` - live has no direct equivalent of that field) |
| `terminalGuid` | string? | The claimed respawn terminal's `TerminalRespawnID` (a `RespawnTerminalCatalog` guid), or absent when none is set |
| `isHost` | bool | Whether this process is hosting (shown for transparency; not enforced) |

`spawn.set` takes `{"teleport":{"x","y","z"}?, "terminalGuid"?, "playerId"?}` and returns no result.
`teleport` moves the character there immediately (`TeleportPlayer`, keeping the character's current
facing). `terminalGuid` claims a different respawn terminal immediately by writing the controller's
own `TerminalRespawnID` field directly - the only field in this pair with no reference-mod
precedent (found in the game's own class layout instead; see `areas/spawn.lua`'s own comment).
Neither happens unless the field is present in the payload - editing values client-side never
moves anyone by itself. `terminalGuid` only ever targets the LOCAL player's own controller,
regardless of `playerId` (there is no getter for a different connected player's controller).

## `companions.list` / `companions.set` - carried pets

A carried pet is an `Item.Pet` row living in the same backpack/equip/hotbar inventory arrays
`inventory.list`/`inventory.set` already read/write (see above) - `companions.list` returns every
OCCUPIED slot across those three (like `inventory.list`, but only non-empty rows) with two extra
fields no other command surfaces:

| Field | Type | Meaning |
|---|---|---|
| `kind`, `slotIndex`, `itemId` | as `inventory.list` | Which slot and what item row is in it |
| `name` | string? | The pet's custom name (`PlayerMadeString_`, the same field `inventory.list`'s slot struct already carries) |
| `health`, `maxHealth` | number | Durability fields, same meaning as `inventory.list`'s `durability`/`maxDurability` |
| `xp`, `mutationProgress`, `petMutation` | number | The pet's `DynamicProperties_` array, keyed by `EDynamicProperty::XP`/`::MutationProgress`/`::PetMutation` - the same array and enum names `PlayerSaveWriter.Pets.cs` uses for the file format |
| `isHost` | bool | Shown for transparency; not enforced |

The Lua mod has no item-data-table catalog of its own, so it returns every occupied slot; deciding
which rows are actually pets (`PetItemCatalog.IsPetItem`, or the Companion equipment slot -
`kind:"equip"`, `slotIndex:12` - regardless of whether the catalog recognises the row) happens on
the .NET side, in `LivePlayerCompanionsSession`.

`companions.set` takes one pet row at a time: `{"kind","slotIndex","clear"?,"itemId"?,"name"?,
"health"?,"maxHealth"?,"xp"?,"mutationProgress"?,"petMutation"?,"playerId"?}`. Applying happens
immediately, one pet at a time - there is no batch form. `clear` empties the slot and ignores every
other field, exactly like `inventory.set`'s `clear`, and replies `{"despawnedFollower":bool}` - see
the round-78 fix below.

**Round-78 bug fix (reported live): removing the active Companion pet left it stuck in the world,
unable to be picked up.** `clear` used to only ever write the inventory slot struct back to
`Empty` - a plain field write, like every other edit in this file - which for the Companion slot
(`kind:"equip"`, `slotIndex:12`, the one slot the game visibly spawns a live follower actor for)
desyncs the follower from its now-empty backing item instead of despawning it. No blueprint
function cleanly releases it (checked for `Server_ReleasePet`/`DetachFromPlayer`/
`RemoveCompanion`/`Dismiss`/`SetOwner`-shaped candidates), but `pets.list`'s own class dump found a
real fix instead: Pest/Skink-family NPCs carry their own `FollowingOwner` reference (see above), so
clearing the Companion slot now also searches Pest/Skink-family actors for one whose
`FollowingOwner` matches the resolved player (compared by `GetFullName()`, the same object-identity
technique `findByFullName` already uses) and destroys it with `K2_DestroyActor()` - the same call
`pets.remove` uses. `despawnedFollower` says whether a match was found and destroyed.

**Round-79: re-checked whether Peccary/Lamogi could be added to that search, against the
installed game's own class data (`LiveClassPropsProbe`, run against the mounted paks).** The
result is conclusive, not unexplored: `NPC_Monster_Peccary_C` and `NPC_Monster_WinterSprite_C`
both declare `super=NPC_Base_ParentBP_C` directly (unlike `NPC_Skink_Basic_C`, which declares
`super=NPC_Monster_Pest_C`), and neither their own properties nor `NPC_Base_ParentBP_C`'s ~150
inherited properties include `FollowingOwner`, `Guid`, `PetName`, or `DynamicProperties`, or any
other player-identity reference. `companions.lua` now searches `NPC_Monster_Pest_C` and
`NPC_Skink_Basic_C` explicitly (the second entry is redundant today, since `FindAllOf` is
hierarchy-inclusive, but protects against that inheritance relationship ever changing), and
deliberately does not search Peccary/Lamogi classes at all: `despawnedFollower` still comes back
`false` for those companions, and this is now a confirmed limit of the current game build rather
than an unresearched gap. A pet merely carried in the hotbar/backpack (not the active follower)
has no such live actor, so clearing those slots never searches at all - only `kind:"equip"`,
`slotIndex:12` does.

**Honesty about `xp`/`mutationProgress`/`petMutation`**: the `DynamicProperties_` array itself is
real (found in the game's own class layout, the identical array/enum the file format already
uses), but no reference-mod command reads or writes it over UE4SS Lua, so reading an enum-keyed
struct array's `Key`/`Value` this way is genuinely new and unverified against the real game until
tested. `itemId`/`name`/`health`/`maxHealth` carry the same confidence as `inventory.list`/`.set`'s
fields (round 74), since they are the identical hash-suffixed struct members.
## `recipes.get` / `recipes.set`

Live recipe-unlock editing, the counterpart to the file editor's RECIPES tab. `recipes.get` takes
an optional `{"playerId":"…"}` payload (omitted targets the local player) and returns
`{"unlockedIds":["Recipe_Foo", ...]}` - only the recipe row names the running character currently
has unlocked (the full catalog of every recipe the game knows comes from the desktop app's own
game-data vocabulary, the same one the file editor uses; the live agent has no path to enumerate
`DT_Recipes`' row names, only what one specific character has already unlocked).

`recipes.get` also reports `canLock`. `recipes.set` accepts `unlockIds` and optional
`lockIds`. Unlocks use the existing game RPC. Relocking requires host authority and
replication support, replaces the FName array after validating all names, marks
`RecipesUnlockedArray` dirty for replication, and invokes its RepNotify. Older agents
omit `canLock`, which the editor treats as false.

## `codex.get` / `codex.set`

Live journal/codex ("GATEPal") editing, the counterpart to the file editor's EMAIL, NOTES, FISH and
COMPENDIUM sections. `codex.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"emails":["Email_Foo"],"journals":["Journal_Bar"],"fish":["Fish_Baz"],"compendium":["Compendium_Qux"]}
```

Each list is the row names the running character currently knows in that section (again, the full
catalog of possible ids comes from the desktop app's own game-data vocabulary). `compendium` is the
union of the three per-category arrays described below, deduplicated.

`codex.set` takes `{"playerId":"…", "emails"?:[...], "journals"?:[...], "fish"?:[...],
"compendium"?:[{"row":"Compendium_Foo","sectionType":"Exploration"}, ...]}` and marks each given
entry known immediately; omitted categories are left untouched.

**`compendium` is settable (round 77).** The game's unlock function,
`Request_UnlockCompendiumSection(CompendiumRow, UnlockType)`, takes an `UnlockType` enum whose
values were previously un-grounded (the one place a real mod calls it,
`CheatConsoleCommands/scripts/Features.lua:894-900`, only ever forwards a value read live off a UI
widget property, never a literal). This round grounded the enum directly: extending
`LiveClassPropsProbe`'s dump with the usmap's own native enum table (`LiveNativeClassPropsProbe`,
since `ECompendiumUnlockType` is a native C++ enum and never its own Blueprint `UEnum` package
export) found:

| Value | Enumerator | Sent by `codex.set`? |
|---|---|---|
| 0 | `Exploration` | yes - `sectionType: "Exploration"` |
| 1 | `Email` | yes - `sectionType: "Email"` |
| 2 | `NarrativeNPC` | yes - `sectionType: "NarrativeNPC"` |
| 3 | `KilLRequirement` | no - unlocked automatically by kill tracking, never through this RPC |
| 4 | `ECompendiumUnlockType_MAX` | no - a sentinel, not a real section |

These three names match the file format's own `DT_Compendium` row data exactly
(`Core/Catalogs/Codex/CodexCatalog.cs`'s `CompendiumEntry.SectionTypes`, built from each section's
`UnlockRequirement` field - `"ECompendiumUnlockType::Exploration"` etc.), so the desktop app already
knows, per compendium row, which section type(s) to send; a row whose entry spans more than one
section type needs one `codex.set` pair per section type to fully unlock it. A row with only a
kill-requirement section has no section type this RPC covers and stays read-only in the desktop
app (its checkbox is disabled, not sent as a request that would silently do nothing).

**Compendium read source (round 77):** `codex.get`'s `compendium` list reads
`Compendium_ExplorationSections`, `Compendium_EmailSections` and `Compendium_NarrativeNPCSections`
on `Abiotic_CharacterProgressionComponent_C` - all plain `FArrayProperty` (`TArray<FName>`), the
same confirmed-working indexed-read technique `EmailsRead`/`JournalEntries`/`FishCaughtArray`
already use. An earlier round read `Local_AllCompendiumEntries` instead (a `TSet<FName>` the game
derives from those same three arrays); that TSet's Lua-array readability was never confirmed, so
this round switched to the better-grounded per-category arrays instead.

`canUnsetKnown` reports whether this host supports clearing known state. See the expanded
codex edit schema below. Older agents omit the capability and remain unlock-only.

## `general.get` / `general.set`

Live "bulk unlocks" editing, the counterpart to the file editor's General tab ITEMS SEEN, ITEMS
CRAFTED, MAPS, BACKGROUND and TRAITS rows (the account/owner-id change has no live counterpart at
all - see below). The desktop app's CHARACTER tab (round 79/80) reuses this same channel for its
own BACKGROUND picker and TRAITS readout, through the same `IPlayerGeneralSession` boundary the
GENERAL tab already binds to - no separate channel or wire command exists for CHARACTER.
`general.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"itemsSeen":["metal_scrap"],"itemsCrafted":["torch"],"maps":["Sector_A"],
 "traits":["Trait_Chef"],"background":"PhD_HumanBio"}
```

`general.set` takes `{"playerId":"…", "itemsSeen"?:[...], "maps"?:[...], "background"?:"…"}` and
discovers/unlocks each given id (and applies the background) immediately; omitted fields are left
untouched.

`general.get` reports `canDiscoverCrafted`. When true, `general.set` accepts
`itemsCrafted:[...]`. The host appends unique names to `CraftedItems`, marks the property
dirty for replication, and invokes `OnRep_CraftedItems`. Older agents remain read-only.

**`background` (round 77) IS a real live write.** `Abiotic_PlayerState_C` declares a plain,
no-hash-suffix `PhD : FNameProperty` with no `OnRep_PhD` - the same row-name concept the file
format's `PhD_` tag stores. `general.set`'s `background` writes it directly on the connected
player's `PlayerState` (found via `APawn.PlayerState`, the base-engine property `main.lua`'s own
`localPlayerId()` already reads off the player CONTROLLER for a different purpose); no RPC is
needed because a replicated UPROPERTY changed on the server's own authoritative object replicates
to owning clients on the next network update.

### `general.trait.set` - toggle a character trait (2026-09-16)

**Traits are now editable live** (implemented, awaiting in-game verification), replacing the
earlier read-only state. The 2026-09-15 bytecode probe found that `InitializeTraits` calls
`Server_AddTraitBuff` using each trait row's buff handle, but also grants starting items and
skill XP, so replaying it whole is unsuitable for an incremental edit. `general.trait.set`
instead does only the incremental part: it takes `{"playerId"?, "id", "enabled", "buffRowName"}`
where `id` is the trait row name (as reported in `general.get`'s `traits` array), `enabled`
selects add/remove, and `buffRowName` is the row's installed trait buff (an empty or `"None"`
value skips the buff call and only updates the trait list, for a trait with no buff of its own).
The host rebuilds `CharacterProgressionComponent.Traits` with the trait added or removed and
marks it for replication, and calls `Server_AddTraitBuff`/`Server_RemoveTraitBuff` with the row's
handle from `BuffDebuffHandleFunctionLibrary` on the player's `CharacterBuffComponent` - never
`InitializeTraits`, so no rewards replay. A failed buff call rolls the trait list back to its
original value before returning the error. `general.get` reports `canEditTraits` (host authority
plus these classes being available on the connected runtime); older agents omit it and traits
stay read-only there.

**The account/owner-id change has no live path at all** and is not part of this wire protocol:
renaming which save file a character belongs to is purely a file-system operation, with no running
in-game concept to change. The desktop app hides that section's CHANGE button when connected live
and shows the connected player's own id (the live directory id `players.list` handed out - a
SteamID64 for a Steam player) as a plain readout instead.

## `appearance.get` / `appearance.set` / `appearance.save` - character look (2026-09-16)

**Appearance is now editable live** (implemented, awaiting in-game verification). The file
editor edits `ScientistCustomization`; live editing instead writes the running
`HumanCustomizationComponent`'s own fields, the same ones `Server_ApplyCustomizationChange`
assigns, then calls each field's real `OnRep_<Property>` so local meshes refresh immediately
without waiting for a remote-client-style replication callback.

`appearance.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"fields":{"Customization_Head":"Head_01", "Customization_HairStyle":"Hair_03", "..."},
 "canEdit":true, "canSaveProfile":true, "hasProfileChanges":false}
```

`fields` covers every customization slot the component exposes: head, head accessory, wristwatch,
tie, upper body, lower body, hair style, hair color, shirt color, shoes, belt, beard (`FacialTrait`
under the wire key `customization_beard`), and ID card. `canEdit` requires host authority and
replication support. `canSaveProfile`/`hasProfileChanges` report whether this connected player is
the local computer's own character (only that profile can be saved - see `appearance.save`) and
whether its live values differ from the saved local profile.

`appearance.set` takes `{"playerId"?, "propertyName", "rowName"}`, host only: `propertyName` is one
of the `fields` keys above, `rowName` is a row from that field's own customization DataTable
(rejected if the row does not exist). The host writes the component's property, marks it for
replication, and calls its `OnRep_<Property>` function directly; a failed write rolls the field
back to its previous value.

`appearance.save` takes an optional `{"playerId"?}` payload, host only, and only for the local
computer's own character (the same restriction `canSaveProfile` reports): it copies the running
component's current field values into the local `CurrentCustomizationSave` profile through the
game's own `SaveGameToSlot` API (so platform-specific save storage is still handled correctly),
after first backing up the existing profile to a `.bak` slot; a failed save restores the backed-up
values.

## `worldunlocks.get` / `worldunlocks.set` - world-wide (not per-player) unlocks (round 77)

The live counterpart to the file editor's world-recipes browser (`WorldStoryTab`'s "WORLD RECIPES"
section, `WorldSaveSession.GlobalRecipes` / the save's `GlobalUnlocks` struct). `worldunlocks.get`
takes no payload and returns:

```json
{"isHost":true,"recipesUnlocked":["recipe_bandage"],"recipesResearched":[],
 "itemsPickedUp":["scrap_metal"],"emailsRead":["Email_Crossbow"],"journalEntries":[],
 "compendiumEmail":[],"compendiumNarrative":[],"compendiumExploration":["Compendium_Office"]}
```

Grounded by extending `LiveClassPropsProbe`'s dump to `Abiotic_Survival_GameState.uasset` (the same
package `story.get` already reads `CurrentQuest` from), which carries `GlobalRecipesUnlocked`,
`GlobalRecipesResearched` (both `FSetProperty`) and `GlobalItemsPickedUp`, `GlobalEmailsRead`,
`GlobalJournalEntries`, `GlobalCompendiumEmail`, `GlobalCompendiumNarrative`,
`GlobalCompendiumExploration` (all `FArrayProperty`) - the world-wide analogues of the per-player
arrays `codex.get`/`recipes.get` already read. The array fields use indexed reads. Recipe sets use the documented UE4SS
`TSet.ForEach` API when available, with the legacy read fallback on older runtimes.

`worldunlocks.get` reports `canEditRecipes`, requiring host authority, TSet editing support,
and replication notification support. `worldunlocks.set` accepts
`{"recipes":[{"id":"recipe_bandage","unlocked":true}]}`. Names and values are validated
before writes. Add/remove applies to both unlocked and researched sets, matching offline
world recipe editing, and both properties are marked dirty for replication. Other global
unlock lists remain read-only. The shared story tab now calls the session interface for
single and bulk recipe edits, retaining its existing story prerequisite gate.

## Recipes/codex/general evidence

All three of the areas above are grounded the same way: `tests/AbioticEditor.Probes/
LiveClassPropsProbe.cs` (fragment "CharacterProgressionComponent") dumps the exported properties
and functions of `Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent.uasset` from
the installed game's own paks - not guessed, and not copied from a mod that implements this exact
feature (no installed mod unlocks recipes, marks codex entries known, or discovers items/maps).
The read side (`RecipesUnlockedArray`, `EmailsRead`, `JournalEntries`, `FishCaughtArray`,
`ItemsPickedUpArray`, `CraftedItems`, `CurrentMaps`) is a direct property read, the same indexed
`for i = 1, #arr do arr[i]:ToString() end` pattern the reference mod's own "traits" console command
uses on a different property (`progressionComponen.Traits`) - real precedent for the TECHNIQUE, not
for these specific property names, hence every read is wrapped in `pcall`. The write side
(`Request_UnlockNewRecipe`, `Server_AddEmailToReadList`, `Server_AddNoteToJournal`,
`Request_UnlockNewFish`, `Server_CheckNewItemPickedUp`, `Server_AddMapToJournal`) is a direct
UFunction call with an `FName` argument built the same way `main.lua`'s `writeSlot()` already
builds one (`FName(str, EFindName.FNAME_Find)`) - real precedent for the CALLING CONVENTION
(confirmed working for `Request_UnlockCompendiumSection` in `Features.lua:900`), not for these
specific function names, hence every call is wrapped in `pcall` too. None of the six write
functions is called by any installed reference mod.

## Extending this for a new area

Adding a new live-editable area (inventory, more of world state, ...) means: a new command pair
on both sides (`<area>.get`/`<area>.set` following the existing naming), a new `Live<Area>Channel`
in `Core/LiveEditing/<Area>/` mirroring the shape of `LivePlayerVitalsChannel`/
`LivePlayerSkillsChannel`, a new handler pair in the Lua mod's `main.lua`, and tests in the Lua harness. The native helper forwards all non-hello commands
(`AbioticEditorLiveAgentHelper/src/main.cpp`). No `hello`/envelope-level change is
needed for a new area; the envelope's `payload`/`result` already accept either a flat object or a
flat array of them, which has covered every area so far.

## Item-table validation for live writes

`inventory.set` and `containers.set` accept optional `dataTable` on each edit;
`dropped.add` and `companions.set` accept it on the request. It is a full Unreal object path,
for example `/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global`. The editor resolves
this from the same item catalog used by offline writers. It is not the slot's `assetId`.

For a replacement, the agent resolves the DataTable UObject, loads the asset on the game
thread if needed, and checks `DataTableFunctionLibrary.DoesDataTableRowExist` before writing
both `DataTable` and `RowName`. An omitted table uses the global item table. A same-item edit
preserves an existing valid table, including a mod override; an invalid existing table is
repaired using the supplied/catalog table. Quantity-only edits without `itemId` and clearing
a slot do not need table lookup. Empty slots may use `Empty` or `None` when read; clearing
writes `Empty`.

Inventory and container batches preflight all slot indices and item-table lookups before
mutating slots. Missing tables, missing rows, and unavailable lookup support return an error
instead of a row-only write. This is validation, not rollback for arbitrary runtime failures.
The existing `OnRep_CurrentInventory` refresh remains once per affected inventory. Blueprint
inspection confirms its delayed inventory update and equipment callback path, but does not
prove multiplayer replication or backpack-capacity behavior in a running game.

See the [live guide](/guide/live-editing#an-item-exists-but-is-invisible) for repairing items
written by an older agent. These changes require updating the installed agent scripts.

## Expanded inventory and codex edits (2026-09-15)

Player and container slot responses include optional `details`:

```json
{"liquidLevel":25,"liquidType":"E_LiquidType::NewEnumerator13","dynamicState":true,
 "playerMadeString":"My item","assetId":"instance-id","variantRowName":"Poster_Art"}
```

Container slots also include `ammoInMagazine`. Edits carrying details use
`inventory.setfull` or `containers.setfull`, retaining the existing request shape. Distinct
command names make older agents reject these writes rather than silently ignore fields.
Empty text clears a custom label; empty/None variant resets its override. Liquid names map
through the actual UEnum values, not their numeric-looking suffix. All edits in a batch
validate before the first slot changes, including variant DataTable row existence. Rich
writes require replication notification support.

`codex.get` reports `canUnsetKnown`. A host may send
`codex.set` with `{"clear":{"section":"emails","ids":["Email_Row"]}}`. Sections are
emails, journals, fish, and compendium. Compendium removal clears matching rows from all
three supported section arrays. Each changed property is marked dirty for replication;
exported RepNotify functions are invoked where present. Kill-only entries remain read-only.

Array assignment is grounded in [UE4SS's property bridge](https://github.com/UE4SS-RE/RE-UE4SS/blob/main/UE4SS/src/LuaType/LuaUObject.cpp).
Set operations follow the [UE4SS TSet API](https://docs.ue4ss.com/dev/lua-api/classes/tset.html).
Direct replicated writes use [UNetPushModelHelpers.MarkPropertyDirty](https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/Engine/Net/UNetPushModelHelpers/MarkPropertyDirty?application_version=5.5).
These additions pass the stub harness, but actual multiplayer propagation and save/reload
persistence still require an installed-game verification run.

## Complete item instance metadata, and moving items into a container (2026-09-16)

**Implemented, awaiting in-game verification.** `Scripts/item_metadata.lua` extends `details`
with `instanceMetadata`, the same complete per-instance state a save-file edit already keeps
(pet progress, weapon coatings, custom variant/item DataTable overrides, and every gameplay tag),
so moving or editing an item live no longer quietly drops it:

```json
{"instanceMetadata":{
  "dynamicProperties":[{"key":"EDynamicProperty::WeaponCoating","value":2},
    {"key":"EDynamicProperty::CoatingDurability","value":30}],
  "gameplayTags":["Item.Special"], "parentGameplayTags":["Item"],
  "itemDataTable":"/Game/Mods/Items.Items", "variantDataTable":"/Game/Mods/Variants.Variants"}}
```

`dynamicProperties` is read from `Abiotic_InventoryChangeableDataStruct`'s
`DynamicProperties`/`GameplayTags` fields, with each dynamic property's numeric `EDynamicProperty`
key resolved to its enum name through `EDynamicProperty:GetNameByValue`. `itemDataTable`/
`variantDataTable` are the instance's own DataTable object paths (not the field-default table),
read via `GetFullName()`; a `.set` write that changes `itemId` while keeping `instanceMetadata`'s
`itemDataTable` unset falls back to preserving the slot's existing table, the same as before
this change, but an explicit `itemDataTable` always wins (needed for a moved item whose row lives
in a modded table the field default does not point at).

Edits carrying `instanceMetadata` use `inventory.setcomplete`/`containers.setcomplete` -
distinct command names, the same pattern as `inventory.setfull`/`containers.setfull` above, so
that an older agent rejects the payload outright instead of silently dropping the metadata
fields it does not understand. All three command names (`.set`/`.setfull`/`.setcomplete`) are
aliases of the exact same handler; only the name differs, purely to gate what an older runtime
accepts. Writing `instanceMetadata` validates every dynamic-property key against the live
`EDynamicProperty` enum, rejects duplicate keys, and range-checks known keys (a weapon coating
below `-1`, or a coating durability below `0`, is refused) before any slot is touched.

`inventory.transfer` (host only) moves whatever is in one slot directly into another, including
across a player inventory and a world container in one call: `{"first":{"slotIndex","containerId"?,
"kind"?,"playerId"?}, "second":{...}}`. `containerId` addresses a loaded container slot (the same
`id` `containers.list` returns); otherwise `kind` (`backpack`/`equip`/`hotbar`/`transmog`) and
optional `playerId` address a player inventory slot, exactly like `inventory.set`. Both
endpoints are resolved and their full complete-metadata snapshots taken on one game-thread
callback before either is written, so a failed validation on one side cannot duplicate or lose the
item on the other; requesting `instanceMetadata` fails outright if either resolved slot's details
predate this change (an older agent). Swapping a slot with itself is a no-op.

## Related screens

See the [screenshot tour](/guide/screenshots) for the player-facing controls. Screenshots illustrate the interface; the schemas and behavior above remain the reference.

![Local game or dedicated server choice in live setup](/screenshots/40-live-location.png)

This is the desktop connection workflow. It does not indicate that a game is connected or that every protocol action is available.
