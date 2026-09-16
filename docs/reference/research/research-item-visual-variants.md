# Research: item visual variants

Probed on 2026-09-14 against the installed game paks, the bundled mappings, and all Cascade
player and world fixtures. The repeatable probes live in
`tests/AbioticEditor.Probes/ItemVariantProbeTests.cs`.

## Result

The game has enough data to build a useful visual-variant catalog, but it does not provide a
direct `item id -> allowed variant rows` table.

There are three related systems that must not be mixed together:

1. An inventory instance can carry `ChangeableData -> TextureVariantRow`, a row handle into
   `DT_TextureVariants`. This is the field already read and written by the editor.
2. Every item definition also has a `TextureVariant` row handle in `ItemTable_Global`. For many
   upgraded or specialized items this is a fixed default appearance, not a player-selectable
   skin.
3. Placed-object paint uses `DT_PaintedDeployables`, a separate 38-row table with material arrays
   for Default plus White, Blue, Red, Green, Orange, Purple, Yellow, Black, Cyan, Lime, Pink,
   Brown, and Glitch. It is not the helmet, poster, or weapon variant system.

`DT_TextureVariants` currently has 575 rows. Its row struct has only:

- item name override
- description override
- flavor-text override
- icon override
- an array of replacement materials

It has no item id, base mesh, compatibility tag, family id, or allowed-items column. The item
table points from an item to one fixed row, but never to a list of choices.

## Fixed appearances versus selectable candidates

Of the 575 texture-variant rows, 383 are referenced as fixed defaults by an item-table row. This
explains most apparent weapon and armor "variants": they are distinct item IDs, often upgrade
tiers, and should not automatically be offered as skins for the base item.

Examples:

| Item ID | Fixed appearance row |
|---|---|
| `shotgun_doublebarrel_U1` | `antiqueshotgun_polished` |
| `shotgun_military_U2` | `shotgun_military_u2` |
| `smg_military_U2` | `smg_u2` |
| `rifle_sniper_U2` | `sniper_u2` |
| `pistol_energy_U1` | `pistol_energy_u1` |
| `armor_helmet_forge_U1` | `armor_helmet_forge_U1` |
| `armor_helmet_forge_U2` | `armor_helmet_forge_u2` |

The other 192 rows are not fixed defaults for any item definition. They are the plausible pool
for instance-level variants, but absence from the item table is a heuristic rather than proof.
Some may be set by placed actors, scripted spawns, DLC logic, or retired content.

## Confirmed in real save fixtures

These non-`None` instance overrides occur in the Cascade fixtures:

| Item ID | Observed variant row(s) |
|---|---|
| `armor_hat_bonnet` | `bonnet_black` |
| `armor_hat_buckethat` | `buckethat_green` |
| `armor_helmet_hydrohat` | `hydrohat_yellow` |
| `backpack_small` | `backpack_small_red` |
| `Bench_Locker` | `lockerroombench_brown` |
| `Deployable_Chair_Executive_01` | `office_chair_executive_black` |
| `Deployable_Couch_Office_Armchair_01` | `office_couch_black`, `office_couch_blue` |
| `Deployable_Couch_Office_Long_01` | `office_couch_blue` |
| `fish_antefish` | `fish_ante_rare1` |
| `fish_rad` | `fish_rad_rare1` |
| `fish_reaper` | `fish_reaper_rare1` |

This is direct evidence that the same item ID can safely carry these alternate appearance rows.

## Strong candidate families

The following families are strong candidates because their unclaimed row names, replacement
material paths, and item catalog entries agree. Only the fixture-observed rows above are proven
by saves in this repository.

### Wearables

| Base item | Candidate rows |
|---|---|
| Basic Backpack (`backpack_small`) | `backpack_small_gray`, `_green`, `_purple`, `_red`, `_white`, `_yellow` |
| Bucket Hat (`armor_hat_buckethat`) | `buckethat_green`, `_navy`, `_red` |
| Hard Hat (`armor_helmet_hardhat`) | `gear_hardhat_blue`, `_brown`, `_gray`, `_green`, `_red`, `_white`, `_yellow` |
| Karate Helmet (`armor_helmet_karate`) | `gear_armor_karatehelmet_black`, `_red`, `_white` |
| Hydroplant Hat (`armor_helmet_hydrohat`) | `hydrohat_green`, `_orange`, `_red`, `_yellow` |
| Lab Mask (`armor_helmet_gasmask`) | `labmask_shiny_blue`, `_dark`, `_gold`, `_green`, `_orange`, `_pink`, `_purple`, `_red` |
| Puffy Coat (`armor_chest_puffycoat`) | `puffycoat_gray`, `_red`, `_white` |

