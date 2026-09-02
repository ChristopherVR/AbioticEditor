-- AbioticEditorLiveAgentLua: the game-interaction half of live editing's hybrid design (see
-- ../../README.md "The Lua+helper hybrid"). This mod does ALL live UObject property access
-- (public UE4SS Lua API, no build step, no SDK access needed) and talks to
-- AbioticEditorLiveAgentHelper.exe (plain Winsock, no UE4SS dependency) over a pair of files in
-- %LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\ - see ../../Shared/LiveAgentServer.h and
-- AbioticEditorLiveAgentHelper's FileMailbox.h for the other side of this bridge, and
-- docs/reference/live-editing-protocol.md for the wire shapes both ultimately carry.
--
-- STATUS: rewritten this round around real, confirmed ground truth pulled from a real published,
-- working UE4SS Lua mod for this exact game (Igromanru's CheatConsoleCommands, already installed
-- in the test environment - see live-agent/README.md "Ground truth from a real mod" for the full
-- source references). The previous version's `FindFirstOf("AbioticCharacterPlayerState")` +
-- `GetClass():ForEachProperty(...)` approach is GONE - that published mod never calls GetClass()
-- for property access anywhere in ~800 lines, and gets the player through
-- `GetMyPlayerController().MyPlayerCharacter` instead of FindFirstOf, which is almost certainly
-- what the previous round's unexplained GetClass() hang was actually about (a wrong/heavy object,
-- not a wrong API). Vitals property names below (CurrentHunger, CurrentHealth_Head, ...) are
-- copied verbatim from that mod's working source, not guessed. The skills file-index <-> live
-- skill-id mapping is newly derived by matching skill names between this repo's own
-- Core/Catalogs/Player/SkillCatalog.cs (file order) and that mod's AFUtils/Enums.lua
-- (CharacterSkills enum) - the two use different, unrelated numbering, confirmed by inspection,
-- not assumed. NOT yet re-tested against a real running game after this rewrite - do that next.

local json = require("json")

local ipcDir = (os.getenv("LOCALAPPDATA") or "") .. "\\AbioticEditorLiveAgent\\ipc"
local requestPath = ipcDir .. "\\request.json"
local responsePath = ipcDir .. "\\response.json"
local responseTempPath = responsePath .. ".tmp"

-- Every call that touches a live UObject MUST run on the game thread - LoopAsync's own callback
-- does not run on it, and calling reflection APIs off-thread deadlocked the whole game in an
-- earlier round (see docs/PROGRESS.md round-67). ExecuteInGameThread is fire-and-forget/async
-- with no synchronous return, so `work` reports its own outcome by calling `respond(result, err)`
-- itself instead of returning a value.
local function runOnGameThread(work, respond)
    ExecuteInGameThread(function()
        local ok, result, err = pcall(work)
        if not ok then
            respond(nil, "handler error: " .. tostring(result))
        else
            respond(result, err)
        end
    end)
end

-- ===== Player access (verbatim pattern from CheatConsoleCommands' PlayersManager.lua /
-- AFUtils/ObjectsGetter.lua - NOT FindFirstOf) =====

---Returns the live player character (the pawn, not PlayerState) for whoever this mod is running
---as, or nil. One process = one local player, matching this project's current scope (a locally
---hosted session, or a dedicated server the operator controls and could extend to
---player-selection later - see docs/reference/live-editing-protocol.md).
local function getMyPlayer()
    local controller = GetMyPlayerController()
    if not controller or not controller:IsValid() then return nil end
    local player = controller.MyPlayerCharacter
    if not player or not player:IsValid() then return nil end
    return player
end

local handlers = {}

-- Touches ZERO UE4SS/game APIs - a safe baseline to confirm the dispatch/mailbox loop itself is
-- healthy before calling anything that reaches into the game. Responds synchronously (no game
-- thread needed for this one).
handlers["ping"] = function(_, respond)
    respond({ pong = true }, nil)
end

-- Calls only GetMyPlayerController()/.MyPlayerCharacter, nothing else - a minimal
-- real-game-touching diagnostic (the live equivalent of the old diag.findplayer).
handlers["diag.findplayer"] = function(_, respond)
    runOnGameThread(function()
        return { found = getMyPlayer() ~= nil }
    end, respond)
end

