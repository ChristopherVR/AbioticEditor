#pragma once
// Registers "skills.get"/"skills.set" against a live Server. Same "not yet verified against the
// real game" status as VitalsCommands.h - see its header comment and ../README.md. Needs the
// real UE4SS SDK to compile; the array-property walk here is a starting guess, not confirmed.

#include "LiveAgentServer.h"

namespace LiveAgent
{
    void RegisterSkillsCommands(Server& server);
}
