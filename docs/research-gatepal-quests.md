# Research: GATEPal UI, Inventory UI, and Story Chapters

Date: 2026-06-11. Sources: game paks via CUE4Parse (probe file
`tests/AbioticEditor.Tests/GatePalQuestProbeTests.cs`, all 8 probes passing) and
abioticfactor.wiki.gg (`/wiki/GATEPal`, `/wiki/Inventory`, `/wiki/Objectives`).
All colors below are `RRGGBB` (or `AARRGGBB`) sRGB hex exactly as serialized in the
widget blueprints. Texture paths are game refs usable with
`ExtractTextureByGameRef` (append `.uasset` for raw pak keys).

---

## 1. GATEPal (in-game journal/PDA) UI structure

### What the GATEPal actually is in-game

The handheld GATEPal item opens the **Journal tab** of the pause/inventory screen.
The whole experience is one widget: `AbioticFactor/Content/Blueprints/Widgets/Journal/W_Player_Journal_Main`
(embedded as page "Panel_Journal" of `W_PlayerInventory_Main`; the nav-bar tab icon
is `Textures/GUI/Icons/icon_pda`). It is **not** green-on-dark: the PDA screen is a
*light paper page with near-black ink*, dark-green/teal accents, and a cyan title
floating on the dark blurred backdrop above the page.

### Frame & chrome (W_Player_Journal_Main, 1920x1080 design canvas)

| Region | Widget | Details |
|---|---|---|
| Page background | `JournalPageBG` Image | `Textures/GUI/Journal/journal_pda_bg` (1301x909) at offset (87,130), tint `EAEAEA` - the rounded PDA "screen" panel |
| CRT noise overlay | `JournalNoiseLayer` Image | `Textures/GUI/Journal/journal_pda_noiselayer`, same rect, 75% opacity, tint `EAEAEA` - gives the screen its grain |
| Title | `TitleMain` TextBlock | "JOURNAL", **Poppins-Black 56**, color `8CFFFB` (cyan), top-left (116,4) - sits on the dark backdrop above the page |
| Subtitle | `SubTitle_RecipeTypeText` | "YOUR FINDINGS", TeX Gyre Adventor, `C6C8C8` |
| Date/status bar | y≈199–240 on the page | Weekday letters `S M T W T F S` (Poppins-Black 20; current day `4B7667` dark green, others ink `101010`); underline strip `journal_pda_datebar_<monday..sunday>` (1155x36, tint `101010`) - one texture per weekday with the highlight baked in; `Text_Date` "Mar 7, 93" in `4B7667`; `Text_Clock` "7:00 am" right side (x≈1215) ink `101010`, TeX Gyre Adventor 20 |
| Back button | `BackButton` CheckBox | left edge (48,251), rounded box, border color `77CEC8`, glyph `Textures/GUI/Icons/icon_arrow_left` |
| Content area | `JournalWidgetSwitcher` | (114,144)–(1799,922) inside the page |

