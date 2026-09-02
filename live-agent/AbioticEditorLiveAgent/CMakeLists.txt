cmake_minimum_required(VERSION 3.20)
project(AbioticEditorLiveAgent CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(SHARED_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../Shared")

# SECONDARY / CURRENTLY BLOCKED approach: a pure C++ UE4SS mod that reads/writes live UObject
# properties directly. Needs UE4SS's real C++ Mod SDK, which needs access this project does not
# have (see ../README.md "Getting from here"). The PRIMARY, buildable-today approach is
# ../AbioticEditorLiveAgentHelper (native networking only) + ../AbioticEditorLiveAgentLua (the
# actual game interaction, in Lua, no SDK needed at all). This target is kept because the SDK
# access gap may close later, and the C++ approach avoids the helper/Lua hybrid's file-polling
# latency once it is unblocked.
#
# Set UE4SS_SDK_DIR to wherever the matching UE4SS C++ Mod SDK was unpacked (must match the game's
# installed UE4SS.dll, see README.md). Not vendored into this repo: like submodules/CUE4Parse and
# submodules/UeSaveGame, an exact-version native dependency like this is acquired and pinned
# deliberately, not grabbed automatically.
if(DEFINED UE4SS_SDK_DIR)
    add_library(AbioticEditorLiveAgent SHARED
        src/Mod.cpp
        src/VitalsCommands.cpp
        src/SkillsCommands.cpp
        "${SHARED_DIR}/LiveAgentServer.cpp"
    )
    target_include_directories(AbioticEditorLiveAgent PRIVATE src "${SHARED_DIR}" "${UE4SS_SDK_DIR}/include")
    target_link_libraries(AbioticEditorLiveAgent PRIVATE ws2_32 bcrypt "${UE4SS_SDK_DIR}/lib/UE4SS.lib")
    set_target_properties(AbioticEditorLiveAgent PROPERTIES OUTPUT_NAME "AbioticEditorLiveAgent")
else()
    message(STATUS
        "UE4SS_SDK_DIR not set: skipping the pure-C++-mod AbioticEditorLiveAgent target "
        "(blocked on private SDK access anyway - see ../README.md). Use "
        "../AbioticEditorLiveAgentHelper + ../AbioticEditorLiveAgentLua instead.")
endif()

# The standalone protocol test has NO dependency on UE4SS_SDK_DIR - it only exercises
# LiveAgentServer + JsonLine (from ../Shared), so it builds even without the SDK. This is what was
# actually compiled and run during development; see its own file header for how.
add_executable(StandaloneProtocolSmokeTest
    tests/StandaloneProtocolSmokeTest.cpp
    "${SHARED_DIR}/LiveAgentServer.cpp"
)
target_include_directories(StandaloneProtocolSmokeTest PRIVATE "${SHARED_DIR}")
target_link_libraries(StandaloneProtocolSmokeTest PRIVATE ws2_32)
