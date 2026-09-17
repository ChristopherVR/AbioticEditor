// AbioticEditorLiveAgentHelper: the native half of the Lua+helper hybrid (see
// ../../README.md "The Lua+helper hybrid"). Handles TCP networking and nothing else - it has NO
// dependency on UE4SS or the game at all, which is exactly why it could be built and verified in
// full, unlike the pure-C++-mod approach's VitalsCommands.cpp/SkillsCommands.cpp.
//
// Every non-"hello" command is forwarded to the Lua mod over a file-based mailbox and the
// response relayed back over the same TCP connection the editor is waiting on.

#include <chrono>
#include <iostream>
#include <string>
#include <thread>
#include <unordered_set>

#include "../../Shared/LiveAgentServer.h"
#include "FileMailbox.h"
#include "TokenStore.h"

namespace
{
    constexpr int PreferredPort = 42117;

    // FileMailbox::Request's own default (5000ms) is tuned for the common case: read/set one
    // player field, one container slot, one recipe. A handful of commands walk EVERY placed
    // actor of a kind in the whole world instead (containers.list every container/bench and,
    // since a Void Chest's own inventory redirects to a single pool shared by every placed
    // instance - see main.lua's containerInventory() remarks - now also that shared pool's full
    // contents on top of everything else). A live report showed containers.list repeatedly
    // failing with "the live-agent Lua mod did not respond in time" on a well-built world - a
    // genuine timeout, not a hang, since a later retry sometimes succeeded. Giving these specific
    // commands more time to actually finish is a direct fix for that failure mode; it does not
    // explain every earlier live report about Void Chests, but a request that keeps timing out
    // cannot produce a correct read OR see a write's own result either, so this is worth trying
    // before assuming those are still-open, separate bugs.
    const std::unordered_set<std::string> SlowCommands = {
        "containers.list", "npcs.list", "bases.list", "pets.list", "vehicles.list", "narrativenpcs.list",
    };
    constexpr int SlowCommandTimeoutMs = 20000;
}

int main()
{
    using namespace LiveAgent;

    auto localAppData = LocalAppDataDir();
    auto rootDir = localAppData + "\\AbioticEditorLiveAgent";
    auto token = LoadOrCreateToken(rootDir);
    // The token itself is deliberately NOT printed here: this console output is piped into a
    // rolling log file by the editor's own launcher (see LaunchHelperHidden in LiveAgentSetup.cs),
    // and a live-edit connection secret has no business sitting in a log file on disk. The file it
    // was already written to (token.txt) is the one place a player who needs to see it (a remote-
    // server operator copying it to the client machine) should ever look.
    std::cout << "AbioticEditorLiveAgentHelper\n"
              << "Token written to " << rootDir << "\\token.txt (open that file if you need it - "
              << "never printed here, since this window's output is saved to a log file).\n"
              << "Preferred port: " << PreferredPort
              << " (falls back to a nearby one automatically if that's already taken).\n"
              << "Keep this window open while you live-edit.\n"
              << "Waiting for the AbioticEditorLiveAgent Lua mod to be loaded in the game...\n";

    FileMailbox mailbox(rootDir + "\\ipc");
    Server server(PreferredPort, token, [](const std::string& line) { std::cout << line << "\n"; });

    // Every command except "hello" is the Lua mod's business: forward it by name and relay the
    // answer. The mod answers "unknown command" itself for anything it does not implement, so
    // adding a live-editing area no longer needs this helper rebuilt - only main.lua (and its
    // areas/ modules) change.
    server.RegisterDefaultHandler([&mailbox](const std::string& command, const LiveAgent::JsonValue& payload) {
        if (SlowCommands.count(command)) return mailbox.Request(command, payload, SlowCommandTimeoutMs);
        return mailbox.Request(command, payload);
    });

    if (!server.Start())
    {
        std::cout << "Could not open a live-edit port - exiting.\n";
        return 1;
    }

    // Written next to the token so the editor can find the port this instance actually bound to
    // (see LiveAgentServer::Start's fallback) the same way it already finds the token - no config
    // file needed to keep the two sides in sync.
    WritePortFile(rootDir, server.Port());

    std::cout << "Listening on port " << server.Port() << ".\n"
              << "Ready.\n";
    while (true) std::this_thread::sleep_for(std::chrono::seconds(1));
}
