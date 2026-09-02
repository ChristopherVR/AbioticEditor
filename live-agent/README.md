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

**NOT verified: whether UE4SS's real Lua API behaves exactly like the fake stand-in above**, and
whether the property names (`Hunger_`, `CurrentSkillXP_`, ...) and class name
(`AbioticCharacterPlayerState`) in `main.lua` are correct. They are reasonable guesses by analogy
with the save-file property names this repo already knows (`Core/Domain/Player/`), not confirmed
live property names - same rule as always: don't trust them until checked against a real running
game (UE4SS's own object-dump tooling, or just watching the mod's `print()` output for errors).
This is a *much* smaller and easier-to-close gap than the pure-C++-mod approach's, because closing
it needs nothing more than dropping the Lua script into a real game and testing it - no rebuild,
no SDK.

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
