#include "LiveAgentServer.h"
#include "FileMailbox.h"
#include <filesystem>
#include <future>
#include <iostream>

using namespace LiveAgent;
namespace {
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
JsonObject AwaitRequest(const std::string& dir) {
    auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    while (std::chrono::steady_clock::now() < deadline) {
        std::ifstream in(dir + "/request.json");
        std::string line;
        if (std::getline(in, line) && !line.empty()) {
            auto request = ParseLine(line);
            return *request.AsObject();
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    throw std::runtime_error("request did not arrive");
}
void Reply(const std::string& dir, const std::string& id, const std::string& value) {
    JsonObject reply;
    if (!id.empty()) reply.emplace("requestId", id);
    reply.emplace("ok", true); reply.emplace("result", value);
    const auto temp = dir + "/reply.tmp", dest = dir + "/response.json";
    { std::ofstream out(temp); out << ToLine(JsonValue(reply)); }
    Check(MoveFileExA(temp.c_str(), dest.c_str(), MOVEFILE_REPLACE_EXISTING) != 0, "publish failed");
}
}
int main() {
    auto dir = (std::filesystem::temp_directory_path() / ("AbioticMailboxTests-" + std::to_string(GetCurrentProcessId()))).string();
    std::filesystem::create_directories(dir);
    try {
        FileMailbox mailbox(dir);
        auto first = std::async(std::launch::async, [&] {
            try { mailbox.Request("slow", JsonValue(), 100); return false; }
            catch (const CommandFailed&) { return true; }
        });
        auto firstId = AwaitRequest(dir).at("requestId").AsString();
        Check(first.get(), "first request must time out");
        auto second = std::async(std::launch::async, [&] { return mailbox.Request("next", JsonValue(), 2000).AsString(); });
        auto secondId = AwaitRequest(dir).at("requestId").AsString();
        Check(firstId != secondId, "request ids must differ");
        Reply(dir, firstId, "wrong result");
        std::this_thread::sleep_for(std::chrono::milliseconds(80));
        Check(second.wait_for(std::chrono::milliseconds(0)) != std::future_status::ready, "late response must not answer second request");
        Check(std::filesystem::exists(dir + "/request.json"), "late response must not delete current request");
        Reply(dir, secondId, "correct result");
        Check(second.get() == "correct result", "current response must be returned");
        auto oldAgent = std::async(std::launch::async, [&] {
            try { mailbox.Request("legacy", JsonValue(), 1000); return false; }
            catch (const CommandFailed& error) { return std::string(error.what()).find("update") != std::string::npos; }
        });
        AwaitRequest(dir); Reply(dir, "", "uncorrelated");
        Check(oldAgent.get(), "old agent must require update");
        std::filesystem::remove_all(dir);
        std::cout << "FileMailbox correlation checks passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n';
        std::filesystem::remove_all(dir);
        return 1;
    }
}