Single alternate rows also exist for the Bonnet (`bonnet_black`), Floppy Hat
(`hat_floppy_orange`), Security Cap (`securitycap_blue`), Safety Vest (`safetyvest_blue`),
Biometric Arms (`armor_arms_biometric_purple`), and Crystal armor (`armor_crystal_u2`). These need
an observed save or blueprint-level confirmation before being treated as a complete family.

### Collectibles and furniture

- `Poster` has eight named art rows: `poster_0091`, `poster_Detour`, `poster_TDL`, `poster_USM`,
  and `poster_VOTV_01` through `_04`. Each row supplies its own in-game name and icon.
- The painting rows contain 26 named artworks. Their prefixes include `painting_a_`,
  `painting_L_`, `painting_S_`, and `painting_V_`, which appear to encode compatible aspect or
  frame families. The game has nine painting item IDs across desk, landscape, square, vertical,
  large, and fancy-frame forms. The tables do not directly state which artwork prefix belongs to
  each frame, so this mapping still needs save evidence or an in-game test.
- There are 24 `photoframe_*` rows, but no matching current item-table ID was found by name. Treat
  these as actor-driven or possibly retired until an item instance confirms the base ID.
- Four arcade cabinet rows and eleven TV screen rows exist.
- Office chairs/couches, fancy and modern couches, residential beds, military cots, fridges,
  cafeteria tables/trays, stools, rugs, a stapler, toolbox, and warning sign all have plausible
  alternate rows. Several office couch/chair choices are already confirmed by fixtures.
- Rare-fish rows cover Antefish, Gem Crab, Darkwater Fish, Eel, Fog Fish, Ice Fish, IS-98, Moon
  Fish, Portal Fish, Radfish, Inkfish, Silk Shark, and Umbra Fish. Three families are confirmed in
  fixtures.

## Painted deployables are separate

`DT_PaintedDeployables` contains these 38 paint profiles:

`bagwall`, `barrelcrafted`, `barricade_office`, `bed`, `bedT2`, `BridgeT1`, `bridgeT2`,
`carboncrate`, `cauldron`, `craftingbench`, `crateT4`, nine cubicle forms, `laser_emitter`,
`Light`, `makeshiftcrate`, `oildrum`, three pet beds, `PlankBarricade`, three carved pumpkins,
`ramp`, `reinforcedcrate`, four rug forms, `teleporterpad`, and `wallshelfing`.

The table describes materials per paint color. It does not identify the inventory
`TextureVariantRow`, and helmets and weapons do not appear in it.

## Safe catalog rule

Do not expose all 575 rows for every item. A safe first catalog should:

1. Include the fixture-confirmed item/row pairs.
2. Add only reviewed families whose row names and material assets clearly match the base item.
3. Keep fixed item defaults out of another item's picker unless separately confirmed.
4. Keep painting frame/aspect groups separate until their mapping is grounded.
5. Preserve an unknown current row from a newer game build, even if the local catalog cannot
   classify it.

## 2026-09-16: grounding the "strong candidate families" and adding them

Re-probed the installed game's `ItemTable_Global` for the mesh (`DeployHologramMesh`/`WorldStaticMesh`)
and blueprint (`DeployedItemClass`) each item points at, and matched those against the replacement
material paths on the still-unclaimed `DT_TextureVariants` rows. Also read every `Player_*.sav` and
`WorldSave_*.sav` under `tests/fixtures` **and**, read-only, the real Cascade dedicated-server backup
at `artifacts/cascade-live-backup-20260915-073257/` (not a repo fixture; never copied in) through the
same Core readers used by the app, counting every `(ItemId, TextureVariantRow)` pair actually saved
in a player inventory, world container, or dropped item.

### New instance evidence (beyond the 2026-09-14 table)

