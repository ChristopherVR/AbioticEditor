# Player and world editor review

Reviewed 15 September 2026. This records a browser interaction review and a comparison
with the game wiki, not a claim that every game version or live action has been tested.
Tests used a copy of the Cascade world. Original game saves were not written.

## Browser coverage

At 1440px and 390px, opened all 12 player tabs, 18 populated Facility tabs, and the five
metadata tabs. Selected representative entries wherever the tab has an item list.
Advanced data was inspected without importing JSON; achievement controls were inspected
without changing Steam achievements. Empty, missing-file, loading and spoiler states
were included. The live connection was not exercised in this review.

| Area | Interactions reviewed |
| --- | --- |
| General, Vitals | Account disclosure, survival and health controls |
| Character | Background and trait controls, selected trait details, appearance loading/missing-file state |
| Transmog | Selected an armor appearance slot |
| Inventory | Selected equipment, a weapon, and a backpack stack; inspected their slot controls and filtered catalog |
| Spawn | Coordinates, region, terminal and available bed choices |
| Companions | Selected a carried pet; name, variant, level, XP, health and bed-transfer controls |
| Skills | Expanded XP/perks and selected a milestone |
| Recipes | Selected a recipe and inspected ingredients, output and crafting station |
| GATEPal | Opened email, notes, compendium and fish entries |
| Achievements, Advanced data | Search and available actions; no external writes/imports |
| Containers, Ground items | Selected a container, its item slot, and a loose item's details |
| Doors, Story events | Selected entries and inspected state controls and help |
| Characters, Pets, Vehicles | Selected rows and inspected their detail editors |
| Bases | Selected a base, bench details and container controls |
| Buttons, Elevators, NPC Spawns, Power Sockets, Resource Nodes | Selected entries and inspected their available fields |
| Teleporter Pads, Trams, Triggers, World Teleporters | Selected entries and inspected their available fields |
| Metadata | Selected story chapter, trader, occupied containment unit and server-entitlement entry; inspected raw-data actions |

## Fixes from the review

- The carried-pet editor clipped wide fields on small screens. Fields now shrink within
  the panel, health controls wrap, and the mobile detail uses ordinary page scrolling.
- Carried pets, journal entries and ground-item names now have keyboard-operable selection
  controls. Pet inputs and journal checkboxes have accessible names.
- Selected states now emit explicit true/false ARIA values across the reviewed lists.
- GATEPal's date/count header and fourth section could disappear off the edge on a phone.
  Its small-screen header and four section buttons now wrap into readable rows.
- Recipe/journal detail close buttons stay compact. The sidebar says Details when showing
  a reference card, and Slot editor when editing an inventory slot.
