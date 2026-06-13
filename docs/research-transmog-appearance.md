# Research: Transmog slot mapping + appearance preview assets

Date: 2026-06-11. Probes: `tests/AbioticEditor.Tests/TransmogAppearanceProbeTests.cs`
(Probe1 = fixture-save dump, Probe2/3/5 = cooked widget/enum dumps, Probe4 = customization tables).

## Task 1 - `TransmogInventory_106_*` slot mapping (DEFINITIVE)

Source of truth: the cooked transmog UI
`AbioticFactor/Content/Blueprints/Widgets/Inventory/W_Inventory_Transmog.uasset`.
Its six `W_TransmogItemSlot_C` subobjects each carry an explicit `SlotIndex`,
`SlotType` (E_InventorySlotType) and tooltip text:

| Transmog index | Role | Widget | SlotType | Tooltip | Empty-slot icon |
|---|---|---|---|---|---|
| 0 | **CHEST** | `W_TorsoArmorSlot` | `NewEnumerator14` | "Chest Armor" | `/Game/Textures/GUI/icon_armor` |
| 1 | **HEAD** | `W_HeadArmorSlot` | `NewEnumerator5` | "Head Armor" | `/Game/Textures/GUI/icon_helmet` |
| 2 | **LEGS** | `W_LegsArmorSlot` | `NewEnumerator6` | "Leg Armor" | `/Game/Textures/GUI/icon_pants` |
| 3 | **BACK** (backpack) | `W_BackpackSlot` | `NewEnumerator7` | "Backpack" | `/Game/Textures/GUI/icon_backpack` |
| 4 | **ARMS** | `W_ArmArmorSlot` | `NewEnumerator12` | "Arm Armor" | `/Game/Textures/GUI/icon_arm` |
| 5 | **FULL BODY SUIT** | `W_SuitArmorSlot` | `NewEnumerator13` | "Full Body Suit" | `/Game/Textures/GUI/icon_suit` |

Note: `W_TorsoArmorSlot` serializes no `SlotIndex` property - i.e. it keeps the
class default `0` (UE omits default-valued properties). Confirmed by save data.

Fixture-save corroboration (Probe1, all four `Cascade/PlayerData/Player_*.sav`):

- `Player_76561198179787042`: transmog `[0]=armor_chest_apron` (chest item),
  `[1]=armor_helmet_chefhat` (helmet).
- `Player_76561198128277890` (J0K3R): transmog `[1]=armor_helmet_cowl` (helmet).
- Transmog indices 0–5 are exactly the first six `EquipmentInventory_11_*` indices
  (0=chest, 1=head, 2=legs, 3=backpack, 4=arms, 5=suit) - same `SlotIndex` and
  `SlotType` values in both widgets.

Empty transmog slots appear as RowName `Empty` (the usual sentinel) or `None`
(observed only in `Player_76561197993781479`, indices 0/1/2/4 - treat both as
"no transmog applied").

### Equipment slot index -> role (from `W_Inventory_EquipSlots.uasset`, authoritative)

Each `EquipSlot_*` (`W_InventoryItemSlot_C`) export carries `SlotIndex` + tooltip:

| Index | Tooltip (in-game name) | SlotType | Editor's current label | Correct? |
|---|---|---|---|---|
| 0 | Chest Armor (`EquipSlot_Torso`) | 14 | CHEST | yes |
| 1 | Head Armor | 5 | HEAD/HELMET | yes |
| 2 | Leg Armor | 6 | LEGS | yes |
| 3 | Backpack (not in this widget; item data confirms `backpack_*`) | 7 | BACK | yes |
| 4 | Arm Armor | 12 | ARMS | yes |
| 5 | **Full Body Suit** | 13 | EYES? | **no -> SUIT** |
| 6 | Headlamp | 15 | HEADLAMP | yes |
| 7 | Trinket | 16 | TRINKET | yes |
| 8 | Wristwatch | 17 | WATCH | yes |
| 9 | **Hacking Device** (keypad) | 18 | TOOL | rename -> HACKING DEVICE |
| 10 | Off-Hand Armament (shield) | 19 | SHIELD | yes |
| 11 | Trinket (second) | 20 | TRINKET2 | yes |
| 12 | **Companion Pet** | 21 | EXTRA | rename -> COMPANION |

`E_InventorySlotType` (Blueprints/Data/E_InventorySlotType.uasset) declaration
order: 0,1,2 (None/Inventory/Hotbar-ish), then 14(Chest),5(Head),6(Legs),
7(Backpack),12(Arms),13(Suit),15(Headlamp),16(Trinket),17(Watch),18(Hacking),
19(Shield),20(Trinket2),21(Companion) - matching equipment order 0..12.

### TransmogVisibility_109 (12 bools) / TransmogDisabledArray_145 (13 bools)

- The component CDO (`Blueprints/Characters/Abiotic_TransmogInventoryComp.uasset`)
  declares `TransmogVisibility[12]` and `DisableTransmogArray[12]`, both
  defaulting to all-`True`. The saved `TransmogDisabledArray_145_*` has grown to
  13 entries (patch drift, same way `EquipmentInventory` grew to 13 with the
  Companion slot).
