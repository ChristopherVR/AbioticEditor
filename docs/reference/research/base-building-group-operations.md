# Group operations and linked identities

Whole-base copy, cross-world placement and power-network duplication all need to give copied objects fresh
identities and decide what happens to links that reach outside the copied set. This note records the
identity fields that exist in the fixtures and what the Core analyzer (`PlacedGroupReferenceAnalyzer`) reports.
The analyzer is read-only. Delete, duplicate and group transforms now exist as staged Core operations (see
"Implemented operations" below); everything in them that depends on game behaviour is listed under "Not verified in-game".

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

## Policy model (implemented as the defaults below)

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

## Implemented operations (Core and CLI)

All of this lives in `StagedBaseEdits` (`Services/World`) and is reachable from the CLI as
`world object move|rotate|delete|duplicate` (`--dry-run` prints the preview). Staging never touches a save; apply is
all or nothing (any blocking finding leaves the save untouched), in one pass: moves and rotations, then duplications
(a copy is made from an object's staged transform), then deletions.

### Delete

Only GUID-keyed, player-built entries. Level-placed actor-path entries, unknown keys and objects that are not marked
player-built are refused. Items stored in the object's own `ContainerInventories_` (and planted `ItemProxies_`) are
deleted with it and listed in the preview. Per-kind policy (`DeletePolicy`), with the safe default first:

| Kind | Default | Other choices |
| --- | --- | --- |
| Outlet records the object owns (`PowerSocketMap`, key = owner + digit) | drop (remove them) | keep (leave them orphaned, as the game already does for 236 records in the fixture), refuse (only stops when an outlet still feeds a device that stays) |
| Other sockets that plug into the object (`PluggedInDeviceAssetID_` / extra devices) | refuse | drop (unplug: set to `-1` and remove from the extra list; only for records in the edited save), keep (leave dangling; the fixture has 64 dangling plugs) |
| Any other record naming the object (generic scan) | refuse | keep (cannot be rewritten safely, so there is no drop) |
| Claimed bed | refuse | drop or keep (the claim goes with the bed; the player save is not touched) |
| Teleporter pad whose tag other pads share | refuse | drop or keep (the peers are left unpaired) |
| Void chest | keep (its shared inventory is not stored in the chest) | refuse |

Links in other region saves are only visible when those saves are supplied (`StagedBaseEdits.OtherSaves`; the CLI scans
the sibling `WorldSave_*.sav` files unless `--no-scan`). A `drop` for a link that lives in another save is refused,
because the edit only writes the primary save.

### Duplicate

The copy is made from a serialize-and-reload clone of the save, so every member of the source, including ones this
editor does not model, keeps its exact hash-suffixed name. Then, per copy:

- New map key (a random uppercase 32-hex GUID, minted when the duplication is staged) and `ChangableData.AssetID` set to it.
- New `ActorPath_`: same package and level, instance number = one below the lowest number the same class already uses
  in the save, skipping any path in use.
- Every string in the copy that names another copied object (key or actor path) points at that object's copy.
- Outlet records the source owns are cloned with id = new owner key + the same digit; a plug or extra device inside the
  group follows the copies. An outlet that fed a device outside the group follows the external policy: drop (default,
  the copy's outlet is unplugged), keep (points at the original device, so two outlets feed one device), refuse.
- Teleporter tag: two or more copied pads sharing a tag get one new shared tag (the lowest unused in 1..133, or a number
  beyond the known list with a warning). A single copied pad follows the external policy: drop (default, tag 0),
  keep (joins the original network), refuse. `--keep-teleporter-tags` keeps the original tag for pairs.
- Bed claim is never copied: the copy carries the bare claim separator.
- Contents: empty by default (every existing slot member is reset to the game's own empty-slot values: row `Empty`, item
  id `-1`, stack 0, liquid level -1, no dynamic properties or tags; planted proxies removed). `--copy-contents` copies the
  items and re-mints every item id; text embedded inside item data (some proxies carry a GUID inside
  `PlayerMadeString`) is copied as is and is not remapped.
- Seat occupancy (`ActiveSeats_`) is reset to unoccupied.
- Validation before anything is written: a copy must have exactly its source's member layout (top level plus the members
  of `Transform_` and `ChangableData_`), its key and actor path set, its outlets carrying their own ids, and no string that
  still names a copied source. A source with an omitted `Translation` member (or an omitted `Rotation` when a yaw is
  requested) is refused, because the editor never creates those members.

### Group transforms, snapping and hints

Move and yaw rotation about the centroid, a chosen object or a point (`PlacedGroupTransforms`), grid snapping for
position and yaw, align yaw to a reference object, and even distribution of centres along an axis (`PlacementMath`).
`ProximityHints` reports objects within a distance of each other (preview default 25 cm). These are HINTS ONLY:
centre-to-centre distance between saved positions, with no collision test and no object sizes.

## Proven by the fixture tests

`BaseEditingTests` and `BaseEditingCliTests` run on the dedicated-server Facility save (3603 objects, 552 sockets).
Each test fingerprints every entry of every map before and after and asserts the exact set of added, removed and
changed entries.

- Deleting an unreferenced object removes exactly that entry; deleting a device with outlets removes exactly the
  object and its outlets; `drop` on inbound plugs changes exactly the feeding sockets.
- Duplicating a real power-linked pair (an owner device plugged into another player-built device) adds exactly the two
  objects and their outlet records; the copied outlet's key is the new owner key plus the same digit and it plugs the
  new device, while the original outlet still plugs the original device. External `drop`, `keep` and `refuse` each
  behave as specified.
- One object of every player-built class in the save (dozens of classes) duplicates, re-reads with its source's exact
  member layout, and changes nothing else. Deleting exactly those copies afterwards restores the original file byte for byte.
- A copied bed is unclaimed and the source keeps its claim. A copied container has the game's own empty-slot shape in every
  slot, or (on request) the same items with new unique item ids.
- Copied teleporter pairs share a fresh unused tag; the single-pad policies give tag 0, the original tag or a refusal.
- Deleting one object and copying another in one apply keeps the map count exact; a move followed by a duplicate copies
  from the moved place; revert per edit and revert all leave the save byte-identical.
- Every refusal path (inbound plug, level-placed key, unknown key, claimed bed, shared teleporter tag, minted-key
  collision, omitted rotation member, `--external-power refuse`) changes zero bytes, in Core and through the CLI (which exits 1
  and creates no `.bak`).
- A transform-only apply changes at most 56 bytes per object; a real CLI write leaves a `.bak` equal to the original.

## Not verified in-game

Nothing here was loaded in the game. The fixtures cannot answer:

- Whether the game accepts a freshly minted GUID key, or regenerates it on load. The fixtures only show existing keys are
  unique across region saves.
- Whether the new `ActorPath_` instance number must be unique, must fall in a range, or is ignored and regenerated when the
  actor spawns. The chosen number is unused in the save, but the game's allocator is unknown.
- Whether a copied outlet record is enough for the game to create the copied device's outlets, or whether the game
  rebuilds sockets from the device and only uses the record to restore the plug (`Update_SaveData` load side untraced).
- Whether removing a device's outlet records is required, harmless or ignored (the game leaves 236 orphans itself).
- What the game does with two outlets naming one device (the `keep` external power policy) or with a plug id that resolves
  nowhere after a delete with the `keep` inbound policy.
- Whether `ExtraPoweredDeviceAssetIDs_` handling is correct: every fixture list is empty, so the remap and unplug code for
  it is proven only to leave empty lists alone. Its element shape and role are still unobserved.
- Teleporter tags: whether a tag chosen from 1..133 that no other pad uses is accepted, and whether a tag beyond 133 works.
- Whether an emptied container (or a plot with no planted crops) is a state the game loads cleanly; the empty slot shape is
  copied from game-written empty slots, but the object as a whole was never seen in that state next to a live save.
- Copied item data: ids are re-minted but strings embedded in item data are not, and whether the game cares is unknown.
- Whether the game accepts a moved or copied object in a streamed sublevel, and coordinate handedness of yaw (see the
  coordinate-spaces note). Overlap hints say nothing about placement validity.
- References the analyzer cannot see: player-save respawn or claim records, quest and codex state, and live actor references.
