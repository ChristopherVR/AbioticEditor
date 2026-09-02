# AbioticEditorLiveAgent

The in-game half of live editing: a UE4SS mod that opens a small local TCP port and speaks the
same protocol as `AbioticEditor.Core.LiveEditing.TcpLiveGameChannel` (the .NET editor's side),
so the desktop app can read and write a running game's memory in real time instead of a `.sav`
file. See `docs/reference/live-editing-protocol.md` for the wire format both sides implement, and
`docs/PROGRESS.md` (round-64 or later) for how this fits into the editor's own history.

**This is NOT part of the .NET solution.** Like the sample plugins under `plugins/`, it is built
and shipped as its own standalone artifact - here, a mod dropped into the game's (or a dedicated
server's) `Binaries/Win64/ue4ss/Mods/` folder, not something `dotnet build` touches.

## What is verified, and what is not (read this before trusting anything here)

- **Verified, by actually compiling and running it**: `src/LiveAgentServer.{h,cpp}` and
  `src/JsonLine.h` (including its array support, added for `skills.get`/`skills.set`) - the TCP
  listener, request/response framing, and JSON encoding. A standalone smoke test
  (`tests/StandaloneProtocolSmokeTest.cpp`) was compiled with MSVC 14.44 and run against the
  *actual* `TcpLiveGameChannel`/`LivePlayerVitalsChannel`/`LivePlayerSkillsChannel` classes from
  this repo's `AbioticEditor.Core`, confirming real cross-language round trips: connect, `hello`
  with a token (accepted and rejected), `vitals.get`/`vitals.set`, `skills.get`/`skills.set` (a
  real JSON array, not just flat objects), and an unknown command all behave exactly as the .NET
  side expects. This is the novel, error-prone part (two different languages agreeing on a
  byte-for-byte wire format) and it is genuinely proven to work. The token itself
  (`Mod.cpp::GenerateRandomToken`) was also isolated and compile-verified separately after an
  automated security review flagged its first draft for using a non-cryptographic RNG.
- **NOT verified**: `src/VitalsCommands.cpp`, `src/SkillsCommands.cpp`, and `src/Mod.cpp` - the
  part that actually touches the game. These need UE4SS's real C++ Mod SDK to compile at all, and
  that SDK was not available in the sessions that wrote them. **A from-source build was
  attempted and hit a hard, real blocker, not a missing shortcut**: the installed game's exact
  UE4SS commit (`01e0a584`) depends on a private submodule
  (`git@github.com:Re-UE4SS/UEPseudo.git`, 404s even over HTTPS) that needs access this project
  does not have - the public `v3.0.1` *tag*'s SDK release is a different, earlier commit
  (`d935b5b`) and is explicitly documented upstream as ABI-incompatible ("C++ mods must be
  rebuilt to work on 3.0.1"), so it would not safely stand in. Whoever picks this up next needs
  either their own access to that private dependency, or to accept the real (if unlikely) crash
  risk of a slightly-mismatched SDK and test carefully. `VitalsCommands.cpp`/`SkillsCommands.cpp`
  are written against UE4SS's documented C++ mod API shape from public examples, and the property
  names (`Hunger_`, `CurrentSkillXP_`, ...) are reasonable *guesses* by analogy with the save-file
  property names this repo already knows about (see `Core/Domain/Player/`), not confirmed live
  property names. **Do not trust them until they have been checked against a real property dump
  of a running game** - the same rule this repo already applies to save-file property names
  (CLAUDE.md: a writer must use a tag's exact name from real data, never a guess).

## Getting from here to a working mod

1. **Get the matching UE4SS C++ Mod SDK.** The installed game logs its exact build at startup
   (`Binaries/Win64/ue4ss/UE4SS.log`): `UE4SS - v3.0.1 Beta #0 - Git SHA #01e0a584`. That commit
   exists in the public [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) repo, but
   building it needs its `deps/first/Unreal` submodule, which is private - get access to that (an
   Epic-linked GitHub account is the usual route for Unreal-source-derived repos) and build UE4SS
   itself at that commit to produce a matching SDK. The public `v3.0.1` tag's release SDK is a
   different commit and is documented upstream as ABI-incompatible with a 3.0.1-family build - do
   not substitute it. A mismatched SDK risks an ABI mismatch that crashes the game on load, the
   same risk this repo already manages for the CUE4Parse/UeSaveGame submodules, just in a
   different language.
2. **Confirm the real property names.** Dump a running local player's `PlayerState` (UE4SS's own
   object-dump console command, or a throwaway Lua mod using `UEHelpers`) and update the
   `Hunger_`/`Thirst_`/`CurrentSkillXP_`/`CurrentXPMultiplier_`/... prefixes in
   `VitalsCommands.cpp`/`SkillsCommands.cpp` to match what is actually there - including the
   actual class name (`FindLocalPlayerState`'s `AbioticCharacterPlayerState` guess also needs
   confirming against the dump) and the `Skills_` array element struct's real type.
3. **Build**:
   ```console
   cmake -B build -DUE4SS_SDK_DIR=<path to the SDK from step 1>
   cmake --build build --config Release
   ```
4. **Install**: copy the built `AbioticEditorLiveAgent.dll` into
   `<game>/Binaries/Win64/ue4ss/Mods/AbioticEditorLiveAgent/dlls/main.dll` (the standard UE4SS
   C++ mod layout) and enable it in `Mods/mods.txt`. On a dedicated server, the same folder
   layout applies (hosting providers document swapping `dwmapi.dll` for `version.dll` there).
5. **Get the token**: the mod writes a random one to
   `Mods/AbioticEditorLiveAgent/token.txt` on first run - copy it into the editor's Live-edit
   connect screen along with the host (`127.0.0.1` for a local game) and port (`42117` by
   default).
6. **Re-run the standalone smoke test** after any change to `LiveAgentServer.cpp`/`JsonLine.h` -
   it costs nothing (no game, no SDK needed) and it is what actually caught the protocol working
   correctly during development.