- **Most plausible mapping: both arrays are indexed by EquipmentInventory slot
  index** (0=chest ... 11=trinket2, [12]=companion in the 13-long array). The
  12-long `TransmogVisibility_109` covers equipment slots 0–11.
- Evidence: `Player_76561197993781479` has `TransmogDisabledArray =
  F,T,F,T,F,F,T,T,T,T,T,T,T` - `False` exactly at 0 (chest), 2 (legs), 4 (arms),
  5 (suit): the four armor-visual slots a player hides to show the scientist
  outfit (the per-slot "eye" toggle, `W_TransmogToggleButton`). All other
  fixtures are all-`True` (default). `TransmogVisibility_109` is all-`True` in
  every fixture, so its per-index semantics are unverified - but its CDO size
  (12) and name pair it with the same equipment indexing.
- Editor guidance: expose per-equipment-slot "armor visible" toggles for indices
  0/1/2/3/4/5 only (the visual slots); preserve the rest untouched.

## Task 2 - Appearance preview assets in `DT_Customization_*`

All 13 customization tables share one row struct. Exact (hash-suffixed) columns,
observed via Probe4 on `DT_Customization_Head`, `_HairStyle`, `_UpperBody`,
`_HairColor`, `_ShirtColor`, `_IDCard`:

| Column | Type | Use |
|---|---|---|
| `DisplayName_63_0B8B1AF54D656E42C963068B22B44D3A` | FText | **Friendly label** - `Head_F01a`->"Beth", `Head_M02a`->"Alessandro", `Hair_Bald`->"Bald", `UpperBody_Rolled`->"GATE Labcoat Rolled Sleeves (M)", `HairColor_Grey`->"Grey", `ShirtColor_Biege`->"Beige", `id_defense`->"Defense Team" |
| `DisplayDescription_64_18455B7D443E0B763E6A259599C5CCB0` | FText | Flavor text (heads only, e.g. "Organized, forthright, dependable."); empty elsewhere |
| `Icon_46_908AB2E34BA99C452FF621A9A34A10A7` | SoftObjectPath | **2D preview texture - exists and is UI-usable.** `/Game/Textures/GUI/CustomizationIcons/Heads/icon_head_F_01a`, `.../Hair/Icon_Bald`, `.../Tops/icon_top_M_LabcoatRolled`, `.../ShirtColors/icon_shirt_peach`, `.../IDCards/icon_idcard_defense` |
| `Material_47_2BCAA56043478B8F8AF097B87369896A` | SoftObjectPath | head/ID-card material (not needed for 2D UI) |
| `StaticMesh_20_A4A408164457F857B4E91487F73D7AC1` | SoftObjectPath | ID cards only (`SM_IDCard_Default`) |
| `SkeletalMesh_21_6AE38B6846F6DB34DA17F38E52D58FB3` | SoftObjectPath | 3D mesh (heads/hair/tops) |
| `EquipmentBasedSkeletalMeshes_57_EFA2C87B4513D9BC283DEA9A06A6F7F2` | Map | per-equipment mesh swaps |
| `DLCRequirement_72_...24BA1E1C488E14F6E03B829A93DF15A8` | RowHandle | e.g. `EarlyAccess` on `M_Upper_Survivor` |
| `AchievementRequired_76_F6007E5A476FADBDDBA86E864BD7CB3C` | RowHandle | unlock gate |
| `UnlockedByDefault_73_8C0F4163431DFF87062EA3ABFC8E8482` | bool | |
| `ColorA_38_48A0590C4984FA0BF470568FBF92F889` | LinearColor | **The swatch color for color tables**: `HairColor_Grey`=`8C8C8C`, `HairColor_DarkGrey`=`4F4F4F`, `ShirtColor_Biege`=`FFCEA6`, `ShirtColor_Banana`=`FFF18A`. Non-color tables leave it `FFFFFF`. |
| `ColorB_39_3822B550469594F922E379847D5EBF48` | LinearColor | constant default `C2B27E` everywhere observed - ignore |
| `ColorC_40_9F427AD14FB5A1947B698EA1A4F8C176` | LinearColor | constant default `433200` everywhere observed - ignore |
| `SkinTone_60_03519A424DBA1796501ECAA5F93B0B50` | byte enum | `E_SkinTones::NewEnumeratorN` (heads only) |
| `Tags_67_53C7CA184101D5AC79BCD983AF71AC7C` | GameplayTags | e.g. `Customization.BeardStyle.A` on heads |

UI recommendations:

- Every picker can show `DisplayName` + the `Icon` texture; icons live under
  `/Game/Textures/GUI/CustomizationIcons/<Heads|Hair|Tops|ShirtColors|IDCards|...>`.
- `DT_Customization_HairColor` rows point `Icon` at the engine placeholder
  `/Engine/EditorLandscapeResources/WhiteSquareTexture` - i.e. the game tints a
  white square with `ColorA`. Render a `ColorA` swatch for hair colors.
- `DT_Customization_ShirtColor` has both real `icon_shirt_*` textures and a
  meaningful `ColorA`; either works (swatch is cheaper).
- Row counts: Head 18, HairStyle 18, UpperBody 51, HairColor 15, ShirtColor 17,
  IDCard 22.
