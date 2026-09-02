#pragma once
// The bridge between this native helper (does TCP networking, nothing else) and the UE4SS Lua
// mod (does the actual game interaction, nothing else) - see ../../AbioticEditorLiveAgentLua and
// ../../README.md for why this hybrid exists (the direct C++-mod approach is blocked on private
// SDK access this project does not have).
//
// One request in flight at a time (matches TcpLiveGameChannel's own single-in-flight design), so
// a single-slot mailbox is enough - no queue, no request ids needed at this layer (the TCP
// envelope's own "id" is handled one level up, in main.cpp).
//
// Both sides derive the IPC folder from %LOCALAPPDATA% independently (no config file to keep in
// sync): this helper via GetEnvironmentVariable, the Lua mod via os.getenv - both are the
// standard way to read that variable on their respective sides, so they agree without either
// telling the other where to look.

#include <chrono>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "JsonLine.h"

namespace LiveAgent
{
    class FileMailbox
    {
    public:
        explicit FileMailbox(const std::string& ipcDir) : m_dir(ipcDir)
        {
            CreateDirectoryA(m_dir.c_str(), nullptr); // No-op (ERROR_ALREADY_EXISTS) if it exists.
        }

        // Writes `command`/`payload` as a request, waits up to `timeoutMs` for the Lua mod's
        // response, and returns its "result" (or throws CommandFailed on ok:false / timeout).
        JsonValue Request(const std::string& command, const JsonValue& payload, int timeoutMs = 5000)
        {
            DeleteFileA(ResponsePath().c_str()); // Clear any stale response from a prior timeout.

            JsonObject requestObject;
            requestObject.emplace("cmd", command);
            requestObject.emplace("payload", payload);
            WriteAtomic(RequestPath(), ToLine(JsonValue(std::move(requestObject))));

            auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(timeoutMs);
            while (std::chrono::steady_clock::now() < deadline)
            {
                if (TryReadAndDelete(ResponsePath(), m_lastLine))
                {
                    DeleteFileA(RequestPath().c_str()); // Defensive: normally the Lua mod already did.
                    JsonValue response = ParseLine(m_lastLine);
                    const JsonObject* object = response.AsObject();
                    if (!object) throw CommandFailed("the live-agent Lua mod sent a malformed response");
                    auto okIt = object->find("ok");
                    if (okIt == object->end() || !okIt->second.AsBool())
                    {
                        auto errorIt = object->find("error");
                        throw CommandFailed(errorIt != object->end()
                            ? errorIt->second.AsString("the live-agent Lua mod rejected the request")
                            : "the live-agent Lua mod rejected the request");
                    }
                    auto resultIt = object->find("result");
                    return resultIt != object->end() ? resultIt->second : JsonValue();
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(20));
            }

            DeleteFileA(RequestPath().c_str());
            throw CommandFailed(
                "the live-agent Lua mod did not respond in time - is it loaded and enabled in the game?");
        }

    private:
        std::string m_dir;
        std::string m_lastLine;

        std::string RequestPath() const { return m_dir + "\\request.json"; }
        std::string RequestTempPath() const { return m_dir + "\\request.json.tmp"; }
        std::string ResponsePath() const { return m_dir + "\\response.json"; }

        static void WriteAtomic(const std::string& path, const std::string& content)
        {
            auto tempPath = path + ".tmp";
            {
                std::ofstream out(tempPath, std::ios::binary | std::ios::trunc);
                if (!out) throw std::runtime_error("could not write " + tempPath);
                out << content;
            }
            if (!MoveFileExA(tempPath.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING))
                throw std::runtime_error("could not publish " + path);
        }

        // Reads and deletes a file if it exists and is non-empty (a reader observing a file mid
        // write - between the writer's create and its final rename - would otherwise see it as
        // present-but-empty; WriteAtomic's rename-from-temp makes that window effectively zero,
        // but the empty check costs nothing and removes any doubt).
        static bool TryReadAndDelete(const std::string& path, std::string& out)
        {
            std::ifstream in(path, std::ios::binary);
            if (!in) return false;
            std::ostringstream buffer;
            buffer << in.rdbuf();
            in.close();
            if (buffer.str().empty()) return false;
            out = buffer.str();
            DeleteFileA(path.c_str());
            return true;
        }
    };
}
