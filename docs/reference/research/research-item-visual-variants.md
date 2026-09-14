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
