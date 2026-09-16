# Research: deployable (placed-object) paint colour

Goal: let players change the paint colour of placed objects (benches, crates, cubicles,
barricades, beds, rugs, and more), following the "never write a field without evidence" rule.
Evidence below comes from the installed game's paks (via `GameAssetProvider`) and from real save
data (fixtures plus a read-only backup of a real Cascade world). No em dashes.

## The table: `DT_PaintedDeployables`

38 rows, row struct `PaintedDeployable`, dumped by
`tests/AbioticEditor.Probes/DeployablePaintProbeTests.cs::Dump_PaintedDeployablesTable`:

`bagwall`, `barrelcrafted`, `barricade_office`, `bed`, `bedT2`, `BridgeT1`, `bridgeT2`,
`carboncrate`, `cauldron`, `craftingbench`, `crateT4`, `cubicle_full`, `cubicle_full_half`,
`cubicle_full_nowindow`, `cubicle_full_window`, `cubicle_half`, `cubicle_half_half`,
`cubicle_half_nowindow`, `cubicle_half_window`, `laser_emitter`, `Light`, `makeshiftcrate`,
`oildrum`, `petbed`, `petbed_luxury`, `petbed_small`, `PlankBarricade`, `pumpkin_carved_classic`,
`pumpkin_carved_gate`, `pumpkin_carved_order`, `ramp`, `reinforcedcrate`, `Rug_Mat`, `Rug_Oval`,
`Rug_Rectangle`, `Rug_Square`, `teleporterpad`, `wallshelfing`.

Each row has a `Materials_Default` array plus 13 named colour columns:
`Materials_White`, `_Blue`, `_Red`, `_Green`, `_Orange`, `_Purple`, `_Yellow`, `_Black`, `_Cyan`,
`_Lime`, `_Pink`, `_Brown`, `_Glitch`. Every row carries all 13 columns (some empty for rows with
no dedicated repaint, e.g. `laser_emitter`, `Light`, `ramp`, `reinforcedcrate`, `teleporterpad`,
`wallshelfing`, three of the four rug forms). This is display data (material asset references)
only; it does not itself say which item ID or class is paintable.

## The colour enum: `EPaintColor`

Confirmed via the usmap (`Dump_PaintEnums`), 15 values:

| Value | Name | Value | Name | Value | Name |
|---|---|---|---|---|---|
| 0 | White | 5 | Purple | 10 | Pink |
| 1 | Blue | 6 | Yellow | 11 | Brown |
| 2 | Red | 7 | Black | 12 | **None** |
| 3 | Green | 8 | Cyan | 13 | Glitch |
| 4 | Orange | 9 | Lime | 14 | `EPaintColor_MAX` |

The 13 selectable colour names (0-11, 13) match `DT_PaintedDeployables`'s 13 colour-column suffixes
exactly. Value 12, `None`, is the class default: "unpainted".

## The deployable class: `AbioticDeployed_ParentBP_C`

Dumped by `Dump_DeployableClassAndSaveStruct`. The shared parent class every deployable derives
from (the same class the live BASES tooling already uses, `AbioticDeployed_ParentBP_C`) carries,
among its 57 properties:

- `PaintedColor` : `FEnumProperty` (backed by `EPaintColor`) - **no hash suffix** in the compiled
  class layout (most of this class's own properties are bare; only a couple of auto-generated
  Timeline track variables carry one).
- `PaintedDeployableRow` : `FStructProperty` of type `PaintedDeployableRowHandle`, defaulting to
  `RowName=None, DataTablePath=.../DT_PaintedDeployables.DT_PaintedDeployables` on the base class.
  Each paintable subclass's own CDO sets a real row name (see below).
- Functions `CanBePainted` (`BlueprintPure`, no inputs, outputs `SupportsPaint`/row-handle-valid),
  `SetPaintColor(Color: EPaintColor, SkipSave: bool)` (`BlueprintCallable`/`BlueprintEvent`, only
  two real inputs; every other listed "param" is a Kismet compiler temp, not an argument),
  `OnRep_PaintedColor()` (no parameters - a normal `RepNotify`), and `SetupPaintAndTexture`.

`SetPaintColor`'s compiled locals include `CallFunc_SetDynamicProperty_ReturnValue`, which pointed
straight at the mechanism confirmed below.

## The save does NOT have its own paint field