-- Every field here is a DIRECT, no-suffix property name confirmed against
-- CheatConsoleCommands/Scripts/{AFUtils/AFUtils.lua,Features.lua,CommandsManager.lua} - e.g.
-- `myPlayer.CurrentHealth_Head = 70.0` and `myPlayer.CurrentHunger = myPlayer.MaxHunger` are used
-- there verbatim. No property-name scanning needed for any of these (unlike skills' XP struct
-- field below, which does carry a hash suffix). "sanity" was not found in that mod's source (it
-- has no sanity-related command), so `CurrentSanity` is inferred from the naming pattern the
-- other eleven fields all share, not directly confirmed - the one field in this table still
-- worth double-checking first if it comes back wrong.
handlers["vitals.get"] = function(_, respond)
    runOnGameThread(function()
        local myPlayer = getMyPlayer()
        if not myPlayer then error("no local player found") end
        return {
            hunger = myPlayer.CurrentHunger,
            thirst = myPlayer.CurrentThirst,
            sanity = myPlayer.CurrentSanity,
            fatigue = myPlayer.CurrentFatigue,
            continence = myPlayer.CurrentContinence,
            money = myPlayer.CurrentMoney,
            head = myPlayer.CurrentHealth_Head,
            torso = myPlayer.CurrentHealth_Torso,
            leftArm = myPlayer.CurrentHealth_LeftArm,
            rightArm = myPlayer.CurrentHealth_RightArm,
            leftLeg = myPlayer.CurrentHealth_LeftLeg,
            rightLeg = myPlayer.CurrentHealth_RightLeg,
        }
    end, respond)
end

handlers["vitals.set"] = function(payload, respond)
    runOnGameThread(function()
        local myPlayer = getMyPlayer()
        if not myPlayer then error("no local player found") end
        if payload.hunger ~= nil then myPlayer.CurrentHunger = payload.hunger end
        if payload.thirst ~= nil then myPlayer.CurrentThirst = payload.thirst end
        if payload.sanity ~= nil then myPlayer.CurrentSanity = payload.sanity end
        if payload.fatigue ~= nil then myPlayer.CurrentFatigue = payload.fatigue end
        if payload.continence ~= nil then myPlayer.CurrentContinence = payload.continence end
        if payload.money ~= nil then
            -- Mirrors CommandsManager.lua's money command: the RPC keeps server-authoritative
            -- state and other systems in sync, the direct set makes it visible immediately
            -- locally. Wrapped in pcall because the RPC's exact signature is copied from a
            -- specific mod version and could drift; the direct set alone still mostly works if it
            -- fails, just without the same immediate server-side consistency.
            pcall(function() myPlayer:Request_ModifyMoney(payload.money - myPlayer.CurrentMoney) end)
            myPlayer.CurrentMoney = payload.money
        end
        if payload.head ~= nil then myPlayer.CurrentHealth_Head = payload.head end
        if payload.torso ~= nil then myPlayer.CurrentHealth_Torso = payload.torso end
        if payload.leftArm ~= nil then myPlayer.CurrentHealth_LeftArm = payload.leftArm end
        if payload.rightArm ~= nil then myPlayer.CurrentHealth_RightArm = payload.rightArm end
        if payload.leftLeg ~= nil then myPlayer.CurrentHealth_LeftLeg = payload.leftLeg end
        if payload.rightLeg ~= nil then myPlayer.CurrentHealth_RightLeg = payload.rightLeg end
        -- AFUtils.HealFullAllLimbs calls this after writing CurrentHealth_* directly, to push the
        -- new values out through replication/UI instead of leaving them locally-set only.
        pcall(function() myPlayer:OnRep_CurrentHealth() end)
        return nil
    end, respond)
end

-- ===== Skills (verbatim pattern from CheatConsoleCommands/Scripts/Skills.lua - a completely
-- different shape than first assumed: a KEY/VALUE MAP on CharacterProgressionComponent, keyed by
-- a CharacterSkills enum id, not a plain array on PlayerState) =====

