-- AbioticEditorLiveAgentLua: the game-interaction half of live editing's hybrid design (see
-- ../../README.md "The Lua+helper hybrid"). This mod does ALL live UObject property access
-- (public UE4SS Lua API, no build step, no SDK access needed) and talks to
-- AbioticEditorLiveAgentHelper.exe (plain Winsock, no UE4SS dependency) over a pair of files in
-- %LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\ - see ../../Shared/LiveAgentServer.h and
-- AbioticEditorLiveAgentHelper's FileMailbox.h for the other side of this bridge, and
-- docs/reference/live-editing-protocol.md for the wire shapes both ultimately carry.
--
-- STATUS: CONFIRMED WORKING against the real, running game (2026-09-02), all six commands, on a
-- real save with real progression data ("Chrissie", 2h57m played) - not a fake/mocked environment.
-- ping, diag.findplayer, vitals.get, vitals.set, skills.get, and skills.set all round-tripped
-- correctly: vitals.get returned real live values (including CurrentSanity, previously unconfirmed
-- - it returned 100 with no error, so that field name is now confirmed too, closing the one gap
-- called out in the previous round's version of this comment); vitals.set's money and head-health
-- writes were confirmed both via a follow-up vitals.get AND visually in a screenshot (the HUD's
-- head-injury indicator cleared when head health was set to 100); skills.get returned real non-zero
-- XP for all 15 file indices (confirming the FileIndexToLiveSkillId table is fully correct, not
-- just plausible); skills.set's remove-then-add RPC pattern wrote an exact value
-- (51102.9 -> 60000 -> reverted to 51102.9) with every other skill left untouched. All test edits
-- were reverted back to their original values before ending the session, since this ran against a
-- real save, not a disposable fixture.
--
-- One real bug was found and fixed on the way here: `GetMyPlayerController()` is NOT a bare UE4SS
-- global (calling it as one failed immediately with "attempt to call a nil value"). It is the
-- CheatConsoleCommands mod's OWN locally-defined function
-- (AFUtils/BaseUtils/BaseUtils.lua), built on UE4SS's bundled shared `UEHelpers` module
-- (ue4ss/Mods/shared/UEHelpers/UEHelpers.lua, `require("UEHelpers")`, available to any mod). The
-- fix here calls `UEHelpers.GetPlayerController()` directly (FindAllOf + IsPlayerController()
-- checks only, no GetClass()/ForEachProperty, so no game-thread freeze risk) then
-- `.MyPlayerCharacter`, which was already correct. See getMyPlayer() below for the exact fix, and
-- live-agent/README.md "Ground truth from a real mod" / docs/PROGRESS.md round 69 for the full
-- story of how this was found (a real published mod's source, not guessing).
--
-- ADDED (round 71, same day): multiple-player support. `players.list` (real API, confirmed via
-- CheatConsoleCommands' PlayersManager.lua and UEHelpers.lua:159-187 - `UEHelpers.GetAllPlayerStates()`
-- reads AGameStateBase.PlayerArray, a base-engine field, not Abiotic-specific, so this works
-- identically whether this process is hosting or has joined someone else's game) lists every
-- connected player and reports whether THIS process has authority (`HasAuthority()`, confirmed
-- used the same way in that mod's own main.lua:60). Every vitals/skills handler now accepts an
-- optional payload.playerId to target a DIFFERENT connected player instead of always the local
-- one - resolvePlayer() below is the single place that lookup happens. Tested live only against a
-- singleplayer/hosted session (one player, isHost=true) - the multi-player path (a second real
-- client actually joined) has NOT been tested against the real game yet, only reasoned about from
-- the reference mod's source.

local json = require("json")
local UEHelpers = require("UEHelpers")

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

-- ===== Player access (verbatim pattern from CheatConsoleCommands' AFUtils/ObjectsGetter.lua /
-- AFUtils/BaseUtils/BaseUtils.lua) =====
--
-- CORRECTION (re-tested live 2026-09-02): `GetMyPlayerController()` is NOT a bare UE4SS global -
-- it is that mod's OWN locally-defined function (AFUtils/BaseUtils/BaseUtils.lua), built on top
-- of `UEHelpers.GetPlayerController()`, a function from UE4SS's bundled shared `UEHelpers`
-- module (ue4ss/Mods/shared/UEHelpers/UEHelpers.lua, available to any mod via
-- `require("UEHelpers")`). Calling the bare global here failed immediately on the real game with
-- "attempt to call a nil value (global 'GetMyPlayerController')" - this mod has no such global
-- because it never required/defined it. `.MyPlayerCharacter` itself (the property on the
-- controller) was already correct, confirmed again at AFUtils/ObjectsGetter.lua:37.
-- UEHelpers.GetPlayerController() uses FindAllOf("PlayerController")/IsPlayerController() checks
-- only - no GetClass()/ForEachProperty walk, so it carries none of the game-thread freeze risk
-- that API had.

---Returns the live player character (the pawn, not PlayerState) for whoever this mod is running
---as, or nil. This is the default target for every command that does not name a playerId - see
---resolvePlayer below for editing a DIFFERENT connected player.
local function getMyPlayer()
    local controller = UEHelpers.GetPlayerController()
    if not controller or not controller:IsValid() then return nil end
    local player = controller.MyPlayerCharacter
    if not player or not player:IsValid() then return nil end
    return player
end

-- ===== Multiple connected players (verbatim pattern from UE4SS's own bundled UEHelpers module -
-- NOT a mod-local wrapper like GetMyPlayerController turned out to be - confirmed independently
-- by CheatConsoleCommands' PlayersManager.lua using the same underlying field under its own
-- wrapper) =====
--
-- `UEHelpers.GetAllPlayerStates()` reads `AGameStateBase.PlayerArray`, a base-engine replicated
-- property listing every connected player (not just the local one) - real, reachable from a
-- joined client exactly the same as from the host. Each PlayerState exposes `.PlayerNamePrivate`
-- (display name) and `.PawnPrivate` (that player's live character, the same kind of object
-- getMyPlayer() above returns for the local player). `.UniquePlayerID` (confirmed at
-- AFUtils/ObjectsGetter.lua:262-263) is Abiotic's own per-player id, used here as this protocol's
-- stable playerId; if a future build ever lacks it, playerId() falls back to a positional id
-- rather than breaking the whole directory.

---@return table[] # every connected player's PlayerState, PawnPrivate/PlayerNamePrivate readable.
local function allPlayerStates()
    local ok, states = pcall(function() return UEHelpers.GetAllPlayerStates() end)
    if not ok or not states then return {} end
    return states
end

local function playerId(playerState, fallbackIndex)
    local ok, uid = pcall(function() return playerState.UniquePlayerID:ToString() end)
    if ok and uid and uid ~= "" then return uid end
    return "index:" .. tostring(fallbackIndex)
end

local function localPlayerId()
    local controller = UEHelpers.GetPlayerController()
    if not controller or not controller:IsValid() then return nil end
    local state = controller.PlayerState
    if not state or not state:IsValid() then return nil end
    return playerId(state, 0)
end

---Resolves which player character a request targets: payload.playerId when given (matched
---against the same id players.list handed out, any connected player - not just the local one),
---otherwise getMyPlayer() - unchanged default behavior for every command from before player
---selection existed, so vitals/skills callers that never send playerId keep working exactly as
---they did.
local function resolvePlayer(payload)
    if not payload or not payload.playerId then return getMyPlayer() end
    for index, state in ipairs(allPlayerStates()) do
        if state:IsValid() and playerId(state, index - 1) == payload.playerId then
            local pawn = state.PawnPrivate
            if pawn and pawn:IsValid() then return pawn end
            return nil
        end
    end
    return nil
end

local handlers = {}

-- Lists every connected player (name + a stable id) plus whether THIS process currently has
-- authority (see the "Host/client authority" note above handlers["vitals.set"] below) - the UI
-- uses this both to offer a player picker and to show whether edits here are expected to stick.
handlers["players.list"] = function(_, respond)
    runOnGameThread(function()
        local myId = localPlayerId()
        local players = { __forceArray = true }
        for index, state in ipairs(allPlayerStates()) do
            if state:IsValid() then
                local ok, name = pcall(function() return state.PlayerNamePrivate:ToString() end)
                local id = playerId(state, index - 1)
                table.insert(players, {
                    id = id,
                    name = (ok and name and name ~= "") and name or ("Player " .. tostring(index)),
                    isLocal = id == myId,
                })
            end
        end
        -- HasAuthority() is a real per-actor AActor::HasAuthority() call, confirmed used the same
        -- way in CheatConsoleCommands' main.lua:60 to decide whether a direct property write on
        -- THIS specific actor will actually stick (vs. get silently overwritten by replication
        -- from whoever the real host is). Checked on the local player's own pawn, not whichever
        -- player is being viewed - authority is about what THIS process (host or client) can
        -- make stick, independent of which player's data is currently on screen.
        local myPlayer = getMyPlayer()
        local hasAuthority = false
        if myPlayer then
            local ok, result = pcall(function() return myPlayer:HasAuthority() end)
            if ok then hasAuthority = result end
        end
        return { players = players, isHost = hasAuthority }
    end, respond)
end

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
handlers["vitals.get"] = function(payload, respond)
    runOnGameThread(function()
        local myPlayer = resolvePlayer(payload)
        if not myPlayer then error("player not found") end
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
        local myPlayer = resolvePlayer(payload)
        if not myPlayer then error("player not found") end
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
local function getProgressionComponent(payload)
    local targetPlayer = resolvePlayer(payload)
    if not targetPlayer then return nil end
    local component = targetPlayer.CharacterProgressionComponent
    if not component or not component:IsValid() then return nil end
    return component
end

handlers["skills.get"] = function(payload, respond)
    runOnGameThread(function()
        local progressionComponent = getProgressionComponent(payload)
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
-- writers take for a property they cannot find. The rows themselves live under payload.skills (not
-- payload directly) so playerId can sit alongside them in the same object, matching every other
-- command's shape now that player selection exists.
handlers["skills.set"] = function(payload, respond)
    runOnGameThread(function()
        local progressionComponent = getProgressionComponent(payload)
        if not progressionComponent then error("no CharacterProgressionComponent found") end

        local rows = payload.skills or {}
        for i = 1, #rows do
            local row = rows[i]
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

-- ===== NPCs (verbatim pattern from CheatConsoleCommands' CommandsManager.lua:1394-1428, the
-- "killall"/"spawnall" commands) - the first live-editing area that genuinely needs host
-- authority: NPC state is server-owned, so a client's direct writes here would just get
-- overwritten by replication from whoever the real host is. No health or position field is
-- evidenced anywhere in that mod's ~800 lines for NPCs - only IsDead/Invincible/IsDisabled/
-- Faction are ever read or written there, so that is the extent of what this exposes too;
-- guessing further would repeat exactly the mistake this project already got burned by once
-- (GetMyPlayerController).

-- Copied verbatim from AFUtils/BaseUtils/BaseUtils.lua:137-140's IsHost() - the exact check
-- CommandsManager.lua:1397 uses to gate the same kind of NPC edit (CheckHasNoAuthority). This is
-- a world-level authority check (unlike players.list's per-actor HasAuthority(), which answers
-- "can MY OWN pawn's writes stick" - NPCs are nobody's own actor, so the question here is
-- "is this process the host at all", which is what AuthorityGameMode validity answers).
local function isHost()
    local ok, world = pcall(function() return UEHelpers.GetWorld() end)
    if not ok or not world or not world:IsValid() then return false end
    local ok2, gameMode = pcall(function() return world.AuthorityGameMode end)
    return ok2 and gameMode ~= nil and gameMode:IsValid()
end

local function allNpcs()
    local ok, npcs = pcall(function() return FindAllOf("NPC_Base_ParentBP_C") end)
    if not ok or not npcs then return {} end
    return npcs
end

-- GetFullName() is a lightweight, direct UE4SS binding on any UObject - confirmed real and safe
-- at CommandsManager.lua:1857/1869 (called straight on the actor, e.g. `hitActor:GetFullName()`,
-- never through GetClass(), so it carries none of that API's game-thread freeze risk). Used here
-- as this protocol's NPC id: a fresh FindAllOf scan is re-run for every npcs.set (an NPC roster
-- changes constantly - wildlife wanders, things die - so an array index from an earlier
-- npcs.list could easily point at a completely different NPC by the time an edit lands; the full
-- name is stable for the life of that specific object, so re-matching by it is always correct or
-- correctly finds nothing, never silently wrong).
local function npcFullName(npc)
    local ok, fullName = pcall(function() return npc:GetFullName() end)
    if ok and fullName then return tostring(fullName) end
    return nil
end

-- The part of GetFullName() before the first space is the object's class (e.g.
-- "BP_FeralOoze_C") - no friendlier display name is evidenced anywhere for this actor type, so
-- this is what the UI shows rather than inventing one.
local function npcLabel(fullName)
    return fullName and fullName:match("^(%S+)") or "NPC"
end

local function findNpcByFullName(target)
    local npcs = allNpcs()
    for _, npc in ipairs(npcs) do
        if npc:IsValid() and npcFullName(npc) == target then return npc end
    end
    return nil
end

handlers["npcs.list"] = function(_, respond)
    runOnGameThread(function()
        local npcs = allNpcs()
        local result = { __forceArray = true }
        for _, npc in ipairs(npcs) do
            if npc:IsValid() then
                local fullName = npcFullName(npc)
                if fullName then
                    table.insert(result, {
                        id = fullName,
                        label = npcLabel(fullName),
                        isDead = npc.IsDead == true,
                        isDisabled = npc.IsDisabled == true,
                        invincible = npc.Invincible == true,
                        faction = npc.Faction,
                    })
                end
            end
        end
        return { npcs = result, isHost = isHost() }
    end, respond)
end

handlers["npcs.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can edit NPCs") end
        local rows = payload.npcs or {}
        for i = 1, #rows do
            local row = rows[i]
            local npc = row.id and findNpcByFullName(row.id)
            if npc then
                if row.isDead ~= nil and npc.IsDead ~= row.isDead then
                    npc.IsDead = row.isDead
                    -- Mirrors the real mod's kill command: pushes the change out through
                    -- replication/UI instead of leaving it locally-set only (the same pattern
                    -- vitals.set already uses for OnRep_CurrentHealth).
                    pcall(function() npc:OnRep_IsDead() end)
                end
                if row.isDisabled ~= nil then npc.IsDisabled = row.isDisabled end
                if row.invincible ~= nil then npc.Invincible = row.invincible end
                if row.faction ~= nil then npc.Faction = row.faction end
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
