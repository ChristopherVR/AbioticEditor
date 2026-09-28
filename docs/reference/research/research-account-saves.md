# Research: account-level saves (Unlocks, PlayerStatsSave, UserSettings)

Source: the three files in `tests/fixtures/SteamSaves/SaveGames/76561197993781479/`.
Reader: `Core/Serialization/Player/AccountSaveReader.cs` (read-only). Tests:
`tests/AbioticEditor.Tests/AccountSaveReaderTests.cs`.

All three round-trip byte-exact through `SaveGame.LoadFrom` -> `WriteTo` (asserted in the tests).
None has a custom header. They live in the account folder beside `Worlds/`, and on Game Pass are the
wgs containers `ProfileUnlocks`, `ProfilePlayerStatsSave`, `ProfileUserSettings` (already carried
by `GamePassConverter`; the blobs are plain GVAS so the same reader works on those bytes).

## Unlocks.sav (`Abiotic_CustomizationUnlocks_Save_C`)

One property: `CustomizationUnlocks`, an `ArrayProperty` of `NameProperty`. Each entry is a row name
from a `DT_Customization_*` table (`Head_M01chemist`, `UpperBody_Engineer`, `id_hydro`,
`Tie_Christmas_01`, `Glasses_Goggles_Broken`, `Belt_Mgt_Miner` ...). The list is a flat union across
all tables; there is no table tag, so the slot is inferred from the row-name prefix (display only, see
`AccountSaveReader.CategoryOf`). Some rows do not follow a prefix (`Engineer_Orange`, `M_Signal`,
`F_Mgt_Miner`, `M_SweaterVest`), so their category reads "Other".

Owned vs unavailable: `CustomizationUnlocksModel.Partition(catalogRows)` splits a catalog table's
rows (from `CustomizationCatalog.LoadFrom`, needs the game paks) into owned and unavailable, and
`UnknownTo` lists stored names the catalog does not contain. Only catalog rows are ever reported as
unavailable, so no invalid name can be offered.

Open: which rows are unlocked by default versus by gameplay (the paks may carry an "unlocked by
default"/"unlock source" column); whether adding a row here is honoured or re-validated on load;
whether the Game Pass and Steam builds share identical row vocabularies. No write is provided.

## PlayerStatsSave.sav (native `/Script/AbioticFactor.PlayerStatsSave`)

- `Stats_Int`: `MapProperty` of `NameProperty` to `IntProperty`. Fixture holds 10 keys, all
  `STAT_KILLS_*` (PEST, ORDER, GKMAGE, GKCHIEF, GKWITCH, GKPHYTER, GKHEAVY, SKINK, SYMPH, GK).
- `Achievements`: `ArrayProperty` of `NameProperty`, the Steam API names (`ACH_CRAFTY`,
  `ACH_KILL_PEST_1`, `ACH_SECTOR_1` ...). 51 entries in the fixture.

Relation to Steam achievements (unverified, from structure only): the array looks like a local mirror
of already-granted achievements, and the kill counters look like the inputs to the tiered kill
achievements (`ACH_KILL_PEST_1` / `_5`). Steam holds its own authoritative state; editing this file
would not grant or revoke a Steam achievement, and Steam stats are per-account cloud data that
`steam_autocloud.vdf` may overwrite. It is not known whether the game re-derives the mirror from
Steam on launch or trusts the file. That must be tested in-game before any write is offered.
No write is provided.

## UserSettings.sav (`Abiotic_SettingsSave_C`)

Top-level properties (all read-only modeled): `FavouriteRecipesList` (names), `PinnedRecipeList`
(names), `HasCreatedACharacter` (bool), `HasPlayedTutorial` (bool), `UIPopupsSeen` (names, e.g.
`journalnotes`, `jobscreen`), `TutorialHintPopupsSeen`, `TutorialPanelsSeen`, `HostPreferences`
(struct `HostPreferences_Struct`), `RecentServers` (array of `StrProperty`: `host:port` and
a 32-hex id).

`HostPreferences_Struct` leaves in the fixture: `SinglePlayer_1_<hash>` (bool) and
`Password_4_<hash>` (StrProperty). **The password is stored in plaintext.** The reader never copies
the value out: `HostPreferencesModel` carries only `HasPassword`, any leaf whose name contains
password/secret/token/passphrase is masked this way, and a test asserts the fixture's password text
does not appear in the serialized model. Any future UI must show "set / not set" only and should not
log the raw tree. `RecentServers` holds real IP addresses, so treat it as personal data in screenshots.

Delta serialization applies: absent booleans read as null, absent arrays as empty. Unrecognised
top-level properties are reported in `UnmodeledProperties` (empty in the fixture; `BenchUpgrades` and
`Weight` seen in the raw strings are struct/array child names, not top-level).

Open: valid value sets for popup/tutorial names (need the paks or a wider fixture set); whether
`HasCreatedACharacter=false` re-triggers the intro; whether recipe names must exist in the recipe
catalog (favourites/pins with stale names are likely ignored, unverified). No write is provided.

## Not delivered

No Razor surface was added (it needs account-folder discovery for Steam, Game Pass and dedicated
hosts plus localization resources; the reader takes bytes or a path so it can be wired to either).
No writes for any of the three files.
