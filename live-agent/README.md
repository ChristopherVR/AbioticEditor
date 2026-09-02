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

**Tested against the real game this round** (see "Real-game debugging session" below for the full
log) - `main.lua` genuinely loads and runs with zero errors in real UE4SS, `FindFirstOf` is
confirmed correct, and a real catastrophic-freeze bug was found and fixed. **Still open**:
`GetClass()` on the confirmed-correct player object does not return within the round-trip budget
even from the game thread, for a reason not yet root-caused - see that section for exactly what
was tried and what to try next. The property name prefixes (`Hunger_`, `CurrentSkillXP_`, ...)
remain unconfirmed guesses until that is resolved.

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

## Getting from here to a fully working setup

1. **Confirm the real property names and class name.** Dump a running local player's
   `PlayerState` (UE4SS's own object-dump console command, or a throwaway diagnostic Lua script)
   and update the prefixes in `main.lua` (`Hunger_`, `CurrentSkillXP_`, ...) and
   `findLocalPlayerState`'s class name to match what is actually there. This is now a
   drop-the-script-in-and-test loop, not a rebuild.
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
