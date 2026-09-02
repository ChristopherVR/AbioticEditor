// Compiles and runs standalone (no UE4SS SDK needed) to verify LiveAgentServer + JsonLine
// actually round-trip the same wire protocol AbioticEditor.Core.LiveEditing.TcpLiveGameChannel
// speaks: connect, send "hello" with a token, register a fake "vitals.get" handler, call it, and
// check a bad token is rejected. This is real verification of the transport/JSON layer, which is
// the part of the live-agent mod that has no dependency on the game or UE4SS at all.
//
// Build (from a "x64 Native Tools Command Prompt for VS 2022"):
//   cl /std:c++20 /EHsc /I ..\src StandaloneProtocolSmokeTest.cpp ..\src\LiveAgentServer.cpp /link ws2_32.lib
//   StandaloneProtocolSmokeTest.exe

#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")

#include <cassert>
#include <chrono>
#include <iostream>
#include <thread>

#include "../src/LiveAgentServer.h"

namespace
{
    using namespace LiveAgent;

    std::string ReadLineBlocking(SOCKET socket)
    {
        std::string line;
        char c;
        while (recv(socket, &c, 1, 0) == 1)
        {
            if (c == '\n') break;
            if (c != '\r') line += c;
        }
        return line;
    }

    void SendLine(SOCKET socket, const std::string& line)
    {
        std::string withNewline = line + "\n";
        send(socket, withNewline.data(), static_cast<int>(withNewline.size()), 0);
    }

    SOCKET ConnectToLoopback(int port)
    {
        SOCKET s = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
        sockaddr_in address{};
        address.sin_family = AF_INET;
        address.sin_port = htons(static_cast<u_short>(port));
        inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
        for (int attempt = 0; attempt < 20; ++attempt)
        {
            if (connect(s, reinterpret_cast<sockaddr*>(&address), sizeof(address)) == 0) return s;
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }
        throw std::runtime_error("could not connect to the test server");
    }
}

int main(int argc, char** argv)
{
    WSADATA wsaData;
    WSAStartup(MAKEWORD(2, 2), &wsaData);

    const int port = 42117;
    Server server(port, "correct-token", [](const std::string& line) { std::cout << line << "\n"; });
    server.RegisterCommand("vitals.get", [](const JsonValue&) {
        JsonObject result;
        result.emplace("hunger", 42.5);
        result.emplace("thirst", 10);
        result.emplace("sanity", 88);
        result.emplace("fatigue", 5);
        result.emplace("continence", 100);
        result.emplace("money", 7);
        result.emplace("head", 100);
        result.emplace("torso", 100);
        result.emplace("leftArm", 100);
        result.emplace("rightArm", 100);
        result.emplace("leftLeg", 100);
        result.emplace("rightLeg", 100);
        return JsonValue(std::move(result));
    });
    server.RegisterCommand("vitals.set", [](const JsonValue&) { return JsonValue(); });
    server.Start();
    std::this_thread::sleep_for(std::chrono::milliseconds(200));

    // "--serve": stay up indefinitely instead of running the self-checks below, so an external
    // client (the real .NET TcpLiveGameChannel, in this repo's cross-interop check) can connect
    // to a genuine compiled instance of this exact source instead of a same-language mock.
    if (argc > 1 && std::string(argv[1]) == "--serve")
    {
        std::cout << "Serving on port " << port << ". Press Ctrl+C to stop.\n";
        while (true) std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    // 1. hello with the wrong token is rejected.
    {
        SOCKET client = ConnectToLoopback(port);
        SendLine(client, R"({"id":"1","cmd":"hello","payload":{"token":"wrong"}})");
        std::string response = ReadLineBlocking(client);
        std::cout << "wrong-token response: " << response << "\n";
        assert(response.find("\"ok\":false") != std::string::npos);
        closesocket(client);
    }

    // 2. hello with the right token, then a registered command round-trips.
    {
        SOCKET client = ConnectToLoopback(port);
        SendLine(client, R"({"id":"1","cmd":"hello","payload":{"token":"correct-token"}})");
        std::string helloResponse = ReadLineBlocking(client);
        std::cout << "hello response: " << helloResponse << "\n";
        assert(helloResponse.find("\"ok\":true") != std::string::npos);
        assert(helloResponse.find("\"protocolVersion\":1") != std::string::npos);

        SendLine(client, R"({"id":"2","cmd":"vitals.get"})");
        std::string vitalsResponse = ReadLineBlocking(client);
        std::cout << "vitals.get response: " << vitalsResponse << "\n";
        assert(vitalsResponse.find("\"hunger\":42.5") != std::string::npos);
        assert(vitalsResponse.find("\"money\":7") != std::string::npos);
        closesocket(client);
    }

    // 3. an unknown command is rejected with ok:false, not a crash.
    {
        SOCKET client = ConnectToLoopback(port);
        SendLine(client, R"({"id":"1","cmd":"hello","payload":{"token":"correct-token"}})");
        ReadLineBlocking(client);
        SendLine(client, R"({"id":"3","cmd":"does.not.exist"})");
        std::string response = ReadLineBlocking(client);
        std::cout << "unknown-command response: " << response << "\n";
        assert(response.find("\"ok\":false") != std::string::npos);
        closesocket(client);
    }

    server.Stop();
    WSACleanup();
    std::cout << "All checks passed.\n";
    return 0;
}
