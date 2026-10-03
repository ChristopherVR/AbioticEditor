# Live sections and object placement

Persistent runtime actors use Facility as their outer level. The native
GetActorLevelName function reports that owner, not the section in which they stand.
Using it alone hid trams, vehicles and pets in the narrower Facility sections.

The embedded WorldRegionVolumes data is from 223 AbioticLevelStreamingVolume actors
in the installed Facility map. Each entry retains its LevelToLoad, brush-component
transform, model bounds and BSP planes. Quaternion inversion and component scale
put actor coordinates in brush space before the BSP point test. Brushes can overlap;
membership in the selected section accepts any containing brush for that section.
An explicit non-Facility actor or spawner map remains authoritative. Positions outside
the known brushes have no inferred section. This is streaming-volume membership, not
a claim that every point has one unique geographical label.

To reproduce the data, set RESPAWN_RECHARGE_PROBE_OUT to a dump path and run the
TerminalGuidProbeTests.Dump_RespawnAndRechargeDetails probe against the installed game.
Then run tools/export-world-region-volumes.py with that path and a JSON destination.
The exporter rejects non-CSG node flags. The current dump regenerates all 223 entries
exactly, including rotated and concave brushes.

Recall-button roots attach to their station's ChildActorComponent and inherit the
component's template offset. Reading only the root's own omitted RelativeLocation
put them at the level origin and generated blank pictures. The actor resolver now
composes the attachment chain, including scale and template defaults. The position
cache uses a new version so old origin results cannot bypass the correction. Three
recall-button pictures were regenerated at the corrected coordinates.

Live placement uses the documented UE4SS UWorld SpawnActor class/location/rotation API:
https://docs.ue4ss.com/dev/lua-api/classes/uworld.html
The donor supplies only an existing player-built object's class. The new actor starts
with class defaults and a new asset identity; donor inventory, wiring and state are
not copied. The installed AbioticDeployed_ParentBP declares DeployedByPlayer,
ConstructionLevel_Current, ConstructionModeActive and SaveDeployable(RemoveFromSave).
The new actor completes construction, marks replicated fields dirty and calls the
game's own save routine. Failure during initialization removes the partial actor.
Movement is restricted to player-built objects, uses K2_TeleportTo and saves only
after a successful teleport. The updated agent advertises the placement capability;
older agents leave the controls disabled.

InspectorPicturesProbe.Inspect_live_scene_gaps, with LIVE_SCENE_GAPS_OUT set, dumps
the cooked parent blueprint and relevant deploy/spawn/construction functions for review.
Lua mocks cover authority, finite coordinates, fresh identity, duplicate rejection,
blocked movement and partial-spawn cleanup. They do not establish native bridge behavior.
Placement, movement, persistence after reload and multiplayer replication still need
an in-game check with the updated agent.