The actor class's own properties are the *live* object; what gets serialized into
`DeployedObjectMap` is a different struct entirely: `SaveData_Deployable_Struct`
(`AbioticFactor/Content/Blueprints/Saves/SaveData/SaveData_Deployable_Struct`, dumped by the same
probe). Its full, exact, hash-suffixed field list is:

```
Class_77_84FAE6234D772064CD9B659BA5046B1C
ActorPath_164_90AA6DAB481E5DC2E125A3A94475F44D
ChangableData_37_6153F4A94F01A776C108038B7F38E256
DeployableDestroyed_56_80BF5DDE46C5F8C6E6CD9EBF6A695E5E
BrokeWhenPackaged_63_852033BB4713434A14C0D5B5792BA116
HasBeenPackaged_59_9C1C3E4D4D61B7BC4E7D13A1B993E1B0
Transform_50_85E8B13D40141C9B1308F4BB943BD753
DeployedByPlayer_71_EA4E6F5C4DBE9C472BC1D1B3ADEE0205
ConstructionMode_82_B226CF9D4E57045A9835B39D8D7AF98D
ConstructionLevel_85_460528D64DD6D1712C19198BC316254B
ContainerInventories_110_3A680B7244ACB095D963B786D9BB6ECB
ActiveSeats_135_E030A01B4CB15C1F95700EA3945F2A85
ItemProxies_149_E2E145CE4015C4EDFA89E2B0CE3F579A
CustomTextDisplay_152_B59A50C74001B5D2234D9E9B0D7CAB7F
FoundByPlayer_154_B3A0D3F6458C7DAD36E130B39DAEDBE3
Supports_158_FE0D33184131D1E1C73782B44057EB5C
NoResetVignette_161_C76AFFC84B04AA28B73A65836D6BB265
CustomSpawnedTime_169_BAD6DE0D42D4F78261A9128279F907FE
```

This matches the existing reader's own prefixes (`Class_`, `Transform_`, `ContainerInventories_`,
`CustomTextDisplay_`) exactly, confirming this is the right struct. **There is no `PaintedColor_`
or `PaintedDeployableRow_` field here at all.**

## Where paint actually lives: `ChangableData_.DynamicProperties_`

`ChangableData_` (note the in-game misspelling, already noted by `BenchUpgradeCatalog`) is itself
typed `Abiotic_InventoryChangeableDataStruct`, the *same* struct item inventory slots use for their
own `ChangeableData_` (correctly spelled there). Its fields include:

```
GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1   (bench upgrades already use this)
DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7   (an array of {Key: EDynamicProperty, Value: int})
```

`EDynamicProperty` (usmap, 27+ values) includes, among `CurrentAmmo`, `WeaponCoating`,
`CoatingDurability`, `XP`, `PetMutation` etc. (all already read/written by
`PetDynamicProperties`/`WorldSaveWriter`): **`[6] PaintColor`**.

**Conclusion, confirmed at the byte level**: a painted deployable carries a
`{Key: "EDynamicProperty::PaintColor", Value: <EPaintColor int>}` entry inside its
`ChangableData_.DynamicProperties_` array - the exact same array/struct pair the editor already
writes for weapon coatings, just a different key.

### Real-save confirmation

`Dump_PaintedDeployablesTable`'s sibling, `Scan_Fixtures_ForPaintedDeployables`, scans every
`WorldSave_*.sav` under the Cascade/Client/Server fixtures for this exact key and found **75**
already-painted deployables across the fixture set (33 in the single
`SteamSaves/Legacy/Cascade/WorldSave_Facility.sav` fixture alone), for example:

```
WorldSave_Facility.sav :: class=.../Deployed_CraftingBench_Default_C value=5   (Purple)
WorldSave_Facility.sav :: class=.../Deployed_Cubicle_Window_C        value=4   (Orange)
WorldSave_Facility.sav :: class=.../Deployed_StorageCrate_Makeshift_T2_C value=8  (Cyan)
WorldSave_Facility.sav :: class=.../Deployed_Barricade_Plank_Full_C  value=8   (Cyan)
WorldSave_Facility.sav :: class=.../Deployed_LiquidContainer_Cauldron_Tech_C value=8 (Cyan)
```