**Pages of `JournalWidgetSwitcher`** (the GATEPal's "apps" at the top level):
0. `Emails_Main` (W_Player_Emails_Main_C)
1. `Compendium_Main` (entry list + article view)
2. `JournalCanvasPanel` - per-sector **Notes** page (sector icon 78px from `Textures/GUI/SectorIcons/icon_*_128`, big sector title Poppins-Black 28, horizontal rule `101010`, scrolling note entries left, up to 5 `W_Player_Journal_MapCard_C` map cards right)
3. `JournalMapCanvas` - **Facility map** page (see below; this is where the quest text lives)
4. `W_Compendium_Index` - the **app grid** (see below)
5. `W_Compendium_FishDatabase`
6. `W_Compendium_SoupRecipes`
7. `W_Compendium_ChemistryRecipes`
8. `W_Compendium_Distillations`

### The app grid (W_Compendium_Index) - the "GATEPal home screen"

- Dark rounded panel `BG`: RoundedBox, corner radius 45, color `CC272727`
  (charcoal at 80% alpha) centered on the page (offsets L371/T380).
- `LOGO` image above the grid: `Textures/GUI/Compendium/T_Compendium_Title`
  tinted `9A999A`.
- `UniformGridPanel` of 10 app tiles (`W_Compendium_Index_Button_C`), each tile =
  145x145 icon Button + uppercase label **below** (TeX Gyre Adventor 18, color
  `9A999A`, `ToUpper` transform). Button tint states: normal `CCFFFFFF`, hover
  `FFFFFF`, pressed `9D9D9D`; click sound `os_mouse_click`. Unread-content badge:
  18px dot `Textures/GUI/Wristwatch/icon_wristwatch_dot` tinted `F89A4F` (orange),
  top-right of tile, flicker animation.

App tiles (grid row/col from UniformGridSlot; fine position nudged via
RenderTransform; matches the wiki's app list):

| Label | Grid (row,col) | Icon texture (`/Game/Textures/GUI/Compendium/...`) |
|---|---|---|
| SOUP RECIPES | 0,0 | `T_Compendium_Soups` |
| E-MAIL | 0,1 | `T_Compendium_Email` |
| ENTITIES | 0,2 | `T_Compendium_Entities` |
| LOCATIONS | 0,3 | `T_Compendium_Locations` |
| CHEMISTRY | 0,4 | `T_Compendium_Chemistry` |
| REGISTRY | 1,1 | `T_Compendium_Registry` |
| PEOPLE | 1,2 | `T_Compendium_People` |
| THEORIES | 1,3 | `T_Compendium_Theories` |
| DISTILLATIONS | 1,4 | `T_Compendium_Distillation` |
| FISH | 2,0 | `T_Compendium_Fish` |

(Also available: `T_Compendium_NoSoup`, `T_Compendium_Border` - the article image
frame - and ~200 `Entries/T_Compendium_<Subject>` 512x512 article illustrations.)

### Compendium content view (W_Compendium_Main + Entry/Section)

- Left column (x45, ~235 wide): `ListView_CompEntries` of `W_Compendium_Entry`
  rows over a 50%-black image strip. Each row is 234x38: title (TeX Gyre
  Adventor 12, ink `101010`), italic subtitle + "0 / 1" section count (10pt),
  white `Border` underline; row highlight CheckBox whose *checked* color is
  `F89A4F` (orange); unread badge ring `itemslot_rounded_bg_nodepth` tinted `F89A4F`.
- Header: `CompendiumTitleText` "{COMP_TITLE}" Poppins-Black, ink `000000`,
  centered over the content column.
- Right column (281–1640): `Scrollbox_CompContent` of `W_Compendium_Section`
  blocks - a 0.65/0.55 two-column HorizontalBox: body text left (TeX Gyre
  Adventor 14, ink `101010`), image right (512x512 entry texture inside
  `T_Compendium_Border` frame tinted `000000`, optional resistance/weakness
  icon row above).

### E-mail app (W_Player_Emails_Main)

Header "E-MAIL ARCHIVE" (Poppins-Black, `000000`); left scroll list of email
entries (x45, ~205 wide); right reading pane (x259, ~890 wide) with body text
TeX Gyre Adventor 15 ink `000000`; faint GATE logo watermark
(`Textures/Decals/T_Signage_Gate_02`, 10% opacity, tint `212121`) behind the
text; bottom attachments bar (black image strip + `HBOX_Attachments`,
placeholder "NO ATTACHMENTS" in `4B7667`); full-screen attachment viewer overlay.

### Facility map page (JournalMapCanvas) - quest display lives here

- `FacilityBGMap`: `Textures/GUI/Journal/journal_facilityshadow_6_bg`
  (1024x1024) tinted `101010` - the facility silhouette drawn as ink on the
  paper page (variants `journal_facilityshadow_bg` ... `_6_bg` exist for the
  sector-unlock stages; pairs with `NumberOfAreasUnlocked` below).
- Dozens of `W_Player_JournalSectorTab_C` pins: sector tabs (Office, MF, Labs,
  Security, Dam, Plant, Residence...), portal-world tabs (`V_Flathill_Tab`,
  `V_AnteverseA/B/C`, `V_Voussoir_Tab`, `V_Canaan`, ...) and trader pins
  (`Trader_Warren`, `Trader_Blacksmith`, dispensers...).
- **Quest panel** top-right (x701..1194, y106): `Quest_text` (TeX Gyre Adventor
  18) + `Quest_description` (14), ink `101010` - the in-game "current chapter"
  text driven by story progression.
- Compendium launcher button top-left: `icon_journal_lowbit` tinted `DDFFF2`
  on `CC272727`; waypoint mode dropdown; pin counter + `icon_pin`; corpse
  waypoint clear button (`icon_sadskull`).

### Palette / typography cheat-sheet for the XAML rework

| Token | Value | Used for |
|---|---|---|
| Paper | `EAEAEA` | PDA page bg tint |
| Ink | `101010` | All on-page text/map silhouette |
| PDA green | `4B7667` | Active weekday, date, secondary labels |
| Teal chrome | `77CEC8` | Back button border |
| Cyan accent | `8CFFFB` | "JOURNAL" title, active nav icons |
| Icon teal (inactive) | `4D7F78` | Nav-bar tab icons |
| Charcoal panel | `CC272727` | App-grid panel, compendium launcher |
| Tile gray | `9A999A` | App labels, compendium logo |
| Unread orange | `F89A4F` | Unread dots, selected entry highlight |
| Warning red | `FD7070` | "no maps" text, close buttons |
| Fonts | Poppins Black / TeX Gyre Adventor | headings / body (both free fonts) |

---

## 2. In-game inventory UI structure

Main widget: `AbioticFactor/Content/Blueprints/Widgets/Inventory/W_PlayerInventory_Main`
(1920x1080 ScaleBox). Backdrop = `BGBlurClickBlocker` BackgroundBlur over the 3D
world + `BG_BlueTint` (tint `08FDE3` at 14% opacity) - the signature blue-teal
wash. Close button top-right (icon_arrow_left tinted `FD7070`).

### Top-level layout

- **Nav bar** (`W_Inventory_NavBar`), anchored top-right, 732x129, bg
  `Textures/GUI/Inventory/Inventory_NavBar` at 43% opacity. Toggle tabs
  (left->right): Crafting `icon_crafting`, Health `icon_health_status`, Skills
  `Icons/icon_skills`, Journal `Icons/icon_pda` (+ notify dot
  `BuffIcons/buff_circle_pale`), Backpack `icon_backpack_inverse`. Inactive
  icons `4D7F78`, active `8CFFFB`; flanked by Q/E keybind chips and
  `icon_uparrow` arrow buttons tinted `8CFFFB`.
- **Page switcher** `WS_Primary`: Panel_Primary (inventory), Panel_Crafting,
  Panel_Journal (hosts `W_Player_Journal_Main` - the GATEPal), Panel_Skills,
  Panel_Health, Panel_TraderScreen.
- **Hotbar** `W_PlayerHUD_Hotbar` bottom-center (slots 1-8, fanny pack adds
  9/0; wiki: hotbar items weigh 75% of normal).
- `W_SplitStackMenu` overlay; crafting reward popup; generic drop area strip
  bottom-center ("drop item here").

### Panel_Primary (the inventory page proper)

| Region | Position | Contents |
|---|---|---|
| Paper doll (left) | glow image at (153,151) 413x691 | `Inventory_CharacterBGGlow` tinted `9CF6FF` behind the live 3D character (`W_Inventory_3DCharacter`); `W_Inventory_EquipSlots` overlays the equipment slots around it (slot indices documented in research-transmog-appearance.md) |
| Backpack panel (center) | (684,36), 592 wide | `W_Inventory_PlayerBackpack` (below) |
| Right panel | (1342,132), 564x806 | `WS_PrimaryRightPanel` switcher: Container / ArmorStand / RepairArea / Transmog / UpgradeBench; behind it `Inventory_TestTube_Colored` (512x1024) at 13.6% opacity |

### Backpack panel anatomy (W_Inventory_PlayerBackpack)

- Header "POCKETS" - Poppins-Black, color `71C5F6` (the inventory's light blue).
- Pane background `CharacterPaneBG`: RoundedBox tint `306481` at 35% opacity,
  3px outline `5292B7`, corner radii (0,25,25,0).
- Row 2: `W_InventoryWeight` weight bar (below) + `MoneyText` "MONEY: ${0}" and
  `WeightNumberText` "12 / 30" (TeX Gyre Adventor 15, `71C5F6`).
- Sort button: 9-slice `Inventory_Button` bg (BackgroundColor `5292B7`) with
  `Icons/icon_resources` glyph tinted `71C5F6`, tooltip "SORT".
- Grid: `ScrollBox` at (16,136) 573x640 containing `W_BackpackGrid`
  (`W_InventoryGrid_C` of `W_InventoryItemSlot_C`; base 12 "pocket" slots,
  expanded by equipped backpack per wiki).
- Backpack equip slot (84px, top-left of panel) + its transmog toggle button.
- Bottom: `W_ButtonPromptHelperBar` (contextual key prompts).

### Weight bar (W_InventoryWeight)

311x14 rounded ProgressBar pair: `WeightBar` (white fill) + overlaid
`CurrentItemWeightBar` whose fill texture is
`Textures/WallTrim/T_WallTrim_WarningStripe_Orange` (hazard stripes = weight of
the hovered/selected item). Threshold markers `Icons/icon_encumbrancemarker`:
"heavy" tick tinted `FFE563` (~53% along), "encumbered" tick tinted `FF7979`
(~114px); scale icon `icon_weight` tinted `71C5F6` at the right end.

### Item tooltip anatomy (W_InventoryItemSlot_Tooltip)

Border `CC000000` (80% black), 10px padding, vertical stack in this order
(everything TeX Gyre Adventor unless noted; hidden rows collapse):

1. Item name - Poppins-Black 12, `8CFFFB`
2. Description - 12, `A7A7A7`
3. Buff list (`BuffApply_VBox`)
4. Weight - 10, `C8C8C8`
5. 1.5px dividers - `65817F`
6. Equip-slot/tag type - 10, `80B4B3`
7. Flavor text - italic 10, `8CFFFB`
8. Stat block (12pt, gray `C8C8C8` for damage/armor/capacity/hunger/thirst/
   fatigue/durability) with color-coded special rows: cold resist `0080F2` +
   `icon_snowflake`, heat resist `FF5F00` + `icon_fire`, cooking/soup `FF9F52`,
   fishing/liquid/coating `449CC8`, craftable `71FF75` + skill icon, plantable
   `71FF75`, paintable `AD7BFF`, upgrade available `3D6BFF` + `icon_upgradearrow`,
   broken `FD7070` + `icon_break`, radioactive `FFEA19` warn / `00FF21` food +
   `icon_radioactive_color` / `icon_radiation`
9. Salvage note - 10, `A5A5A5` ("CAN BE SCRAPPED AT REPAIR BENCH")

Wiki cross-check (/wiki/Inventory): 12 base pocket slots, hotbar 1-8 (+9/0 with
fanny pack, 75% weight in hotbar), armor/gear panel left with character preview,
containers use an orange panel with F/Q/R quick actions, death drops main
inventory only (corpse bag) while gear+hotbar are kept.

### Extractable inventory chrome textures (`/Game/Textures/GUI/...`)

`Inventory/Inventory_NavBar`, `Inventory/Inventory_Button`,
`Inventory/Inventory_CharacterBGGlow`, `Inventory/Inventory_TestTube_Colored`,
`Inventory/Inventory_SkillPip_{Active,Disabled,Empty,Fill}`,
`Inventory/T_ItemUpgrade_BG(_Em)`, `Inventory/T_Repair_Salvage_BG`,
`Inventory/crafting_warning_bg`, `Inventory/EquipSpeed_Weight*` (analog scale
parts), `icon_weight`, `Icons/icon_encumbrancemarker`, `Icons/icon_resources`,
`icon_crafting`, `icon_health_status`, `Icons/icon_skills`, `Icons/icon_pda`,
`icon_backpack_inverse`, `icon_uparrow`, plus the empty-slot glyphs already
catalogued in research-transmog-appearance.md.

---

## 3. Story chapters (DT_StoryProgression, 37 rows)

Source of truth: `AbioticFactor/Content/Blueprints/DataTables/DT_StoryProgression`
(full dump in Probe5). Columns per row - there is **no description text in the
table**; the player-facing wording below is synthesized from the wiki's
Objectives page and flag names:

- `WorldFlag_2_*` (DataTableRowHandle) - the WorldFlag that, once present in a
  region save's `WorldFlags` array, advances `WorldSave_MetaData.StoryProgressionRow`
  to this row.
- `StoryImage_18_*` (SoftObjectPath) - `/Game/Textures/GUI/ServerBrowser/map_*`
  chapter card art (great for the quest UI; e.g. `map_office1`, `map_dflabs`).
- `DisplayText_17_*` (FText) - the region/sector heading shown in-game.
- `NumberOfAreasUnlocked_16_*` (int) - drives map silhouette stage / unlock count.

The save's `StoryProgressionRow` stores the **row name** (e.g. `MFBlacksmith`).
Index = position below (0-based, row order).

| # | Row | Region (DisplayText) | Trigger WorldFlag | Card art | Areas | Player-facing description |
|---|---|---|---|---|---|---|
| 0 | Office | The Office Sector | Office_NewGameStarted | map_office1 | 1 | Containment breach day. You wake up in the Office Sector, scavenge your first tools, let Dr. Jager into the cafeteria and report to sector security officer Warren. |
| 1 | Office2 | The Office Sector | Office_InformationFound | map_office2 | 2 | You've learned the only way out is through Manufacturing - but a forklift holding the blast door needs Power Cells. Grayson points you to the energy research team. |
| 2 | Office3 | The Office Sector | Office_ThirdFloorReached | map_office3 | 4 | You reach the Office Sector's third floor and convince Archie Roberts (or Regal) to open Silo 3, where the Power Cells were taken. |
| 3 | Flathill | The Office Sector | Office_Silo3Opened | map_office3 | 5 | Silo 3 is open. All signs point through the portal inside: step into Flathill, a fog-drowned suburban anomaly, to recover Power Cells. |
| 4 | PostFlathill | The Office Sector | Fog_Completed | map_office3 | 5 | You survived Flathill and can now farm Power Cells there. Power the forklift and raise the blast door into Manufacturing. |
| 5 | MF | Manufacturing West | MF_ManufacturingOpen | map_MFStart | 6 | Manufacturing West is open. Somewhere in this sector is a tunnel to the surface - explore and find your way out. |
| 6 | MFBlacksmith | Manufacturing West | MF_MetBlacksmith | map_MFStart | 6 | You meet the Blacksmith (Varsha), who knows the sector: the Surface Tunnel is the main heavy-traffic route out. |
| 7 | MFMines | Manufacturing West | MFMines_Entered | map_MFStart | 7 | The Surface Tunnel is blocked. You descend into the mines beneath Manufacturing to find Frake, who may know another way. |
| 8 | MFFrake | Manufacturing West | MF_MetFrake | map_MFStart | 8 | You found Frake. The plan: get the Blacksmith's help forging parts to repair the sector's three electron pumps. |
| 9 | MFTrain | Manufacturing West | MF_OpenTrainStation | map_MFStart | 9 | The train station is open, connecting Manufacturing's far reaches while you gather pump materials. |
| 10 | MFPumpsFixed | Manufacturing West | MF_PumpsFixed | map_MFStart | 9 | All three electron pumps (yellow, green, red) are repaired. Time to activate the Synchrotron and overload it to blast an exit. |
| 11 | Pens | Cascade Laboratories | MF_ExitOpened | map_labs | 10 | The synchrotron overload opened the way out of Manufacturing. You emerge into Cascade Laboratories through the holding pens. |
| 12 | Labs | Cascade Laboratories | Labs_MiddleProgression | map_labs | 11 | Explore the Labs Sector for a way out; survivors suggest the Inner Wing containment blocks hold what you need. |
| 13 | Containment | Cascade Laboratories | Labs_Containment_Entered | map_labs | 12 | You're inside the Inner Wing containment blocks - deadly experiments, turrets, and the route toward the Control Center. |
| 14 | Helmholtz | Cascade Laboratories | Labs_Helmholtz_Opened | map_labs | 13 | The Helmholtz Wing is open, the path to the Control Center where the security system (and its turrets) can be reset. |
| 15 | Tarasque | Cascade Laboratories | LABS_TurretsDeactivated | map_labs | 14 | Via the Furniture Store anomaly you reached the Control Center and reset security - the turrets are off (and the Tarasque met). |
| 16 | Mycofields | Cascade Laboratories | LABS_AnteverseBFixed | map_labs | 15 | The Anteverse 2 portal is fixed: gather chemicals in the Mycofields' "Shroom" zone to make Antethermite for the Vacuum Chamber door. |
| 17 | PostLabs | Security Sector | LABS_OpenVacuumDoor | map_security1 | 16 | The vacuum door is breached - you infiltrate the Security Sector, aiming for the Large Surface Elevator, one of the facility's most secure exits. |
| 18 | SecSurfaceElevator | Security Sector | Security_SurfaceElevatorEvent | map_security2a | 17 | The surface elevator crashes catastrophically. Stranded, you explore Canaan for ingredients and open the remaining gates to escape the sector. |
| 19 | EndSecurity | Hydroplant | Security_ExitOpened | map_dam1 | 18 | The Security Sector exit is open; there's only one way forward - down into the Hydroplant. |
| 20 | ElectricalStation | Hydroplant | Dams_ReachedCentral | map_dam2 | 18 | You reach the central dam. Survivors in the Electrical Station (Jonas) can help - reactivate the station, reboot the spillway computer, and open the flow controls. |
| 21 | Voussoir | Hydroplant | Voussoir_Entered | map_dam3b | 19 | You step through to Voussoir, the drowned cathedral anteverse reachable from the Hydroplant. |
| 22 | EndDam | Hydroplant | Dams_SpillwayOpen | Map_Dam4 | 20 | The spillway is open. The only way out of the Hydroplant is wet: ride the water down toward the Reactors. |
| 23 | PowerServices | The Reactors | Plant_EnteredPlant | map_powerservices | 21 | You enter Power Services. A massive fungal bloom blocks the way to the Reactors - you need something to kill it. |
| 24 | AnteverseC | The Reactors | Plant_AnteC_Entered | map_anteversec | 22 | You enter Anteverse C (the Far Garden) to gather what's needed for Anti-Fungal Gelatin. |
| 25 | ReactorsEntry | The Reactors | Plant_ExitOpened | map_reactorsentry | 23 | The bloom is destroyed and the way into the Reactor Sector is open - you hope that was the right thing to do. |
| 26 | Reactors1Labs | The Reactors | Reactors_FirstContact | map_dflabs | 24 | First contact in the Deep Field labs: Dr. Cahn explains the Gatekeeper's exit waygate needs four reactors online. Activate Dusk Reactor and report back. |
| 27 | ReactorsAll | The Reactors | Reactors_S1Labs_Complete | map_radwaste | 25 | The S1 labs are searched (Cloud Reactor explored). Now bring Gale, Mist and Cloud Reactors online to join Dusk. |
| 28 | Shadowgate | The Reactors | Reactors_SG_Opened | map_shadowgate | 28 | All four reactors power the Fusion Generator's central waygate: the Shadowgate. Inside the mirrored Intrados facility, destroy the five containment devices and expel the entity. |
| 29 | InqEnd | The Praetorium | Reactors_SG_End | map_inq2 | 29 | The creature fled through a massive rift. Beyond it lies somewhere immense and old - with Hasta's guidance, enter the Praetorium (IS-0101) and locate the Sun Disk. |
| 30 | Residence | Residence Sector | V_INQ_SunDiskTouched | map_residence1 | 30 | The Sun Disk's gift lets you endure the cold at last: enter the frozen Residence Sector. |
| 31 | Residence2 | Residence Sector | Res_Objective1_Complete | map_residence2 | 31 | With Abe and Janet's help the storm suppressor is fixed; the upper floor is searchable, but a frozen ice wall bars the way ahead. |
| 32 | Fracture | Residence Sector | Residence_IceWallRemoved | map_fracture1 | 32 | An intense heat source melted the ice wall - you push into the Fracture, hunting the Dark Lens that can get everyone out. |
| 33 | Botanical | Residence Sector | Res_EnteredBotanicals | map_botanical | 33 | You enter the Botanical Gardens, the overgrown heart of the Residence Sector's upper reaches. |
| 34 | DarkLens | Residence Sector | Residence_Wall | map_fracture2 | 34 | Past the wall lies the Dark Lens. Collect its fragments, face The Fallow, and open the way out of the facility at last. |
| 35 | SouthIsland | Residence Sector | Residence_Fracture_Complete | map_southisland | 35 | The Fracture is behind you: you arrive at the South Island, whose altar can supposedly take you anywhere - Dr. Cahn and Thule have thoughts. |
| 36 | EndGame | Main Story Complete | EndBossDefeated | map_endgame | 35 | The Wayseeker is defeated. The end? Talk to Dr. Cahn, Janet, and the Sister of the Unlost. Main story complete. |

Notes for the quest UI:

- Chapter card art lives at `/Game/Textures/GUI/ServerBrowser/map_<name>` - one
  per chapter (some shared), ideal thumbnails for a linear quest list.
- The in-game quest panel (Journal map page) shows only a short
  `Quest_text`/`Quest_description` pair - our richer per-chapter blurbs above are
  a superset, written from the wiki's objective texts.
- Region grouping for the UI: Office (0–4), Manufacturing West (5–10), Cascade
  Laboratories (11–16), Security (17–18), Hydroplant (19–22), The Reactors
  (23–28), The Praetorium (29), Residence (30–35), Complete (36). Portal-world
  chapters (Flathill, Voussoir, AnteverseC, Shadowgate, Fracture, SouthIsland)
  belong to the region whose DisplayText they carry.
- Triggers: writing the chapter's WorldFlag row name into the owning region's
  `WorldFlags` array is what the game itself keys on; `StoryProgressionRow` in
  `WorldSave_MetaData.sav` is the cached current row name.

---

## Appendix: richest extractable texture sets (game refs)

- GATEPal chrome: `/Game/Textures/GUI/Journal/journal_pda_bg`,
  `journal_pda_noiselayer`, `journal_pda_datebar_{monday..sunday}`,
  `journal_facilityshadow_bg` ... `journal_facilityshadow_6_bg`
- App icons: `/Game/Textures/GUI/Compendium/T_Compendium_{Soups,Email,Entities,Locations,Chemistry,Registry,People,Theories,Distillation,Fish,Title,Border,NoSoup}`
- Compendium article art: `/Game/Textures/GUI/Compendium/Entries/T_Compendium_*` (~200)
- Sector icons (notes page/map): `/Game/Textures/GUI/SectorIcons/icon_{office,manufacturing,labs,security,dam,plant,residence,intro,ante_a,ante_b,ante_c,canaan,fog,inq,island,mirror,night,rise,shore,signal,suomi,tile,train,wall,warehouse,alps,spacequeen}_128`
- Chapter cards: `/Game/Textures/GUI/ServerBrowser/map_*` (one per DT_StoryProgression row)
- Inventory chrome: `/Game/Textures/GUI/Inventory/*` (see section 2),
  `/Game/Textures/GUI/Icons/icon_pda`, `icon_pda_invertedalpha`,
  `/Game/Textures/GUI/Hud/UI_ItemNotify_BG_PDA`
- Misc accents: `/Game/Textures/GUI/Wristwatch/icon_wristwatch_dot` (unread),
  `/Game/Textures/GUI/icon_journal_lowbit`, `icon_pin`, `icon_sadskull`
