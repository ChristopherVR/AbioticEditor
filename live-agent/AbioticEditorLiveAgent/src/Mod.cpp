// UE4SS C++ mod entry point. NOT YET COMPILED against the real SDK - see README.md. Written
// against UE4SS's documented CppUserModBase shape and its exported RegisterMod() convention.
#include <Mod/CppUserModBase.hpp>
#include <DynamicOutput/DynamicOutput.hpp>

#include <filesystem>
#include <fstream>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")

#include "LiveAgentServer.h"
#include "VitalsCommands.h"

namespace
{
    // Default port and where the token is read from: a plain text file the mod writes on first
    // run (a random token, so nobody has to hand-pick one) and the player copies into the
    // editor's connect screen once. Kept as a config file, not baked into the mod, so re-running
    // does not just accept whatever token an attacker on the same LAN happened to send first.
    constexpr int DefaultPort = 42117;
}

class AbioticEditorLiveAgentMod : public RC::CppUserModBase
{
public:
    AbioticEditorLiveAgentMod()
    {
        ModName = STR("AbioticEditorLiveAgent");
        ModVersion = STR("0.1.0");
        ModDescription = STR("Live in-game editing bridge for the Abiotic Editor desktop app.");
        ModAuthors = STR("AbioticEditor");
    }

    ~AbioticEditorLiveAgentMod() override
    {
        if (m_server) m_server->Stop();
    }

    auto on_unreal_init() -> void override
    {
        std::string token = LoadOrCreateToken();
        m_server = std::make_unique<LiveAgent::Server>(DefaultPort, token, [](const std::string& line) {
            RC::Output::send<RC::LogLevel::Normal>(STR("[AbioticEditorLiveAgent] {}\n"), RC::to_wstring(line));
        });
        LiveAgent::RegisterVitalsCommands(*m_server);
        m_server->Start();
        RC::Output::send<RC::LogLevel::Normal>(
            STR("[AbioticEditorLiveAgent] Ready on port {}. Token is in AbioticEditorLiveAgent-token.txt "
                "next to this mod.\n"), DefaultPort);
    }

private:
    std::unique_ptr<LiveAgent::Server> m_server;

    static std::string LoadOrCreateToken()
    {
        // Deliberately simple (a file beside the mod, no keychain/registry): this targets a
        // cooperative-host trust model on a game with no anti-cheat (see the repo's live-editing
        // research notes), not a hostile-network threat model. Regenerating is one delete away.
        namespace fs = std::filesystem;
        fs::path tokenPath = fs::path(UE4SSProgram::get_program().get_working_directory())
            / "Mods" / "AbioticEditorLiveAgent" / "token.txt";
        if (fs::exists(tokenPath))
        {
            std::ifstream in(tokenPath);
            std::string token;
            std::getline(in, token);
            if (!token.empty()) return token;
        }
        std::string token = GenerateRandomToken();
        fs::create_directories(tokenPath.parent_path());
        std::ofstream out(tokenPath);
        out << token;
        return token;
    }

    // Uses the OS CSPRNG (not std::mt19937, whose state is recoverable from its own output and
    // so is not fit to generate a bearer secret) - this token is the only thing standing between
    // "the editor" and "anyone on the network who can guess it" being able to write to a live
    // game process, so it needs real cryptographic randomness, not merely well-distributed bytes.
    static std::string GenerateRandomToken()
    {
        static const char alphabet[] = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        constexpr size_t length = 32;
        unsigned char randomBytes[length];
        NTSTATUS status = BCryptGenRandom(
            nullptr, randomBytes, static_cast<ULONG>(length), BCRYPT_USE_SYSTEM_PREFERRED_RNG);
        if (status != 0 /* STATUS_SUCCESS */)
            throw std::runtime_error("BCryptGenRandom failed while generating the live-agent token");

        std::string token(length, '\0');
        for (size_t i = 0; i < length; ++i)
            token[i] = alphabet[randomBytes[i] % (sizeof(alphabet) - 1)];
        return token;
    }
};

#define ABIOTICEDITORLIVEAGENT_API __declspec(dllexport)
extern "C"
{
    ABIOTICEDITORLIVEAGENT_API RC::CppUserModBase* start_mod()
    {
        return new AbioticEditorLiveAgentMod();
    }

    ABIOTICEDITORLIVEAGENT_API void uninstall_mod(RC::CppUserModBase* mod)
    {
        delete mod;
    }
}
