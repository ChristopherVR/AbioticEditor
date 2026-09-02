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
  `src/JsonLine.h` - the TCP listener, request/response framing, and JSON encoding. A standalone
  smoke test (`tests/StandaloneProtocolSmokeTest.cpp`) was compiled with MSVC 14.44 and run
  against the *actual* `TcpLiveGameChannel`/`LivePlayerVitalsChannel` classes from this repo's
  `AbioticEditor.Core`, confirming a real cross-language round trip: connect, `hello` with a
  token (accepted and rejected), `vitals.get`, `vitals.set`, and an unknown command all behave
  exactly as the .NET side expects. This is the novel, error-prone part (two different languages
  agreeing on a byte-for-byte wire format) and it is genuinely proven to work.
- **NOT verified**: `src/VitalsCommands.cpp` and `src/Mod.cpp` - the part that actually touches
  the game. These need UE4SS's real C++ Mod SDK to compile at all, and that SDK was not available
  in the session that wrote them (no vendored copy, and no reliable way to fetch and build it in
  that session). They are written against UE4SS's documented C++ mod API shape from public
  examples, and the property names in `VitalsCommands.cpp` (`Hunger_`, `Thirst_`, ...) are
  reasonable *guesses* by analogy with the save-file property names this repo already knows about
  (see `Core/Domain/Player/CharacterStats.cs`), not confirmed live property names. **Do not trust
  them until they have been checked against a real property dump of a running game** - the same
  rule this repo already applies to save-file property names (CLAUDE.md: a writer must use a
  tag's exact name from real data, never a guess).

## Getting from here to a working mod

1. **Get the matching UE4SS C++ Mod SDK.** The installed game logs its exact build at startup
   (`Binaries/Win64/ue4ss/UE4SS.log`): `UE4SS - v3.0.1 Beta #0 - Git SHA #01e0a584`. Get the SDK
   for that exact tag/SHA from the [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)
   releases - a mismatched SDK version risks an ABI mismatch that crashes the game on load, the
   same risk this repo already manages for the CUE4Parse/UeSaveGame submodules, just in a
   different language.
2. **Confirm the real property names.** Dump a running local player's `PlayerState` (UE4SS's own
   object-dump console command, or a throwaway Lua mod using `UEHelpers`) and update the
   `Hunger_`/`Thirst_`/... prefixes in `VitalsCommands.cpp` to match what is actually there -
   including the actual class name (`FindLocalPlayerState`'s `AbioticCharacterPlayerState` guess
   also needs confirming against the dump).
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