| Item ID | Observed variant row(s) |
|---|---|
| `armor_helmet_hydrohat` | `hydrohat_blue` (see "stale row" note below) |
| `Deployable_Couch_Office_Armchair_01` / `_Long_01` | also `office_couch_grey` |
| `Deployable_Fridge` | `fridge_office_gray` |
| `TV` | `tv_tips_designations`, `tv_tips_trams`, `tv_tips_wayseeker` |
| `votv_journal_01` | `book_votv_journal` (this is the item's own fixed default, not a choice) |

The `TV` finding matters most: it proves the deployable television's item id is the literal string
`TV`, and that players already carry the tip-reel rows as instance data, not just as scripted set
dressing.

One saved pair, `armor_helmet_hydrohat | hydrohat_blue`, does not match any row currently in
`DT_TextureVariants` (only `hydrohat_green/orange/red/yellow` exist today). This is exactly the
"unknown current row" case the safe-catalog rule already covers: the row was renamed or removed
upstream since that save was written. It is deliberately **not** added to `ItemVariantCatalog`,
because offering it back out as a choice would be inventing a value the live table no longer
defines; the reader/writer path already preserves whatever is on disk regardless of whether the
catalog recognizes it.

### Families added, with their grounding

- **Wall paintings and desk photos** (9 frame items, 35 art rows). The game has no single "painting"
  item; each frame shape is its own id, and each item's `DeployHologramMesh` names the exact frame
  asset:
  - `Painting_Desk` deploys `SM_Painting_DeskFrame`. All 24 `photoframe_*` rows replace
    `M_PhotoFrame_*` materials (desk-photo subjects: coworkers, pets, VOTV portraits). The mesh name
    ("desk frame") and the material family name ("photo frame") are the same object; this is the
    item the 2026-09-14 note could not find. Mapped in full.
  - `Painting_Landscape` / `Painting_Landscape_Fancy` deploy `SM_Painting_Landscape[_Frame]`. The five
    unprefixed rows (`painting_coldmountains`, `_desertclouds`, `_is0042`, `_moodypines`,
    `_sunsetswamp`) are all literal landscape scenes by title and description; mapped to both the
    plain and fancy-frame item (same artwork, nicer frame), matching the existing precedent of
    `office_couch_black/blue` already being shared across the Armchair and Long office-couch items.
  - `Painting_Landscape_Large` / `_Large_Fancy` deploy `SM_LPainting_Landscape[_Frame]`. The four
    `painting_L_*` rows (`JJ`, `orb`, `pig`, `rabbit`) share the row-name prefix `L` with the mesh's
    `LPainting` prefix ("Large Painting"). Mapped to both large-frame items.
  - `Painting_Square` / `_Square_Fancy` deploy `SM_Painting_Square[_Frame]`. The one `painting_S_plague`
    row shares the `S` prefix with the item's "Square" shape. Mapped to both.
  - `Painting_Vertical` / `_Vertical_Fancy` deploy `SM_Painting_Vertical[_Frame]`. The five
    `painting_V_*` rows (`jack`, `kitten`, `medieval_01`, `Med_Cliffs`, `serpents`) share the `V`
    prefix with "Vertical". Mapped to both.
  - The 11 `painting_a_*` (`M_WallArt_*`) rows still have **no** item whose mesh or blueprint
    references `WallArt`; they remain unmapped. They are the same size/shape of art as the desk
    photos but nothing ties them to a specific frame item, so per rule 2 they stay out.
- **Television** (`TV`, item id confirmed above). All 11 `tv_*` rows (`tv_channel5`, `tv_standby`,
  8 `tv_tips_*` rows, matching `DT_TVTips`) replace `MI_TV_Black/Brown/Tan`, materials that live in
  the same `/Game/MP_4/` package as the TV's own `SM_TV_A01` mesh. Mapped in full; 3 of the 11 are
  also save-confirmed.
- **Office and cafeteria furniture**, each grounded by an exact mesh-name or material-family match
  to one item's `DeployHologramMesh`:
  - `Deployable_Chair_Office_01` ("Office Chair", mesh `SM_Chair_Office_01_Full`) gets
    `office_chair_black/blue/leather` and `Office_Chair_Order`, all `M_ChairSeat_*` materials.
  - `Deployable_Couch_Office_Armchair_01`, `_Long_01`, and the previously-uncataloged
    `_Medium_01` ("Two-Seater Office Couch") all get the full `office_couch_black/blue/grey/plaid`
    set: the fixture evidence already showed the same rows applied across the Armchair and Long
    items despite their differently-named meshes, so treating this as one shared family for the
    third sibling item is the same inference, not a new one.
  - `Deployable_Couch_fancy_01` ("Lounge Couch", mesh `SM_Couch_Fancy_01`) gets
    `couch_fancy_01_black/red/teal` (`M_Couch_Fancy_01_*`). The separate `Couch_Fancy_L` ("Fancy Mega
    Couch") has no matching rows and stays unmapped.
  - `Couch_Modern_01` (mesh `SM_MP_Couch_Modern_01`) gets `couch_modern_blue/gray/yellow`
    (`M_ModernSofa_*`).
  - `Deployable_Stool_Office_01` gets `stool_office_blue/red` (`M_Stool_Office_*`).
  - `Deployable_Fridge` ("Refrigerator", mesh `SM_Refrigerator_Office_01`) gets
    `fridge_office_blue/gray/red`; one instance (`_gray`) is save-confirmed. The separate
    `Fridge_Hazard` item has its own hologram mesh and no matching rows, so it is intentionally
    left out.
  - `Deployable_Table_Cafeteria` gets `cafeteria_table_blue/red`; the standalone `cafeteriatray` item
    gets `cafeteria_tray_beige/blue`.
  - `Deployable_Cot_Military` gets its one alt row, `military_cot_blue`.
  - `Bed_Res_01` ("Residential Bed", mesh `SM_RES_Bed_01`) gets `Bed_Res_01_blue/green/purple/redblack`
    (`M_RES_Bed_*`, an exact name match).
  - `bed_votv_sleepingbag` ("Sleeping Bag") gets `bed_votv_sleepingbag_02/03`
    (`M_votv_SleepingBag_02/03`, same asset family as the item's own `SM_votv_SleepingBag` mesh).
  - `rug_arcade` ("Arcade Rug") gets its one row, `rug_oval_arcade`.
  - `stapler` gets `stapler_blue` (`M_Stapler`, an exact mesh/material name match).
  - `Deployable_Toolbox` gets `toolbox_red` (`M_ToolBox_Bot/top_yellow`; the row is misleadingly named
    "red" but the materials are the toolbox mesh family and there is no other toolbox item).
  - `Deployable_WarningSign` gets `warningsign_rad` (`M_Deployable_RadiationSign`); the mesh name
    differs from the material name but both are the same unique hazard-sign item with no competing
    candidate.

### Still unmapped (and why)

- `painting_a_*` (11 rows, `M_WallArt_*` materials): no item table row or blueprint references
  `WallArt` by name or mesh. Candidate but ungrounded.
- `armchair_fancy_bloodstain` and `armchair_IS0018` (`M_ChairFancy_*` materials): the mesh name
  `ChairFancy` does not exactly match any catalogued armchair's own mesh name (`Armchair_Fancy_01`,
  `_02`, `Chair_Fancy_03`/`_04` also exist and were not individually probed). `armchair_IS0018` is
  additionally the fixed default appearance of the standalone `IS0018` item and must never appear in
  another item's picker regardless. Left out for both reasons.
- `Fridge_Hazard`: has its own hologram mesh (`SM_HazardFridge_Hologram`) with no matching
  `DT_TextureVariants` rows; not related to the `Deployable_Fridge` colors above.
- `Couch_Fancy_L` ("Fancy Mega Couch"): no rows match its `SM_Couch_Fancy_L_01` mesh specifically
  (the `couch_fancy_01_*` rows belong to the smaller `Deployable_Couch_fancy_01` item instead).

### Registry regeneration

The bundled `assets/registry/registry.json` (and its per-locale siblings) already contain the full,
current 575-row `DT_TextureVariants` dump, including every row referenced by the families added
above (`painting_L_JJ`, `tv_channel5`, `photoframe_acahn`, etc., all present). `CuratedRows` is
hand-written code in `ItemVariantCatalog`, not registry data, so this change needed no regeneration.
Regeneration would only be needed if a future game patch adds or renames `DT_TextureVariants` rows.
