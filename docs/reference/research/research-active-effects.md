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
- `Debuff_LacticAcid_Arms` is **not in the current table** at all. A save can carry rows a newer
  game removed. The Vitals tab flags such rows "not in this game version".

## What is and is not supported

- **Remove / clear: supported** (`PlayerSaveWriter.ApplyActiveBuffs`). Removing entries, or the
  whole struct when none remain, is how the game stores a character with no effects.
- **Add: not supported.** Permanent (`bNoExpiration`) rows could be added with expiry `-1`, but
  the only such saved rows are harmful ones (broken limbs, stinky, last legs). Timed rows need
  the session clock zero point, which is unverified; a wrong value expires the effect at once or
  never. The live agent has no verified add path either (the usmap carries properties, not
  functions; `Server_AddTraitBuff` exists only for traits).
- To settle it: in a running game, add a timed buff (`Buff_Caffeinated`, 60 s), save, and
  compare its `BuffExpireTime` with `GetTimeSeconds()` on the same world.
