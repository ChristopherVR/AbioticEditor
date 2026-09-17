#include "LiveAgentServer.h"

#include <sstream>

#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

namespace LiveAgent
{
    namespace
    {
        // How many ports past the preferred one to try before giving up. A second helper left
        // running from an earlier manual test (see docs/PROGRESS.md - this happened during
        // development) or literally anything else on the machine bound to the same fixed port
        // used to mean this helper opened no listener at all and only a log line said why.
        constexpr int MaxPortAttempts = 10;

        // Reads one newline-delimited line from a blocking socket. Returns false on a clean
        // close or a socket error, matching how the .NET side's StreamReader.ReadLineAsync
        // returning null signals the same thing.
        bool ReadLine(SOCKET socket, std::string& outLine)
        {
            outLine.clear();
            char buffer;
            while (true)
            {
                int received = recv(socket, &buffer, 1, 0);
                if (received <= 0) return !outLine.empty() ? true : false;
                if (buffer == '\n') return true;
                if (buffer != '\r') outLine += buffer;
            }
        }

        bool WriteLine(SOCKET socket, const std::string& line)
        {
            std::string withNewline = line + "\n";
            int total = 0;
            while (total < static_cast<int>(withNewline.size()))
            {
                int sent = send(socket, withNewline.data() + total,
                    static_cast<int>(withNewline.size()) - total, 0);
                if (sent <= 0) return false;
                total += sent;
            }
            return true;
        }
    }

    bool Server::TryBindAndListen(int port)
    {
        SOCKET listenSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        if (listenSocket == INVALID_SOCKET) return false;

        // Bound to loopback + whatever interface a dedicated server operator explicitly wants
        // reachable is the operator's call, not this mod's - it binds all interfaces (0.0.0.0)
        // like any normal game server port, and relies on the token (see Dispatch) plus whatever
        // firewall/port-forwarding the operator already controls for a rented/self-hosted server.
        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_addr.s_addr = INADDR_ANY;
        address.sin_port = htons(static_cast<u_short>(port));

        if (bind(listenSocket, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == SOCKET_ERROR
            || listen(listenSocket, /*backlog*/ 1) == SOCKET_ERROR)
        {
            closesocket(listenSocket);
            return false;
        }

        m_listenSocket = static_cast<std::uintptr_t>(listenSocket);
        m_port = port;
        return true;
    }

    bool Server::Start()
    {
        if (m_running.load()) return true;

        WSADATA wsaData;
        if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
        {
            m_log("Live agent: WSAStartup failed, the live-edit port will not open.");
            return false;
        }

        auto preferredPort = m_port;
        bool bound = false;
        for (int attempt = 0; attempt < MaxPortAttempts; ++attempt)
        {
            if (TryBindAndListen(preferredPort + attempt)) { bound = true; break; }
        }

        if (!bound)
        {
            m_log("Live agent: could not bind/listen on any port from " + std::to_string(preferredPort)
                + " to " + std::to_string(preferredPort + MaxPortAttempts - 1)
                + " - is something else already using all of them?");
            WSACleanup();
            return false;
        }

        m_log(m_port == preferredPort
            ? "Live agent: listening on port " + std::to_string(m_port) + "."
            : "Live agent: port " + std::to_string(preferredPort) + " was already in use, listening on port "
                + std::to_string(m_port) + " instead.");

        m_running = true;
        m_acceptThread = std::thread([this] { AcceptLoop(); });
        return true;
    }

    void Server::Stop()
    {
        if (!m_running.exchange(false)) return;
        if (m_listenSocket) { closesocket(static_cast<SOCKET>(m_listenSocket)); m_listenSocket = 0; }
        if (m_acceptThread.joinable()) m_acceptThread.join();
        WSACleanup();
    }

    void Server::AcceptLoop()
    {
        SOCKET listenSocket = static_cast<SOCKET>(m_listenSocket);
        while (m_running.load())
        {
            SOCKET client = accept(listenSocket, nullptr, nullptr);
            if (client == INVALID_SOCKET) break; // Stop() closed the listening socket.
            m_log("Live agent: client connected.");
            ServeClient(static_cast<std::uintptr_t>(client));
            m_log("Live agent: client disconnected.");
        }
    }

    void Server::ServeClient(std::uintptr_t clientSocketHandle)
    {
        SOCKET client = static_cast<SOCKET>(clientSocketHandle);
        bool authenticated = false;
        std::string line;
        while (m_running.load() && ReadLine(client, line))
        {
            std::string id;
            JsonValue payload;
            std::string command;
            bool ok = true;
            std::string error;
            JsonValue result;

            try
            {
                JsonValue request = ParseLine(line);
                const JsonObject* object = request.AsObject();
                if (!object) throw std::runtime_error("request was not a JSON object");
                id = object->at("id").AsString();
                command = object->at("cmd").AsString();
                auto payloadIt = object->find("payload");
                if (payloadIt != object->end()) payload = payloadIt->second;

                result = Dispatch(command, payload, authenticated);
            }
            catch (const CommandFailed& failure)
            {
                ok = false;
                error = failure.what();
            }
            catch (const std::exception& exception)
            {
                ok = false;
                error = std::string("malformed request: ") + exception.what();
            }

            // Command name and the (fixed, player-safe - see CommandFailed's own contract) error
            // message only, never `payload` or `line`: the raw request for "hello" specifically
            // carries the connection token, and logging the parsed request/payload wholesale here
            // would put it in a file on disk. A rejected "hello" itself is still worth a line (the
            // error text is always one of a small set of fixed strings like "bad token", never the
            // token's own value - see Dispatch), since that is exactly the kind of mismatch a
            // player debugging a stuck connection needs to see.
            if (!ok) m_log("Live agent: '" + command + "' failed: " + error);

            JsonObject response;
            response.emplace("id", id);
            response.emplace("ok", ok);
            if (ok) response.emplace("result", result);
            else response.emplace("error", error);

            if (!WriteLine(client, ToLine(JsonValue(std::move(response))))) break;
        }
        closesocket(client);
    }

    JsonValue Server::Dispatch(const std::string& command, const JsonValue& payload, bool& authenticated)
    {
        if (command == "hello")
        {
            const JsonObject* object = payload.AsObject();
            std::string token = object ? object->at("token").AsString() : std::string();
            if (token != m_token) throw CommandFailed("bad token");
            authenticated = true;
            JsonObject result;
            result.emplace("protocolVersion", 1);
            result.emplace("agentVersion", std::string("AbioticEditorLiveAgent/0.1"));
            return JsonValue(std::move(result));
        }

        if (!authenticated) throw CommandFailed("not authenticated - send 'hello' with the token first");

        CommandHandler handler;
        DefaultHandler fallback;
        {
            std::lock_guard lock(m_handlersMutex);
            auto it = m_handlers.find(command);
            if (it != m_handlers.end()) handler = it->second;
            else fallback = m_default;
        }
        if (handler) return handler(payload);
        if (fallback) return fallback(command, payload);
        throw CommandFailed("unknown command '" + command + "'");
    }
}
