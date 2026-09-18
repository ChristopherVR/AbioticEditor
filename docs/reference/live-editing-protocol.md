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
{"id":"3","cmd":"vitals.get","payload":{…}}
```

- `id`: a string the response echoes back. The client assigns it; the agent does not need to
  interpret it, only return it unchanged.
- `cmd`: the command name, `"hello"` for the initial handshake, otherwise `"<area>.<action>"`
  (e.g. `"vitals.get"`, `"vitals.set"`).
- `token`: sent only inside `"hello"`'s own `payload` (see below), never as a top-level field -
  the agent reads it from the payload and answers `bad token` otherwise. Every later request on
  the same connection relies on that
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
Both `flags.set` and `story.set` answer `{"skipped":[...]}`: names the game's flag table does not
carry are left alone and listed there instead of failing the request, so the rest of the batch still
applies and the editor can say which names the game did not know.

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

`containers.get` (round 91) takes `{"id"}` and returns `{"container":{...},"isHost":bool}` - ONE
row in exactly the `containers.list` shape (the mod builds both from the same function). It is a
single actor lookup plus one row, not a world scan, and is what the editor uses after every slot
write, rename and transfer, and on its periodic tick for the container the player has open;
`containers.list` is only sent by an explicit refresh or a fresh tab visit. Errors with
"container not found" when the game no longer has that actor loaded. Not host-gated (a read).

## `dropped.list` / `dropped.remove` / `dropped.add`

`dropped.list` returns `{"items":[{"id","itemId","stack","x","y","z"}],"isHost":bool}` for every
item lying loose in the loaded world that nobody has picked up. `dropped.remove` takes
`{"ids":[...]}` and returns `{"removed":n}` - the count actually found and despawned. Host only.

`dropped.add` (round 77; optional position, round 111) takes
`{"itemId","stack"?,"durability"?,"maxDurability"?,"playerId"?,"x"?,"y"?,"z"?}` and spawns a
brand-new item on the ground. There is no `SpawnDroppedItem`/"give item" precedent anywhere in the
reference mod (checked: no additem/spawnitem/give-style command exists in it at all), so this
chains two already-proven mechanisms instead of constructing a dropped-item actor from scratch: it
writes the item into a free slot of the target player's own inventory (the same `writeSlot`
`inventory.set` already uses live), then calls the player's own
`Request_DropInventorySlot(Inventory, Index)` RPC - a real function confirmed from the game's own
class layout with exactly the two simple parameters (an object reference and an int) this module
calls it with. Not exercised by any mod, so genuinely unproven end-to-end; with no `x`/`y`/`z` the
item lands wherever the game's own `FindBestItemDropLocation` puts it (near the player), unchanged
from round 77.

**Position (round 111, all three of `x`/`y`/`z` or none)**: the game mode's own
`SpawnItem(InTransform, ItemRow, StackSize, Durability, NoPhysics, NoCollision, ConnectToComponent,
ConnectToBone, ...)` was considered and rejected - its `ItemRow` parameter is a `DataTableRowHandle`
struct that has to be built and passed ACROSS a function-call boundary, exactly the class of
struct-marshaling that crashed the whole game for bench upgrades (see `bases.lua` below); the one
proven precedent for passing a `DataTableRowHandle`-shaped table as a function *argument*
(`weatherRowHandleToTable`, `world.set`) only works because its fields are copied from a handle the
engine itself already enumerated (`GetAllWeatherEventRowHandles`) - there is no equivalent
enumeration function for the item table, so building `SpawnItem`'s `ItemRow` here would repeat the
fabricated-handle situation that already crashed the bridge once, on a function with several more
parameters. Instead `dropped.add` moves the actor the drop RPC itself already created, with
`K2_TeleportTo(Location, Rotation)` - a real `AActor` function, used verbatim by the reference
mod's own `BaseUtils.TeleportActorToActor`, and already proven live for `vehicles.set`/`spawn.set`
with exactly the plain `{X=,Y=,Z=}` table this reuses. The new dropped-item actor is told apart
from every item already on the ground by snapshotting `Abiotic_Item_Dropped_C` actors just before
the drop RPC and diffing after it; if that diff finds no new actor (the drop may have merged into
an existing ground stack) or more than one (another drop landed in the same instant), the whole
call fails with an honest reason instead of silently leaving the item wherever it actually landed.
On success the moved actor's position is read back with `K2_GetActorLocation` and checked against
the request before reporting success. Genuinely unproven end-to-end against the running game. Host
only.

## `bases.list` / `bases.set` - deployables (round 76, bench upgrades round 77, upgrade removal 2026-09-16, paint colour 2026-09-16, name field fixed round 121)

`bases.list` returns `{"deployables":[{"id","className","x","y","z","customName","hasInventory",
"storedItemCount","supportsUpgrades","canEditUpgrades","installedUpgrades":[...],"paintColor"?}],
"isHost":bool,"supportsBenchUpgrades":bool,"supportsBenchUpgradeRemoval":bool}` for every
deployable currently loaded (`AbioticDeployed_ParentBP` and every subclass - benches, furniture,
defenses, containers). `supportsUpgrades`/`canEditUpgrades`/`installedUpgrades` are meaningful
only for benches; every other deployable reports `false`/`false`/`[]`. `paintColor` is the raw
`EPaintColor` integer value (0-11 or 13; omitted/`null` when unpainted - `EPaintColor::None`,
value 12, is never sent). `bases.set` takes `{"id","customName"?,"upgradeRow"?,
"upgradeInstalled"?,"paintColor"?}` and renames the object, installs or removes a bench upgrade,
and/or sets its paint colour immediately. Host only, like `containers.set`/`doors.set`.

**Custom name field fixed (round 121).** `customName` used to read/write
`AbioticDeployed_ParentBP_C`'s `AlternativeObjectName` (`FTextProperty`, "Edit | BlueprintVisible |
DisableEditOnInstance" - no `Net` flag at all), which is why a bench renamed in-game never showed a
name on this tab: a write with no `Net` flag is only ever seen by whichever machine made it. It now
reads/writes `PlayerMadeString` (a replicated `Net | RepNotify` `StrProperty` declared on
`AbioticDeployed_Furniture_ParentBP_C` - benches, containers and furniture all derive from it) with
the exact same mark-dirty + `NewPlayerMadeString()` refresh `containers.rename` already uses, and
which matches the save file's own `CustomTextDisplay_` leaf
(`WorldSaveWriter.ApplyDeployableCustomText`/`ApplyContainerCustomName`). A deployable class with no
`PlayerMadeString` at all (not Furniture-derived - lights, turrets, and similar non-nameable
placeables) still reports `AlternativeObjectName` as a read-only fallback so it does not regress to
showing nothing, but `bases.set` no longer writes that field for a class that has `PlayerMadeString`.

**Not region-scoped** (unchanged, round 121 investigation). `bases.list` sweeps every currently
loaded `AbioticDeployed_ParentBP_C` instance with no filter on the actor's map/level path, exactly
like every other region-scoped live area (`doors.list`/`containers.list`, `destructibles.lua`,
`triggers.lua` - none of them filter either); the only scoping this whole protocol has is the
desktop app dropping its cached BASES session when `world.info`'s `levelToken` changes
(`ResetRegionScopedWorldSessions` in `LiveConnect.razor`), so the next tab visit re-sweeps whatever
is loaded now. A live report described bases "for places not related to the world save being
viewed"; no working precedent exists anywhere in this mod for filtering `FindAllOf` by map path, so
rather than guess at one, the desktop app now shows each row's own sub-level (parsed client-side
from `id` - the same `DoorIdParser` the DOORS tab already uses on `WorldDoor.Id`, exposed as
`WorldDeployable.SubLevel`) and a distance-based "Nearest first" sort (`WorldDeployable.DistanceTo`,
driven by the tab's `PlayerPosition` parameter) so a player can tell which listed base is actually
near them. Neither addition changes the wire shape above: `x`/`y`/`z` already carried everything
both features need.

**Paint colour** (implemented, awaiting in-game verification): a plain property write, not a
function call. `AbioticDeployed_ParentBP_C` carries a bare `PaintedColor` `EPaintColor` property
(no hash suffix in the compiled class layout) with its own `OnRep_PaintedColor()` (no
parameters - a normal `RepNotify`), confirmed from the game's own class layout (see
`docs/reference/research/research-deployable-paint.md`). `bases.set` writes
`obj.PaintedColor = paintColor`, replays `OnRep_PaintedColor()` via `pcall`, then marks the
property dirty with `NetPushModelHelpers.MarkPropertyDirty` - the same set-then-notify shape
`bases.set`'s rename and `vehicles.set`'s `driveable` already use. This deliberately never calls
the Blueprint `SetPaintColor(Color, SkipSave)` function: it is a real, plain-parameter function
(no struct argument, unlike the bench-upgrade functions that crashed the bridge), but nothing in
this codebase has exercised calling a Blueprint function with a byte-enum parameter from Lua yet,
so the direct-property path already proven for other fields was used instead. The save's own
paint field lives elsewhere entirely (`ChangableData_.DynamicProperties_`, an
`EDynamicProperty::PaintColor` entry - see the research note), so `bases.set` also upserts that
entry in the live object's own `ChangeableData` dynamic-property array (same field names as an
inventory item's, see `item_metadata.lua`), marks `ChangeableData` dirty and calls
`SaveDeployable()` best-effort - the same both-sides shape `bench_tags.lua` uses for upgrade tags.
An object without that array is still repainted live. Whether the game itself would also have
re-derived the entry from `PaintedColor` on the next save (as `SetPaintColor`'s internal
`SetDynamicProperty` call implies) is unverified without a running game.

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

**Round 111 re-check of `canEditUpgrades`**: re-examined against a fresh class probe rather than
assumed still correct. Two findings. First, new grounding: `AddUpgrade`'s own disassembly
(`AbioticDeployed_CraftingBench_ParentBP_C`) ends its success path in a local
`CallFunc_AddTagToChangeableData_ReturnValue` call - the native function's own internal
implementation is itself a tag write into `ChangeableData`, so `bench_tags.lua`'s direct write is
not an approximation of `AddUpgrade`, it is the same operation `AddUpgrade` performs internally,
just reached without marshaling the crash-prone row-handle struct across the function-call
boundary. Second, the gate itself was checked for being wider than it needs to be and is not: it
already requires exactly the two things a tag write needs (replication support, and this specific
instance's `UpgradeTagContainer` and `ChangeableData` tag struct both being readable) rather than a
loose class-level check, so it was left unchanged. `supportsBenchUpgradeRemoval` reporting the
identical value to `supportsBenchUpgrades` (see `bases.lua`) is intentional, not a leftover: since
round 79/80 removal uses the exact same tag-write path as install (no separate native call the way
round 77's `AddUpgrade`-only shape needed), removal genuinely needs nothing install does not
already have. The desktop app's own explanation of this gate was stale and has been fixed: the
BASES tab used to show a blanket "Bench upgrades: offline editor only" line even in a live session
where several benches usually could be edited, and had no "you are not the host" banner at all
(every other live world tab has one) - see `WorldBasesTab.razor`'s `WorldBases_LiveUpgradeCaveat`/
`LiveBases_NotHostWarning`/`WorldBases_UpgradesUnavailableLive` strings and
`IWorldBasesSession.BenchHasUpgradeSlot` (the class-level "has upgrade slots at all" question,
kept separate from `BenchSupportsUpgrades`'s "can edit them right now" so a bench with slots that
just can't be confirmed on this connection gets an explanation instead of silently vanishing).

Opening a bench or crate's contents inline (the file editor's slot grid) is still file-only - it
shares the CONTAINERS tab's staged slot model; use the CONTAINERS tab for live slot editing.

## `vehicles.list` / `vehicles.set` - round 76, wrecked state round 77, on-board storage the coordinator round after 79

`vehicles.list` returns `{"vehicles":[{"id","vehicleId","vehicleClass","driveable","wrecked",
"x","y","z","containerId"?,"hasInventory","inventoryItemCount"}],"isHost":bool,
"supportsWreckedState":true}` for every vehicle currently loaded (`ABF_Vehicle_ParentBP` and its
subclasses). `vehicles.set` takes `{"id","driveable"?,"wrecked"?,
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

**On-board storage** (closed the coordinator round after round 79, re-probing the game's own
class layout - fragment `ABF_Vehicle_ParentBP` - rather than guessing from the save's own field
names). `ABF_Vehicle_ParentBP_C` carries a plain, unsuffixed `StorageContainer` property (a
`ChildActorComponent`) and a BlueprintPure `GetVehicleContainers()` whose own bytecode dynamic-
casts `StorageContainer.ChildActor` to `Deployed_Container_ParentBP_C` and reads its
`ContainerInventory`. The forklift's own placed child-actor default is
`Deployed_Container_ForkliftCargo_C` (confirmed from the Blueprint export's `ChildActorTemplate`),
which chains `Deployed_Container_ForkliftCargo_C` -> `Deployed_Container_Cargo_C` ->
`Deployed_Container_ParentBP_C` - the exact class `containers.lua`'s own `CONTAINER_CLASSES` sweep
already scans for (the security cart's `Deployed_Container_SecurityCartCargo_C` chains the same
way). **A vehicle's on-board cargo is therefore a genuine, independently-loaded
`Deployed_Container_ParentBP_C` actor, not a bespoke vehicle-only structure**: it is already
listed, read and written by the existing `containers.list`/`containers.get`/`containers.set`
handlers with no changes there at all. `vehicles.lua` only resolves `StorageContainer.ChildActor`
(pcall-guarded - a vehicle type with no cargo child actor reports no storage instead of erroring)
and reports its own `fullName()` as `containerId`, plus `hasInventory`/`inventoryItemCount` read
the same way `containers.lua` counts a container's own non-empty slots. The app's VEHICLES tab
"open storage" button now jumps straight to that `containerId` in the CONTAINERS tab (one
slot-edit code path, the same one every other placed container already uses) instead of the old
hardcoded `hasInventory: false`. **Not yet exercised in the running game.**

## `pets.list` / `pets.set` / `pets.remove` - round 76 (no path), partially closed round 77, removal added round 78, generic tamed sweep added round 105, species change added round 109

`pets.list` returns `{"pets":[{"id","npcClass","isDead","customName","x","y","z","limbHealth":
{...},"xp","matched"}],"isHost":bool,"available":true,"supportsSpeciesChange":true,
"supportsRemoval":true,"reason":"..."}`. `supportsSpeciesChange` is reported by the live agent
itself (round 109 - see below); an older agent build that never sends the field is read as `false`
by the app, so its creature-type control stays hidden automatically. Round 76 found no general
live path for tamed pets: the
fields a world save's `PetNPC` record needs are exposed wildly inconsistently between creature
families. Round 77 re-checked the game's own class layout and found a real, **partial** path
instead of guessing a universal one:

- The Pest family (and Skink, which inherits from it) directly exposes, with no hash suffix:
  `PetName` (`FTextProperty`, real `OnRep_PetName`), `Guid` (`FStrProperty` - a stable id matching
  the save's own `PetNPC` key), `DynamicProperties` (the same `{Key,Value}` shape
  `companions.list`'s carried-pet XP already reads/writes), and `FollowingOwner`
  (`FObjectProperty`, a reference to the player it is currently following - see `companions.set`
  below for what this unlocked). `pets.list` only lists actors of this family, matched by `id` =
  their own `Guid` string. These rows come back `matched:true`.
- Per-limb health is **universal**, not pet-specific: `AbioticCharacter` (the native base of
  every player AND every NPC) carries `CurrentHealth_Head/Torso/LeftArm/RightArm/LeftLeg/
  RightLeg` as plain unsuffixed floats with one shared `OnRep_CurrentHealth` - the exact fields
  `vitals.set` already writes for the local player, confirmed live. `pets.set` writes these the
  same way, for both matched and unmatched rows (see below).
- Peccary and Lamogi family pets were re-checked and confirmed to still carry none of
  `Guid`/`PetName`/`DynamicProperties`/`FollowingOwner` as their own properties - there is still no
  stable id for them, so species change is refused for these rows even though it is now attempted
  for matched ones (round 109, see below): there is no Guid to hand the game to preserve identity
  with, and nothing to verify a "same pet" result against.

**Round 105: Peccary/Lamogi pets are listed now, not omitted.** Rather than re-confirming round
77/79's conclusion unchanged, this round found a real, generic tamed-creature marker:
`NPC_Monster_WinterSprite_C`'s own compiled graph calls a static library function,
`AbioticFunctionLibrary::IsTamedPet(Actor)` (bool, one parameter), from three of its own
overridden functions. It is a general-purpose actor query, not specific to any one creature
family, so `pets.list` now also sweeps `NPC_Base_ParentBP_C` (the same hierarchy-inclusive parent
class `npcs.list` already sweeps for the whole CREATURES tab - no Peccary/Lamogi/future-family
class list needed) and feature-detects per instance: anything with its own `Guid` is skipped
(already covered by the Pest/Skink path above), anything else `IsTamedPet` reports true for is
listed with `matched:false` and `id` set to the live actor's own full path (the same id scheme
`npcs.list` uses) rather than a save key, since these creatures still expose no stable id a save
row could share. Calling `IsTamedPet` on a non-WinterSprite actor is new and unverified against
the real game until tested live, wrapped in `pcall` like every other first-use call in this
project. An unmatched row's `customName` is always `null` and `xp` is always `0` (the class has
neither field); `isDead`/`limbHealth` are real and stay editable exactly like a matched row's.

**Round 105: species change stayed refused through this round, with sharper evidence.** The game's
own `Abiotic_Survival_GameMode_C.SpawnPet(Class, SpawnTransform, Guid, Name, Owner,
DynamicProperties, Tamed)` is a real function with exactly the shape a "respawn as a different
class" edit would need - but `SpawnTransform` is an `FTransform`, a nested struct
(rotation/translation/scale) this project had no working construction precedent for anywhere over
UE4SS Lua reflection, unlike the flat `FVector`/`FRotator` tables round 76 proved out for
`spawn.set`. Guessing an unverified struct shape for a native-bridged call is exactly what caused
the BASES tab's fatal, non-catchable crash in round 79, so this stayed refused project-wide through
round 105.

**Round 109: species change, for MATCHED (Pest/Skink-family) pets only, no longer refused.**
Re-examined against a fresh pak dump of `Abiotic_Survival_GameMode_C.SpawnPet` (full bytecode, not
just its signature) rather than re-asserting the round-76/105 conclusion unchanged. The blocker was
never "structs are unsafe to pass" in general - round 76 already proved that an engine-*returned*
`FVector`/`FRotator` struct (from `K2_GetActorLocation`/`K2_GetActorRotation`) can be handed
straight back into another native call's matching struct parameter, unchanged or with individual
leaf fields overwritten (`spawn.lua`'s `TeleportPlayer` path, `vehicles.lua`'s `K2_TeleportTo`
path). The round-79 BASES crash came from a *hand-fabricated* struct **table** for a parameter type
nobody had ever seen a real instance of - a categorically different risk. `pets.lua`'s
`trySpeciesChange` avoids that mistake entirely: it reads the OLD pet actor's own current transform
fresh via `npc:K2_GetActorTransform()` - a standard, zero-argument, `BlueprintPure` `AActor`
function, the same category of call as the already-proven `K2_GetActorLocation`/
`K2_GetActorRotation` - and passes the result to `SpawnPet` completely UNCHANGED (no field is ever
read, guessed, or written on it), which is a strictly *smaller* risk than the already-proven
vector/rotator case. Every other `SpawnPet` argument is likewise read straight off the OLD actor,
never fabricated: `Guid`/`Tamed` are plain scalars, `Name` is the OLD pet's own `PetName` `FText`
userdata passed through unchanged, `Owner` is its own `FollowingOwner` object reference (confirmed
real and readable on this family by `companions.lua`, round 78/79), and `DynamicProperties` is its
own live array passed by reference (which is why XP/mutation progress survive a species change
without this module touching them directly). Health/limb state is *not* part of `SpawnPet`'s
signature, so it does **not** carry over - the new actor spawns with its class's normal health.

Safety ordering: `SpawnPet` is called first; the returned actor must be valid **and** report back
the *same* `Guid` that was passed in before the OLD actor is destroyed. Any failure at any step
(unresolved target class, an unreadable transform, the call itself erroring, an invalid or
mismatched-identity result) leaves the OLD pet completely untouched and comes back as a warning,
never a thrown error and never a destroy without a confirmed replacement - a spawn that "worked"
but reports the wrong identity is the one case that can leave a stray, unmatched extra actor behind
even though the edit itself is reported as failed. The one honest caveat that sets this call apart
from almost everything else in this project: a wrong-shaped argument to a native `UFunction` call
is the one class of failure `pcall` cannot be trusted to catch (that is exactly what made round
79's BASES crash non-catchable) - every reasoning step above argues why this specific call should
not hit that failure mode, but `K2_GetActorTransform`/`SpawnPet` have never actually run against
the real game; this is proven only against the Lua stub harness so far. `supportsSpeciesChange` is
now `true` when the connected live agent supports this path (a matched row only - unmatched rows
are refused with a named warning, since there is no Guid to preserve identity with).

`pets.set` takes `{"id","isDead"?,"customName"?,"limbHealth"?,"xp"?,"npcClass"?}`. `npcClass` is
only ever treated as a real species-change *request* when it differs from the pet's own current
class (`WorldPetsTab.razor`'s own `Apply()` resends the pet's current class unchanged on every
other edit, so a plain health/name/xp call never attempts one by accident) - and only for a matched
row; an unmatched row's requested change is refused with a warning naming why. Host only. It
replies `{"warnings":[...]}` rather than failing outright when one field could not be applied - see
the round-78 bug fix below. On an unmatched (`matched:false`) row, a requested `customName`/`xp`
change is never attempted (the class has no such field) and comes back as a warning instead of a
silent no-op or a thrown error; `isDead`/`limbHealth` apply the same way as a matched row.

`pets.remove` takes `{"id"}` and destroys the pet's actor outright
(`npc:K2_DestroyActor()` - the same standard `AActor` call the reference CheatConsoleCommands
mod's own "deleteobject" console command already uses on an arbitrary world actor). No blueprint
function cleanly "releases" a tamed world pet back into the wild (checked `CreatePetItem`/
`ReleaseFromAIDirector`/`IsFollower` on `NPC_Base_ParentBP_C` - none of them detach-and-vanish an
already-world-placed NPC), so this is the closest evidenced removal there is. Host only, and there
is no undo once it returns. Works on matched and unmatched rows alike - removal never needed a
save-matchable id, only a live actor reference.

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

## `elevators.list` / `elevators.set` - fixed elevator platforms (round 79, mechanics confirmed and discovery made subclass-generic round 95)

The live twin of the `elevators` world-map feature
(`Core/WorldSaves/Features/ElevatorMapFeature.cs`, the save's `ElevatorMap`, whose only persisted
leaf is `TopOpen_<hash>`). `elevators.list` returns
`{"elevators":[{"id","label","controllable","topOpen","moving","x","y","z"}],"isHost":bool}`.
`elevators.set` takes `{"elevators":[{"id","topOpen"?}]}`. Host only.

**Confirmed from a real class+bytecode probe** (`tests/AbioticEditor.Probes/ElevatorButtonProbe.cs`),
correcting an earlier guess (a live `TopOpen` bool with `OnRep_TopOpen`) that was wrong -
`Elevator_ParentBP_C` (super `Actor`) has neither. The real live state is a replicated byte enum,
`ElevatorCurrentMode` (`E_ElevatorMovementTypes`: 0 StoppedAtBottom, 1 StoppedAtTop, 2
MovingToTop, 3 MovingToBottom), with `OnRep_ElevatorCurrentMode` as its notify.
`OnLoadedFromSave(Top: bool)` sets `ElevatorCurrentMode` to 1 when `Top` is true and 0 otherwise
(a plain Select in its bytecode), so `topOpen` means exactly `ElevatorCurrentMode ==
StoppedAtTop`; the reverse, save-time direction runs through `SaveElevatorStateToWorldSave` and
the game mode's own `UpdateActorToWorldSave`, outside this class and not itself traced - inferred
by symmetry with the confirmed load-time mapping, not independently confirmed. `topOpen` is
written by pressing the game's own `TryPressTopButton(Activated: bool)`/
`TryPressBottomButton(Activated: bool)`, never `TopOpen` directly. Both only act when `Activated`
is true and check neither `IsServer()` nor `IsPowered()` internally; traced per-mode:
`TryPressTopButton` mode 0 -> 2 (start moving up), mode 3 -> 2 (redirect up), mode 2 -> unchanged
("already on its way up"), mode 1 -> 3 (a real **toggle-away quirk**: pressing the top button
while already at the top sends it back down). `TryPressBottomButton` is the exact mirror.
`elevators.set` never presses the button for the side the elevator already occupies (avoiding
that quirk when the caller only wanted to confirm arrival), refuses with a named reason if
`IsElevatorMoving()` or not `IsPowered()`, and - since moving the platform takes real travel time
- accepts a press that starts or continues the right direction as success rather than requiring
an already-arrived read-back; a press with no confirmed effect is an error, not a false success
(same partial-apply shape as `doors.set`/`portals.set` for an unresolved id).

**Discovery is subclass-generic, not a hardcoded class list.** `elevators.list` sweeps only the
parent class, `FindAllOf("Elevator_ParentBP_C")` - UE4SS returns subclass instances too, the same
idiom `npcs.list` already relies on for `NPC_Base_ParentBP_C` - so `Elevator_Office_BP_C`
(confirmed `super=Elevator_ParentBP_C`), any other elevator variant the game ships, and a future
DLC addition are all found with no class name anywhere in this module. A short, explicitly
data-only fallback class list is consulted only if that parent sweep returns nothing. Every
instance is read through `pcall` feature-detection: an elevator type this module cannot read
`ElevatorCurrentMode` from still lists (`controllable: false`, its real class name as `label`)
instead of erroring or being dropped, and a set attempt against it is refused by name. Not yet
exercised in the running game.

## `buttons.list` / `buttons.set` - world buttons (round 80, property/function names confirmed round 95, hierarchy-based discovery round 96, pressedOnce made settable round 110)

The live twin of the `buttons` world-map feature (`Core/WorldSaves/Features/ButtonMapFeature.cs`,
the save's `ButtonMap`, whose leaves are `ButtonID_`/`ButtonHasBeenPressedOnce_`/
`ButtonIsEnabled_`/`ButtonActivated_`/`NoReset_`). `buttons.list` returns
`{"buttons":[{"id","label","enabled"?,"activated"?,"pressedOnce"?,"noReset"?,"x","y","z"}],"isHost":bool}`
for every loaded actor found by a single `FindAllOf("Button_Generic_C")` sweep - `FindAllOf` is
hierarchy-inclusive (the same idiom `bases.lua`/`main.lua`'s `CONTAINER_CLASSES` and `pets.lua`
already rely on), so every current subclass (`Button_Keypad[_VOTV[_Terminal]]`,
`Button_LightSwitch`/`Button_VOTV_Lightswitch`, `Button_Tram`/`Button_TramRecall`,
`Button_ValveWheel`, `Button_VehicleRecall`, `Button_WeatherEnd`, `Button_DFWarReactor`,
`Button_ORDER`, `Button_Torii_Lantern[_Hanging]`, confirmed from each class's own `super=` chain)
comes back with no class name hardcoded anywhere, and so will any future one the game adds. A
second, currently-empty table of additional root classes exists for a button-shaped class that
does NOT chain up to `Button_Generic_C` (data, not logic - see `buttons.lua`'s own header
comment); nothing qualifies today. Every property read/write is per-instance feature-detected
(`pcall`), never assumed present, so a class this area has never seen still lists with whatever it
has and reports the rest unavailable rather than erroring or being silently dropped, and its real
runtime class always comes through as `label`. Two look-alikes are excluded because their own
`super=` chain shows they are not part of this system at all, not because of a missing list entry:
`Button_SpecialImageButton_C` is a UMG widget (`WidgetBlueprintGeneratedClass`, never a placed
level actor, so it could never match the sweep anyway), and `CartRecallButton_C` derives from
`VehicleRecallStation_C`, not `Button_Generic_C`, and has none of these properties at all.

Confirmed this round against the coordinator's own CUE4Parse class dump and `Button_Generic_C`'s
own blueprint bytecode (`UpdateButtonSaveData`, `CanButtonSave`, the two `OnRep_` functions - see
`live-agent/AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua`'s own header comment for the full
citations): `enabled` is `NOT ButtonDisabled` (inverted; replicated, `OnRep_ButtonDisabled`),
`activated` is `Activated` (direct; replicated, `OnRep_Activated`), `noReset` is `NoVignetteReset`
(direct; plain, not replicated - no `OnRep_` exists for it). `buttons.set` writes the matching
property directly, calls its `OnRep_` where one exists, then calls `UpdateButtonSaveData(true)` -
the same "Force" idea `portals.lua`'s `SavePortalState(true)` already uses, and the exact shape the
game's own save path takes (bypassing the `CanButtonSave()` gate entirely). A `null` field in a
`buttons.list` row means this specific actor could not be read just now, not a guessed `false`.

**`pressedOnce` is genuinely settable, honestly, as of round 110.** It is readable
(`ButtonSaveData.ButtonHasBeenPressedOnce_110_C4AE20D34162FCD3FA3323907300CB1F`, a struct-nested
leaf accessed by its exact hash-suffixed name - the same discipline `main.lua`'s `SKILL_XP_FIELD`
already documents) and the bytecode shows `UpdateButtonSaveData` always sets it `true`
unconditionally the moment it runs at all - but that is a property of that wrapper function, not of
the underlying save data. `buttons.set` writes the leaf directly, then persists by calling
`Abiotic_Survival_GameMode_C:UpdateActorToWorldSave(Self, false, 4)` **itself**, deliberately
skipping `UpdateButtonSaveData` (calling it would immediately re-force the leaf back to `true`).
`UpdateActorToWorldSave` is confirmed real and callable (`FUNC_Public | FUNC_BlueprintCallable |
FUNC_BlueprintEvent`, params `(Actor, RemoveFromSave: bool, SaveType: E_SaveType byte)`) and is the
exact same "persist this now" call `UpdateButtonSaveData` already ends with - it only serializes
whatever the actor's SaveGame-tagged properties already hold, it does not recompute them, so a
direct leaf write followed by this call persists exactly the value just written. The `4` is copied
verbatim from the one call site inside `UpdateButtonSaveData`'s own bytecode (not derived from the
`E_SaveType` enum's own names, which were not part of the dump) - the same "reuse the game's own
literal" discipline `trams.lua`'s header comment documents for its own `14`. `buttons.set` reads
the leaf back after writing and only reports success once it matches what was requested.

**Honest caveat, confirmed not assumed**: `UpdateButtonSaveData` is called from exactly one place
in the whole class - `ExecuteUbergraph_Button_Generic` (every one of its five call sites falls
inside that one function's statement range), the shared interaction graph a player's own press (or
`TriggerButtonWithoutUser()`, which jumps straight into the same graph) runs. So a `pressedOnce:
false` write is real and persists immediately, but is only durable until this exact button is next
actually interacted with (by a player, a linked-button chain, or a future
`TriggerButtonWithoutUser()` call) - that re-runs `UpdateButtonSaveData` and forces the leaf back to
`true` again. A `pressedOnce: true` write has no such caveat - nothing ever clears it back to
`false` on its own, live or offline. Note that a successful edit to `enabled`/`activated`/`noReset`
on the SAME button (via the same batched `buttons.set` call) still forces `pressedOnce` true as a
side effect of `UpdateButtonSaveData`, but an explicit `pressedOnce` value in that same row is
applied AFTER that and wins. Host only. Same partial-apply behavior as `doors.set`/`portals.set` (a
row with an unresolved `id` does not block the others in the same call; a failed `pressedOnce`
write - e.g. a class with no live `ButtonSaveData` struct at all - fails that row by name without
blocking the rest). Not yet exercised in the running game.

## `resourcenodes.list` / `resourcenodes.set` - harvestable resource nodes (round 101)

The live twin of the `resource-nodes` world-map feature
(`Core/WorldSaves/Features/ResourceNodeMapFeature.cs`, the save's `ResourceNodeMap`, whose leaves
are `HasBeenPickedUp_`/`DayPickedUp_`/`CurrentPosition_`). `resourcenodes.list` takes an optional
`{"classFilter":string}` payload (a case-insensitive substring match against the actor's own class
name, e.g. `"GlassPane"`) and returns
`{"nodes":[{"id","label","harvested"?,"dayPickedUp"?,"x","y","z"}],"isHost":bool}` for every
currently-loaded resource node found by a single `FindAllOf("ResourceNode_ParentBP_C")` sweep.
`resourcenodes.set` takes `{"nodes":[{"id","harvested"?,"dayPickedUp"?}]}`. Host only.

**Discovery is a single hierarchy sweep, not a hardcoded class list.** `ResourceNode_ParentBP_C`
(super `AbioticActor_C`) is the root nearly every harvestable in the game chains up to - confirmed
from the coordinator's class dump across dozens of concrete classes
(`ResourceNode_WoodCrate_Manufacturing_C`, `ResourceNode_GlassPane_C`,
`ResourceNode_AnalysisMachine_C`, `ResourceNode_Hydropanel_C`, `ResourceNode_Turbine_C`, and more) -
and its own subclass `Resource_MicroNode_ParentBP_C` (every `Resource_MicroNode_*`/
`Resource_Micronode_*` class, e.g. `Resource_MicroNode_DuctTape_C`,
`Resource_Micronode_LeyakEssence_TWO_C`), so the one sweep already covers both families with no
class name hardcoded anywhere in this module, and so will any future harvestable the game adds.
Every property read is per-instance `pcall`-feature-detected, so a class this module has never
heard of still lists (with its real class name as `label`) instead of erroring or being dropped.

**Field mapping, confirmed from `ResourceNode_ParentBP_C`'s own `ChildProperties` and
`SaveNodeToWorldSave`'s own bytecode** (not guessed from the save's leaf names):
- `harvested` = the live `IsDepleted` bool (replicated, `OnRep_IsDepleted`).
  `SaveNodeToWorldSave`'s own bytecode reads exactly this property before persisting a node,
  grounding the mapping directly.
- `dayPickedUp` = the live `DayWasDepleted` int - **plain, not replicated** (confirmed from its own
  `PropertyFlags` carrying no `Net` flag, so it has no `OnRep_`).
  `SaveNodeToWorldSave`'s bytecode sets it to `DayNightManager.CurrentDay` at the moment it
  persists a depleted node, confirming both the field and its meaning.
- `position`/`x,y,z` = the live actor transform (the same `K2_GetActorLocation` read every other
  fixed-actor feature in this protocol uses) - `ResourceNode_ParentBP_C` carries no separate
  position property of its own, unlike the save's `CurrentPosition_` leaf.

**`harvested` is never a bare property write.** `ResourceNode_ParentBP_C` exposes two real,
zero-parameter `FUNC_BlueprintCallable | FUNC_BlueprintEvent` functions, `RespawnResourceNode()`
and `Force_DepleteNode()`. Traced through the shared ubergraph both jump into: for the ordinary
case (no `ContinualRespawnFlag` world flag configured on the node), `RespawnResourceNode` calls
`FlushNetDormancy()`, sets `IsDepleted=false`, re-places the node on the ground (`PlaceOnGround`,
gated on the streaming location being loaded), then calls `OnRep_IsDepleted()` and
`NetPushModelHelpers.MarkPropertyDirtyFromRepIndex(self, IsDepleted)` - the game's own "make this
node visibly reappear" path, used here rather than a bare field write. `Force_DepleteNode` is the
exact mirror (`FlushNetDormancy()`, `IsDepleted=true`, the same confirmed `OnRep_IsDepleted()`/
`MarkPropertyDirtyFromRepIndex` tail). `resourcenodes.set` presses whichever function moves
`IsDepleted` toward the requested value (skipping the call entirely when the node is already
there, the same "already parked" idiom `elevators.set` uses) and re-reads `IsDepleted` afterward,
only reporting success once it actually matches - the same "a call with no confirmed effect is an
error, not a false success" honesty `elevators.set` established, not a blind fire-and-forget.

**Known, documented quirk (not silently papered over):** a node that carries a valid,
currently-set `ContinualRespawnFlag` world flag takes a different branch inside
`RespawnResourceNode` - it calls `Server_SetDormant()`, plays a portal-vanish effect/sound, and
then falls into the SAME depleting tail `Force_DepleteNode` uses, i.e. the node ends up depleted,
not respawned, for that one call. There is no exposed function to read whether that flag is
currently set from outside the node's own bytecode, so this is not specially detected - the
readback check above catches it (and any other unconfirmed call) as an honest failure rather than
a false success.

`dayPickedUp` is a direct write (no confirmed setter function, and none needed since the field is
not replicated) - the same `NoVignetteReset` precedent `buttons.set` already documents for a
plain, non-replicated bool.

**Deliberately excluded from the desktop app's periodic live refresh loop.** A single loaded region
can carry well over a thousand resource-node entries in the save (more than any other live world
area this protocol covers), so `LiveConnect.razor` only fetches this area once per world-surface
visit (or after a region change), never on a timer - see that file's `ActiveLiveSessions` switch
and `resourcenodes.lua`'s own header comment. `resourcenodes.list`'s optional `classFilter` exists
for a caller that wants to narrow a request to one harvestable type instead of paying for every
node in the loaded area, though the desktop app's own `WORLD > Resource Nodes` tab does not use it
today (it lists everything, matching the file editor's own unfiltered `ResourceNodeMap` view, and
relies on that tab's own virtualized/filterable row list for the resulting size). Same partial-apply
behavior as `doors.set`/`portals.set`/`buttons.set` (an unresolved `id`, or a field that failed its
readback check, does not block other rows in the same call). Removal is refused live (unlike the
offline feature, which drops the whole map entry so the game recreates the actor at its blueprint
default) - `RespawnResourceNode` only clears the harvested flag and re-places the existing actor, it
does not reset position or any other persisted state, so mapping "remove" onto it would overstate
what actually happens. Not yet exercised in the running game.

## `destructibles.list` / `destructibles.set` - breakable world objects (round 100)

The live twin of the `destructibles` world-map feature (`Core/WorldSaves/Features/DestructibleMapFeature.cs`,
the save's `DestructibleMap`, whose only editable leaf is `Broken_`). `destructibles.list` returns
`{"destructibles":[{"id","label","broken","x","y","z"}],"isHost":bool}` for every loaded actor found
by a single `FindAllOf("Abiotic_GenericDestructible_BP_C")` sweep (hierarchy-inclusive, the same
idiom `buttons.lua`/`elevators.lua` already document), so every current subclass (ice walls, spore
webbing, ceiling tiles, security doors, x-ray fields, and every other fixture-confirmed
`Destructible_*`/`IceWall_*`/`Webbing_*` class, confirmed from each class's own `super=` chain)
comes back with no class name hardcoded anywhere, and so will any future one the game adds.
`destructibles.set` takes `{"destructibles":[{"id","broken"?}]}`. Host only.

Confirmed against the coordinator's own CUE4Parse class dump and
`Abiotic_GenericDestructible_BP_C`'s own blueprint bytecode (see `destructibles.lua`'s own header
comment for the full citations): `broken` is `actor.Broken` (direct, replicated, RepNotify
`OnRep_Broken`). The class's own ubergraph (the code a world-flag-triggered break and
`TryApplyDamage` reaching zero health both jump into) does exactly `Broken = true; OnRep_Broken();
SetStateBroken(NoFX)`, so `destructibles.set` mirrors that shape for `broken: true` - write
`Broken = true`, then call the real `OnRep_Broken()` (which itself calls `SetStateBroken(NoFX)`,
with `NoFX` computed from whether the actor "just loaded", so a live-triggered break plays FX/SFX
the same as a real player-caused one).

**Repair has no live path, confirmed not assumed.** `OnRep_Broken`'s own bytecode starts with an
unconditional "if not Broken then return" - there is no branch at all for the false case, and
nothing else in the class (every function in the dump was checked: `SetStateBroken`,
`TryApplyDamage`, `Server_InitialBreakEvent`, `WorldFlagBreakCheck`, `HealthUpdated`,
`UserConstructionScript`) ever re-enables the intact mesh's collision/visibility or disables the
destroyed mesh's once `SetStateBroken` has run - `SetStateBroken` itself takes only a `NoFX` bool,
never a "which state" argument, so it is a one-way break, not a toggle. `destructibles.set` refuses
any request that sets `broken: false` outright (`"this object cannot be repaired live..."`) before
writing anything, rather than desyncing the save's own flag from what the player still sees (a
permanently broken mesh/collision) - the same "refuse before writing anything" shape
`buttons.set`'s `pressedOnce` refusal already documents. A `null` `broken` field in a
`destructibles.list` row means this specific actor could not be read just now, not a guessed
`false`. Same partial-apply behavior as `doors.set`/`buttons.set` (a row with an unresolved `id`
does not block the others in the same call; a `broken: false` request in the same batch as
resolvable `broken: true` rows still lets those apply before the whole reply becomes an error).

**Deliberately excluded from the desktop app's periodic live refresh loop**, for the same reason
`resourcenodes` is: a loaded region can carry a great many `Webbing_BP`/`IceWall_BP` actors at once
- see `LiveConnect.razor`'s `ActiveLiveSessions` switch. Removal is not offered live (the offline
feature disables it too, for the same reason: an entry only exists once broken, so removing it
would have the same effect as `broken: false`, which is refused live anyway). Not yet exercised in
the running game.

## `corpses.list` / `corpses.remove` - NPC corpses (round 100)

The live twin of the `corpses` world-map feature (`Core/WorldSaves/Features/CorpseMapFeature.cs`,
the save's `CorpseMap`, which has no editable field offline either - only removal). `corpses.list`
returns `{"corpses":[{"id","label","gibbed"?,"looted"?,"x","y","z"}],"isHost":bool}` for every
loaded actor found by a single `FindAllOf("CharacterCorpse_ParentBP_C")` sweep (hierarchy-inclusive,
the same idiom `buttons.lua`/`elevators.lua`/`destructibles.lua` already document), so every current
subclass (`CharacterCorpse_Human_BP_C` and its own named variants - `CharacterCorpse_OrderGrunt_C`,
`CharacterCorpse_OrderSniper_C`, `CharacterCorpse_OrderBreacher_C`, `CharacterCorpse_OrderCaptain_C`,
`CharacterCorpse_LabRat_C`, `CharacterCorpse_Human_GATESecurity_C` - plus `CharacterCorpse_MonsterGeneric_C`,
confirmed from each class's own `super=` chain) comes back with no class name hardcoded anywhere,
and so will any future one the game adds. `corpses.remove` takes `{"id"}`. Host only.

Confirmed against the coordinator's own CUE4Parse class dump of `CharacterCorpse_ParentBP_C`:
`gibbed` is `actor.IsGibbed` (direct, replicated, RepNotify `OnRep_IsGibbed`) and `looted` is
`actor.HasBeenLooted` (direct, replicated, no RepNotify) - one field name apart from the save's own
leaves (`IsGibbed_`/`IsLooted_`), same meaning. Both stay read-only here too, matching
`CorpseMapFeature.cs`'s own reasoning ("no in-game reason to flip either by hand"); a `null` field
in a `corpses.list` row means this specific actor could not be read just now, not a guessed `false`.

**Removal, confirmed not guessed.** Every function `CharacterCorpse_ParentBP_C` declares
(`SaveCorpse`, `DropLoot`, `RefreshGibbedState`, `OnRep_IsGibbed`/`OnRep_CurrentGibCuts`,
`GetTypeOfInteractableCorpse`, `MergeAndClearSkeletals`, `GetAttackerLootChance`, ...) was checked
against the dump and none of them cleanly despawns an already-placed corpse - there is no
"DespawnCorpse" or equivalent, matching round 78's identical finding for tamed pets
(`pets.remove`). `corpses.remove` therefore uses the same standard `K2_DestroyActor()` the reference
CheatConsoleCommands mod's own "deleteobject" command already uses on an arbitrary world actor. No
undo once this runs, matching the offline feature's own remove description ("clear the clutter, and
any loot still on it"). This is the first live world-map feature session
(`LiveCorpsesFeatureSession`) where removal really is supported, rather than every field/removal
combination the file editor supports having no live equivalent.

Not yet exercised in the running game.

## `powersockets.list` / `powersockets.set` - power sockets (round 103)

The live twin of the `power-sockets` world-map feature
(`Core/WorldSaves/Services/WorldMapFeatures/PowerSocketMapFeature.cs`, the save's `PowerSocketMap`).
`powersockets.list` returns
`{"sockets":[{"id","label","socketId"?,"pluggedInDevice","hasTimer"?,"timerMode"?,"powered"?,"x","y","z"}],"isHost":bool}`
for every loaded actor found by a single `FindAllOf("PowerSocket_ParentBP_C")` sweep
(hierarchy-inclusive, the same idiom every other area here uses), so every current subclass
(`PowerSocket_MgtCore_C`, `PowerSocket_ORDER_C`, `PowerSocket_VWinter_C`, `PowerSocket_XMAS25_C`,
confirmed from each class's own `super=` chain) comes back with no class name hardcoded anywhere.
`socketId` is the actor's own `GetPowerSocketID()` value (confirmed by bytecode to be
`BreakSoftObjectPath(MakeSavedObjectPath(Self)).PathString` - the same id space the save's
`PowerSocket_<hash>` leaf stores). `pluggedInDevice` reads the live `PluggedInDevice` object
reference directly and reports its real class name ("nothing plugged in" when free) - it needs no
game-data catalog, unlike the offline feature's asset-id resolution.

**Read-only, and confirmed to have no live-settable path at all** (`powersockets.set` exists only
to give a named, evidenced refusal rather than a generic "unknown command" - the C# session
(`LivePowerSocketsFeatureSession`) already refuses every field locally before ever reaching it).
Traced `PowerSocket_ParentBP_C`'s own bytecode exhaustively (every hash-suffixed struct-member
write in the class): `LatestSaveData.HasTimer_<hash>` and `LatestSaveData.TimerMode_<hash>` are
written in exactly one place, `Update_SaveData` (called from `SavePowerSocketToWorldSave`, the
actor's own "persist me now" entry point), and in BOTH branches of that function's own if/else
(attach vs. detach) they are set unconditionally to `false`/`0` - there is no branch, gate, or other
write site that ever sets either to anything else, and no read of either leaf exists in this class
either. This means whatever a save file carries for these two leaves is only ever what load-time
code restored (not itself traced; inferred by symmetry with every other area's load/save pairing),
and the very next time anything triggers a save on that socket both are forced back to false/0
regardless of what this module or a player wrote. `hasTimer`/`timerMode` are exposed read-only for
this reason (a `null` value means this specific actor's `LatestSaveData` struct could not be read
just now, not a guessed default). The full `E_PowerTimerModes` enum dump (9 real values plus
`E_MAX`) shows every enumerator is still an auto-generated `NewEnumeratorN` name with no meaningful
English label, so `timerMode` is reported as the raw byte rather than an invented friendly name -
offline's own `PowerSocketMapFeature` was left unchanged for the same reason (the fuller enum dump
does not add a meaningful choice list, only confirms the offline "cannot be determined" note was
already correct). `powered` is a bonus read-only field off the confirmed `IsPowered()` function.
Not yet exercised in the running game.

## `trams.list` / `trams.set` - trams (round 103, recall write path added round-103 follow-up, Facility only)

The live twin of the `trams` world-map feature
(`Core/WorldSaves/Services/WorldMapFeatures/TramMapFeature.cs`, the save's `TramMap`).
`trams.list` returns
`{"trams":[{"id","label","previousStation"?,"targetStation"?,"moving"?,"positiveDirection"?,"isAtStation"?,"hasPassengers"?,"containers","recallStations","x","y","z"}],"isHost":bool}`
for every loaded actor found by a single `FindAllOf("Tram_ParentBP_C")` sweep (hierarchy-inclusive),
covering both confirmed subclasses (`Tram_Default_C`, `Tram_ContainmentLift_C`). `previousStation`/
`targetStation` are the friendly station labels (`TramMapFeature`'s own `FriendlyStation` shape,
"PersistentLevel." stripped) read from the live `PreviousStation`/`TargetStation` object references.
`containers` is the on-board storage count off the confirmed `GetTramContainers()` function.
`recallStations` is the (possibly empty) array of friendly station labels a real
`TramSystem_RecallStation_C` actor links to this specific tram (see below). `trams.set` takes
`{"trams":[{"id","targetStation"?}]}`. Host only.

**`previousStation` is confirmed, not inferred by symmetry**: tracing `Tram_ParentBP_C`'s own
arrival sequence in its ubergraph bytecode shows that on reaching a stop the graph sets
`PreviousStation = TargetStation` (a plain instance-to-instance property copy), marks it dirty and
calls `OnRep_PreviousStation()`, then immediately calls
`GameMode:UpdateActorToWorldSave(Self, false, 14)` - the actual "persist this tram now" call, run
right after `PreviousStation` is updated. This is exactly the save-time mapping
`TramMapFeature.cs`'s own doc comment describes for `LastStation_`. Separately confirmed this round:
`TramSystem_Station_C:TramReachedLocation`'s own bytecode is a short, unbranching function whose
only meaningful statement sets its `ContinueMoving` out-param to `false` unconditionally - every
station stop is a real, full stop; a tram never sails through an intermediate station toward a
farther target on its own.

**`trams.set` (round-103 follow-up): a real recall write path, not fully bytecode-confirmed end to
end.** `Button_TramRecall_C` (super `Button_Tram_C`, itself super `Button_Generic_C` with no
properties/functions of its own) overrides only `GetInteractText` (confirmed from its own bytecode -
calls `TramReference:FindNextStation(Positive)` purely to build display text) and has no other
logic; `Tram_ParentBP_C`'s own bytecode never references the recall system at all. The real trigger
is `TramSystem_RecallStation_C` (a leaf class, no parent of its own beyond `Actor` - discovered with
a plain `FindAllOf`, no hierarchy sweep needed), with `LinkedTram`/`LinkedStation` object properties
and a `TramRecallPressed(Activated: bool)` function whose own local-variable list (properties-only
view; its `ScriptBytecode` was not part of this round's dump) - `FindNextStation`/
`GetDirectionFromStation`/`GetNextStopPoint`/`IsStationLocked` calls plus a counted loop - proves it
performs real multi-hop pathfinding to determine whether `LinkedStation` is reachable and which
direction reaches it, not a guessed name. `SetNextStopPoint(Positive, CurrentPoint)` on the tram
itself IS fully traced this round: it calls the rail's `GetNextStopPoint`, writes `TargetStation`,
and calls `SetMoving(true)` - a real, working one-hop "start heading this way" call.

`trams.set`'s `targetStation` finds a `TramSystem_RecallStation_C` whose `LinkedTram` is the
requested tram and whose `LinkedStation` (by friendly label) is the requested station, then calls
that instance's own `TramRecallPressed(true)` - the game's own function, never reimplemented.
Refuses up front if the tram's moving state cannot be confirmed or the tram is already moving, and
refuses if no recall station links this exact tram/station pair (live can only reach a station some
placed recall station actually serves - narrower than offline's "any station the save has ever
referenced", but real). After pressing, re-reads `Moving`/`PreviousStation` and accepts either the
tram now moving (a hop toward the target started - the journey may still be in progress; every
station is a confirmed full stop, so a distant recall is asynchronous and multi-step) or the tram
already at the requested station as success; a press with no confirmed effect is an honest error,
matching `elevators.set`'s own discipline. **Still open for a future round**: `TramRecallPressed`'s
own bytecode (to confirm exactly what it calls on `LinkedTram` and whether it gates on
host/`IsServer()` itself) and `TramSystem_Rail_C`'s bytecode (`GetNextStopPoint`/
`GetDirectionFromStation`) were not part of this dump - the coordinator can supply
`TramSystem_RecallStation.json`/`TramSystem_Rail.json` to close this with full certainty. Not yet
exercised in the running game.

## `npcspawns.list` / `npcspawns.set` - NPC spawners (round 102, cooldownRemainingSeconds made settable round 110)

The live twin of the `npc-spawns` world-map feature (`Core/WorldSaves/Features/NpcSpawnMapFeature.cs`,
the save's `NPCSpawnMap`, whose leaves are `CurrentCooldownRemaining_`/`LastDayOnCooldown_`/
`SpawnCount_`/`HasSpawnedOnce_`/`MinutesPassedCooldownStarted_`/`HasBeenEncounteredOnce_`).
`npcspawns.list` returns
`{"spawners":[{"id","label","controllable","onCooldown"?,"cooldownRemainingSeconds"?,"cooldownDaysRemaining"?,"hasSpawnedOnce"?,"hasBeenEncounteredOnce"?,"spawnCount"?,"x","y","z"}],"isHost":bool}`.
`npcspawns.set` takes
`{"spawners":[{"id","cooldownRemainingSeconds"?:number,"resetCooldown"?:true,"forceSpawn"?:true}]}`.
Host only.

**Class discovery covers three roots**, a genuine correction against the round's own task brief
(which assumed a single hierarchy): the coordinator's CUE4Parse dump shows the overwhelming
majority of spawner classes (every zombie/pest/gatekeeper/order/pillager/darklens/security-bot/
peccary/winter-sprite/single-grunt family, confirmed one by one from each class's own `super=`)
chain up to `Abiotic_NPCSpawn_ParentBP_C`, so a single `FindAllOf` on that root covers them. But
`NPCSpawn_Entity_C` and `NPCSpawn_Narrative_C` both declare `super=Actor` directly in the dump -
two genuinely separate roots (`NPCSpawn_Trader_Chef_C`/`NPCSpawn_Trader_Marion_C` chain from the
narrative one; `NPCSpawn_VOTV_UFO_C`/`NPCSpawn_VOTV_Wisp_C` from the entity one), swept as
additional roots. Neither exposes any of the cooldown/count system below at all, so their rows
always report `controllable:false` with every state field absent.

**Cooldown/count state lives partly on the spawner actor itself and partly on a native world
subsystem, `AIDirectorSubsystem`** (`/Script/AbioticFactor`, obtained the same way the spawner's
own bytecode gets it: `SubsystemBlueprintLibrary::GetWorldSubsystem`, passing itself as the
spawner argument to the subsystem's own per-spawner functions):
- `onCooldown` - the spawner's own `IsOnCooldown()` (confirmed bytecode:
  `GetCurrentCooldownRemainingFromSpawner > 0 OR GetCooldownDaysRemainingFromSpawner > 0`).
- `cooldownRemainingSeconds` - `AIDirectorSubsystem:GetCurrentCooldownRemainingFromSpawner(spawner)`.
- `cooldownDaysRemaining` - `AIDirectorSubsystem:GetCooldownDaysRemainingFromSpawner(spawner)`.
- `hasBeenEncounteredOnce` - `AIDirectorSubsystem:GetHasBeenEncounteredOnceForSpawner(spawner)`.
- `hasSpawnedOnce` - the spawner's own direct `HasSpawnedOnce` property.
- `spawnCount` - the spawner's own `GetCurrentSpawnedCount(false)`.

The offline leaf `MinutesPassedCooldownStarted_` has no confirmed live counterpart in the dump and
is not exposed here - **re-checked round 110** against `SetSpawnOnCooldown`'s own day argument
(see below): it is whole-day granularity only (fed straight from `AI
Director.DayNightManager.CurrentDay`, itself a whole-day counter), with no minutes-within-the-day
component to derive or set this leaf from, so the conclusion stands.

**`cooldownRemainingSeconds` is a real, persistent editable value (round 110), not just a
read-only figure.** `SetSpawnOnCooldown(TimeRemaining: double, InCurrentDay: int)` is a real,
actor-level `BlueprintCallable` function whose full bytecode was traced this round: it
unconditionally sets `CooldownDay = InCurrentDay` first, then - only when `InCurrentDay==0` and
`AI Director`/`AI Director.DayNightManager` are both valid - overwrites `CooldownDay` with the real
current game day looked up from the `DayNightManager`. It then calls
`AIDirectorSubsystem:SetCooldownForSpawner(spawner, TimeRemaining, CooldownDay,
spawner.OnlySpawnOnce)`, passing `TimeRemaining` through **unchanged** (no clamping or zeroing
anywhere in the traced bytecode) - confirming arbitrary values really do reach the subsystem, not
just `0`. `npcspawns.set` calls `spawner:SetSpawnOnCooldown(wanted, 0)` for an explicit
`cooldownRemainingSeconds` request - `InCurrentDay=0` so the day resolves to "today" the same way
`resetCooldown` already relies on, changing only the seconds figure. A non-numeric value fails
with a named error (`"cooldownRemainingSeconds must be a number"`).

**The other two writes remain momentary "do it now" toggles, not persistent state.** `resetCooldown`
calls the same `SetSpawnOnCooldown(0.0, 0)` - exactly "let this spawner fire again right now"; the
function calls `AIDirectorSubsystem:SetCooldownForSpawner` itself, so this module never calls that
subsystem function directly. **When both `cooldownRemainingSeconds` and `resetCooldown` are sent
in the same row, `resetCooldown` wins** (applied second, matching `triggers.lua`'s own "the more
complete reset action wins over an arbitrary value sent in the same row" precedent). `forceSpawn`
calls the spawner's own `TrySpawnNPCNew(false, true, false)`, falling back to the older
`TrySpawnNPC` with the same arguments when the newer function is absent - `ForceSuccessByTrigger=true`
is confirmed from the bytecode to gate multiple individual spawn-check `JumpIfNot` branches,
matching what a `Trigger_*` volume would pass to force a spawn. **Genuinely unverified against the
running game**: whether the resulting NPC actually appears is not confirmed by any live capture,
only by this bytecode reading - a call that does not error is reported as requested, not a
confirmed spawn.

Large map: 918 entries in the Facility fixture. Deliberately excluded from the desktop app's
periodic live-tab refresh loop, the same performance care `containers`/`resourcenodes`/
`destructibles` already document - a tab visit still fetches once, and the REFRESH pattern those
areas use applies here too. Not yet exercised in the running game.

## `triggers.list` / `triggers.set` - scripted world triggers (round 102)

The live twin of the `triggers` world-map feature (`Core/WorldSaves/Features/TriggerMapFeature.cs`,
the save's `TriggerMap`, whose leaves are `UniqueTriggerID_`/`TimesTriggered_`). `triggers.list`
returns
`{"triggers":[{"id","label","timesTriggered"?,"hasBeenTriggeredOnce"?,"triggerLimit"?,"x","y","z"}],"isHost":bool}`.
`triggers.set` takes `{"triggers":[{"id","timesTriggered"?,"reset"?:true}]}`. Host only.

**Rows are identified by `id` = the trigger's own `UniqueTriggerID` string** (e.g.
`WF_NewGameStarted`, `CA_PunchCard_TutorialPanelTrigger`), NOT the actor's `GetFullName()` the way
every other live world area in this file keys its rows - deliberate, and confirmed directly off
`Abiotic_TriggerVolume_ParentBP_C`'s own declared, unsuffixed `UniqueTriggerID` property
(`FNameProperty`, set per placed instance in the level) - it matches the save file's own
`TriggerMap` key exactly. The round's own task brief guessed the live counts might live in a
single map on the game mode/game state; checked and wrong: `Abiotic_WorldSave_C` does carry its
own `TriggerMap` (confirmed in the dump), but each placed trigger actor keeps and persists its own
entry directly (see below) - there is no separate live map object to read or write through.

**Class discovery** sweeps one confirmed root, `Abiotic_TriggerVolume_ParentBP_C` (the dump's own
example subclass, `Trigger_CompendiumExploration_C`, chains to it directly). `FindAllOf` is
hierarchy-inclusive, so every one of the offline feature's ~17 `Trigger_*` classes is expected to
come back from that single sweep with no leaf-class list, though the probe's package set only
happened to include the one confirmed example.

**Field mapping**, read straight off the class's own declared properties and its
`ResetTriggerState()`/`SaveTriggerData()` bytecode: `timesTriggered` is the direct `TimesTriggered`
property; `hasBeenTriggeredOnce`/`triggerLimit` are informational reads of the matching direct
properties (`triggerLimit` is never written here). `timesTriggered` writes directly then calls the
trigger's own real, no-argument, actor-level `SaveTriggerData()` to persist it - the same
persistence call `ResetTriggerState()` itself calls internally. `reset` instead calls the trigger's
own real, no-argument `ResetTriggerState()`, confirmed from its own bytecode to do exactly:
`TimesTriggered = 0`, `HasBeenTriggeredOnce = false`, re-enable the trigger volume's collision and
re-allow overlap on its linked trigger arrays, then call `SaveTriggerData()` - a strictly more
complete reset than a bare `timesTriggered=0` write, so `reset` on the same row as a
`timesTriggered` value ignores the latter. Not yet exercised in the running game.

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

**Round 79: `mutationProgress` is now editable, not just a readout.** Both the offline and the
live COMPANIONS tab expose it through the shared "Feeding and mutation" panel. Negative values are
rejected; nothing else is capped, because `DT_Pets` carries no explicit threshold field and the
largest value observed across this project's fixture saves (`PetCatalog.ObservedMaxMutationProgress`,
currently `3`) comes from only two pets, so it is shown as a hint rather than enforced.

**Session 2026-09-17: `petMutation` is now editable too, through the same panel.** Cross-checking
this project's two real carried pets against the installed game's own `DT_Pets` mutation lists
(`PetCareCatalog.MutationOptionsFor`, see `docs/reference/research/research-garden-crops-and-pet-mutation.md`)
showed the stored int is a 1-based position in the pet's own mutation family, resolvable back to
the pet's exact current identity in both cases. The UI now offers a picker built from that
resolution (plus "Not mutated" = 0, plus the current saved value when it does not resolve),
wired identically for the offline session and the live COMPANIONS tab - no Lua change was needed,
since `companions.lua` already read and wrote `petMutation` symmetrically with `mutationProgress`.
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

**Round 106 re-grounding: `canLock` is already as wide as the game honestly allows.** Re-checked
against a fresh pak dump (`pass2\Abiotic_CharacterProgressionComponent.json`/`layouts.txt`):
`RecipesUnlockedArray` is confirmed a plain `FArrayProperty`, `OnRep_RecipesUnlockedArray` is a
real exported function, and no dedicated "forget"/"lock"/"remove recipe" RPC exists anywhere on
this class - array-replace-then-RepNotify is the only relock path the game exposes. Host authority
stays a hard requirement, not just a cautious default: a non-host client's own write to a
replicated property never actually persists (only the server's authoritative copy does, and it
would simply overwrite the client's local change on the next network update), so there is no
honest way to widen `canLock` to a non-host client even with replication support present. Added
`tests/cases/recipes.lua` to the Lua harness to cover this (previously untested: no `recipes` case
file existed, and the host relock branch's `OnRep_RecipesUnlockedArray()` call had no fixture
method to call, so a real bug there would not have failed any test).

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

**Round 106: kill-requirement compendium sections are settable too.** The table row above ("3 |
`KilLRequirement` | no") reflected round 77's assumption, based on no installed mod ever calling
the RPC that way - not on reading the function it forwards to. This round disassembled the full
bytecode of that private function, `Server Try Unlock Compendium Section` (dumped whole in
`pass2\Abiotic_CharacterProgressionComponent.json`): it runs an `EX_SwitchValue` on the unlock
type with four cases, not three - 0/1/2 select `Compendium_ExplorationSections`/`EmailSections`/
`NarrativeNPCSections` exactly as already documented, and case 3 selects `Compendium_KillSections`
(an `FArrayProperty` with the same `Net | RepNotify` shape as the other three, `RepNotifyFunc`
`OnRep_Compendium_KillSections`), then calls `KismetArrayLibrary.Array_Add` with `CompendiumRow`
on whichever array the switch selected. The only gate before that add is a duplicate check
(`HasCompendiumSectionUnlocked`) shared by all four cases - nothing in this path reads
`Compendium_KillCount`/`AllowedCompendiumKills`, so the RPC adds the row unconditionally, the same
as the other three types. `codex.get` now also reports `canUnlockKillSections`
(`sectionType: "KillRequirement"` is accepted by `codex.set`'s `compendium` array, and
`Compendium_KillSections` is included in the `compendium` read/clear arrays); older agents omit
the field, and the desktop app keeps a kill-requirement-only row read-only against them.

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
{"isHost":true,"canEditRecipes":false,"globalRecipeEditsUnavailableReason":"runtime-unsupported",
 "recipesUnlocked":["recipe_bandage"],"recipesResearched":[],
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
and replication notification support. When `canEditRecipes` is `false` it also reports
`globalRecipeEditsUnavailableReason`: `"not-host"`, `"no-replication"`, or
`"runtime-unsupported"` (an older UE4SS build without `TSet.Add`/`Remove`/`ForEach` on the recipe
sets - update UE4SS to fix it) - `null` once edits are supported. `WorldStoryTab` turns this into
a specific, localized message next to the disabled controls instead of just disabling them with
no explanation. `worldunlocks.set` accepts
`{"recipes":[{"id":"recipe_bandage","unlocked":true}]}`. Names and values are validated
before writes. Add/remove applies to both unlocked and researched sets, matching offline
world recipe editing, and both properties are marked dirty for replication. Other global
unlock lists remain read-only. The shared story tab now calls the session interface for
single and bulk recipe edits, retaining its existing story prerequisite gate.

**Round 106: the six `FArrayProperty` global lists are settable too** (`itemsPickedUp`,
`emailsRead`, `journalEntries`, `compendiumEmail`, `compendiumNarrative`,
`compendiumExploration`), independent of the recipe `TSet`s' extra runtime-capability check.
`worldunlocks.get` now also reports `canEditGlobalLists` and, when false,
`globalListEditsUnavailableReason` (`"not-host"` or `"no-replication"` - never
`"runtime-unsupported"`, since plain array assignment needs no `TSet` methods and works on every
runtime the recipe TSets themselves need an updated UE4SS build for). `worldunlocks.set` accepts
any of `{"itemsPickedUp":[{"id":"scrap_metal","present":true}], "emailsRead":[...], ...}` in the
same request as `recipes`, or on their own. Each list writes through the exact array-replace
technique `codex.lua`'s per-player `clear` path already uses (snapshot the current array, apply
additions/removals, validate every name, reassign the whole array), then a best-effort
replication-dirty mark: the pak dump shows none of these six properties (nor the two recipe
`TSet`s beside them) carries a `Net`/`RepNotify` flag, and no `OnRep_Global*` function exists
anywhere on the class, so no RepNotify is called after the write. This does not block real,
durable editing: the host's own authoritative `Abiotic_Survival_GameState_C` is what
`WorldSave_MetaData.sav` is written from, so a host write to any of these six lists is real
regardless of whether other connected clients' own local copies pick it up live. No UI surfaces
these six lists yet (the offline file editor has no browser for them either - only world recipes
get one); `LiveWorldUnlocksChannel.SetGlobalListAsync`/`LiveStorySession`'s matching members are
ready for a future `WorldStoryTab` extension.

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
