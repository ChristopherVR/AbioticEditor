#pragma once
// Registers "vitals.get"/"vitals.set" against a live Server (see LiveAgentServer.h). This is the
// ONE file in this mod that actually touches the game's live UObjects, so it is the one file
// that needs UE4SS's real C++ Mod SDK headers to compile - see ../README.md for exactly which
// SDK build this must be compiled against (it must match the installed UE4SS.dll's ABI, the
// same discipline this repo already applies to the CUE4Parse/UeSaveGame submodules).
//
// NOT YET VERIFIED AGAINST THE REAL GAME. Written against UE4SS's documented C++ mod API shape
// (CppUserModBase, UObjectGlobals::FindFirstOf, property lookup by name) from public UE4SS mod
// examples, but has never been compiled against real UE4SS headers or run against the live game -
// there was no way to do either in the session that wrote this (no vendored SDK, and driving the
// actual game reliably from here had already proven unreliable). Treat the exact property names
// below as a starting guess to verify against a live property dump, not as confirmed fact - the
// same "never trust a name until you have seen it in real data" discipline this repo's file
// writers already follow for GVAS tag names (see CLAUDE.md's FullNames tables) applies here too.

#include "LiveAgentServer.h"

namespace LiveAgent
{
    // Registers the vitals command pair on `server`. Call once, after UE4SS has finished its own
    // init (on_unreal_init in the mod's CppUserModBase override), so the reflection system it
    // uses is actually ready.
    void RegisterVitalsCommands(Server& server);
}
