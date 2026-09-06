#pragma once
// The transport half of the live agent: a single-connection-at-a-time TCP listener speaking the
// same newline-delimited JSON protocol as AbioticEditor.Core.LiveEditing.TcpLiveGameChannel (see
// docs/reference/live-editing-protocol.md). Deliberately has NO dependency on UE4SS or the game -
// it only knows about sockets and JSON, so it can be built and tested standalone (see
// tests/StandaloneProtocolSmokeTest.cpp) even without the UE4SS SDK the real mod needs.
//
// Runs its accept/serve loop on its own thread so it never blocks the game's main thread; command
// handlers you register are invoked ON THAT THREAD, so any handler touching live UObjects (see
// VitalsCommands.cpp) must marshal onto the game thread itself - this class does not do that for
// you, because only the UE4SS-integrated caller knows the right way to do it for this game/engine
// version.

#include <atomic>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>

#include "JsonLine.h"

namespace LiveAgent
{
    // A command handler returns the "result" value on success, or throws CommandFailed with a
    // player-safe message on a rejected/invalid command (bad params, live object not found, ...).
    class CommandFailed : public std::runtime_error
    {
    public:
        explicit CommandFailed(const std::string& message) : std::runtime_error(message) {}
    };

    using CommandHandler = std::function<JsonValue(const JsonValue& payload)>;
    // Receives every authenticated command that has no explicit RegisterCommand entry, with the
    // command name, so a host can forward "anything else" to another layer (the Lua mod).
    using DefaultHandler = std::function<JsonValue(const std::string& command, const JsonValue& payload)>;

    class Server
    {
    public:
        // `logLine` receives short diagnostic lines (connects, disconnects, errors) for the mod
        // to forward to UE4SS's own log; kept as a callback so this header stays UE4SS-free.
        Server(int port, std::string token, std::function<void(const std::string&)> logLine)
            : m_port(port), m_token(std::move(token)), m_log(std::move(logLine)) {}

        ~Server() { Stop(); }

        void RegisterCommand(const std::string& name, CommandHandler handler)
        {
            std::lock_guard lock(m_handlersMutex);
            m_handlers[name] = std::move(handler);
        }

        void RegisterDefaultHandler(DefaultHandler handler)
        {
            std::lock_guard lock(m_handlersMutex);
            m_default = std::move(handler);
        }

        // Starts the accept loop on a background thread. Safe to call once; call Stop() before a
        // second Start() if the port or token needs to change.
        void Start();
        void Stop();

    private:
        void AcceptLoop();
        // Serves exactly one client to completion (until it disconnects or sends a bad line),
        // then returns so AcceptLoop can accept the next one. One connection at a time matches
        // TcpLiveGameChannel's own single-connection, single-in-flight-request design.
        void ServeClient(std::uintptr_t clientSocket);
        JsonValue Dispatch(const std::string& command, const JsonValue& payload, bool& authenticated);

        int m_port;
        std::string m_token;
        std::function<void(const std::string&)> m_log;
        std::atomic<bool> m_running{false};
        std::thread m_acceptThread;
        std::uintptr_t m_listenSocket = 0; // SOCKET, kept as uintptr_t so this header need not include <winsock2.h>.
        std::mutex m_handlersMutex;
        DefaultHandler m_default;
        std::unordered_map<std::string, CommandHandler> m_handlers;
    };
}
