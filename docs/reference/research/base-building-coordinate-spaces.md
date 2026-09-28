# Base-building phase 2: coordinate spaces

What the fixtures show about placed-object transforms, what existing code assumes, and what is still
unproven. **Movement is not enabled in any UI.** Core has a staged-transform model and a writer method,
justified only at the byte level (see the last section); nothing here says the game accepts a moved object.

## What the fixtures show (evidence)

Source: `abioticeditor world census` over the four fixture worlds (see `base-building-placed-object-census.md`).

- **Units.** Translation members are doubles; values run to about +/-340,000. Unreal units are
  centimetres, so that is +/-3.4 km, which matches a world where the vignette "islands" sit kilometres
  apart. The same numbers are what `DoorLocationResolver`, `SectorMapCalibration` and the base detector already
  treat as centimetres.
- **Precision.** The world save stores Translation and Rotation as 64-bit doubles (the package carries the
  `LARGE_WORLD_COORDINATES` version, which switches `VectorStruct` / `QuatStruct` to doubles). Values such as
  `-16325.345441402958` are double-precision results, not single-precision widened.
- **Rotation representation.** `Transform_.Rotation` is a quaternion (X, Y, Z, W), unit length on all 3603
  dedicated-world objects. 3105 are pure yaw (X = Y = 0); 498 have pitch/roll. Example: a water cooler with
  Z = 0.92388, W = 0.38268 decodes to a yaw of 135.0 degrees using the Unreal quaternion to rotator formulas
  (`PlacedQuaternion.YawDegrees`). That shows the *representation*; it does not show the handedness of the
  in-game yaw sign, which is why `StagedPlacedTransforms.RotateYawBy` documents "Unreal yaw convention" as an
  assumption.
- **Scale.** `Scale3D` is present on every object; (1,1,1) except five (0.832 uniform, one mirrored
  (-1,1,1), and small non-uniform stretches). Scale is data to preserve, and a mirrored scale flips
  handedness for that object.
- **One world frame across regions.** The bounding boxes of the level-placed statics of each region save are
  almost all disjoint in X/Y or in Z (for example the Dark Fusion levels sit around Z = -15,000 to -17,000, the
  Facility office levels sit near Z = 0 to 3,000, `V_ISLAND` sits near Z = 134,000, the vignettes at
  |X| or |Y| up to 340,000). Region saves do not each start at their own origin; they share one absolute frame.
- **Player-built objects live in the persistent level.** All 911 player-built objects of the dedicated world are
  in `WorldSave_Facility.sav` with an `ActorPath_` under `/Game/Maps/Facility.Facility:PersistentLevel`, yet 29 of
  them are more than 100,000 cm from the origin (for example (331410, -333781) and (318842, 319545)) where only
  other sublevels' statics live. So their `Transform_` is an absolute world-space position, not relative to the
  Facility sublevel. This is the strongest fixture evidence that no sublevel offset is applied to placed
  objects' saved transforms: a persistent-level actor's transform *is* its world transform.
- **Level-placed statics.** These also sit in one absolute frame (their extents fall inside plausible per-region
  boxes, e.g. Office2 X -23,223..-15,313, Y 5,177..17,086, Z 745..1,671) and keep the actor path of the sublevel
  (`/Game/Maps/Facility_Office2.Facility_Office2:PersistentLevel...`). In that path `PersistentLevel` is the
  sublevel's own persistent level; it is **not** the Facility one.
- **Height.** Z is the vertical axis; player-built Z runs from -17,179 to 57,012 with a median of 1,341.
  No floor table exists in the save.

## What existing code assumes

- `DoorLocationResolver` reads a level's cooked `.umap` (`AbioticFactor/Content/Maps/<map>.umap`) and takes the
  **root component's `RelativeLocation`** as the actor's world position, "first positioned component wins". For
  level-placed actors this is only the world position if the actor sits directly in the world frame with no
  attachment or level transform applied. It is keyed by actor instance name. Its own comment states the
  assumption; nothing in the code verifies it against a save-side transform.
- `SectorMapCalibration` fits an affine (orientation variant, scale, offset) from that same world-unit actor
  cloud to the pamphlet drawings, so map fits assume level-placed umap positions share the frame of door
  positions. Per its own header, Office Level 2 and the Dam did not settle and are left without a fit.
- `WorldDeployable.X/Y/Z` and `BaseDetector` read `Transform_.Translation` directly and treat it as world-space
  centimetres (distance to a player position uses the same units). `WorldVehicle` (and `ApplyVehicles`, the only
  existing transform write) treats `Transform_` the same way.
- `DoorIdParser` splits `/Game/Maps/<Map>.<Map>:PersistentLevel.<Actor>` into (map, actor); for a player-built object
  that gives ("Facility", actor) regardless of position.

## What remains unproven (needs level assets or in-game checks)

1. **Save transform vs umap transform.** Whether a level-placed static's saved `Transform_` equals its umap
   root `RelativeLocation` (no sublevel offset) has not been compared; that needs the installed paks. If the
   sublevel has a level transform, `DoorLocationResolver` positions and save transforms would differ by that offset.
2. **Streaming of moved objects.** A player-built object saved in `WorldSave_Facility.sav` but located inside another
   sublevel's volume: does the game spawn it at save load, or only when that sublevel streams in? Untested.
3. **Handedness and yaw sign.** Unreal is left-handed, Z up, positive yaw clockwise seen from above. The math in
   `PlacedQuaternion` follows the Unreal quaternion/rotator formulas but has not been checked against an in-game
   screenshot.
4. **Floor and elevation semantics.** No floor index exists in a save; "floor" would be inferred from Z bands per
   level, which needs level geometry.
5. **Placement validity.** Collision, overlap, support (`Supports` array), snapping and per-class placement rules
   are not in the save. A moved player-built piece could end inside geometry or lose its support relations.
6. **Level-placed statics.** These are re-placed by the level asset; whether a saved transform for them is honoured
   is unknown, so the staged model refuses them by default.
7. **Runtime-named "static" objects.** The 36 `DeployedByPlayer_` false water coolers/figurines with `INT32_MAX`-range names in
   `WorldSave_Facility.sav` show key shape is not a complete static/player-built classifier.

## Core half of phase 4 and what the writer proof covers

- `PlacedObjectTransform`, `PlacedVector`, `PlacedQuaternion` (Domain) model the struct as stored, with omitted
  members left null (delta serialization) and effective defaults exposed separately.
- `WorldSaveWriter.ApplyPlacedObjectTransform` rewrites the existing `Translation` / `Rotation` members in place
  and refuses (returns false) when the entry or member is absent; it never creates a member because no serialized
  shape for a created member was verified. Every fixture object has all three members, so this covers all
  observed data.
- Proof (`PlacedObjectBuildingTests`, run on the fixture worlds): an unmodified read/write is byte-identical; writing
  back the value just read is byte-identical; writing a changed translation and rotation keeps the file length,
  changes at most 56 bytes (3 + 4 doubles), re-reads equal, and leaves `Scale3D` unchanged.
- `StagedPlacedTransforms` holds pending edits by object key: move-by, yaw-by, absolute stage, per-object revert, revert
  all, a before/after preview (distance in cm, yaw delta, warnings) and `ApplyTo`, which is the only call that mutates
  the in-memory save. It refuses level-placed objects unless explicitly allowed. It is not wired to any UI.

Byte-level proof means the file stays valid GVAS with the new numbers. It does not mean the game will accept
them: items 1 to 6 above are all open. In-game validation on a disposable save copy (roadmap phase 7) is required
before any UI exposes movement.
