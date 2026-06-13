# Wiki Research - Round 10

Source: https://abioticfactor.wiki.gg (fetched 2026-06-11).
Topics: (1) skill milestones, (2) character customization / transmog / respawn / teleport, (3) item-page field inventory for our item detail view.

---

## 1. Skills and Milestones

### Canonical skill list (15 skills, 3 categories)

The wiki ([/wiki/Skills](https://abioticfactor.wiki.gg/wiki/Skills)) groups the 15 skills as:

| Category | Skills |
|---|---|
| **Fitness** | Sprinting, Strength, Throwing, Sneaking |
| **Combat** | Blunt Melee, Sharp Melee, Accuracy, Reloading, Fortitude |
| **Survival** | Crafting, Construction, First Aid, Cooking, Agriculture, Fishing |

**Naming corrections vs. the list in the task prompt:** there is no single "Melee" skill (it is split into **Blunt Melee** and **Sharp Melee**), "Stealth" is **Sneaking**, "Gardening" is **Agriculture**, and there is **no Piloting skill**. This matches our positional-skill finding: 15 real rows in `DT_Skills` (path `AbioticFactor/Content/Blueprints/DataTables/Customization/DT_Skills`) after dropping the two DONOTUSE rows.

### Max level and XP mechanics

- Max skill level is **20**. All skills share the same XP curve.
- XP is earned by performing the skill's action ("The player can level up their skills by acquiring XP (experience points) when doing actions specific for each skill.").
- XP gain modifiers (e.g. the Wrinkly Brainmeat trait) **stack multiplicatively**: two +20% bonuses on 1 XP yield 1.44 XP (1 × 1.2 × 1.2).
- Cumulative XP thresholds (level -> total XP required):

| Lvl | XP | Lvl | XP | Lvl | XP | Lvl | XP |
|---|---|---|---|---|---|---|---|
| 1 | 200 | 6 | 3,699 | 11 | 18,242 | 16 | 51,631 |
| 2 | 500 | 7 | 5,379 | 12 | 23,307 | 17 | 60,608 |
| 3 | 940 | 8 | 7,587 | 13 | 29,101 | 18 | 70,354 |
| 4 | 1,572 | 9 | 10,417 | 14 | 35,776 | 19 | 80,755 |
| 5 | 2,464 | 10 | 13,950 | 15 | 43,310 | 20 | 91,655 |

(The 91,655 L20 threshold is the same value we verified against capped end-game saves; see player-save-schema.md.)

### Milestone perks per skill

Milestone levels are **irregular per skill** (not a uniform 5/10/15/20) - the milestone-track UI must take a per-skill list of levels. Perk names and effects below are the wiki's wording, condensed.

#### Sprinting (Fitness)
Per-level passive: +2 max stamina, +1% sprint speed, +2% stamina regeneration.

| Level | Perk | Effect |
|---|---|---|
| 5 | Athletic | Chance to not lose stamina for any action |
| 10 | Lightspeed | Along with passive gains, you sprint 5% faster overall |
| 15 | Red Shift | While sprinting, enemies are more likely to miss when targeting you |
| 20 | Out Of My Way! | Sprinting speed increases over several seconds |

#### Strength (Fitness)
Per-level passive: +2 carrying capacity.

| Level | Perk | Effect |
|---|---|---|
| 5 | Step Aside | Shake Vending Machines & stomp Carbuncles in 1 hit |
| 8 | Heavy Weapons | Strong enough to properly wield heavy melee weapons |
| 12 | Nerd Rage | When bleeding near a hostile enemy, melee attack damage and speed are enhanced |
| 15 | Heavy Armor Specialization | Can wear any weight of armor |
| 20 | Superior Gains | All items weigh 25% less |

#### Throwing (Fitness)
Per-level passive: +10 projectile velocity, +2 damage.

| Level | Perk | Effect |
|---|---|---|
| 2 | Underhand Toss | Gentle underhand toss with all throwable items |
| 5 | Projectile Pickup | Automatically pick up projectiles you've thrown |
| 8 | Hazy Recollection | Weapons thrown by you are highlighted for 30 seconds |
| 10 | Projectile Predictor | See the predicted path of your throwables |
| 12 | Tinkerer | Crafting throwables yields 1 additional item |
| 15 | Terminal Velocity | Hitting enemies with a thrown weapon staggers them |
| 20 | Quantum Displacement | Thrown items sometimes split into 2 additional projectiles |

#### Sneaking (Fitness)
Per-level passive: −5% enemy detection speed (caps at 95%).

| Level | Perk | Effect |
|---|---|---|
| 2 | Sneak Attack | First attack on an idle enemy has 25% chance to deal double damage |
| 5 | Biotic Shadow | Move 15% faster while crouched; can perform a roll |
| 8 | Nimble | Don't trigger Tripwires, Tripwire Lasers, or Carbuncles |
| 10 | Night Worker | Much quieter footsteps; increased Sneak Attack chance at night |
| 15 | Office Assassin | Attacks on unaware enemies always deal double damage |
| 20 | Interdimensional | Enemy attacks just sometimes... miss? |

#### Blunt Melee (Combat)
Per-level passive: +1 blunt damage.

| Level | Perk | Effect |
|---|---|---|
| 3 | Power Attack | Heavy windup attacks with blunt melee weapons |
| 7 | Battle Charge | Attacking during a sprint becomes a driving power attack |
| 10 | Stunning Slam | Stagger enemies more easily with blunt melee |
| 12 | Power Hungry | Power Attacks use 50% less stamina per swing |
| 15 | Crusher | Enemies resistant to Blunt... aren't |
| 20 | Smash | Chance to instantly explode human-sized or smaller enemies |

#### Sharp Melee (Combat)
Per-level passive: +1 sharp damage.

| Level | Perk | Effect |
|---|---|---|
| 3 | Sharp Throwing | Can throw sharp melee weapons |
| 5 | Clean Cutter | 25% higher chance to not end up with Bio Scrap (butchering) |
| 9 | No Survivors | Enemies killed by sharp melee attacks outright die (no second forms) |
| 12 | Slice n' Dice | Higher chance to cause Severe Bleeding |
| 15 | Eviscerator | Cut through Sharp-resistant enemies as if not resistant |
| 20 | Heartseeker | Very small chance to instantly kill small/medium enemies |

#### Accuracy (Combat)
Per-level passive: −0.075 aim sway, −0.05 bullet spread.

| Level | Perk | Effect |
|---|---|---|
| 5 | Squint | Alt-fire while aiming a ranged weapon zooms vision |
| 7 | Mil-Spec / Bio-Metric Armwraps | Confidence to handle advanced firearms (recipe unlock) |
| 10 | Straight as an Arrow | Projectiles less likely to break on impact |
| 13 | Boomstick | Smaller creatures may flee when you fire |
| 15 | Bio-Mimic Armwraps Recipe | Advanced Bio-Mimic Armwraps recipe |
| 18 | Bio-Fusion Imitator Recipe | Advanced Bio-Fusion Imitator trinket recipe |
| 20 | Stopping Power | Projectiles/bullets have a small chance to stun the target |

#### Reloading (Combat)
Per-level passive: faster reload speed (magnitude unspecified on wiki).

| Level | Perk | Effect |
|---|---|---|
| 3 | Ammo Crafter | Craft ammo twice as fast |
| 5 | Just In Case | Reload weapons you aren't otherwise qualified to use |
| 10 | Basic Geometry | Less clumsy at reloading all weaponry |
| 15 | Speedloader | Sprint and reload at the same time |
| 20 | Loose Rounds | Sometimes find spare rounds when reloading |

#### Fortitude (Combat)
Per-level passive: +2 max health per level (wiki phrases this around limb/head health; the skill "increases health points").

| Level | Perk | Effect |
|---|---|---|
| 5 | Habituation | Regenerate health a bit more frequently |
| 8 | Group Effort | Resting within 8 m of other resting scientists doubles rest rate |
| 10 | Spongy Tissue | Slightly reduced fall damage and vehicle impact damage |
| 15 | Reflective Mantle | Melee contact may reflect damage back to the enemy |
| 20 | Strong Ecosystem | Regenerate 1 health every second |

#### Crafting (Survival)
Per-level passive: crafting time reduced; −2% chance of crafted-item durability loss per level.

| Level | Perk | Effect |
|---|---|---|
| 2 | More Bench | First set of Crafting Bench upgrades |
| 5 | Mega Bench | Second tier of Crafting Bench upgrades |
| 8 | Beautiful Blueprints | Recipes shared with you skip the research phase |
| 10 | Eye For Detail | Crafted items gain temporary bonus durability |
| 15 | Super Bench | Final tier of Crafting Bench upgrades |
| 20 | Precision Engineering | Small chance to consume 1 less item in multi-item recipes |

#### Construction (Survival)
Per-level passive: faster building/deconstructing (wiki gives no numbers).

| Level | Perk | Effect |
|---|---|---|
| 5 | Pack Your Desk | Package small deployables twice as fast |
| 10 | Razed With Care | 50% chance of double resources when dismantling |
| 15 | Castle Doctrine | Fortification build costs reduced by half |
| 20 | Spontaneous Furniture Event | Chance of double furniture when packaging non-player-built items |

#### First Aid (Survival)
Per-level passive: medical items heal more (magnitude unspecified).

| Level | Perk | Effect |
|---|---|---|
| 3 | Refreshing Touch | Healing teammates also grants them +5 hydration |
| 4 | Rad Remover | Recipe: Pentetic Acid Syringe (radiation removal) |
| 5 | Bedside Manner | See others' medical debuffs when close |
| 8 | Bonesetter | Applied splints heal bones faster |
| 11 | Aftercare | Revived teammates get a healing buff; +20% boost when using a medical item |
| 15 | Combat Doctor | Apply medical items to self and others twice as fast |
| 20 | Brink of Death | Your revives grant an "Extra Down" instead of outright death |

#### Cooking (Survival)
Per-level passive: every 5 levels, fried and baked food quality upgrades (more nutrition).

| Level | Perk | Effect |
|---|---|---|
| 3 | Soupsmith | Cook soups in a pot with water and ingredients |
| 5 | Mother Knows Best | See nearby players' hunger and thirst |
| 8 | Prep Chef | All cooking recipes craft in half the time |
| 10 | Hearty & Oven Recipes | Extra health during food regen; unlocks Convection Oven + Raw Dough recipes |
| 12 | Expert Gibbing | Advanced knife recipe; corpse cutting without useless scrap |
| 15 | Chef Sense | Notified when food finishes cooking, even remotely |
| 17 | Serving Seconds | Soups, pies, etc. contain 2 extra portions |
| 20 | Fast Food | All food cooks 25% faster on cooktops/ovens |

#### Agriculture (Survival)
Per-level passive: harvested plants regrow faster.

| Level | Perk | Effect |
|---|---|---|
| 4 | Fertilizer Tier 1 | Recipe: Anomalous Compost (T1) |
| 8 | Fertilizer Tier 2 | Recipe: Anomalous Fertilizer (T2) |
| 10 | Photosynthetic Synergy | Garden plants within 15 m grow 10% faster |
| 13 | Entangled Ecosystem | Nearby plants collectively consume less water |
| 15 | Fertilizer Tier 3 | Recipe: Anomalous Plant Food (T3) |
| 20 | Prudent Plucking | +1 extra harvest when gathering wild plants |

#### Fishing (Survival)
Per-level passive: stronger line (resists breaking during catches).

| Level | Perk | Effect |
|---|---|---|
| 3 | Fish Sense | Detect fishing hotspots; craft Fish Trap |
| 5 | Tacklebox | Recipe: Tacklebox |
| 10 | Lucky Fishing Hat | Recipes: Lucky Fishing Hat and Mud Waders |
| 12 | Bait and Switch | 33% chance to keep bait when fishing up junk |
| 15 | Freshwater Friends | Certain underwater creatures no longer feel threatened |

**Note:** Fishing has **no level-20 perk** (verified - top milestone is 15). Milestone counts range from 4 (Sprinting/Construction) to 8 (Cooking), so the milestone-track control needs variable pip counts and positions.

### Where milestone data lives in game data

The wiki has display text only. For internal data, `DT_Skills` (in `AbioticFactor/Content/Blueprints/DataTables/Customization/`) defines the 15 skill rows and is the positional vocabulary for the save's `Skills_` array; `StringTable_Skills` carries display strings. Perk/milestone definitions were not confirmed to be in `DT_Skills` itself - when wiring the milestone UI, dump `DT_Skills` rows first (SchemaDumpTests) and check for an unlocks/perks array; if absent, the milestone table can simply be hardcoded from this document since perks are display-only for a save editor (the game re-derives perks from level).

---

## 2. Character Customization

### Creation vs. appearance - two separate systems

- **Character Creation** ([/wiki/Character_Creation](https://abioticfactor.wiki.gg/wiki/Character_Creation)) happens once per world: pick a **Job** (PhD - our `DT_PhDs`) and distribute points on **Traits** (`CDT_AllTraits`). These are **permanent for the save**.
- **Character Appearance** ([/wiki/Character_Appearance](https://abioticfactor.wiki.gg/wiki/Character_Appearance)) is editable **any time** from the main menu (and in-game at the Transmogrifier's mirror side). Appearance has no gameplay effect.

### Appearance options (wiki-documented names)

- **Heads (16):** Male - Hubert, Alessandro, Douglas, Curtis, Tomás, Scholar, Albert, Hudson, Simon, Chemist Hubert. Female - Beth, Leigh, Sofia, Ada, Anna, Chemist Leigh.
- **Voices (2):** "Dr. H" (male, voiced by Gianni Matragrano) and "Dr. R" (female, Esmée Myers). The creator has a button to play random lines with the selected voice.
- **Hair styles (~17):** Clean Cut, Bald, Professor, Practical Ponytail, Agent, Business Bun, Extravagant Swoop, Curly, Combed, Two Buns, Vogue, Wild Science, Rugged Ponytail, Miner's Cut, Corinthian, Singed, Patchy.
- **Hair colors (14):** White, Grey, Dark Grey, Black, Bronze, Light Brown, Brown, Dark Brown, Blonde, Platinum, Silver, Red, Strawberry, Auburn.
- **Facial hair (~14):** None, Dutch, Goatee, Muttonchops, Professor 1/2, Wild Science, Ivan, Wild Science (Mustache), Greek, Mad Scientist, Spiked Goatee, Curled Moustache, Astronomer's Beard.
- **Tops (gendered, ~18):** GATE Labcoat (+Rolled Sleeves), Survivor Labcoat, Torii Labcoat Closed/Open, Coatless (+Rolled), Engineer (+Rolled), Hydroplant (+Rolled), Sweater Vest, Holiday Vest, ASO Labcoat, Miner's Jacket, Fast Food Worker, Botanist Coat (+Rolled).
- **Shirt colors (~17):** Mauve, Beige, Banana, Lime, Cyan, Bubblegum, Orange, Lavender, White, Light Gray, Peach, Grey, Brown, Sea Green, Red, Blue, Engineer Orange.
- **Fabrics/patterns (50+):** GATE Standard, Launch, GATE Gold Standard, Survivor, Torii, Red Stripes, Ocean, Roses, Night Lights, Galaxy, Forest, Rainbow Voronoi, Nuts and Bolts, Glossy Tiles, holiday/themed patterns, etc.
- **Accessories (~21):** None, Broken Glasses, Goggles in many colors (Golden/White/Red/Blue/Green/Yellow/Beige/Broken), Rectangles variants, Lenses, Lamogi Sunglasses, Monocle, Oval Glasses, Tortoiseshell Glasses.
- **Bottoms (gendered, ~23):** Lab Pants, Survivor Lab Pants, Torii Pants, color khakis/pants, Engineer/Hydroplant/Miner's/ASO/Botanist Pants, Holiday Shorts; skirt equivalents (Lab Skirt, pencil skirts in colors).
- **Belts (~14):** Standard, Survivor, Torii, Brown, Light Brown, Black, Black Gold, Red, Blue, Beige, Green, Dots, Snakeskin, Mining Belt.
- **Shoes (~15):** Black/Survivor/Torii/Botanist/Charcoal/White/Brown/Beige/Green/Chesnut/Cyan/Navy/Purple/Pink/Yellow Boots.
- **ID Cards (~21):** Research Division, Defense Team, Manufacturing, Containment, The Company, Accounting Department, Human Resources, Hydroplant, TransRecon, Reactor Crew, Food Services, Research Survivor, Torii, Caveling ID, Gold TransRecon, Keystone, Joker, Joker License, Botanist, ASO ID Card, Test Tubes.

Editor implication: counts grow with patches (Hudson head, Patchy hair, Oval/Tortoiseshell glasses were added in updates), so the editor should enumerate options from game DataTables (the `Customization/` DataTables folder that holds DT_Skills is the natural place to look for head/hair/voice tables) rather than hardcoding the wiki lists. The wiki lists above are still useful as display-name cross-checks.

### Armor Transmogrifier

Source: [/wiki/Transmogrifier](https://abioticfactor.wiki.gg/wiki/Transmogrifier) (note: page is "Transmogrifier", not "Armor_Transmogrifier").

- Deployable with two sides: a **mannequin-head/mirror side** to re-edit your scientist's appearance in-game, and a **vest side** with **purple gear slots** for armor transmog.
- Putting an item in a purple slot changes the *appearance* of the worn gear in that slot **without changing stats**; worn items don't need to be unequipped. Each slot has an **eye icon to toggle visibility** (hide that armor piece entirely).
- Cosmetic sets exist specifically for this (e.g. Greek Armor Set: 4 armor, no set bonus, intended for transmog).
- Craft-only: 4 Reinforced Wooden Planks, 4 Metal Scrap, 2 Anomalies, 1 Power Cell at a Crafting Bench. Weight 25, stack 1, durability 350.
- **Save storage:** our own schema notes already identify an unmodeled `TransmogInventory_` array in the player save (see docs/player-save-schema.md "Not yet modeled") - that is almost certainly where the per-slot transmog items live, presumably parallel to equipment slots with the `"Empty"` sentinel. Visibility toggles likely sit alongside it (probe with CustomizationProbeTests keywords).

### Beds, respawn, and sleep

Sources: [/wiki/Sleep](https://abioticfactor.wiki.gg/wiki/Sleep), [/wiki/Death](https://abioticfactor.wiki.gg/wiki/Death), [/wiki/Makeshift_Bed](https://abioticfactor.wiki.gg/wiki/Makeshift_Bed), [/wiki/Sleeping_Bag](https://abioticfactor.wiki.gg/wiki/Sleeping_Bag), [/wiki/Carbon_Bed](https://abioticfactor.wiki.gg/wiki/Carbon_Bed).

- On death, the player can respawn at **their assigned bed** if a valid one exists (otherwise fallback spawn).
- **Sleeping in a bed sets it as your respawn point.** The Makeshift Bed cannot be slept in without moving your respawn (no separate "rest only" option). **Sleeping Bags** rest without overriding the spawn point.
- Better beds (e.g. **Carbon Bed**) serve as respawn points and add comfort buffs (Blissful).
- Save linkage: player save also has `TerminalRespawnID_` and `LastSafeWorldLocation_` (already noted in player-save-schema.md); the bed respawn assignment is per-player state the editor could surface (bed actor reference / location - probe needed to confirm field name).

### Personal Teleporter ("set teleport location")

Source: [/wiki/Personal_Teleporter](https://abioticfactor.wiki.gg/wiki/Personal_Teleporter), [/wiki/Teleporter_Pad](https://abioticfactor.wiki.gg/wiki/Teleporter_Pad).

- The **Personal Teleporter** is a 1-weight handheld that teleports you to a **Crafting Bench it has been synced to**, once per full battery charge (battery 100; full depletion per use, recharge required).
- **Setting the destination:** hold the teleporter and press **F on a powered Crafting Bench** to sync. The bench must be powered to sync but *not* to receive the teleport. Synced destinations can be **named**, which customizes the teleporter's tooltip.
- Re-sync is required if the bench is dismantled/destroyed/packaged; an unassigned teleporter does nothing. Cannot be used while sitting, on ladders, in the Tram, or in the Elevator (may waste the charge).
- Crafting: 1 Raw Exor Heart, 1 Memory Brick, 1 Keyboard, 1 Desk Phone.
- **Teleporter Pads** (deployable pair-based fast travel) formerly supported custom player-set tags pre-1.0; current behavior is pad-to-pad linking.
- Editor angle: the teleporter's sync target is per-item state (likely in the item's nested struct in inventory - same area as durability/battery `CurrentItemDurability_`-style fields); worth probing for a bench GUID/actor ref + custom name string.

---

## 3. Item Page Anatomy (for the in-app item detail view)

### Representative pages examined

**Weapon - [Sledgehammer](https://abioticfactor.wiki.gg/wiki/Sledgehammer):**
Infobox: Weight 9 · Stack 1 · Durability 20 · Loss Chance 100% · Repair Item: Rebar (1) · Research Material: Metal · Salvage Results: Wood Plank (1) · Type: Heavy Blunt Melee · Damage: 45.
Sections: Stats, Durability, Research, Salvage Results, Weapon, Crafting (used as ingredient), Used In (-> Explosive Sledge), **Sources (map locations with coordinates)**, Media (attack-animation GIF).

**Food/Soup - [Gooey Mushroom Soup](https://abioticfactor.wiki.gg/wiki/Gooey_Mushroom_Soup):**
Infobox: Weight 1 · Stack 1 · **Hunger Fill 31.25 · Thirst Fill 17.25 · Applies: Souper Satisfied (buff) · Portions: 4**.
Sections: Cooking (uncooked->cooked transformation, 2-minute cook), Soup (portion mechanics), Recipe (Full Pot of Water + Raw Larva Meat + Carbuncle Mushroom + Salt). Soups require Cooking 3 (Soupsmith); all soups grant Souper Satisfied (hunger/thirst drain 20% slower).

**Armor - [Extemp Chestplate](https://abioticfactor.wiki.gg/wiki/Extemp_Chestplate):**
Infobox: Weight 3.5 · Stack 1 · Durability 47 · Loss Chance 50% · Repair Item: Metal Scrap (1) · Salvage: Metal Scrap (2) + Duct Tape (4) + Cloth Scrap (4) · **Slot: Chest Armor · Armor: 17 · Cold Resistance +1 · Set Bonus (Half): Employee of the Week · Set Bonus (Full): Employee of the Month**.
Sections: Upgrading (crafted by upgrading Jugaar Chestplate), Upgrades (-> Renovo Chestplate), Sources (upgrade-only), See Also (set siblings). Set pages note bonuses trigger at half/full set worn.

### Field gap analysis vs. our current detail view

We already show: name, description, icon, stack size, durability, weight, gameplay tags, crafted-by recipes, used-in recipes, sold-by traders, upgrade paths.

Recommended additions, ranked by wiki-likeness payoff:

| Field | Wiki section | Derivable from game data? |
|---|---|---|
| **Damage + weapon type/class** (melee blunt/sharp, heavy flag, ranged) | Weapon box | Yes - ItemTable_Global weapon struct (damage, damage type); type also implied by gameplay tags |
| **Armor value, slot, resistances (cold/heat/radiation), set bonus names** | Gear box | Yes - equipment struct in ItemTable_Global; set bonus ids likely a DataTable (probe `DT_ArmorSets`-like names); bonus *descriptions* may need StringTable |
| **Food stats: hunger fill, thirst fill, portions, buff applied (and buff duration)** | Consumable box | Yes - consumable struct in ItemTable_Global; buff -> `Applies` row id; friendly buff text from string tables |
| **Repair item + count, durability loss chance** | Durability box | Yes - repair fields sit alongside durability in ItemTable_Global |
| **Salvage results (scrap/recycle outputs)** | Salvage Results box | Yes - salvage/scrapping fields in the item row (list of item+count) |
| **Research material category** ("Material: Metal") | Research box | Yes - research/material field in item row (drives Research Bench) |
| **Cooking transformations** (raw -> cooked -> burnt chains) | Cooking section | Yes - cooking DataTable(s); presentable as a mini chain like our upgrade paths |
| **Set siblings ("See Also")** | See Also | Yes - group items sharing a set bonus id |
| **World sources (map + coordinates where item spawns)** | Sources | **Mostly wiki-only.** Loot spawns live in level actors/loot tables, not in ItemTable_Global; per-location coordinates are community-curated. Skip, or link out to the wiki page. |
| **Trivia / version history** | Trivia, History | **Wiki-only prose.** Skip. |
| **Attack animation media** | Media | Wiki-only (GIF captures). Skip. |

Practical recommendation: adding the **Weapon / Gear / Consumable stat blocks, repair+salvage, and research material** makes our detail pane essentially match a wiki infobox 1:1, and every one of those is a column in `ItemTable_Global` we already load via CUE4Parse. The only wiki-distinctive content we cannot derive is Sources/Trivia - a "View on wiki" deep link (`https://abioticfactor.wiki.gg/wiki/<Display_Name_with_underscores>`) covers that cheaply, though note wiki page titles use display names (with punctuation like `"Carrot"_&_Pumpkin_Soup`), so the link needs URL-encoding and won't always resolve.
