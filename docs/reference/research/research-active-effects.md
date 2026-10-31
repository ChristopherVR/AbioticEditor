# Research: character active effects (`CurrentBuffDebuffs_`)

Follow-up to `research-player-slot-flags-and-effects.md`, using the installed game's own
buff table. Probes: `tests/AbioticEditor.Probes/BuffTableProbeTests.cs` and
`BuffExpiryClockProbeTests.cs`.

## The table

- `AbioticFactor/Content/Blueprints/DataTables/BuffsDebuffs/DT_BuffsDebuffs`, struct
  `BuffDebuff`, 543 rows (also `DT_StatModifiers`, 122 rows, referenced by each row's
  `StatModifierMap`).
- Useful columns: `DisplayName`, `DisplayDescription`, `BuffTags`, `BuffType`
  (`Buff` / `Trait` / ...), `DefaultDuration` (seconds), `bNoExpiration`, `ApplyStyle`,
  `Severity`, `BuffLogic_Object` (a blueprint that runs while it is active).
- **Only rows tagged `Buff.Save` are written to a character save: 39 of 543.** That is the whole
  set an offline editor could ever meet: broken limbs, sprains, bleeding, `Debuff_Stinky`, the
  food and drink buffs (`Buff_Caffeinated`, `Buff_Radpills`, `Buff_SweetTooth`, ...), the zombie
  virus, spores and poison.
- Read by `BuffCatalog` (Core) and `BuffVocabularyService` (host); the Vitals tab shows the
  in-game name for each saved effect.

## Expiry value

- `bNoExpiration = True` rows are saved with `BuffExpireTime = -1`. Confirmed: `Debuff_Stinky`
  (`bNoExpiration=True`) is `-1` in the dedicated fixture.
- Timed rows store a positive number. The only example (`Debuff_LacticAcid_Arms`, 5298.2188) is
  not comparable to the world clock in the same world (`WorldSave` time of day was 66900 s), so it
  is not the day clock. It is larger than any `DefaultDuration` (max 2100 s), so it is not a
  plain remaining time either. Best reading: a value on the running session's own clock
  (seconds since the level loaded); unverified.
- **How the game reads it back (settled from the blueprint, 2026-10-31):**
  `Abiotic_PlayerState_C` "Apply Save Data to Character" loops over the saved `Buffs_` and calls
  `CharacterBuffComponent.Server_ApplyBuff(BuffRow, bOverrideDefaultDuration = true,
  NewDuration = BuffExpireTime, Limb = ParentLimb, LinkedActor = None, bSkipDialog = false)`
  (`artifacts/live-parity-probe/Abiotic_PlayerState.json`, around line 17740). So whatever the
  save holds is used as the effect's duration after loading. The writing side
  (`GetBuffsToSave`, native) is still unseen, which is why 5298 looks odd; it does not matter
  for an editor, which writes a duration.
- `Debuff_LacticAcid_Arms` is **not in the current table** at all. A save can carry rows a newer
  game removed. The Vitals tab flags such rows "not in this game version".

## What is and is not supported

- **Remove / clear: supported** (`PlayerSaveWriter.ApplyActiveBuffs`). Removing entries, or the
  whole struct when none remain, is how the game stores a character with no effects.
- **Add: supported** (`PlayerSaveWriter.AddActiveBuffs`), from the load path above: a timed row
  is written with its `DefaultDuration`, a `bNoExpiration` row with `-1`, limb `AllBones`. The
  file editor offers only `Buff.Save` rows (anything else would be dropped at the next game
  save). A missing list is created in the save's own layout: the `BuffSave_Struct` wrapper in
  older saves, a plain `ActiveBuffs` list of `BuffDebuffEntry` in native 1.5 saves (from the
  usmap: `AbioticCharacterSaveData.ActiveBuffs`; no real native save with effects has been seen
  yet). The same usmap lists `Radiation` and `BodyTemperature` in `AbioticCharacterStatsSave`.
- **Live (round 2026-10-31, `areas/effects.lua`):** list `CurrentBuffs`, add with
  `Server_ApplyBuff(handle, override, seconds, 0, nil, true)`, remove with
  `Server_RemoveBuff(handle, true, limb, false)` (the Crystal Lens call shape), clear debuffs,
  and "block new debuffs": a pre-hook on `Server_ApplyBuff` shortening `Debuff*` rows (except
  `Debuff_Underwater`) to 0.001 s, as CheatConsoleCommands' "No Debuffs" does. Other game
  functions seen in the blueprints: `Server_RemoveSpecificBuffs(handles[], false, limbs[])`
  (encumbrance, sanity), `HasBuff`, `HasBuffTag`. Lua harness only; not yet run in the game.
