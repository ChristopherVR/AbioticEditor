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
array of `{"kind":"backpack"|"equip"|"hotbar","slotIndex","itemId","isEmpty","stack",
"durability","maxDurability"}` and `inventory.set` takes `{"edits":[{"kind","slotIndex",
"clear"?,"itemId"?,"stack"?,"durability"?,"maxDurability"?}],"playerId"?}`.

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

## `recipes.get` / `recipes.set`

Live recipe-unlock editing, the counterpart to the file editor's RECIPES tab. `recipes.get` takes
an optional `{"playerId":"…"}` payload (omitted targets the local player) and returns
`{"unlockedIds":["Recipe_Foo", ...]}` - only the recipe row names the running character currently
has unlocked (the full catalog of every recipe the game knows comes from the desktop app's own
game-data vocabulary, the same one the file editor uses; the live agent has no path to enumerate
`DT_Recipes`' row names, only what one specific character has already unlocked).

`recipes.set` takes `{"playerId":"…", "unlockIds":["Recipe_Foo", ...]}` and unlocks each id
immediately. **There is no way to re-lock a recipe live** - the game's own
`Abiotic_CharacterProgressionComponent_C` has no lock/relock/remove-recipe function anywhere in its
exported API (confirmed by `tests/AbioticEditor.Probes/LiveClassPropsProbe.cs`, fragment
"CharacterProgressionComponent"), only "unlock" ones. The desktop app's RECIPES tab disables
un-checking an already-unlocked row when connected live instead of sending a request that would
silently do nothing.

## `codex.get` / `codex.set`

Live journal/codex ("GATEPal") editing, the counterpart to the file editor's EMAIL, NOTES and FISH
sections. `codex.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"emails":["Email_Foo"],"journals":["Journal_Bar"],"fish":["Fish_Baz"],"compendium":["Compendium_Qux"]}
```

Each list is the row names the running character currently knows in that section (again, the full
catalog of possible ids comes from the desktop app's own game-data vocabulary). `codex.set` takes
`{"playerId":"…", "emails"?:[...], "journals"?:[...], "fish"?:[...]}` and marks each given id known
immediately; omitted categories are left untouched.

**`compendium` is read-only** - it is reported by `codex.get` but `codex.set` does not accept it.
The game's only unlock function for it, `Request_UnlockCompendiumSection(CompendiumRow,
UnlockType)`, takes an `UnlockType` enum parameter this project could not ground: the one place a
real mod calls it (`CheatConsoleCommands/scripts/Features.lua:894-900`, the journal-entry-unlocker
hook) only ever forwards a value read live off a UI widget property, never a literal, and the pak
dump carries no enum value names to guess from. The desktop app's COMPENDIUM section is shown but
not editable when connected live, for the same reason.

**There is no way to un-know an e-mail, note or fish live either** (same one-directional limit as
recipes above - no such function exists). The desktop app disables un-checking an already-known row
when connected live.

## `general.get` / `general.set`

Live "bulk unlocks" editing, the counterpart to the file editor's General tab ITEMS SEEN, ITEMS
CRAFTED and MAPS rows (the account/owner-id change has no live counterpart at all - see below).
`general.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"itemsSeen":["metal_scrap"],"itemsCrafted":["torch"],"maps":["Sector_A"]}
```

`general.set` takes `{"playerId":"…", "itemsSeen"?:[...], "maps"?:[...]}` and discovers/unlocks
each given id immediately; omitted categories are left untouched.

**`itemsCrafted` is read-only** - it is reported by `general.get` but `general.set` does not accept
it. The game's `CharacterProgressionComponent` tracks crafted items automatically (from actually
crafting something) but exposes no single-item "mark as crafted" function anywhere in its exported
API, unlike items-seen (`Server_CheckNewItemPickedUp`) and maps (`Server_AddMapToJournal`). The
desktop app's ITEMS CRAFTED row disables its DISCOVER ALL button when connected live.

**The account/owner-id change has no live path at all** and is not part of this wire protocol:
renaming which save file a character belongs to is purely a file-system operation, with no running
in-game concept to change. The desktop app hides that section's CHANGE button when connected live
and shows the connected player's own id as a plain readout instead.

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
`LivePlayerSkillsChannel`, a new handler pair in the Lua mod's `main.lua`, and the command names added to the
native helper's forwarding allowlist (`AbioticEditorLiveAgentHelper/src/main.cpp`). No `hello`/envelope-level change is
needed for a new area; the envelope's `payload`/`result` already accept either a flat object or a
flat array of them, which has covered every area so far.
