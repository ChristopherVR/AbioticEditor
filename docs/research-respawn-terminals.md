# Research: resolving `TerminalRespawnID_122` to a human-readable name

**Date:** 2026-06-11 · **Probe:** `tests/AbioticEditor.Tests/TerminalGuidProbeTests.cs`

## Verdict

**Derivable - and trivially so.** The player save's `TerminalRespawnID_122_*` NameProperty is the
`SpawnedAssetID` of a `Deployed_PunchCardTerminal_C` actor placed statically in the persistent
Facility level (`AbioticFactor/Content/Maps/Facility.umap`). Each such actor also carries a
`LocationName` NameProperty with the exact human-readable area name. Both fixture GUIDs resolved.

Earlier research (research-customization.md, Probe7) concluded the GUIDs appeared "nowhere in any
WorldSave"; that was correct but misleading - the GUIDs are not save data at all. They are baked
into the cooked map as FNames (the byte scan found them as ASCII in `Facility.umap`'s name table;
the serialized-FGuid binary pattern appears nowhere, because `SpawnedAssetID` is a NameProperty).

## How it was found

1. `Probe2` byte-scanned all 77 cooked `.umap` packages for the fixture GUIDs in four
   representations (FGuid LE bytes, BE bytes, ASCII, lowercase ASCII). Only ASCII hit, only in
   `Facility.umap` (2 hits per GUID = name-table entry + export data reference).
2. `Probe4` loaded `Facility.umap` (52,750 exports, ~1 s with usmap mappings) and walked every
   export's properties. Both GUIDs matched the `SpawnedAssetID` property (and a copy inside
   `ChangeableData.AssetID`) of `Deployed_PunchCardTerminal_C` actor instances.
3. `Probe6` byte-scanned all maps for the class name (only `Facility.umap` references it) and
   enumerated every instance.

## Full GUID -> terminal table (game build as of 2026-06, Facility persistent level)

| `SpawnedAssetID` (= TerminalRespawnID) | LocationName | Actor | World position (root) |
|---|---|---|---|
| `AFB31D8E4DFBB5BE74BEAAADD681A636` | Manufacturing West | `Deployed_PunchCardTerminal_C_0` | (-4352, 24123, 810) |
| `95CAED254C17360B69B3738E468CD49C` | Hydroplant | `Deployed_PunchCardTerminal_C_1` | (-27352, -2498, 529) |
| `E57CB02C4853F46D2BB7CA80303EB6A3` | Cascade Laboratories | `Deployed_PunchCardTerminal_C_2` | (9173, 9599, -1181) |
| `35DCF84F4AC366B8DCBB61A93D9C83C0` | Security Sector | `Deployed_PunchCardTerminal_C_3` | (-3660, -3650, 534) |
| `7CCB5D3A4072BAE875ECA2A05F35AF0F` | Power Services | `Deployed_PunchCardTerminal_C_4` | (-29671, -544, -9812) |
| `7996635040409DC197A441922A831284` | The Office Sector | `Deployed_PunchCardTerminal_C_5` | (-15691, 20611, 2510) |
| `AC917C804463D66ABBBB3FB89A0174AB` | The Reactors | `Deployed_PunchCardTerminal_C_6` | (-13238, 21451, -16451) |
| `35BDEE3649830558D65FCDBDC194C725` | The Mines | `Deployed_PunchCardTerminal_C_8` | (7510, 44947, 755) |
| `601B417D44683BA9E7D422AF8AE457D8` | Residence Sector | `Deployed_PunchCardTerminal_C_9` | (-22764, 25001, 135) |
| `476CCD1247914D2A067353BCF0DCC849` | Shopping District | `Deployed_PunchCardTerminal_C_10` | (-34192, 55638, -987) |

(Index 7 does not exist - the actor was presumably deleted in-editor; numbering gaps are normal.)

Fixture check (Cascade): 3 players -> `95CAED25...` = **Hydroplant**; 1 player -> `35DCF84F...` =
**Security Sector**. Matches a late-game co-op save.

## Recommended app lookup

1. **Hardcoded dictionary** of the 10 GUIDs above -> `"Respawn terminal in {LocationName}"`.
   The GUIDs are editor-assigned and stable across game updates unless the devs delete/re-place
   an actor; new game sectors may add terminals. Cheap, no pak dependency at runtime.
2. **Optional asset-backed refresh** (if a game install + usmap is available): load
   `AbioticFactor/Content/Maps/Facility.umap` via `GameAssetProvider`, filter exports with
   `Class?.Name == "Deployed_PunchCardTerminal_C"`, read `SpawnedAssetID` + `LocationName`
   (both NameProperties). ~1 s load; suitable for a cached one-time build at startup.
3. **Fallbacks:** if the GUID is not in the table, check each world save's `DeployedObjectMap`
   keys (a player-deployed terminal would live there with a `Class_` identifying it); otherwise
   show the shortened GUID (`95CAED25...`).

Note: `LocationName` values are plain FNames, not localized FText - display them as-is.
