# Research: character-save slot flags, favorites, distillery history, effects, hotbar selection

Scope: the `CharacterSaveData` properties `TransmogDisabledArray_`, `FavoritedSlots_`,
`ItemsDistilled_`, `CurrentBuffDebuffs_` and `LastHotbarSelection_`. Survey of every
`Player_*.sav` under `tests/fixtures/` (Steam `SaveGames` + `Legacy`, dedicated server; 13 files,
9 distinct characters/worlds). The Game Pass fixture is an Oodle-packed wgs bundle with no
loose player files, so it was not surveyed directly (Game Pass members are the same GVAS body,
see `game-pass-format.md`). Nothing here was verified in-game (game paks unavailable).

Full property names (hash suffixes are identical in every fixture that has the property):

| Property | Full name | GVAS type |
| --- | --- | --- |
| Transmog disabled | `TransmogDisabledArray_145_2BA8A3F74C6661475F021A9999C06090` | Array of Bool |
| Favorited slots | `FavoritedSlots_125_BD14BA2A40F37FA19BC7C6816BCC3F3C` | Array of Bool |
| Distilled items | `ItemsDistilled_162_360CC93D4F1B4060A4AB61AC1E77FFC0` | Array of Str |
| Buffs | `CurrentBuffDebuffs_150_9E6DA0704D0DE0DEF375ACA4CFD2D80A` | Struct `BuffSave_Struct` |
| Hotbar selection | `LastHotbarSelection_75_9D56EAE8464F9FFF52C04AA7B388D489` | Int |

## TransmogDisabledArray_

- Present in all 13 saves, always exactly 13 bools. That equals the `EquipmentInventory_` length
  (13), not the 6 `TransmogInventory_` slots or the 12 `TransmogVisibility_` flags.
- 9 of 13 saves are all `true`. The others (patterns over indices 0-12):
  `0001000111111` (two saves of the same character), `1011110111111` (two saves),
  `0101001111111` (one Legacy save).
- Observed correlation, not proof: in the `0001000111111` character the transmog slots holding
  items are 0,1,2,4 and those are exactly the leading `0` bits, plus 5 and 6 (suit and
  headlamp equipment slots). In the `1011110111111` character transmog slot 1 holds a helmet and
  bit 1 is `0`, but bits 4 and 5 are also `0` with nothing in the transmog slot. A counter-
  example: another character has an apron and a chef hat in transmog slots 0 and 1 and every
  bit is `true`. So a `0` is not simply "transmog item present".
- Unknown: what `true` vs `false` means (the name says "disabled", yet the common value is
  `true`), and whether index N is equipment slot N (the 13-long size strongly suggests it, with
  the equipment role map CHEST, HEAD, LEGS, BACK, ARMS, SUIT, HEADLAMP, TRINKET, WATCH, HACKER,
  SHIELD, TRINKET, PET). Answering it needs the game's `W_Inventory_Transmog` / equipment
  blueprint graph (not available) or an in-game toggle test.
- Editor: read into `PlayerSaveData.TransmogDisabled`. `PlayerSaveWriter.ApplyTransmogDisabled`
  exists (in-place bool patch, never resized; shape identical to `TransmogVisibility_`), but no UI
  calls it because the meaning is unconfirmed.

## FavoritedSlots_

- Present in all 12 non-Chrissie saves; absent from the Chrissie world save (delta serialized,
  no favorites). Lengths seen: 24, 30, 35, 36, 37. Values are sparse `true` flags.
- The length does not equal `Inventory_` length: dedicated player 76561197993781479 has a
  30-slot backpack and a 37-flag array; Legacy player 76561198128277890 has 36 slots and 35 flags;
  Steam `SaveGames` player 76561197993781479 has 30 slots and 36 flags with `true` flags at
  indices 24-32, ie past the end of a 30-slot backpack. The array most likely records the
  backpack size at some past moment and is never trimmed, or it uses an index base that is not
  `Inventory_` alone (a fixed offset such as hotbar/equipment slots is possible but not
  confirmed).
- Patterns: runs of 9 consecutive `true` flags at the tail in four saves; pairs at a stride of 6
  in `100011000011000011000011000011000000` (the same in the dedicated-server and Steam copies of
  one character, so it is stable across platforms). Neither pattern is explained.
