cmake_minimum_required(VERSION 3.20)
project(AbioticEditorLiveAgentHelper CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(SHARED_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../Shared")

# The PRIMARY, buildable-today half of live editing's in-game side: plain Winsock networking, ZERO
# dependency on UE4SS or the game. The other half - actually reading/writing live game state -
# lives in ../AbioticEditorLiveAgentLua (Lua, needs no build step or SDK access at all). See
# ../README.md "The Lua+helper hybrid" for why this split exists.
add_executable(AbioticEditorLiveAgentHelper
    src/main.cpp
    "${SHARED_DIR}/LiveAgentServer.cpp"
)
target_include_directories(AbioticEditorLiveAgentHelper PRIVATE src "${SHARED_DIR}")
target_link_libraries(AbioticEditorLiveAgentHelper PRIVATE ws2_32 bcrypt)
