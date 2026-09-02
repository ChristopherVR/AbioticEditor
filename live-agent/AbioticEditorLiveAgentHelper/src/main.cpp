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

#include "../../Shared/LiveAgentServer.h"
#include "FileMailbox.h"
#include "TokenStore.h"

namespace
{
    constexpr int Port = 42117;

    // Registers one command as "forward the payload to the Lua mailbox, return its result" -
    // every command this helper knows about (vitals.get/set, skills.get/set, and any area added
    // later) is exactly this shape, so adding a new area is one line here plus the matching
    // handler in main.lua - no new C++ needed.
    void ForwardToLua(LiveAgent::Server& server, LiveAgent::FileMailbox& mailbox, const std::string& command)
    {
        server.RegisterCommand(command, [&mailbox, command](const LiveAgent::JsonValue& payload) {
            return mailbox.Request(command, payload);
        });
    }
}

int main()
{
    using namespace LiveAgent;

    auto localAppData = LocalAppDataDir();
    auto rootDir = localAppData + "\\AbioticEditorLiveAgent";
    auto token = LoadOrCreateToken(rootDir);
    std::cout << "AbioticEditorLiveAgentHelper\n"
              << "Token (also written to " << rootDir << "\\token.txt): " << token << "\n"
              << "Listening on port " << Port << ". Keep this window open while you live-edit.\n"
              << "Waiting for the AbioticEditorLiveAgent Lua mod to be loaded in the game...\n";

    FileMailbox mailbox(rootDir + "\\ipc");
    Server server(Port, token, [](const std::string& line) { std::cout << line << "\n"; });

    for (const char* command : {"ping", "diag.findplayer", "diag.getclass", "diag.countprops",
        "vitals.get", "vitals.set", "skills.get", "skills.set", "players.list",
        "npcs.list", "npcs.set"})
        ForwardToLua(server, mailbox, command);

    server.Start();
    std::cout << "Ready.\n";
    while (true) std::this_thread::sleep_for(std::chrono::seconds(1));
}