- Unknown: the index base (whether index 0 is `Inventory_[0]`), and whether favorites follow an
  item or stay put when slots move. Conservative rule for any future writer: keep the array
  length untouched and never move flags when the editor swaps slots until the game behavior is
  confirmed by a manual test.
- Editor: read into `PlayerSaveData.FavoritedSlots`; view-only count shown in the Advanced
  data tab. No writer.

## ItemsDistilled_

- Array of lower-case item row names (`food_milksac`, `food_tomato`, `sugarcrystal`,
  `gib_peccary_leg`, `liquidcrystal`, `essence_leyak`, `goo_exor`, `gel`, ...). Present in 12
  saves, absent from Chrissie. Lengths: 1, 12, 14, 16.
- Discovery-ordered and prefix-stable: shorter lists are exact prefixes of longer ones across
  characters and versions (the first entry is `food_milksac` everywhere), so it is an append-only
  "has ever been distilled" list, keyed by row id, independent of inventory slots. Adding or
  removing entries cannot desync with any other array, so a name-array editor (as for
  `CraftedItems_`) would be safe on shape. Whether the row ids must exist in `ItemTable_Global`
  and what unlocking an entry does (likely just the distillery UI's "known" marker) is unverified.
- Editor: read into `PlayerSaveData.ItemsDistilled`, shown in the Advanced data tab. No writer
  yet; the shape (`Array<Str>`) is confirmed, the gameplay effect is not.

## CurrentBuffDebuffs_

- Only two of 13 saves have it (both copies of one character: dedicated and Legacy). It is
  absent when the character has no effects.
- Shape: `BuffSave_Struct { Buffs_8_2276236A4B2235B483FBBD8E932EAA87: Array<BuffDebuffEntry> }`.
  Each `BuffDebuffEntry` has `BuffRow` (`BuffDebuffRowHandle { RowName: Name }`), `ParentLimb`
  (`EnumProperty` `EBodyLimbs::AllBones`) and `BuffExpireTime` (float).
- Observations: `Debuff_Stinky` with expire time `-1` (no expiry, dedicated save) and
  `Debuff_LacticAcid_Arms` with `5298.2188` (Legacy save; both saves have `AllBones`, even
  for the arm-scoped acid debuff, so `ParentLimb` is not always the visible limb).
- Unknown: whether the expire time is an absolute game-clock second (likely, since it is not
  a small duration) or seconds remaining; the full set of `DT_BuffDebuff` rows; stacking rules.
  Editing would need buff-table rows from the paks and a known clock, and a wrong expiry is
  either instant removal or permanent. Not written.
- Editor: read-only `PlayerSaveData.ActiveBuffs` (`ActiveBuff` record); names listed in the
  Advanced data tab.

## LastHotbarSelection_

- Present only in the newest fixture (Steam `SaveGames/.../Worlds/Chrissie`), value `5`, as an
  `IntProperty` placed between `CurrentSurvivalStats_` and `CurrentMoney_`. Older saves omit
  it (also plausibly omitted when it equals the default 0).
- The hotbar has 8 slots (`HotbarInventory_` length 8 in every fixture), so 0-7 is the
  presumed range; only `5` is actually observed. It most likely selects the slot equipped on
  load and has no other effect (unconfirmed in-game).
- Editor: read into `PlayerSaveData.LastHotbarSelection` (null when absent).
  `PlayerSaveWriter.ApplyLastHotbarSelection` updates the tag or creates it under the exact
  full name above and rejects slots outside the hotbar length. No UI control yet, since a
  selected slot pointing at an empty or non-holdable slot is untested in game.

## Remaining unknowns

1. Meaning of each `TransmogDisabledArray_` bit and index-to-slot mapping.
2. Index base of `FavoritedSlots_` and whether favorites must follow items when slots move.
3. Semantics of `ItemsDistilled_` beyond "seen in the distillery" (safe to edit shape-wise).
4. Units of `BuffExpireTime` and the buff-table vocabulary.
5. Whether `LastHotbarSelection_` beyond 0-7 or on an empty slot misbehaves.
6. Game Pass character saves were not surveyed directly.
