#pragma once
// Owns the shared connection secret: this helper generates it once (a real CSPRNG, not a
// general-purpose one - see the commit that fixed the same mistake in the pure-C++-mod approach's
// Mod.cpp) and both this helper (to check "hello") and the Lua mod (to display it, read-only)
// agree on where to find it via %LOCALAPPDATA%, no config file needed to keep them in sync.

#include <fstream>
#include <stdexcept>
#include <string>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")

namespace LiveAgent
{
    inline std::string LocalAppDataDir()
    {
        char buffer[MAX_PATH];
        DWORD length = GetEnvironmentVariableA("LOCALAPPDATA", buffer, MAX_PATH);
        if (length == 0 || length >= MAX_PATH)
            throw std::runtime_error("could not resolve %LOCALAPPDATA%");
        return std::string(buffer, length);
    }

    inline std::string GenerateRandomToken()
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

    // Returns the token at `path`, creating it (and its parent directory) with a fresh
    // CSPRNG-generated value if it does not exist yet.
    inline std::string LoadOrCreateToken(const std::string& dir, const std::string& fileName = "token.txt")
    {
        CreateDirectoryA(dir.c_str(), nullptr);
        auto path = dir + "\\" + fileName;

        if (std::ifstream in(path, std::ios::binary); in)
        {
            std::string token;
            std::getline(in, token);
            if (!token.empty()) return token;
        }

        auto token = GenerateRandomToken();
        std::ofstream out(path, std::ios::binary | std::ios::trunc);
        if (!out) throw std::runtime_error("could not write " + path);
        out << token;
        return token;
    }
}