-- File-position (0-based, matches this repo's own Core/Catalogs/Player/SkillCatalog.cs order,
-- which the file editor's UI and tests are already built around) to the live CharacterSkills
-- enum value (from AFUtils/Enums.lua in a real published mod). These are two independent,
-- differently-ordered numbering schemes - built by matching skill NAMES between both real
-- sources, not by any formula (index+1, etc. do NOT hold - confirmed by inspection).
local FileIndexToLiveSkillId = {
    [0] = 1,   -- Sprinting
    [1] = 15,  -- Strength
    [2] = 16,  -- Throwing
    [3] = 4,   -- Sneaking
    [4] = 6,   -- BluntMelee
    [5] = 5,   -- SharpMelee (the live enum spells this "SharpMeele")
    [6] = 2,   -- Accuracy
    [7] = 3,   -- Reloading
    [8] = 14,  -- Fortitude
    [9] = 8,   -- Crafting
    [10] = 9,  -- Construction
    [11] = 10, -- FirstAid
    [12] = 12, -- Cooking
    [13] = 11, -- Agriculture
    [14] = 7,  -- Fishing
}

-- The skill struct's XP field DOES carry a compiler hash suffix (unlike the vitals fields above -
-- it lives inside a UStruct, not directly on the character UClass), confirmed exact in
-- CommandsManager.lua: `skillStruct.CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556`. Hardcoded
-- rather than scanned for (unlike the file-format writers' FindByPrefix discipline) because this
-- exact string is proven working in a real published mod right now, and struct-instance property
-- scanning (as opposed to UObject scanning) has no confirmed-working precedent from that mod to
-- copy - if this breaks on a future game patch, that is the trade-off to revisit then, with a
-- real error message pointing at exactly which field name stopped resolving.
local SKILL_XP_FIELD = "CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556"

---@return userdata? progressionComponent
local function getMyProgressionComponent()
    local myPlayer = getMyPlayer()
    if not myPlayer then return nil end
    local component = myPlayer.CharacterProgressionComponent
    if not component or not component:IsValid() then return nil end
    return component
end

handlers["skills.get"] = function(_, respond)
    runOnGameThread(function()
        local progressionComponent = getMyProgressionComponent()
        if not progressionComponent then error("no CharacterProgressionComponent found") end
        local keys = progressionComponent.CharacterSkills_Keys
        local values = progressionComponent.CharacterSkills_Values

        local result = { __forceArray = true }
        for fileIndex = 0, 14 do
            local liveId = FileIndexToLiveSkillId[fileIndex]
            local xp = 0
            for i = 1, #keys do
                if keys[i] == liveId then
                    local ok, value = pcall(function() return values[i][SKILL_XP_FIELD] end)
                    if ok and value then xp = value end
                    break
                end
            end
            table.insert(result, { index = fileIndex, xp = xp, xpMultiplier = 1 })
        end
        return result
    end, respond)
end

-- Unlike vitals, skill XP is not a direct property write - Skills.lua's AddXp/RemoveXp use
-- server RPCs (Server_AddXPToSkill / Server_RemoveAllXPFromSkill) that go through the game's own
-- validated progression system, so setting an ABSOLUTE xp value (this protocol's contract, see
-- docs/reference/live-editing-protocol.md) means remove-then-add rather than one direct set.
-- xpMultiplier has no confirmed live equivalent (that mod does not implement a per-skill XP-rate
-- feature) - accepted but not applied, same "unknown on this build, do not guess" stance the file
-- writers take for a property they cannot find.
handlers["skills.set"] = function(payload, respond)
    runOnGameThread(function()
        local progressionComponent = getMyProgressionComponent()
        if not progressionComponent then error("no CharacterProgressionComponent found") end

        for i = 1, #payload do
            local row = payload[i]
            local liveId = FileIndexToLiveSkillId[row.index]
            if liveId and row.xp ~= nil then
                progressionComponent:Server_RemoveAllXPFromSkill(liveId)
                local outSuccess = { Success = false }
                progressionComponent:Server_AddXPToSkill(liveId, math.floor(row.xp), true, outSuccess)
            end
        end
        return nil
    end, respond)
end

-- ===== The file-mailbox poll loop =====
-- Atomic publish: write to a temp file, then rename over the real path, so the helper's reader
-- never observes a half-written response (matches FileMailbox::WriteAtomic on the helper side).
-- File I/O itself is fine off the game thread (only the Unreal reflection calls above are not),
-- so this runs directly from the LoopAsync callback like the rest of the polling logic.
local function writeResponseAtomic(text)
    local file = io.open(responseTempPath, "wb")
    if not file then return end
    file:write(text)
    file:close()
    os.remove(responsePath) -- os.rename does not overwrite an existing file on Windows.
    os.rename(responseTempPath, responsePath)
end

local function respondToCurrentRequest(result, err)
    if err then
        writeResponseAtomic(json.encode({ ok = false, error = err }))
    else
        writeResponseAtomic(json.encode({ ok = true, result = result }))
    end
end

local function handleOneRequest()
    local file = io.open(requestPath, "rb")
    if not file then return end
    local text = file:read("*a")
    file:close()
    if not text or text == "" then return end -- Reader raced a writer that has not finished yet.
    os.remove(requestPath)

    local ok, request = pcall(json.decode, text)
    if not ok or not request or not request.cmd then
        writeResponseAtomic(json.encode({ ok = false, error = "malformed request" }))
        return
    end

    local handler = handlers[request.cmd]
    if not handler then
        writeResponseAtomic(json.encode({ ok = false, error = "unknown command '" .. request.cmd .. "'" }))
        return
    end

    -- The handler itself calls respondToCurrentRequest (immediately for "ping", or later via
    -- runOnGameThread's ExecuteInGameThread for anything that touches the game) - this call does
    -- NOT produce the response itself, unlike a plain synchronous-return design.
    local dispatchOk, dispatchErr = pcall(handler, request.payload or {}, respondToCurrentRequest)
    if not dispatchOk then
        -- The handler function itself raised before calling runOnGameThread/respond at all
        -- (a bug in the handler's own setup code, not inside the async game-thread work).
        writeResponseAtomic(json.encode({ ok = false, error = "dispatch error: " .. tostring(dispatchErr) }))
    end
end

-- 50ms polling: fast enough that a player pressing APPLY does not notice the round trip, slow
-- enough not to matter for a file-existence check on every game tick. LoopAsync stops if the
-- callback returns true; this one never does, so it runs for the mod's whole lifetime.
LoopAsync(50, function()
    local ok, err = pcall(handleOneRequest)
    if not ok then
        print("[AbioticEditorLiveAgentLua] poll error: " .. tostring(err) .. "\n")
    end
    return false
end)

print("[AbioticEditorLiveAgentLua] Ready. Polling " .. ipcDir .. " every 50ms.\n")
