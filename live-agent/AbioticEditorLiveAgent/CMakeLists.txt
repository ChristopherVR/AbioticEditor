cmake_minimum_required(VERSION 3.20)
project(AbioticEditorLiveAgent CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Set this to wherever the matching UE4SS C++ Mod SDK was unpacked (see README.md for exactly
# which build/SHA that must be - it must match the game's installed UE4SS.dll). Not vendored
# into this repo: like submodules/CUE4Parse and submodules/UeSaveGame, an exact-version native
# dependency like this is acquired and pinned deliberately, not grabbed automatically.
if(NOT DEFINED UE4SS_SDK_DIR)
    message(FATAL_ERROR
        "Set -DUE4SS_SDK_DIR=<path to the UE4SS C++ Mod SDK> matching UE4SS v3.0.1 Beta, "
        "git SHA 01e0a584 (see README.md).")
endif()

add_library(AbioticEditorLiveAgent SHARED
    src/Mod.cpp
    src/VitalsCommands.cpp
    src/SkillsCommands.cpp
    src/LiveAgentServer.cpp
)
target_include_directories(AbioticEditorLiveAgent PRIVATE src "${UE4SS_SDK_DIR}/include")
target_link_libraries(AbioticEditorLiveAgent PRIVATE ws2_32 bcrypt "${UE4SS_SDK_DIR}/lib/UE4SS.lib")
set_target_properties(AbioticEditorLiveAgent PROPERTIES OUTPUT_NAME "AbioticEditorLiveAgent")

# The standalone protocol test has NO dependency on UE4SS_SDK_DIR - it only exercises
# LiveAgentServer + JsonLine, so it builds even before the real SDK is available. This is what
# was actually compiled and run during development; see its own file header for how.
add_executable(StandaloneProtocolSmokeTest
    tests/StandaloneProtocolSmokeTest.cpp
    src/LiveAgentServer.cpp
)
target_include_directories(StandaloneProtocolSmokeTest PRIVATE src)
target_link_libraries(StandaloneProtocolSmokeTest PRIVATE ws2_32)
