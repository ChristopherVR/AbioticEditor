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
installed mod exercises this actor class; this is the first live write to it.
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
"health"?,"maxHealth"?,"xp"?,"mutationProgress"?,"petMutation"?,"playerId"?}` and returns no
result. `clear` empties the slot and ignores every other field, exactly like `inventory.set`'s
`clear`. Applying happens immediately, one pet at a time - there is no batch form.

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
CRAFTED, MAPS, BACKGROUND and TRAITS rows (the account/owner-id change has no live counterpart at
all - see below). `general.get` takes an optional `{"playerId":"…"}` payload and returns:

```json
{"itemsSeen":["metal_scrap"],"itemsCrafted":["torch"],"maps":["Sector_A"],
 "traits":["Trait_Chef"],"background":"PhD_HumanBio"}
```

`general.set` takes `{"playerId":"…", "itemsSeen"?:[...], "maps"?:[...], "background"?:"…"}` and
discovers/unlocks each given id (and applies the background) immediately; omitted fields are left
untouched.

**`itemsCrafted` is read-only** - it is reported by `general.get` but `general.set` does not accept
it. The game's `CharacterProgressionComponent` tracks crafted items automatically (from actually
crafting something) but exposes no single-item "mark as crafted" function anywhere in its exported
API, unlike items-seen (`Server_CheckNewItemPickedUp`) and maps (`Server_AddMapToJournal`). The
desktop app's ITEMS CRAFTED row disables its DISCOVER ALL button when connected live.

**`background` (round 77) IS a real live write.** `Abiotic_PlayerState_C` declares a plain,
no-hash-suffix `PhD : FNameProperty` with no `OnRep_PhD` - the same row-name concept the file
format's `PhD_` tag stores. `general.set`'s `background` writes it directly on the connected
player's `PlayerState` (found via `APawn.PlayerState`, the base-engine property `main.lua`'s own
`localPlayerId()` already reads off the player CONTROLLER for a different purpose); no RPC is
needed because a replicated UPROPERTY changed on the server's own authoritative object replicates
to owning clients on the next network update.

**`traits` (round 77) is read-only** - it is reported by `general.get` but `general.set` does not
accept it. `CharacterProgressionComponent.Traits` is read the same way the reference mod's own
"traits" console command does. The only functions that touch it (`SetTraits`/`GetTraits`/
`InitializeTraits`) carry no `Server_`/`Request_` prefix - they are not RPCs, and are used only by
the one-time character-creation flow (`Abiotic_PlayerController.Server_SetupInitialTraits` ->
`Client_DoTraitSelectionSequence` -> `GoToTraitsSelection`); calling them mid-game would re-run
that flow rather than swap one trait. The native engine's only trait-adjacent RPCs
(`UCharacterBuffComponent::Server_AddTraitBuff`/`Server_RemoveTraitBuff(FBuffDebuffRowHandle)`,
found in the shipped PDB) apply a different, temporary gameplay buff keyed by a buff/debuff row
handle - they do not touch `CharacterProgressionComponent.Traits` or the save's `Traits_` array,
so calling them would not actually add or remove a trait the way this list means. The desktop
app's GENERAL tab shows TRAITS as a plain readout with a note pointing to the file-based CHARACTER
tab for full add/remove.

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
