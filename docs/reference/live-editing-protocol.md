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
"durability","maxDurability"}` and `inventory.set` takes `{"edits":[{"kind","slotIndex",
"clear"?,"itemId"?,"stack"?,"durability"?,"maxDurability"?}],"playerId"?}`. `transmog` reads the
same `Abiotic_InventoryComponent_C` slot struct as the other three kinds, over the player's
`TmogInventory` component - the web editor's `LiveInventorySession` sends this kind for a
transmog slot exactly like backpack/equip/hotbar, so no separate command pair exists for it.
Armor-visibility toggles have no confirmed live property yet, so the web editor's transmog tab
shows them read-only for a live session instead of an edit that would silently not apply.

An `id` in any world area is the game's own full object name for that exact actor
(`GetFullName()`), re-resolved by a fresh scan on every write: the loaded set of doors, crates,
NPCs and loose items changes constantly, so an index from an earlier list is never trusted.

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

`world.set` takes any subset of `{"timeSeconds","day","weather","nextWeather"}`. `weather`
triggers that event immediately (`None` ends the current one); `nextWeather` queues it for the
next in-game day. Host only.

## `flags.list` / `flags.set` - quest and story flags

`flags.list` returns `{"flags":[{"name","isSet"}],"isHost":bool}`: every world-flag row the game
knows (the same names as the world save's `WorldFlags` array and `QuestFlagCatalog`), plus any
set flag the table does not list. `flags.set` takes `{"flags":[{"name","isSet"}]}` and applies
them in order through the game's own world-flag subsystem, so dependent doors, effects and
triggers react exactly as if the flag had been earned in play. Host only.

## `story.get` / `story.set` - main-quest indicator (read-only)

`story.get` takes no payload and returns `{"currentQuestRow":string,"isHost":bool}`.
`currentQuestRow` is the running game's current-quest row name (`"None"` when it reports no
active quest), read from the replicated `CurrentQuest` field on `Abiotic_Survival_GameState_C`
(confirmed by `tests/AbioticEditor.Probes/LiveClassPropsProbe.cs` dumping
`AbioticFactor/Content/Blueprints/Meta/Abiotic_Survival_GameState.uasset`: a `CurrentQuest`
`FStructProperty` plus an `OnRep_CurrentQuest` client notify). The Razor host feeds this row into
the same `StoryProgressionCatalog` lookup the file editor's chapter checklist uses; a row the
catalog does not recognise renders as "unknown chapter", the existing graceful fallback for an
unfamiliar save value.

**There is no live write path for the story chapter.** The shipped
`AbioticFactor-Win64-Shipping.pdb` has a native `bool UWorldFlagSubsystem::FindCurrentQuest(
FQuestRowHandle&)` and a `UQuestHandleFunctionLibrary` (`MakeQuestRowHandle`,
`GetQuestRow(FQuestRowHandle, FQuestData&, ERowValid&)`, `GetAllQuestRowNames/Handles`,
`DoesQuestRowExist`), but no `SetCurrentQuest` or any other native function that writes it, and no
settable `OnRep_CurrentQuest` (it is an outbound notify, not an input). `story.set` therefore
always returns `ok:false` with a player-safe explanation; the shared story tab hides its SET
controls whenever the session reports `CanSetStoryChapter: false` instead of offering a button
that cannot work. Setting a chapter's own trigger flags on the QUEST FLAGS tab (`flags.set`)
remains the real live way to advance the story, exactly like the file editor's "unlock story
through here" action does on disk.

The world clock and weather that used to have their own `LiveWorldTab` now render inside the same
shared story tab (`WorldStoryTab`, bound to `IWorldStorySession`) - see `world.get`/`world.set`
above; nothing changed in that wire shape, only which Razor component renders it.

## `doors.list` / `doors.set`

`doors.list` returns `{"doors":[...],"isHost":bool}` with, per loaded door: `id`, `label` (class
name), `kind` (`simple` for hinged doors, `security` for sliding security doors), `state` (the
`E_DoorStates` number the file editor's `DoorStateNames` maps: 0 closed, 1 open, 2 locked, ...),
`isOpen`, `oneWayUnlocked`, `disabled`, and world position `x`/`y`/`z` in centimetres.
`doors.set` takes `{"doors":[{"id","kind","state"?,"isOpen"?,"oneWayUnlocked"?,"disabled"?}]}`
- `state` applies to hinged doors, `isOpen` to security doors. Host only.

## `containers.list` / `containers.set`

`containers.list` returns `{"containers":[{"id","label","x","y","z","slots":[...]}],"isHost":bool}`
where each slot is `{"slotIndex","itemId","isEmpty","stack","durability","maxDurability"}` - the
same slot shape as `inventory.list`, because a container's storage is the same inventory
component class as a player's backpack. `containers.set` takes `{"id","edits":[{"slotIndex",
"clear"?,"itemId"?,"stack"?,"durability"?,"maxDurability"?}]}`. Host only.

## `dropped.list` / `dropped.remove`

`dropped.list` returns `{"items":[{"id","itemId","stack","x","y","z"}],"isHost":bool}` for every
item lying loose in the loaded world that nobody has picked up. `dropped.remove` takes
`{"ids":[...]}` and returns `{"removed":n}` - the count actually found and despawned. Host only.

## `bases.list` / `bases.set` - deployables (round 76)

`bases.list` returns `{"deployables":[{"id","className","x","y","z","customName","hasInventory",
"storedItemCount"}],"isHost":bool,"supportsBenchUpgrades":false}` for every deployable currently
loaded (`AbioticDeployed_ParentBP` and every subclass - benches, furniture, defenses,
containers). `bases.set` takes `{"id","customName"}` and renames the object immediately. Host
only, like `containers.set`/`doors.set`.

`supportsBenchUpgrades` is always `false` and there is no way to install or remove a bench
upgrade over this protocol: the live bench class does expose an `UpgradeTagContainer` property,
but no installed mod touches it, there is no confirmed way to build a valid
`BenchUpgradeRowHandle` from a bare row name (unlike weather/flags, which can always enumerate
real handles from a matching function library), and the array-of-struct add/remove API for a
live `GameplayTagContainer` has no working precedent to copy. The shared `WorldBasesTab` hides
the bench-upgrades section entirely for a live session instead of guessing. Opening a bench or
crate's contents inline (the file editor's slot grid) is also file-only - it shares the
CONTAINERS tab's staged slot model, which has no live equivalent wired into this area; use the
CONTAINERS tab for live slot editing instead.

## `vehicles.list` / `vehicles.set` - round 76

`vehicles.list` returns `{"vehicles":[{"id","vehicleId","vehicleClass","driveable","x","y","z"}],
"isHost":bool,"supportsWreckedState":false}` for every vehicle currently loaded
(`ABF_Vehicle_ParentBP` and its subclasses). `vehicles.set` takes `{"id","driveable"?,"x"?,"y"?,
"z"?}` - `driveable` is a direct property write (`VehicleDriveable` + `OnRep_VehicleDriveable`,
confirmed on the live class layout); a position takes effect via `K2_TeleportTo` (confirmed real,
used the same way in `CheatConsoleCommands/AFUtils/BaseUtils/BaseUtils.lua`'s
`TeleportActorToActor`), keeping the vehicle's current rotation. Host only.

`supportsWreckedState` is always `false` and `destroyed` is never accepted or returned: the
closest hit anywhere in the game's own class layout for the save's `Destroyed` flag is a local
variable inside the vehicle's own `UpdateWorldSave` function (computed from something at save
time, not a class member this protocol can read or write), so the shared `WorldVehiclesTab`
hides the "Wrecked" checkbox for a live session rather than showing a value nobody can read.
On-board vehicle storage is also not exposed here (`hasInventory`/`inventoryItemCount` are
always `false`/`0` for a live vehicle) - it is a different inventory component than the world
containers this protocol's `containers.*` commands already cover.

## `pets.list` - round 76 (no `pets.set`; no general live path)

`pets.list` returns `{"pets":[],"isHost":bool,"available":false,"reason":"..."}`. There is
deliberately no `pets.set`. Research found no general live path for tamed pets: a tamed pet is
the same `NPC_Base_ParentBP_C` actor `npcs.list` already finds, but the fields a world save's
`PetNPC` record needs are exposed wildly inconsistently between creature families in the game's
own class layout - the Pest family (and Skink, which inherits from it) directly exposes
`PetName`/`Guid`/`FollowingOwner`/`DynamicProperties`/`SanitizedName`; the Peccary family exposes
none of those; the Lamogi family exposes only a bare `WasTamed` bool. There is no single property
to match a live actor back to a specific save record across every species, health has no
confirmed setter (`GetCurrentHealthMap` exists only as a getter the game's own save-writing code
calls), and species mutation would mean an unconfirmed despawn/respawn through the GameMode's
`SpawnPet` function. The shared `WorldPetsTab` shows `reason` instead of an empty list so this
reads as "not supported yet", not "no pets here".

## Extending this for a new area

Adding a new live-editable area (inventory, more of world state, ...) means: a new command pair
on both sides (`<area>.get`/`<area>.set` following the existing naming), a new `Live<Area>Channel`
in `Core/LiveEditing/<Area>/` mirroring the shape of `LivePlayerVitalsChannel`/
`LivePlayerSkillsChannel`, a new handler pair in the Lua mod's `main.lua`, and the command names added to the
native helper's forwarding allowlist (`AbioticEditorLiveAgentHelper/src/main.cpp`). No `hello`/envelope-level change is
needed for a new area; the envelope's `payload`/`result` already accept either a flat object or a
flat array of them, which has covered every area so far.
