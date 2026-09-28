# Research: how power connections are stored (fixture evidence)

Scope: roadmap section "Power routing and network construction", step 1 (trace the stored links), step 3
(connection rules: what is known and what is not), and the live-durability note. Everything below was measured
on the real fixtures; nothing here was verified in a running game. The read-only model that encodes these rules
is `src/AbioticEditor.Core/Services/World/PowerNetwork/` (tests: `tests/AbioticEditor.Tests/PowerGraphTests.cs`).

## 1. The three stored fields

A `PowerSocketMap` entry (map in every `WorldSave_<Region>.sav`) carries:

| Leaf | Type | Meaning |
|---|---|---|
| `PowerSocket_` | Str | The socket's own id. Equal to the map key in all 552 server-fixture records. |
| `PluggedInDeviceAssetID_` | Str | The 32-hex asset id of the device drawing from this socket, or `-1` for nothing. |
| `ExtraPoweredDeviceAssetIDs_` | Array of Str | Never non-empty in any fixture (all four world folders). Meaning unverified. |
| `HasTimer_`, `TimerMode_` | Bool, Byte | Timer state, see section 4. |

### Device identity

`PluggedInDeviceAssetID_` is a `DeployedObjectMap` key. Evidence (server Cascade world, 40+ region saves,
355 plugged sockets): every resolved id is a `DeployedObjectMap` key AND the value of that entry's
`ChangableData.AssetID` leaf (289 distinct ids hit both places, 0 hits anywhere else except 2 unrelated hits in
`LeyakContainmentIDs`). The live agent's bytecode notes agree: `Update_SaveData` writes the plugged device's
changeable-data `AssetID` into this leaf. So the id is the deployable's stable GUID, not an actor path.

The device records themselves (`Deployed_PlugStrip_C`, `Deployed_CableReroute_C`, `Deployed_Battery_T*_C`,
`Deployed_Plugboard_C`, `Deployed_LaserPowerConverter_C`, ...) have NO power leaf. Their property set is the
generic deployable set (`Class_`, `ActorPath_`, `ChangableData_`, `Transform_`, `ContainerInventories_`, ...).
Battery charge is `ChangableData.LiquidLevel` (T1 43-50, T2 100, T3 350 in fixtures; `CurrentLiquid` is
`E_LiquidType::NewEnumerator8`), which is a stored charge, not a connection.

### Which side owns a link

The socket record owns it. A string search of every top-level map of every save for each plugged device GUID
finds only: the socket's `PluggedInDeviceAssetID_`, the device's own map key, and its `ChangableData.AssetID`.
There is no reciprocal record on the device, in any other map, or in the metadata save. A rewrite therefore has
exactly one record to change per link (but see the write caveat in section 5).

### Socket identity: two key shapes

Server world, 552 sockets, 0 duplicate keys:

* 45 level-placed sockets: key is an actor path (`/Game/Maps/Facility_Office1.Facility_Office1:PersistentLevel.PowerSocket_ParentBP_C_2`,
  also `PowerSocket_MgtCore_C_5`, `PowerSocket_ORDER_C_15`, `PowerSocket_VWinter_C_0`). These live in the
  region save of the level that contains them.
* 507 device-owned sockets: key is the owning device GUID (32 hex) followed by a 1-digit outlet number, e.g.
  `4E4122BA4C5DAC171C5B67B88DEA954F2`. All 507 are in `WorldSave_Facility.sav`.

Ownership by key prefix is an inference, but a strongly supported one: of the 507, the 271 whose 32-char prefix
resolves to a `DeployedObjectMap` entry resolve ONLY to power classes (Battery T1/T2/T3, CableReroute, PlugStrip,
Plugboard, LaserPowerConverter, plus one `Deployed_ExerciseBike_C`), with class-consistent outlet counts:
CableReroute and Battery always exactly outlet `1`; PlugStrip outlets `1..3`; Plugboard outlets `1..6`;
ExerciseBike and LaserPowerConverter outlet `0`. There were zero counterexamples.

So a link reads: "outlet N of device O (or a fixed level socket) supplies device P".

### Cross-save and missing endpoints (server world)

* 355 plugged links: 254 resolve in the same save, 37 resolve in a DIFFERENT save (all level sockets in region
  saves pointing at devices in `WorldSave_Facility.sav`), 64 resolve nowhere.
* 236 of the 507 device-owned sockets have an owner GUID that appears in NO save (159 distinct owners; the GUID
  appears only in the socket key). These are orphaned socket records. Almost all are unplugged; 2 still plug a
  device. The cause is not known from data (hypotheses: the owning device was packaged or destroyed and its
  socket record was not cleaned up, or it lives in a save not in the fixture set). The model reports them as
  `SocketOwnerMissing` rather than guessing.