Every observed value (1, 4, 5, 8) is one of the 13 selectable `EPaintColor` values, and every
observed class is one of the confirmed paintable classes below. This is direct, real-save proof of
the mechanism, not an inference from game data alone.

Also confirmed in the fixture: **every** deployable entry (604/604 in `WorldSave_Facility.sav`)
already carries a `ChangableData_` struct with a `DynamicProperties_` array (possibly empty), so
adding a first `PaintColor` entry is always an in-place append, never a "graft a whole array from
elsewhere" situation in practice - though the writer still falls back to that path (see
`PetDynamicProperties.ApplyOne`) for the theoretical case where it is not.

## Which classes are paintable: the class -> row map

The save carries no per-instance row/class-paint field, so paintability is a property of the
*class*, not the instance. `Dump_PaintableClassRowMap` scans every blueprint under
`Blueprints/DeployedObjects/` for its own compiled CDO (`Default__<Class>`) setting a non-`None`
default `PaintedDeployableRow`, and found **46** confirmed classes (matching
`DeployablePaintCatalog`'s class map row for row), for example:

```
Deployed_CraftingBench_Default_C -> craftingbench
Deployed_Furniture_CraftedBed_C -> bed
Deployed_CementBagWall_Block_C -> bagwall
Deployed_Cubicle_Window_C -> cubicle_full_window
Deployed_TeleporterPad_C -> teleporterpad
Deployed_Rug_Mat_C -> Rug_Mat
```

Three classes explicitly set `RowName=None` (the parent `AbioticDeployed_ParentBP_C` itself,
`Deployed_FoodWarmer_C`, `Deployed_WishingShelf_C`) and are therefore confirmed **not** paintable
rather than merely unconfirmed.

Not every one of the 38 table rows has a confirmed class in this scan: some rows (e.g.
`pumpkin_carved_gate`, `pumpkin_carved_order`, some cubicle/rug variants) are reached only by a
subclass that *inherits* its parent's row without its own CDO re-declaring the property, which
this scan (by design) does not follow up the inheritance chain for - the "safe catalog" rule this
repo already applies to item texture variants (see `research-item-visual-variants.md`) is followed
here too: only classes with their own confirmed default are exposed.

## What remains unverified

- No in-game verification that the live write (setting `PaintedColor` directly and replaying
  `OnRep_PaintedColor`) actually repaints the mesh. The agent also upserts the
  `EDynamicProperty::PaintColor` entry in the live object's own `ChangeableData` dynamic-property
  array and calls `SaveDeployable()`, so the saved side no longer depends on the game re-deriving
  it, but that the entry survives a real world save is likewise unverified.
- The handful of table rows without a confirmed class (inherited-only rows, see above) are not
  exposed as paintable even though they likely are; a future pass could walk the class hierarchy to
  fill these in.

## Deliverable level reached

Full: Core catalog, domain model, reader/writer with a byte-level round-trip test against the real
fixture, UI in the BASES tab, and a live write path (flagged "awaiting in-game verification").

## Files

- Probe: `tests/AbioticEditor.Probes/DeployablePaintProbeTests.cs` (run explicitly; not part of
  `dotnet test tests/AbioticEditor.Tests`).
- Catalog: `src/AbioticEditor.Core/Catalogs/World/DeployablePaintCatalog.cs`.
- Domain: `src/AbioticEditor.Core/Domain/World/WorldBase.cs` (`WorldDeployable.PaintColorValue`).
- Reader/writer: `src/AbioticEditor.Core/Serialization/World/WorldSaveReader.Containers.cs`,
  `WorldSaveWriter.WorldState.cs` (`ApplyDeployablePaintColor`), and the generalized
  `PetDynamicProperties.ApplyOne` helper.
- Tests: `tests/AbioticEditor.Tests/DeployablePaintWriteTests.cs`.
- UI: `src/AbioticEditor.Web.Shared/Components/World/WorldBasesTab.razor` ("PAINTED OBJECTS" section).
- Live: `live-agent/AbioticEditorLiveAgentLua/Scripts/areas/bases.lua` (`paintColor` in
  `bases.list`/`bases.set`), `src/AbioticEditor.Core/LiveEditing/World/LiveBasesChannel.cs`,
  `src/AbioticEditor.Web.Shared/Models/LiveBasesSession.cs`, test case
  `live-agent/AbioticEditorLiveAgentLua/tests/cases/bases.lua`.
