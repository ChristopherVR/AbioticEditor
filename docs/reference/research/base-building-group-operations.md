# Group operations and linked identities

Whole-base copy, cross-world placement and power-network duplication all need to give copied objects fresh
identities and decide what happens to links that reach outside the copied set. This note records the
identity fields that exist in the fixtures and what the Core analyzer (`PlacedGroupReferenceAnalyzer`) reports.
The analyzer is read-only. No duplicate/remap writer exists, and none should be written until in-game evidence
covers each item marked open.

## Identity model (evidence from the fixtures)

- A player-built object's identity is its `DeployedObjectMap` key: a 32-hex GUID. Its `ActorPath_` is a second
  identity (`/Game/Maps/Facility.Facility:PersistentLevel.<Class>_<large n>`), and the two are different strings.
  A copy needs a new key **and** a new, unused actor path; whether the game requires the instance number to be
  unique or regenerates it is open.
- Level-placed statics use the actor path as the key; they are not copyable in any meaningful sense.
- Keys are unique across the region saves of a world (no duplicates in 3603 objects), so a cross-save key match is a
  real reference, not a coincidence of reused ids.

## Reference fields found

| Kind | Where | Key/reference | Internal to a selection when | Notes |
| --- | --- | --- | --- | --- |
| Power socket ownership | `PowerSocketMap` entry, `PowerSocket_` | 32-hex owner key + 1 suffix digit (507 of 552 sockets, dedicated world) | the owner key is selected | 271 owner keys resolve in the same save; the rest of the 33-char ids are owned by objects in other region saves or are absent |
| Plugged device | `PowerSocketMap`, `PluggedInDeviceAssetID_` | a `DeployedObjectMap` key (`-1` = none) | both socket owner and device are selected | 355 of 552 plugged; 254 resolve in the same save, 37 in another region save of the same world, 64 nowhere in the fixtures |
| Extra powered devices | `PowerSocketMap`, `ExtraPoweredDeviceAssetIDs_` | list of keys | both ends selected | **empty on every socket in every fixture**; the array's element shape is therefore unobserved with data |
| Level-placed sockets | `PowerSocketMap`, long path-like `PowerSocket_` (45 of 552) | actor path of the socket in a sublevel | never (the socket is not a placed object) | these can point at player-built devices in `WorldSave_Facility.sav`, e.g. `Facility_Botanical` sockets plugged into GUID-keyed devices |
| Teleporter network | pad's `ChangableData_` dynamic property `TeleporterFrequency` | shared integer tag (0 = unassigned) | all pads with that tag are selected | not a key reference: any pad, in or out of the selection, with the same tag joins. 25 pads in the dedicated world |
| Bed claim | bed's `CustomTextDisplay_` = `<ownerId>}\|!\|{<name>` | owner id (SteamID64, or an opaque token on non-Steam) | never (a player, not a placed object) | 3 of 15 beds claimed in the dedicated world; unclaimed beds carry the bare separator |
| Shared inventory | Void chest has no `ContainerInventories_`; contents are `CustomInventoryMap["Void"]` | map key | never | every Void chest shows the same shared inventory (4 in the dedicated world) |
| Containment unit occupancy | metadata save `LeyakContainmentIDs`: creature row -> unit key | unit's `DeployedObjectMap` key | cross-file (metadata save) | see `world-save-schema.md`; found by the analyzer's generic string scan when the metadata save is supplied |
| Inline data | `ContainerInventories_`, `ChangableData_` | none | always | stored inside the object, so it moves with it |

Also reference-shaped and **not** covered: respawn/terminal ids in player saves (`TerminalRespawnID_`), quest or
codex state that mentions a placed object, and anything in the live game (actors referencing each other at
runtime). Player saves are outside this analyzer.

## What the analyzer reports

`PlacedGroupReferenceAnalyzer.Analyze(primary, selectedKeys, primaryName, otherSaves)` returns:

- `References`: every link touching the selection, each marked `IsInternal` when both ends are inside the selection
  (`Internal` / `External` views). Kinds: `PowerSocketOwnedBySelection` (a socket that belongs to a selected object
  and where its plugged/extra devices point), `PowerSocketTargetsSelection` (an unselected socket that feeds a
  selected device: an inbound link a copy would lose), and `KeyReference` (any other string field or map key in the
  scanned saves equal to a selected key or actor path, so an unknown link kind still surfaces).
- `IdentityBindings`: `BedClaim`, `TeleporterTag` (with the unselected peer pads sharing the tag), `SharedInventory`.
- `MissingKeys`: selected keys not in the primary save.

Pass sibling region saves and the metadata save as `otherSaves` to catch cross-file references into the selection;
without them only the primary file is scanned and cross-save links are invisible (a limitation to surface in any UI).

## Proposed policy (not implemented)

For each external link a group operation must choose one of: **retain** (the copy keeps pointing at the original
outside device: for a socket, this means two sockets feeding one device), **rebind** (point at a device chosen by the
user), **drop** (clear the link), or **report** (refuse to continue until decided). Internal links are remapped
together: a new key for each selected object, then every internal socket id, `PluggedInDeviceAssetID_` and shared
reference rewritten through the same old-to-new map. Teleporter tags, bed claims and Void chests are choices, not
remaps: default to *drop the claim*, *keep the tag only if the user asks*, *warn that contents are shared*.

## Open questions (need in-game evidence)

- Is a power link stored on both sides? The saves show sockets referencing devices; whether the device side needs a
  reciprocal record is not visible in the fixtures (the roadmap's power-routing plan covers capturing this).
- Whether a duplicated `PowerSocket_` id must use the new owner key plus the same suffix digit, and what the digit means.
- Which suffix digit / socket count each device class has (the fixtures show only digits, not a class table).
- What the game does with a `PluggedInDeviceAssetID_` that resolves nowhere (64 such links exist in the dedicated
  fixture world, so the game tolerates dangling ids at save time).
- How a copied object's `ActorPath_` instance number must be chosen.
- Extra powered devices: the array is empty everywhere, so its element shape and role are unverified.