* 2 devices are named by two sockets each (`1F43BB0A40083FFDB0ACF291D2EE5591`, a crafting bench, fed by an
  unresolved-owner outlet and by a T2 battery outlet; `9B3A63ED4B0C04D4AA331B8CDD79B337`, a plug strip, fed by an
  unresolved-owner outlet and a LaserPowerConverter). Whether the game permits this, or whether one of the two is
  a stale record, is not verified. Reported as `DeviceFedByMultipleSockets`.
* No cycles. Longest upstream chain in the fixture: 18 links. Nine groups of linked items never reach a
  level-placed socket (largest 37 items); that may just mean batteries or other devices can be sources, which is
  not verified.

### Runtime actor references

Deployables carry `ActorPath_` (e.g. `/Game/Maps/Facility.Facility:PersistentLevel.Deployed_PlugStrip_C_2147471057`),
a `SoftObjectPath` naming the spawned actor. Nothing in the saves ties a socket to an actor path: device-owned
socket keys use the GUID, not an actor name. Mapping a stored link to the live actors on either end has to go
through the GUID (`AssetID` on the live device) and the live `GetPowerSocketID()`, whose equality with the save
key is asserted by the live agent's notes (`live-agent/.../powersockets.lua`) but is not confirmed in a running
game. `PowerLiveOverlay` relies on that equality and says so.

## 2. Device roles the model recognises

By class-name substring only (`PowerGraphBuilder.RoleOf`): Battery, PlugStrip, Plugboard, CableReroute,
LaserPowerConverter; everything else that gets plugged in is `Other`. No switch, generator or relay class appears
in any fixture's power data, so none is modeled; the model does not invent them. `Container_LaserCollector_C`,
`Deployed_LaserEmitter_C` and `Deployed_TeslaCoil_C` appear only as plugged-in consumers.

## 3. Connection rules: known vs unverified

Known from data (stored shape):

* A link is one socket-record field naming one device GUID. Sockets seen have at most one plugged device.
* Outlet numbers per device class (above).
* Level-placed sockets are fixed world objects; they have no owner device and cannot be created or deleted from a
  save (they are level actors, recreated at blueprint default when the record is absent, which is what the existing
  "Disconnect" action relies on).
* Cross-region links exist and are normal: level sockets in region saves supply hub devices.

Unverified (deliberately left as Unknown, never guessed):

* Whether a device may be named by two sockets, or plugged into its own outlet.
* Capacity, drain, ordering, battery priority, and whether a group without a level socket is unpowered.
* What `ExtraPoweredDeviceAssetIDs_` holds (always empty here).
* What in-game action creates or removes an outlet record for a newly placed or packaged device, and whether the
  game deletes the orphaned records above on its own.
* Whether a device's `ActorPath_` or transform matters for reconnecting after a load. Re-plugging by asset id at
  load is a plausible reading of `Update_SaveData`, but the load side has not been traced.

## 4. Why timer-only live changes are not durable

From the live agent's bytecode citations (`powersockets.lua`, `docs/reference/live-editing-protocol.md`
`powersockets.list`): `PowerSocket_ParentBP_C.Update_SaveData` is the only function that writes the socket's
save struct, and in BOTH branches of its attach/detach if-else it sets `HasTimer_` to false and `TimerMode_` to 0
unconditionally. `SavePowerSocketToWorldSave` always calls it before handing off to the game mode's world-save
update, and it is invoked by any save-triggering event on that socket (plugging, unplugging, and so on). So a live
write of the timer fields is undone by the game's own next save of that socket. This is why the live channel is
read-only, and why a live power-routing editor needs the game's real plug and unplug operations (calls that make
`Update_SaveData` run with the new `PluggedInDevice`) plus a re-read after a game save to prove the change survived;
setting fields cannot provide that. Neither the operation names nor the durability proof exist yet.

The offline editor is unaffected: it edits the file directly and never runs `Update_SaveData`.

## 5. What is deliberately NOT implemented

No offline connection writes were added. The stored shape of "socket S supplies device P" is proven (a socket
record names the device GUID), but no fixture holds a before/after pair of a player rewiring a device, so
the following are unproven and left out: creating an outlet record for a device that has none, deleting the record
when a device is removed, whether the game requires the previous socket's field to be cleared when a device is
moved, and what happens to a stale second reference. A proper write step needs a before/after save pair captured
from the game around a single plug or unplug.