- Appearance loading now has visible feedback even before an appearance save is discovered.
- Pet XP used a fitted curve with only the first and last levels anchored. Replaced it
  with the wiki's published intermediate thresholds: 20 XP is level 3, not level 2.
  Existing stored XP stays unchanged unless the user edits it. Boundary tests cover all
  20 levels. Source: [Pets](https://abioticfactor.wiki.gg/wiki/Pet#Leveling_Pets).

## Implementation follow-up (15 September 2026)

The review now has an implementation pass. See [the user guide](../../guide/review-features.md)
for steps and limits. Game schemas were checked in the installed paks, including blueprint
bytecode and the current mappings, rather than inferred from wiki labels.

- Coatings: installed-table picker, separate durability, shared player/container readers and
  writers. Existing values survive edits and upgrades; replacement/removal clears old coatings.
- Gardens: water capacity checks, per-spot fertilizer and named growth-stage/progress controls.
  A planting-spot selector keeps the detail panel short. Crop identity is read-only; digital
  plots and missing fields are excluded.
- Chemistry: three input readouts, output readout and a link to flask contents. ProcessingActive
  and ProcessingTimestamp are runtime properties; no offline batch timer is fabricated.
- Pets: expandable food/mutation guidance from DT_Pets, plus carried mutation-progress readout.
  Feeding countdowns and mutation-target writes remain unavailable without a verified contract.
- Power Chairs: a dedicated, storage-free battery panel with a 0-200 bound. The mapping follows
  RechargeableComponent's LiquidLevel and the Chair_Power item capacity. No real chair fixture
  was available; a constructed deployable-layout test covers the field writer.
- Characters: names resolve through NarrativeNPC_ConversationRow and DT_NPC_Conversations.NPCName.
  The stage picker preserves the six known values and unknown stored states. Semantic names for
  stages remain unavailable because the game supplies only per-character numbered phases.
- INI: Add a setting loads exact keys/defaults/types/options from DT_SandboxOptions, excludes
  existing and unimplemented entries, stages additions and retains backup/revert behavior.
  Verified current keys include ApocalypticAbilities and MaximizeEnemySpawns.

Coating indices follow DT_WeaponCoatings row order. Dynamic properties use WeaponCoating and
CoatingDurability. Garden growth uses GrowthStage (0-7) and GrowthProgress (0-10000) in item
proxies. Fertilizer is the spot-indexed PlayerMadeString sequence separated by `,|,`.
The implementation preserves raw tags and clones the existing complete type layout when an
item needs a new dynamic-properties array.

Browser checks used copied saves at 1440px and 390px. They exercised the coating picker,
pet guidance, garden selection, chemistry contents navigation, resolved character names and
adding/saving a sandbox default. In-game replication/rendering is still unverified for these
new offline controls. They must not be advertised as new live-editing capabilities.

## Original review recommendations

These recommendations were recorded before the implementation pass above. A wiki describes gameplay;
it does not establish the serialized field names, defaults or replication behavior needed
for safe editing. Each addition needs current save fixtures and a game verification pass.

| Priority | Addition | Current coverage and evidence | Next implementation step |
| --- | --- | --- | --- |
| High | Applied weapon coating and remaining coating durability | Coating items and chemistry recipes appear in the catalog, but the slot editor has no dedicated applied-coating fields. The wiki documents a separate coating durability and one coating per weapon. [Chemistry](https://abioticfactor.wiki.gg/wiki/Chemistry) | Compare the same weapon before/after coating and after wear; model the coating identity and durability together in Core, then expose a compact picker. Verify upgrade preservation. |
| High | Garden plot care | Seeds and plots are catalog items, and placed objects can be found through bases. There is no dedicated crop, water, growth or fertilizer editor among the registered world features. [Farming](https://abioticfactor.wiki.gg/wiki/Farming), [Water](https://abioticfactor.wiki.gg/wiki/Water) | Capture planted, watered, fertilized and harvested versions of one plot. Start with a readable crop/water summary before adding writes. |
| Medium | Chemistry bench contents and production state | Chemistry recipes and ordinary container slots already exist. There is no dedicated three-input mixture or production-progress editor. [Chemistry Bench](https://abioticfactor.wiki.gg/wiki/Chemistry_Bench) | Identify ingredient/flask storage and processing state in deployable save data; distinguish idle contents from a running batch. |
| Medium | Pet feeding and mutation progress | Both carried and world pets already support names, variants, levels and health. The carried model preserves MutationProgress and PetMutation but the pet form does not expose them. Feeding cooldown and mutation-food guidance are absent. [Pets](https://abioticfactor.wiki.gg/wiki/Pet) | Trace food identity, progress and last-fed fields together. Start with a progress readout and valid mutation choices, rather than unexplained numeric values. |
| Medium | Deployed Power Chair state | Generic item/catalog support should not be confused with a complete deployed-vehicle editor. The current world vehicle form covers drivable/wrecked state, position and storage, with no chair-specific power control. The wiki identifies the chair as battery-powered, deployable and without storage. [Power Chair](https://abioticfactor.wiki.gg/wiki/Power_Chair) | Verify its deployed actor and charge storage. Reuse item battery metadata where possible; omit storage actions for chairs. |
| Medium | Friendly story-character stages | The NPC form still exposes raw story-stage values and some generic actor names. Guessing labels could change the wrong dialogue stage. | Resolve names and valid stage meanings from installed game data, keep unknown states visible, and add a named picker only for verified mappings. |
| Medium | New sandbox settings discovery | The INI editor edits keys that are already present, but does not provide a catalog for adding omitted defaults. Recent patch notes list new enemy-ability and spawn-point options. [Community Update 4](https://abioticfactor.wiki.gg/wiki/1.4.0_Community_Update_4) | Extract exact INI keys, defaults and types from the installed version, then offer Add setting with short explanations. Do not infer keys from wiki display labels. |

## Existing support to build on

Recipes already include chemistry and soups. Skills expose XP, levels and perk details.
Pet species changes, companion slots, bed transfers, bench upgrades, vehicle recovery,
trader information, containment swaps, world flags and region-specific controls are already
implemented. Expanding these should reuse the shared Core models and writers, with live
support assessed separately from offline save support.

The wiki currently lists the 1.4 update family. Its changes to pets, recipes, compendium
entries and sandbox settings make catalog/version compatibility a continuing requirement,
not a reason to hard-code a second item list in the UI.
[Patch notes](https://abioticfactor.wiki.gg/wiki/Patch_Notes).

Some direct wiki requests returned HTTP 403. This review used the search engine's indexed
wiki pages for those articles and checked repository code to distinguish actual gaps from
features that already exist. No new field layout was inferred solely from wiki text.

## Verification result

111 focused pet, localization and UI contract tests passed. The final host build completed
with no warnings or errors. Final browser checks verified keyboard selection, the corrected
pet level at 20 XP, fitting pet fields, all four mobile GATEPal sections, and compact recipe
details. Appearance editing itself remains unverified: the copied world has no associated
account appearance save, and only its loading/missing-file surface was reviewed.
