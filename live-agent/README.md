# Live editing: the in-game side

The in-game half of live editing: something that opens a small local TCP port and speaks the
same protocol as `AbioticEditor.Core.LiveEditing.TcpLiveGameChannel` (the .NET editor's side), so
the desktop app can read and write a running game's memory in real time instead of a `.sav` file.
See `docs/reference/live-editing-protocol.md` for the wire format, and `docs/PROGRESS.md`
(round-64 onward) for how this fits into the editor's own history.

**None of this is part of the .NET solution.** Like the sample plugins under `plugins/`, it is
built and shipped as its own standalone artifact(s), not something `dotnet build` touches.

## Two approaches, one primary

### The Lua + helper hybrid (PRIMARY - buildable and verified today)

- `AbioticEditorLiveAgentLua/` - a UE4SS **Lua** mod. Does all the actual game interaction
  (finding the player, reading/writing property values) through UE4SS's public Lua API. No
  build step, no SDK, no special access - drop the `Scripts/` folder into the game's `Mods/`
  folder like any other UE4SS Lua mod.
- `AbioticEditorLiveAgentHelper/` - a small standalone native `.exe`. Does the TCP networking and
  nothing else - **zero dependency on UE4SS or the game**, just Winsock. Run it alongside the
  game (it does not need to be injected into anything).
- They talk to each other through a two-file mailbox in
  `%LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\` (`request.json` / `response.json`, each published
  atomically via a temp-file rename), which the Lua mod polls every 50ms via `LoopAsync`. Both
  sides derive that path from `%LOCALAPPDATA%` independently, so there is no config file to keep
  in sync.
- Exists because the *other* approach (below) turned out to need access this project could not
  get. See "Why the hybrid, not just the C++ mod" below for the full story.

### The pure C++ mod (SECONDARY - blocked on private SDK access)

- `AbioticEditorLiveAgent/` - a single UE4SS **C++** mod that would do networking and game
  interaction in one process, no file-polling latency. Kept because the access gap below may
  close later, at which point this becomes the better long-term choice.

## What is verified, and what is not (read this before trusting anything here)

**Verified, by actually running real interpreters/compilers, not by inspection:**
- `Shared/LiveAgentServer.{h,cpp}` and `Shared/JsonLine.h` (including array support, for
  `skills.get`/`skills.set`) - the TCP listener, request/response framing, and JSON encoding.
  Compiled with MSVC 14.44 and run against the *actual* `TcpLiveGameChannel`/
  `LivePlayerVitalsChannel`/`LivePlayerSkillsChannel` classes from `AbioticEditor.Core.`
- `AbioticEditorLiveAgentHelper` - compiled and run for real (a real process, listening on a real
  port, generating a real token via `BCryptGenRandom`).
- `AbioticEditorLiveAgentLua/Scripts/json.lua` - a hand-rolled JSON encode/decode with no
  UE4SS dependency, run against a real Lua 5.4.7 interpreter (built from lua.org source with the
  same MSVC toolchain) and round-trip tested against every shape the protocol sends, including a
  bug the interpreter caught (`isArray`'s heuristic miscounted a decoded array's own internal
  marker key - fixed, then reverified).
- `AbioticEditorLiveAgentLua/Scripts/main.lua`'s **request/response/dispatch/property-prefix
  logic** - run against the real Lua interpreter under a **fake UE4SS environment** (stubbed
  `FindFirstOf`/`LoopAsync` and a fake player-state object matching UE4SS's documented
  `GetClass():ForEachProperty` / `GetPropertyValue` / `SetPropertyValue` shape). This is the
  actual, unmodified `main.lua` - not a copy or a simplified stand-in.
- **The full hybrid pipeline together, end to end**: the real .NET `TcpLiveGameChannel` connected
  over real TCP to the real compiled `AbioticEditorLiveAgentHelper.exe`, which read/wrote the real
  file mailbox, which the real Lua interpreter (running the real, unmodified `main.lua` against
  the same fake player-state stand-in) polled and answered. Vitals and skills reads, writes, and
  a re-read after each write to confirm it actually stuck, all passed. Only the connection between
  Lua and an *actual* running game is outside what could be exercised this way.

**CONFIRMED WORKING against the real, running game (2026-09-02)** - see "Ground truth from a real
mod" and the dated section below it for the full story. All six commands (`ping`,
`diag.findplayer`, `vitals.get`, `vitals.set`, `skills.get`, `skills.set`) round-tripped correctly
against a real save with real progression data, including `CurrentSanity` (previously an
unconfirmed guess, now confirmed) and every entry in the `FileIndexToLiveSkillId` table (confirmed
by getting back non-zero XP for all 15 skills, not just no-error). The earlier
`GetClass()`-times-out mystery is now understood to have been the wrong approach entirely (see
below) rather than an unsolved timing bug - the fix replaced `GetClass()`/`FindFirstOf` with
`UEHelpers.GetPlayerController().MyPlayerCharacter`, and no property-name scanning is used
anywhere in the current `main.lua`.

**Also not verified**: `AbioticEditorLiveAgent/src/VitalsCommands.cpp`,
`AbioticEditorLiveAgent/src/SkillsCommands.cpp`, and `Mod.cpp` (the secondary, pure-C++-mod
approach) - unchanged status, see "Why the hybrid" below.

## Why the hybrid, not just the C++ mod

The installed game's exact UE4SS commit (`01e0a584`) depends on a private submodule
(`git@github.com:Re-UE4SS/UEPseudo.git`) that contains the actual `Unreal::UObject`/`FProperty`
C++ types every UE4SS C++ mod needs for real property access (confirmed: `UE4SS/include/` has no
top-level `Unreal/` folder at all - those types live entirely inside that private submodule,
included via `deps/first/CMakeLists.txt`'s `add_subdirectory("Unreal")`). The public `v3.0.1`
*tag*'s own SDK release is a different, earlier commit (`d935b5b`) and is explicitly documented
upstream as ABI-incompatible ("C++ mods must be rebuilt to work on 3.0.1"), so it is not a usable
substitute.

Getting access is a real, standard, two-part thing, not a special favor:
1. **Epic Games source access** (free, ~5 minutes, one-time): link your GitHub account to an
   Epic Games account at epicgames.com and accept the Unreal Engine EULA. UE4SS's own README says
   this explicitly. This grants access to *Epic's own* private repos.
2. **`Re-UE4SS/UEPseudo` access specifically**: a *separate* third-party org's own private mirror
   of Unreal pseudo-code. Confirmed this round: even with (1) done, cloning it over authenticated
   HTTPS still 404s - Epic-linking does not automatically grant membership in a third party's own
   private org, which apparently gates its own access independently (likely a manual
   request/invite process with that specific community, not something either the account holder
   or this tooling can complete unilaterally).

Rather than stay blocked on (2), UE4SS's public **Lua** API (`FindFirstOf`, `GetPropertyValue`,
`SetPropertyValue`, `ForEachProperty`, `LoopAsync`) - all documented, all usable with zero build
step or source access - does the same property-level work the blocked C++ code would have done,
with the same prefix-matching discipline. Only the networking piece needed C++, and pure Winsock
networking needs no UE4SS dependency at all.

## Real-game debugging session (2026-09-02)

The game was actually launched and the mod actually tested live this round (see
`docs/PROGRESS.md` round-67). What happened, in order:

1. **The mod loaded cleanly.** `require("json")`, `os.getenv("LOCALAPPDATA")`, `LoopAsync` - all
   worked with zero errors on the very first try, printing the correct real path.
2. **`FindFirstOf("AbioticCharacterPlayerState")` is confirmed correct.** Isolated behind a
   `diag.findplayer` command with no other API calls: returns a real object, fast, every time.
3. **`GetClass()` called from `LoopAsync`'s own callback froze the entire game.** Not just this
   mod - zero log activity from *any* mod for 2+ minutes afterward, consistent with the whole
   UE4SS event loop being stuck. Confirmed by isolating it behind a `diag.getclass` command: the
   debug log showed `calling GetClass()` and then nothing, ever, from any mod. Had to force-kill
   the game.
4. **Root cause identified**: `LoopAsync`'s callback does not run on the game thread, and Unreal
   reflection calls need to. `ExecuteInGameThread` is UE4SS's documented mechanism for exactly
   this (confirmed via its own docs and a GitHub issue describing the identical class of bug in
   another mod - "eliminate FindAllOf calls on the async thread"). Fixed: every actual
   game-touching call now goes through `runOnGameThread`, which wraps `ExecuteInGameThread`
   (fire-and-forget/async, confirmed no synchronous return - handlers now report their outcome via
   a `respond` callback instead of a return value).
5. **That fix worked - no more freeze.** Debug timestamps showed `ExecuteInGameThread`'s callback
   firing ~5ms after being queued, every time, and `ping` (which touches no game API) kept
   answering successfully throughout every subsequent test, proving the game stayed healthy.
6. **But `vitals.get` still timed out** - consistently, at exactly the helper's 5000ms budget, not
   gradually improving. Investigation found a real, separate bug: the original code called
   `findPropertyNameByPrefix` **once per field** - 12 full `ForEachProperty` scans for one
   `vitals.get` call. Fixed: `collectPropertyNames` now scans once per object and
   `getByPrefix`/`setByPrefix` look up against that single collected list.
7. **Still times out after that fix, at the exact same budget.** Re-added targeted debug prints
   (kept in the file, `MAX_PROPERTIES_TO_SCAN` temporarily lowered to 30) and re-tested: the debug
   log shows `ExecuteInGameThread callback FIRING`, then `collectPropertyNames: calling
   GetClass()`, then **nothing** - `GetClass()` itself does not return within the budget, even
   though it is now confirmed to be running on the correct thread, and even though this time nothing
   else freezes (`ping` still answered afterward).
8. **Ruled out API misuse**: found a real published UE4SS mod on GitHub
   (`Matraweber/PalWorkPriority`, `Scripts/icons.lua`) using the exact same
   `object:GetClass():ForEachProperty(...)` pattern successfully, so this is not a wrong method
   name or wrong calling convention.

**Where this leaves things**: `ExecuteInGameThread` is the documented right mechanism and does
dispatch to the game thread correctly and fast, but `GetClass()` on this specific object still
does not complete within budget for a reason not yet identified. Candidates for a future session,
roughly in order of promise:
- Call it from `RegisterHook` on a function that already runs on the game thread naturally (a
  Tick-equivalent, or reuse an existing hook point another mod in this install already
  registers), instead of `ExecuteInGameThread` - this is the pattern most working mods that do
  per-frame reflection access actually use, and was not tried this round.
- Raise the helper's `FileMailbox::Request` timeout well past 5000ms temporarily and see if it
  eventually completes at all (currently unknown whether it is stuck forever or just very slow).
- Attach a native debugger to the game process during the hang to see what `GetClass()`'s native
  implementation is actually blocked on.

Every restart cycle in this session cost real time and real risk (the game had to be force-killed
more than once), so this was deliberately stopped here rather than continuing to guess live -
resume with a fresh session and one of the above, informed by everything above instead of
starting from zero.

## Ground truth from a real mod (2026-09-02, same day)

The user pointed at [Nexus mod 28](https://www.nexusmods.com/abioticfactor/mods/28) -
Igromanru's CheatConsoleCommands - which happened to already be installed in the test
environment (`Binaries/Win64/ue4ss/Mods/CheatConsoleCommands/`, a real, working, published Lua
mod for this exact game). Its full source was readable directly off disk, so instead of guessing
at the `GetClass()` mystery further, this round read how a real working mod does the exact same
kind of thing. It does it completely differently, and that difference almost certainly explains
the earlier hang:

- **Never calls `GetClass()`/`ForEachProperty` anywhere, in ~800 lines**, for property access.
  Properties are read/written by **direct dot-indexing** (`myPlayer.CurrentHunger`,
  `myPlayer.CurrentHunger = value`) - UE4SS's own `__index`/`__newindex` metamethods resolve the
  name, no manual reflection walk needed at all for these fields.
- **Gets the player via `GetMyPlayerController().MyPlayerCharacter`**, never `FindFirstOf`.
  (CORRECTION, confirmed by testing against the real game: `GetMyPlayerController()` itself is
  NOT a bare UE4SS/UEHelpers global - it is that mod's own locally-defined function
  (`AFUtils/BaseUtils/BaseUtils.lua`), built on the real global `UEHelpers.GetPlayerController()`
  from UE4SS's bundled shared module. This repo's `main.lua` now calls
  `UEHelpers.GetPlayerController()` directly instead. `.MyPlayerCharacter` itself was already
  right.) `FindFirstOf("AbioticCharacterPlayerState")` had tested as
  returning "found" in the earlier round, but `FindFirstOf`'s short-name matching against
  *some* instance is not the same guarantee as reaching the live player through the real object
  graph a working mod actually uses - a wrong/unexpected instance (a CDO, a stale replicated
  proxy, ...) is the leading theory for why `GetClass()` hung specifically on whatever it
  returned.
- **Most vitals fields carry NO hash suffix at all** - confirmed exact, working names:
  `CurrentHunger`/`MaxHunger`, `CurrentThirst`/`MaxThirst`, `CurrentFatigue`,
  `CurrentContinence`/`MaxContinence`, `CurrentMoney`, `CurrentHealth_Head/Torso/LeftArm/
  RightArm/LeftLeg/RightLeg`. (`CurrentSanity` was not found in that mod's source - it has no
  sanity-related command - so it is inferred from the pattern the other eleven share, not
  directly confirmed; worth checking first if it comes back wrong.) Money changes go through
  `Request_ModifyMoney` first (a server RPC, for proper multiplayer replication) with a direct
  set alongside for immediate local feedback - the same pattern this repo mirrors now.
- **Skills are a completely different shape than assumed**: not a plain array on PlayerState, but
  a **key/value map** (`CharacterSkills_Keys` + `CharacterSkills_Values`, parallel arrays) on a
  **`CharacterProgressionComponent`** on the player character, keyed by a `CharacterSkills` enum
  with its *own*, unrelated numbering to this repo's file-position order (built and verified by
  matching skill names between `Core/Catalogs/Player/SkillCatalog.cs` and that mod's
  `AFUtils/Enums.lua` - `main.lua`'s `FileIndexToLiveSkillId` table). The XP struct field DOES
  carry a hash suffix (`CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556`, confirmed exact from
  that mod's source) - hardcoded rather than scanned for, since struct-instance property
  scanning has no confirmed-working precedent to copy the way object scanning does, and this
  exact string is proven working in a real published mod today. Setting XP is not a direct write
  either - it goes through `Server_RemoveAllXPFromSkill` + `Server_AddXPToSkill` RPCs, the game's
  own validated progression system.

`main.lua` was rewritten around all of this and re-verified the same rigorous way as before: the
real Lua interpreter, a fake environment now shaped like the real object graph above (not
`FindFirstOf`/`GetClass`), and the full real-compiled-helper + real-.NET-client pipeline - all
passing, including the corrected file-index/live-skill-id mapping and the remove-then-add RPC
write pattern.

## Confirmed working end to end against the real game (2026-09-02, later the same day)

Re-tested live immediately after the rewrite above. First attempt hit one real bug:
`GetMyPlayerController()` failed with `attempt to call a nil value (global
'GetMyPlayerController')` - it genuinely is not a bare UE4SS global (see the correction inline in
the section above). Fixed by requiring `UEHelpers` and calling
`UEHelpers.GetPlayerController()` directly, then redeployed.

After the fix, all six commands were exercised against the actual running game, on a real save
with real progression ("Chrissie", 2h57m played, loaded through the game's own main menu - not a
fresh/empty character):

- `ping` - baseline dispatch/mailbox check, unchanged from earlier rounds.
- `diag.findplayer` - `found: false` at the main menu (no player exists yet, correctly), `found:
  true` once a world was actually loaded into gameplay.
- `vitals.get` - returned real live values for all twelve fields on the first try, including
  `CurrentSanity: 100` with no error, closing the one previously-unconfirmed guess in the vitals
  table.
- `vitals.set` - set `money` and `head` health; a follow-up `vitals.get` showed the exact new
  values with every other field untouched, and a screenshot confirmed it visually too (the HUD's
  head-injury indicator cleared once head health was set to 100).
- `skills.get` - returned real, non-zero XP for all 15 file indices on a save with actual playtime
  behind it - not just "no error", but the `FileIndexToLiveSkillId` table actually resolving every
  single entry against the live `CharacterSkills_Keys`/`_Values` arrays.
- `skills.set` - set file index 0 (Sprinting) from `51102.9` to `60000` via the remove-then-add RPC
  pair; a follow-up `skills.get` showed exactly `60000` for that entry and the original value for
  every other skill.

All test edits (money, head health, the one skill's XP) were reverted back to their original
values before the game was closed, since this ran against a real save rather than a disposable
fixture.

**Net result: the Lua+helper hybrid architecture, end to end, against the actual game, works.**
Every remaining "unconfirmed guess" flagged earlier in this document (`CurrentSanity`, the full
`FileIndexToLiveSkillId` table, the remove-then-add RPC pattern) is now confirmed by a real
round trip, not just "no error was thrown."

## World areas confirmed live (2026-09-06, round 75)

Five more command pairs - `world.get/set` (clock + weather), `flags.list/set` (quest/story
flags), `doors.list/set`, `containers.list/set`, and `dropped.list/remove` - were built from
the game's OWN class layouts rather than from a reference mod: `tests/AbioticEditor.Probes/
LiveClassPropsProbe.cs` dumps the blueprint property/function lists (DayNightManager_C,
SimpleDoor_ParentBP_C, SecurityDoor_C, Deployed_Container_ParentBP_C, Abiotic_Item_Dropped_C)
and the native usmap layouts, and the quest-flag path came from the shipped PDB: the native
`UWorldFlagSubsystem` (`SetWorldFlag(FWorldFlagRowHandle, bool, UObject*)`, `GetWorldFlags(
TArray<FName>&)`) plus `UWorldFlagHandleFunctionLibrary` (`GetAllWorldFlagRowNames/RowHandles`),
which is exactly what every in-game `Trigger_WorldFlag_C` and story-gated door calls. Earlier
rounds had concluded flags and containers had no live path only because no *installed mod*
touched them.

Every one of them was then exercised against the real running game ("Chrissie", day 22, host):

- `world.get` returned day 22, 11:31, clear weather, and all seven weather rows straight from
  the game's table. `world.set` moved the clock to 21:00 (the game flipped to night on its next
  tick and the HUD's cold indicator appeared), triggered `Fog` (`currentWeather` read back
  `Fog`), then cleared it and put the clock back to midday.
- `flags.list` returned 257 known flags with 59 set (the real story state). `flags.set` set
  `MapReveal_Security` and read back set; clearing it read back clear.
- `doors.list` returned 76 loaded doors (42 hinged, 34 security incl. tram rail doors).
  `doors.set` opened hinged door `SimpleDoor_ParentBP_C_9` (state 0 -> 1, `isOpen` true) and
  closed it again.
- `containers.list` returned 193 containers (mostly loot-spill bags, 69 player-usable ones with
  a free slot). `containers.set` put `scrap_metal x3` into slot 1 of a tram storage container,
  read back exactly, then cleared it back to `Empty`.
- `dropped.list` returned 112 loose items; `dropped.remove` on a warning sign returned
  `removed: 1`, and the item was gone from the next list a few seconds later (`InitDespawn` is
  timer-based, so the very next read can still show it). A bogus id returns `removed: 0`.

One correction found live: loot-spill bags carry the row name `None` (not `Empty`) in an
unused slot, so `isEmpty` now treats both as empty, matching the file editor.

Test edits were reverted (flag cleared, door closed, slot emptied, weather cleared, clock
restored); the one dropped warning sign stays removed. The character was also found dead on
load and healed/respawned first, as in earlier rounds.

## The stub-environment test harness (round 76/77): coverage without launching the game

`AbioticEditorLiveAgentLua/tests/` grew into a small, standalone test suite for `main.lua` and
every `Scripts/areas/*.lua` module, so a round of live-editing work can be exercised, and its
bugs found and fixed, without ever launching the game:

- `tests/harness.lua` fakes just enough of the UE4SS Lua API to load the real, unmodified
  `main.lua` and drive its handlers exactly the way the mailbox loop does: `H.object(className,
  fields, methods)` builds a fake UObject that answers `IsValid()`/`GetFullName()`/`IsA()`, and
  a call to any method NOT declared on it fails loudly (mirroring "attempt to call a nil value" /
  "UFunction expected" live) instead of silently doing nothing. `FindAllOf`/`FindFirstOf`/
  `StaticFindObject`, `FName`/`FText`, `NAME_None`/`EFindName`, and `LoopAsync`/
  `ExecuteInGameThread` are all stubbed - deliberately with NO `FVector()`/`FRotator()`
  constructor, because those are not real UE4SS Lua globals either (found live in round 76: a
  plain `{X,Y,Z}`/`{Pitch,Yaw,Roll}` table is what the real API accepts). `H.dispatch(cmd,
  payload)` calls a handler and round-trips its reply through the mod's own `json.lua`, so an
  FString/FName/UObject userdata a handler forgot to convert fails the same way it failed live
  (round 76's actual bug) instead of silently "working" in the test.
- `tests/cases/*.lua` (registered in `tests/cases/manifest.lua`) are the actual test suites, one
  per area, each `return function(H) ... end` using `H.hostSession()`/`H.clientSession()` for a
  ready-made connected-player fixture and `H.eq`/`H.ok`/`H.fails`/`H.check` to assert. `core.lua`
  covers the original areas from `main.lua` (dispatch, players, vitals, inventory, flags, world,
  doors, containers, dropped items, NPCs); one file per round-76 area (`containment.lua`,
  `traders.lua`, `portals.lua`, `spawn.lua`, `companions.lua`, `bases.lua`, `vehicles.lua`) plus
  `doors.lua` (a deeper pass on both door kinds) and `players.lua` (a second connected player,
  `playerId` targeting) followed in round 77. Every case asserts non-host refusal on every
  host-gated write and that a missing object id fails with a player-safe message, not a raw Lua
  error.
- `tests/run.lua` loads the harness, loads every area module the same way `main.lua` itself does
  (a module that fails to load fails the run), then runs every case in the manifest and exits 0
  only if every check passed.

**Running it by hand** (from the repo root, with a Lua 5.4 interpreter on PATH or given
directly):
```console
lua live-agent/AbioticEditorLiveAgentLua/tests/run.lua
```

**Building a Lua 5.4 interpreter** (there is no package manager involved on Windows - this is how
round 75/76 built the one used for every syntax check and harness run in this project, with no
local Lua install and no vendored copy in the repo):
1. Download the `lua-5.4.7` source tarball from lua.org and extract it.
2. From an "x64 Native Tools Command Prompt for VS 2022" (so MSVC's `cl`/`link` are on PATH),
   `cd` into the extracted `src/` folder and compile every `.c` file to an object file
   (`cl /c /O2 *.c`), then link `lua.exe` from `lua.obj` plus every OTHER library object file
   (`lapi.obj`, `lauxlib.obj`, `lbaselib.obj`, ... - everything except `luac.obj`, which carries
   its own separate `main()` for the bytecode compiler and is not needed here and would conflict
   if linked into the same executable as `lua.c`'s `main()`).
3. The resulting `lua.exe` (Windows) or `lua` (Linux/macOS, via that platform's own `make`
   instead) is a complete, standalone interpreter with `io`/`os` available - point
   `ABIOTIC_LUA_EXE` at it, or put it on PATH as `lua`, `lua54`, or `lua5.4`.

**Running it as part of `dotnet test`**: `tests/AbioticEditor.Tests/LiveAgentLuaHarnessTests.cs`
shells out to `tests/run.lua` above and asserts exit code 0, printing the harness's own output
either way. It resolves the interpreter the same three ways: the `ABIOTIC_LUA_EXE` environment
variable first, then `lua54`, `lua5.4`, or `lua` on PATH. When none is found it **skips** (via
`Xunit.SkippableFact`) rather than failing, so CI and any machine without a Lua interpreter stay
green:
```console
dotnet test tests/AbioticEditor.Tests -f net10.0 --filter "FullyQualifiedName~LiveAgentLuaHarnessTests"
```

**Where this stops being useful**: the harness proves a handler resolves the objects/functions it
expects, converts everything it hands back into something `json.lua` can encode, and honours host
gating - it can NEVER prove a property or function actually exists with that exact name on the
real class (the fake objects are only as honest as the class dump that built them - see
`tests/AbioticEditor.Probes/LiveClassPropsProbe.cs`), and it cannot exercise anything that
depends on real UE4SS marshalling behavior the stub cannot fully model (for example: a plain Lua
string assigned to a native `FStrProperty` struct field is accepted and auto-converted by the
real engine on write, confirmed live in round 76 for a pet's custom name, but the stub's own
nested struct tables are plain Lua tables with no such auto-conversion - so a case testing that
kind of write checks the raw field it landed on directly, rather than requiring a full round
trip back through the corresponding `*.list` handler). Anything the harness cannot settle either
way is still a real-game verification item, not a pass.

## Getting from here to a fully working setup

1. **Test against a real running game.** Most property names in `main.lua` are now copied
   verbatim from a real working mod (see "Ground truth from a real mod" above), not guessed -
   only `CurrentSanity` is unconfirmed. If something still does not resolve, the fix is a
   drop-the-script-in-and-test loop, not a rebuild - no SDK, no compiling.
2. **Install the Lua mod**: copy `AbioticEditorLiveAgentLua/Scripts/` into
   `<game>/Binaries/Win64/ue4ss/Mods/AbioticEditorLiveAgentLua/Scripts/` and enable it in
   `Mods/mods.txt` (matches how any other UE4SS Lua mod installs - this game already has several).
3. **Build the helper**:
   ```console
   cmake -S live-agent/AbioticEditorLiveAgentHelper -B live-agent/AbioticEditorLiveAgentHelper/build
   cmake --build live-agent/AbioticEditorLiveAgentHelper/build --config Release
   ```
   (or compile directly: `cl /std:c++20 /EHsc /I Shared AbioticEditorLiveAgentHelper/src/main.cpp Shared/LiveAgentServer.cpp /Fe:AbioticEditorLiveAgentHelper.exe /link ws2_32.lib bcrypt.lib`
   from `live-agent/`, in an "x64 Native Tools Command Prompt for VS 2022" - this is exactly how
   it was compiled and verified this round).
4. **Run it**: launch `AbioticEditorLiveAgentHelper.exe` (console window, keep it open) alongside
   the game. It prints its token on first run and writes it to
   `%LOCALAPPDATA%\AbioticEditorLiveAgent\token.txt`.
5. **Connect from the editor**: host `127.0.0.1`, port `42117`, the token from step 4.
6. **Re-run the tests** after any change - they cost nothing (no game needed):
   `AbioticEditorLiveAgent/tests/StandaloneProtocolSmokeTest.cpp` (native, see its header for the
   build command) and a Lua 5.4 interpreter run of `main.lua` under a fake environment (build one
   the same way this round did: `lua.org` source + the same MSVC toolchain compiles a `lua.exe`
   in under a minute).

## If UE4SS SDK access ever closes

The pure-C++-mod approach (`AbioticEditorLiveAgent/`) stays in the repo for that day: set
`-DUE4SS_SDK_DIR=<path>` when configuring its CMake project once a matching SDK is available, and
follow the same "confirm real property names" step before trusting it.
