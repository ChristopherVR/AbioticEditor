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

---True when `player` is a genuine in-world player character with a loaded save behind it, not a
---main-menu/default pawn. At the main menu the controller can already have a valid
---MyPlayerCharacter, so getMyPlayer() alone is not proof anything real is loaded - vitals.get used
---to trust it anyway and hand back plausible-looking default numbers (0/nil fields on a menu
---pawn), which the C# side then read as "connected" only to fail moments later once it also asked
---for skills. CharacterProgressionComponent is the same object skills.get already requires (see
---getProgressionComponent below), so checking it here too means both commands agree on what
---"ready" means instead of only one of them noticing the game has no world loaded yet.
local function hasLoadedWorldState(player)
    if not player or not player:IsValid() then return false end
    local ok, component = pcall(function() return player.CharacterProgressionComponent end)
    return ok and component ~= nil and component:IsValid()
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

-- ===== Per-player region (round 80: which area of the map each connected player is standing
-- in, for the sidebar's "who's where" display - previously only ever answerable for the LOCAL
-- player, since UEHelpers.GetPlayerController() (used by world.info/spawn.get's levelName) has
-- no "for a different connected player" form) =====
--
-- This file's own "Player access" comment above already established that
-- UEHelpers.GetPlayerController() "uses FindAllOf('PlayerController')/IsPlayerController() checks
-- only" and then picks out the local one - the underlying FindAllOf call is proven, real, and
-- already running on every single command that resolves the local player. What's new here is
-- simply NOT filtering that list down to one: calling FindAllOf("PlayerController") directly and
-- walking every result, matching each back to a connected player via its own PlayerState
-- back-reference (the same UniquePlayerID players.list already keys everyone by).
--
-- Unproven for a joined CLIENT: standard Unreal networking only replicates a PlayerController to
-- its OWNING connection, so a client's own game process most likely only ever finds its own
-- controller in this list, not other players' - the same "client sees less than host" limitation
-- already documented for several other commands in this mod (see vitals.set's own comment). A
-- controller this process cannot see just means that player's region comes back nil below rather
-- than a guess.
local function allPlayerControllers()
    local ok, list = pcall(function() return FindAllOf("PlayerController") end)
    if not ok or not list then return {} end
    return list
end

---The ActiveLevelName of whichever controller in `controllers` belongs to the player with id
---`targetId`, or nil when no controller could be matched (not visible to this process, or the
---match failed) - never a guess.
local function regionForController(controllers, targetId)
    for _, controller in ipairs(controllers) do
        if controller:IsValid() then
            local ok, state = pcall(function() return controller.PlayerState end)
            if ok and state and state:IsValid() and playerId(state, -1) == targetId then
                local okLevel, level = pcall(function() return controller.ActiveLevelName:ToString() end)
                if okLevel and level and level ~= "" then return level end
                return nil
            end
        end
    end
    return nil
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
-- Round 80: also reports each player's own region (see regionForController's own comment above
-- for what this can and cannot see), so the sidebar can show every occupied area, not only
-- wherever the LOCAL player happens to be standing.
handlers["players.list"] = function(_, respond)
    runOnGameThread(function()
        local myId = localPlayerId()
        local controllers = allPlayerControllers()
        local players = { __forceArray = true }
        for index, state in ipairs(allPlayerStates()) do
            if state:IsValid() then
                local ok, name = pcall(function() return state.PlayerNamePrivate:ToString() end)
                local id = playerId(state, index - 1)
                table.insert(players, {
                    id = id,
                    name = (ok and name and name ~= "") and name or ("Player " .. tostring(index)),
                    isLocal = id == myId,
                    region = regionForController(controllers, id),
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
        -- "Is a world loaded" is a question about THIS game's own state, so it is the local
        -- character that gets checked - not the resolved target, which may be a joined guest
        -- whose pawn is a lighter replicated proxy on this machine.
        if not hasLoadedWorldState(getMyPlayer()) then error("no world loaded") end
        local myPlayer = resolvePlayer(payload)
        if not myPlayer then error("player not found") end
        local result = {
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
        -- A transient nil read (the pawn's health component not fully replicated/initialized
        -- yet, e.g. right after a world finishes loading) must fail loudly rather than silently
        -- go missing: Lua drops a table key entirely when its value is nil, and the .NET side's
        -- VitalsWire record has no default for these fields, so a missing key used to silently
        -- deserialize to 0.0 - "body health shows as 0 even though we're full" was this exact
        -- shape (a genuinely full Torso momentarily read as nil, not a real 0).
        for key, value in pairs(result) do
            if type(value) ~= "number" then
                error("could not read " .. key .. " right now (try again)")
            end
        end
        return result
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

-- keys[i] can surface as a plain Lua number OR as a UE4SS enum-wrapper value depending on how
-- this specific TMap<CharacterSkills, ...> property gets bound (this is the one enum-KEYED TMap
-- read anywhere in this mod - every other keyed lookup here is by FName/string, see
-- getProgressionComponent's callers). A wrapper value never equals a plain Lua number with `==`,
-- which would make every comparison below fail silently and read back as "every skill has 0 XP" -
-- exactly a locked-forever skills tab, with no error to point at why. tostring() a real UE4SS
-- enum wrapper renders as "EnumName::Value" or the bare number depending on binding version, so
-- comparing tonumber(tostring(x)) first (falls back to the raw value when that fails to parse)
-- matches a plain integer either way without needing to know which shape this build returns.
local function skillKeyToNumber(key)
    return tonumber(tostring(key)) or key
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
                if skillKeyToNumber(keys[i]) == liveId then
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
--
-- IMPORTANT: only send rows for skills that actually changed. Remove-then-add briefly zeroes a
-- skill before restoring it, and the game's own level-up popup fires on that restore regardless
-- of whether the net XP moved - so resending every skill on every save (the editor used to do
-- this) re-triggers that popup for every UNTOUCHED skill too. Fishing sits last in the file's
-- positional order (Core/Catalogs/Player/SkillCatalog.cs's CanonicalOrder), so its redundant
-- restore was always the last one applied and its popup was the one left on screen - read by a
-- player as "editing any skill says Fishing unlocked" even though the skill they actually edited
-- was written correctly underneath. LivePlayerSkillsSession.SaveAsync now filters to dirty rows
-- before calling skills.set, so this handler no longer needs to guard against it itself, but keep
-- it that way rather than reintroducing a full-list resend here.
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
            -- pcall per NPC (matching containers.list's own per-container guard, see that
            -- handler's comment): this walks every wildlife/monster actor loaded in the world, and
            -- IsDead/IsDisabled/Invincible/Faction below are read directly with no guard - one
            -- actor in an unusual state (mid-death, mid-despawn, a modded/DLC creature) used to
            -- raise an uncaught Lua error that failed this ENTIRE request, which the editor could
            -- only show as "not available" for the whole CREATURES tab. A bad NPC is skipped
            -- instead now.
            pcall(function()
                if not npc:IsValid() then return end
                local fullName = npcFullName(npc)
                if not fullName then return end
                table.insert(result, {
                    id = fullName,
                    label = npcLabel(fullName),
                    isDead = npc.IsDead == true,
                    isDisabled = npc.IsDisabled == true,
                    invincible = npc.Invincible == true,
                    faction = npc.Faction,
                })
            end)
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

-- ===== Player inventory (backpack/equip/hotbar) - verbatim pattern from CheatConsoleCommands'
-- AFUtils/ObjectsGetter.lua:60-86 (GetMyInventoryComponent/GetMyEquipmentInventory/
-- GetMyHotbarInventory, real getters returning CharacterInventory/CharacterEquipSlotInventory/
-- CharacterHotbarInventory - each a UAbiotic_InventoryComponent_C with a .CurrentInventory array
-- of FAbiotic_InventoryItemSlotStruct) and AFUtils/AFUtils.lua:682-695 (SetItemSlot, the exact
-- hash-suffixed field names below).
--
-- HONESTLY WEAKER EVIDENCE THAN EVERY OTHER AREA IN THIS FILE, worth flagging plainly: grepping
-- every installed mod found SetItemSlot/AddToItemStack/GetMyInventoryComponent are real, defined
-- functions with real hash-suffixed field names, but they are never actually CALLED by any
-- shipped, ENABLED command in the reference mod - the only two call sites
-- (CommandsManager.lua:1488, Features.lua:925) are both commented out, and both are about slot
-- COUNT, not slot content. This is real source, not a guessed API - the field names are exact,
-- hash-suffixed matches against the same struct the real GETTERS above return - but it has not
-- been exercised by any live gameplay test this session has evidence of, unlike vitals/skills/NPCs
-- which all have at least one real, currently-active command doing the same write. Built and
-- tested live anyway since the write shape (direct field assignment, same pattern vitals/NPCs
-- already use) carries low blast-radius risk to verify empirically - but if this behaves
-- unexpectedly, this comment is where to look first.
--
-- Inventory writes call OnRep_CurrentInventory once per changed inventory. The installed
-- game's Blueprint routes this through DelayedInventoryUpdate and InventoryUpdated,
-- including equipment callbacks. Multiplayer propagation still needs gameplay verification.
-- Empty is the game's empty-slot sentinel. Resolve the table before using FNAME_Find
-- for a replacement row, since loading an asset can introduce previously unknown names.

-- Ordered (not a plain hash-iterated table) so inventory.list's output is stable across calls.
-- "transmog" reads/writes the same Abiotic_InventoryComponent_C slot structs as the other three
-- kinds, over TmogInventory (confirmed in the blueprint class dump) - the web editor's
-- LiveInventorySession sends this kind for a transmog slot the same way it does backpack/equip/
-- hotbar, so no new command pair was needed for it (see docs/reference/live-editing-protocol.md).
local INVENTORY_KINDS = { "backpack", "equip", "hotbar", "transmog" }
local INVENTORY_PROPERTY_BY_KIND = {
    backpack = "CharacterInventory",
    equip = "CharacterEquipSlotInventory",
    hotbar = "CharacterHotbarInventory",
    transmog = "TmogInventory",
}

local function inventoryComponent(player, kind)
    local propName = INVENTORY_PROPERTY_BY_KIND[kind]
    if not propName then return nil end
    local ok, inv = pcall(function() return player[propName] end)
    if not ok or not inv or not inv:IsValid() then return nil end
    return inv
end

local function slotRowName(slot)
    local ok, rowName = pcall(function()
        return slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName:ToString()
    end)
    if ok and rowName then return rowName end
    return ""
end

-- FDataTableRowHandle needs BOTH its UObject table and its row name. Keeping the old
-- table (often ItemTable_Pickups on an empty slot) makes a newly assigned item invisible.
local ITEM_TABLE_GLOBAL = "/Game/Blueprints/Items/ItemTable_Global.ItemTable_Global"
local function isDataTable(value)
    local ok, valid = pcall(function() return value and value:IsValid() and value:IsA("/Script/Engine.DataTable") end)
    return ok and valid
end

-- Resolves any DataTable row by asset path + row name, with the LoadAsset fallback a not-yet-
-- loaded table needs. Shared with areas/care.lua (via ctx) for setting a garden plot's crop,
-- which needs the exact same "find or load, then verify the row really exists" behavior.
local function resolveDataTableRow(path, itemId)
    local library = StaticFindObject("/Script/Engine.Default__DataTableFunctionLibrary")
    if not library or not library:IsValid() then error("cannot validate item data on this game build") end
    if type(path) ~= "string" or path:sub(1, 1) ~= "/" then error("invalid item DataTable path") end
    local dataTable = StaticFindObject(path)
    if not isDataTable(dataTable) then
        -- LoadAsset is a documented UE4SS global; this helper only runs on the game thread.
        -- Find the object again because LoadAsset's return value varies between UE4SS builds.
        if type(LoadAsset) == "function" then pcall(function() LoadAsset(path) end) end
        dataTable = StaticFindObject(path)
    end
    if not isDataTable(dataTable) then
        error("item DataTable is unavailable: " .. path .. ". Check matching game data in Settings.")
    end
    -- Loading the table interns its names. FNAME_Find before loading can return None.
    local name = FName(itemId, EFindName.FNAME_Find)
    local ok, exists = pcall(function() return library:DoesDataTableRowExist(dataTable, name) end)
    if not ok then error("cannot validate the item's DataTable on this game build") end
    if name:ToString() == "None" or exists ~= true then
        error("item '" .. itemId .. "' was not found in " .. path .. ". Reload game data in Settings.")
    end
    return dataTable, name
end

local function resolveItemHandle(slot, row)
    local library = StaticFindObject("/Script/Engine.Default__DataTableFunctionLibrary")
    if not library or not library:IsValid() then error("cannot validate item data on this game build") end
    local function hasRow(dataTable, name)
        if not isDataTable(dataTable) then return false end
        local ok, exists = pcall(function() return library:DoesDataTableRowExist(dataTable, name) end)
        if not ok then error("cannot validate the item's DataTable on this game build") end
        return exists == true
    end
    local handle = slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B
    local name = FName(row.itemId, EFindName.FNAME_Find)
    -- Preserve a working instance table for a same-item edit, including mod overrides.
    if not (row.details and row.details.instanceMetadata and row.details.instanceMetadata.itemDataTable)
        and slotRowName(slot) == row.itemId and hasRow(handle.DataTable, name) then
        return handle.DataTable, name
    end
    return resolveDataTableRow(row.dataTable or ITEM_TABLE_GLOBAL, row.itemId)
end

-- Field names and types are verified against Abiotic_InventoryChangeableDataStruct's
-- exported schema. Use UEnum values rather than assuming NewEnumerator suffixes are ordinals.
local LIQUID_FIELD = "CurrentLiquid_19_3E1652F448223AAE5F405FB510838109"
local LEVEL_FIELD = "LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A"
local TEXT_FIELD = "PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9"
local ASSET_FIELD = "AssetID_25_06DB7A12469849D19D5FC3BA6BEDEEAB"
local STATE_FIELD = "DynamicState_39_7597AC6549E292B931C61BB13C9E42EB"
local VARIANT_FIELD = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636"
local VARIANT_TABLE = "/Game/Blueprints/DataTables/Customization/DT_TextureVariants.DT_TextureVariants"
local function textValue(value)
    if value == nil then return nil end
    if type(value) == "string" then return value end
    return value:ToString()
end
local function liquidEnum()
    local enum = StaticFindObject("/Game/Blueprints/Data/E_LiquidType.E_LiquidType")
    if not enum or not enum:IsValid() then error("liquid enum is unavailable on this game build") end
    return enum
end
local itemMetadata = require("item_metadata")
-- skipMetadata (round 79): itemMetadata.read() walks the DynamicProperties array (a fresh
-- StaticFindObject enum lookup PER ENTRY, no caching) plus the GameplayTags/ParentTags arrays (an
-- FName:ToString() per entry) - real, compounding cost for every NON-EMPTY slot it touches.
-- containers.list can be asked to do this for every slot of every placed container/bench in the
-- whole world in one call; on a well-built base that was slow enough to freeze the game and blow
-- past the live-agent helper's own 5-second response timeout outright (reported live: the
-- CONTAINERS tab permanently "not available" even after the empty-slot skip above). A plain
-- field edit (writeSlot with no `details`) never touches existing metadata either way - see
-- applyItemDetails's own `if not prepared then return end` guard - so omitting it here from a
-- LIST response cannot lose data, only the "this item has a coating/pet XP" read for BROWSING a
-- container; inventory.list (one player's own ~50 slots, inherently bounded) keeps full detail.
local function readItemDetails(data, slot, skipMetadata)
    if not data then return nil end
    local liquid = data[LIQUID_FIELD]
    local liquidName = liquid ~= nil and liquidEnum():GetNameByValue(liquid):ToString() or nil
    local variant = data[VARIANT_FIELD]
    -- Not "skipMetadata and nil or itemMetadata.read(...)": that classic Lua ternary idiom
    -- breaks the moment the "true" branch is nil/false, since `nil` is itself falsy and the
    -- `or` then always falls through to the right-hand side regardless of skipMetadata - it was
    -- silently reading metadata every time despite this flag (caught by the Lua test suite).
    local metadata = nil
    if not skipMetadata then metadata = itemMetadata.read(data, slot) end
    return { liquidLevel = data[LEVEL_FIELD] or 0, liquidType = liquidName,
        dynamicState = data[STATE_FIELD] == true, playerMadeString = textValue(data[TEXT_FIELD]),
        assetId = textValue(data[ASSET_FIELD]), variantRowName = variant and textValue(variant.RowName) or nil,
        instanceMetadata = metadata }
end
local function prepareItemDetails(data, details)
    if details == nil then return nil end
    if type(details) ~= "table" then error("invalid item details") end
    local result = { details = details, metadata = itemMetadata.prepare(data, details.instanceMetadata) }
    if details.liquidLevel ~= nil and (type(details.liquidLevel) ~= "number" or details.liquidLevel < -1
        or details.liquidLevel > 2147483647 or details.liquidLevel % 1 ~= 0) then error("invalid liquid level") end
    if details.dynamicState ~= nil and type(details.dynamicState) ~= "boolean" then error("invalid item state") end
    for _, field in ipairs({ "playerMadeString", "assetId", "variantRowName", "liquidType" }) do
        if details[field] ~= nil and type(details[field]) ~= "string" then error("invalid " .. field) end
    end
    if details.liquidType ~= nil then
        liquidEnum():ForEachName(function(name, value)
            if name:ToString() == details.liquidType then result.liquid = value; return true end
        end)
        if result.liquid == nil then error("unknown liquid type: " .. details.liquidType) end
    end
    if details.variantRowName ~= nil then
        if not data[VARIANT_FIELD] then error("texture variant field is unavailable") end
        if details.variantRowName == "" or details.variantRowName == "None" then
            result.variantName = FName("None", EFindName.FNAME_Find)
        else
            local variantPath = details.instanceMetadata and details.instanceMetadata.variantDataTable or VARIANT_TABLE
            local tableObject = StaticFindObject(variantPath)
            if not isDataTable(tableObject) and type(LoadAsset) == "function" then
                LoadAsset(variantPath); tableObject = StaticFindObject(variantPath)
            end
            if not isDataTable(tableObject) then error("texture variant table is unavailable") end
            local name = FName(details.variantRowName, EFindName.FNAME_Find)
            local library = StaticFindObject("/Script/Engine.Default__DataTableFunctionLibrary")
            if not library or not library:IsValid() or not library:DoesDataTableRowExist(tableObject, name) then
                error("unknown texture variant: " .. details.variantRowName)
            end
            result.variantTable, result.variantName = tableObject, name
        end
    end
    return result
end
local function applyItemDetails(data, prepared)
    if not prepared then return end
    itemMetadata.apply(data, prepared.metadata)
    local d = prepared.details
    if d.liquidLevel ~= nil then data[LEVEL_FIELD] = d.liquidLevel end
    if prepared.liquid ~= nil then data[LIQUID_FIELD] = prepared.liquid end
    if d.dynamicState ~= nil then data[STATE_FIELD] = d.dynamicState end
    if d.playerMadeString ~= nil then data[TEXT_FIELD] = d.playerMadeString end
    if d.assetId ~= nil then data[ASSET_FIELD] = d.assetId end
    if prepared.variantName then
        if prepared.variantTable then data[VARIANT_FIELD].DataTable = prepared.variantTable end
        data[VARIANT_FIELD].RowName = prepared.variantName
    end
end

local function prepareSlotWrite(slot, row)
    local prepared = { slot = slot, row = row }
    if row.ammoInMagazine ~= nil and (type(row.ammoInMagazine) ~= "number"
        or row.ammoInMagazine < 0 or row.ammoInMagazine > 2147483647 or row.ammoInMagazine % 1 ~= 0) then
        error("ammo in magazine must be a non-negative integer")
    end
    if not slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B
        or not slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 then
        error("item slot data is unavailable")
    end
    if not row.clear and row.itemId ~= nil and row.itemId ~= "" then
        if type(row.itemId) ~= "string" or row.itemId == "Empty" or row.itemId == "None" then
            error("use clear to empty an item slot")
        end
        prepared.dataTable, prepared.name = resolveItemHandle(slot, row)
    end
    if row.clear and slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313[LIQUID_FIELD] ~= nil then
        prepared.defaultLiquid = prepareItemDetails(slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313, { liquidType = "E_LiquidType::NewEnumerator0" }).liquid
    end
    if not row.clear then prepared.details = prepareItemDetails(slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313, row.details) end
    return prepared
end

local function applySlotWrite(prepared)
    local slot, row = prepared.slot, prepared.row
    local handle = slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B
    local data = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
    if row.clear then
        itemMetadata.clear(data)
        handle.RowName = FName("Empty", EFindName.FNAME_Find)
        data.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0
        data.CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D = 0
        data.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0
        data.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0
        data[LEVEL_FIELD] = -1
        if prepared.defaultLiquid ~= nil then data[LIQUID_FIELD] = prepared.defaultLiquid end
        data[TEXT_FIELD] = ""
        data[ASSET_FIELD] = "-1"
        data[STATE_FIELD] = false
        if data[VARIANT_FIELD] then data[VARIANT_FIELD].RowName = FName("None", EFindName.FNAME_Find) end
        return
    end
    if prepared.name then
        handle.DataTable = prepared.dataTable
        handle.RowName = prepared.name
    end
    if row.stack ~= nil then data.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = row.stack end
    if row.ammoInMagazine ~= nil then
        data.CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D = row.ammoInMagazine
    end
    if row.durability ~= nil then data.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = row.durability end
    if row.maxDurability ~= nil then data.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = row.maxDurability end
    applyItemDetails(data, prepared.details)
end

local function writeSlot(slot, row)
    applySlotWrite(prepareSlotWrite(slot, row))
end

handlers["inventory.list"] = function(payload, respond)
    runOnGameThread(function()
        local player = resolvePlayer(payload)
        if not player then error("player not found") end

        local result = { __forceArray = true }
        for _, kind in ipairs(INVENTORY_KINDS) do
            local inv = inventoryComponent(player, kind)
            if inv and inv.CurrentInventory then
                for i = 1, #inv.CurrentInventory do
                    local slot = inv.CurrentInventory[i]
                    local rowName = slotRowName(slot)
                    local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
                    -- "Empty" (confirmed real, capitalized) is this game's own empty-slot
                    -- sentinel. Accept None as well for uninitialized slots.
                    local isEmpty = rowName == "" or rowName == "Empty" or rowName == "None"
                    -- Skipped for an empty slot: there is nothing for it to report (no tags,
                    -- no dynamic properties), and this walks several TArrays with a native
                    -- call per entry - real cost per slot, and most inventories/containers are
                    -- mostly empty slots. A big base's CONTAINERS listing paying that cost on
                    -- every empty slot in every container was a real contributor to the
                    -- multi-second freeze (and outright timeout) reported live. NOT
                    -- "isEmpty and nil or readItemDetails(...)": that classic Lua ternary idiom
                    -- breaks the moment the "true" branch is nil - `nil` is itself falsy, so the
                    -- `or` always falls through to readItemDetails regardless of isEmpty, which
                    -- silently read it for every slot anyway (caught by the Lua test suite).
                    local details = nil
                    if not isEmpty then details = readItemDetails(changeableData, slot) end
                    table.insert(result, {
                        kind = kind,
                        slotIndex = i - 1,
                        itemId = rowName,
                        isEmpty = isEmpty,
                        stack = changeableData and changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 or 0,
                        durability = changeableData and changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 or 0,
                        maxDurability = changeableData and changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B or 0,
                        ammoInMagazine = changeableData and changeableData.CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D or 0,
                        details = details,
                    })
                end
            end
        end
        return result
    end, respond)
end

-- Not host-gated (unlike npcs.set): an inventory component belongs to a specific player's own
-- pawn, the same "player-owned data" category vitals.set already writes without an isHost() check
-- - a client editing their OWN connected player's inventory has authority over their own pawn the
-- same way vitals.set already relies on; editing a DIFFERENT player's inventory as a non-host
-- client carries the same known limitation vitals.set already has (the write may not stick,
-- silently, same as it already can for vitals).
handlers["inventory.set"] = function(payload, respond)
    runOnGameThread(function()
        local player = resolvePlayer(payload)
        if not player then error("player not found") end

        local rows = payload.edits or {}
        -- Every inventory component actually touched below, so it can be pushed out through
        -- replication/UI once after the loop - see the OnRep_CurrentInventory call below.
        local touchedInventories, prepared = {}, {}
        local replication = require("replication")
        local helper
        for _, row in ipairs(rows) do
            if row.details then helper = replication.requireHelper(); break end
        end
        -- Resolve every slot/table before changing any slot in a multi-item operation.
        for i = 1, #rows do
            local row = rows[i]
            local inv = row.kind and inventoryComponent(player, row.kind)
            if type(row.slotIndex) ~= "number" or row.slotIndex < 0 or row.slotIndex % 1 ~= 0 then
                error("invalid inventory slot index")
            end
            local slot = inv and inv.CurrentInventory and inv.CurrentInventory[row.slotIndex + 1]
            if not slot then error("inventory slot is unavailable; refresh and retry") end
            prepared[i] = prepareSlotWrite(slot, row)
            touchedInventories[inv] = inv
        end
        for i = 1, #prepared do applySlotWrite(prepared[i]) end
        -- Keep the existing game inventory/equipment update path once per component.
        for inv in pairs(touchedInventories) do
            if helper then replication.mark(helper, inv, "CurrentInventory") end
            pcall(function() inv:OnRep_CurrentInventory() end)
        end
        return nil
    end, respond)
end

-- ===== Shared world helpers for the areas below =====
-- Separate command names make older agents reject rich edits instead of silently
-- ignoring the new details object.
handlers["inventory.setfull"] = handlers["inventory.set"]
handlers["inventory.setcomplete"] = handlers["inventory.set"]
--
-- ADDED (round 75, 2026-09-06): world clock + weather, quest/story flags, doors, world
-- containers, and dropped items. Every UObject/UFunction name below was taken from the game's
-- OWN class layouts (blueprint property/function lists and native-class signatures pulled from
-- the installed paks/usmap and the shipped PDB by tests/AbioticEditor.Probes/
-- LiveClassPropsProbe.cs), cross-checked against CheatConsoleCommands where that mod touches the
-- same object - not guessed from naming patterns. Where a call has NO precedent in a working mod
-- it says so at the call site and is wrapped in pcall so one wrong signature degrades that one
-- field, never the whole command.

-- FVector -> plain numbers. K2_GetActorLocation is used verbatim at AFUtils.lua:641 and
-- LocationsManager.lua:81 in the reference mod.
local function actorLocation(actor)
    local ok, loc = pcall(function() return actor:K2_GetActorLocation() end)
    if ok and loc then
        local okx, x, y, z = pcall(function() return loc.X, loc.Y, loc.Z end)
        if okx then return x, y, z end
    end
    return 0, 0, 0
end

local function fullName(obj)
    local ok, name = pcall(function() return obj:GetFullName() end)
    if ok and name then return tostring(name) end
    return nil
end

local function classLabel(name)
    return name and name:match("^(%S+)") or "?"
end

local function findAll(className)
    local ok, list = pcall(function() return FindAllOf(className) end)
    if not ok or not list then return {} end
    return list
end

local function findByFullName(className, target)
    for _, obj in ipairs(findAll(className)) do
        if obj:IsValid() and fullName(obj) == target then return obj end
    end
    return nil
end

-- Reads the FName elements of an out-param TArray<FName> the way AFUtils.lua:553-567 /
-- WeatherManager.lua:22-25 read GetAllWeatherEventRowNames/RowHandles: each element is a
-- LocalUnrealParam whose :get() is the value.
local function outNames(fill)
    local out = {}
    local ok = pcall(fill, out)
    local names = {}
    if not ok then return names end
    for i = 1, #out do
        local okName, name = pcall(function() return out[i]:get():ToString() end)
        if okName and name then table.insert(names, name) end
    end
    return names
end

-- ===== World clock + weather (DayNightManager_C) =====
-- Fields/functions confirmed on the blueprint class itself: CurrentTimeInSeconds, CurrentDay,
-- IsNight, CurrentWeatherEvent (FName), DayNightManuallyPaused, RequiredDaysBetweenWeather,
-- Weather_RequestByPlayer, TriggerWeatherEvent(EventRow), IsCurrentlyDaytime(),
-- OnRep_CurrentTimeInSeconds, OnRep_IsNight, OnRep_CurrentDay. The time/weather writes below
-- are the reference mod's own settime / setweather / setnextweather commands
-- (AFUtils.lua:546-593, 715-729; CommandsManager.lua:1243-1370), which are host-only there too.

local function dayNightManager()
    local ok, manager = pcall(function() return FindFirstOf("DayNightManager_C") end)
    if ok and manager and manager:IsValid() then return manager end
    return nil
end

local function weatherLibrary()
    local ok, lib = pcall(function()
        return StaticFindObject("/Script/AbioticFactor.Default__WeatherEventHandleFunctionLibrary")
    end)
    if ok and lib and lib:IsValid() then return lib end
    return nil
end

local function weatherRowHandleToTable(handle)
    return { RowName = handle.RowName, DataTablePath = handle.DataTablePath }
end

local function triggerWeather(manager, eventName)
    local lib = weatherLibrary()
    if not lib then error("weather library not available") end
    local out = {}
    lib:GetAllWeatherEventRowHandles(out)
    if #out == 0 then error("no weather events found") end
    if eventName == "None" then
        local handle = out[1]:get()
        handle.RowName = NAME_None
        manager:TriggerWeatherEvent(weatherRowHandleToTable(handle))
        return
    end
    for i = 1, #out do
        local handle = out[i]:get()
        if handle.RowName:ToString() == eventName then
            manager:TriggerWeatherEvent(weatherRowHandleToTable(handle))
            return
        end
    end
    error("unknown weather event " .. tostring(eventName))
end

handlers["world.get"] = function(_, respond)
    runOnGameThread(function()
        local manager = dayNightManager()
        if not manager then error("the world clock is not loaded (are you in a world?)") end
        local weatherNames = {}
        local lib = weatherLibrary()
        if lib then
            weatherNames = outNames(function(out) lib:GetAllWeatherEventRowNames(out) end)
        end
        local weatherOptions = { __forceArray = true, "None" }
        for _, name in ipairs(weatherNames) do
            if name ~= "None" then table.insert(weatherOptions, name) end
        end
        local okWeather, currentWeather = pcall(function() return manager.CurrentWeatherEvent:ToString() end)
        local okPaused, paused = pcall(function() return manager.DayNightManuallyPaused == true end)
        return {
            isHost = isHost(),
            day = manager.CurrentDay,
            timeSeconds = manager.CurrentTimeInSeconds,
            isNight = manager.IsNight == true,
            paused = okPaused and paused or false,
            currentWeather = (okWeather and currentWeather) or "None",
            weatherOptions = weatherOptions,
        }
    end, respond)
end

handlers["world.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can change the world clock or weather") end
        local manager = dayNightManager()
        if not manager then error("the world clock is not loaded (are you in a world?)") end
        if payload.timeSeconds ~= nil then
            -- AFUtils.SetGameTime, minus its "+10 seconds" nudge (the editor sends the exact
            -- second it wants). OnRep pushes it to clients/UI; the IsNight recompute mirrors
            -- AFUtils.CalculateAndSetDaytime but asks the manager's own IsCurrentlyDaytime()
            -- instead of re-deriving the hour thresholds (which that mod hardcodes).
            manager.CurrentTimeInSeconds = payload.timeSeconds
            pcall(function() manager:OnRep_CurrentTimeInSeconds() end)
            pcall(function()
                local isDay = manager:IsCurrentlyDaytime()
                if manager.IsNight == isDay then
                    manager.IsNight = not isDay
                    manager:OnRep_IsNight()
                end
            end)
        end
        if payload.day ~= nil then
            manager.CurrentDay = payload.day
            pcall(function() manager:OnRep_CurrentDay() end)
        end
        if payload.weather ~= nil and payload.weather ~= "" then
            triggerWeather(manager, payload.weather)
        end
        if payload.nextWeather ~= nil and payload.nextWeather ~= "" then
            -- AFUtils.SetNextWeatherEvent verbatim.
            manager.RequiredDaysBetweenWeather = 0
            manager.Weather_RequestByPlayer.RowName = FName(payload.nextWeather, EFindName.FNAME_Find)
        end
        return nil
    end, respond)
end

-- Which region of the world is currently loaded (round 78) - the live counterpart of picking a
-- WorldSave_<Region>.sav file offline, so the desktop app's live sidebar can show every region of
-- this world (disabled) alongside the one actually loaded right now. Reuses the exact same
-- evidenced read spawn.get already uses (the local controller's own ActiveLevelName - a
-- display-only streaming level name, e.g. "Facility_MFWest") rather than introducing a new,
-- unverified UEHelpers.GetWorld():GetMapName() call.
handlers["world.info"] = function(_, respond)
    runOnGameThread(function()
        local levelToken
        local ok, controller = pcall(function() return UEHelpers.GetPlayerController() end)
        if ok and controller and controller:IsValid() then
            local okLevel, level = pcall(function() return controller.ActiveLevelName:ToString() end)
            if okLevel and level and level ~= "" then levelToken = level end
        end
        return { levelToken = levelToken, isHost = isHost() }
    end, respond)
end

-- ===== Quest / story flags (native UWorldFlagSubsystem) =====
-- Earlier rounds could not find a live path for these because no installed mod touches them.
-- The game's own blueprints do: doors, triggers and effects all call GetWorldSubsystem ->
-- HasWorldFlag / SetWorldFlag on a native world subsystem, and its exact native signatures are in
-- the shipped PDB:
--   bool HasWorldFlag(const UObject* WorldContext, FWorldFlagRowHandle Flag)
--   void SetWorldFlag(FWorldFlagRowHandle Flag, bool Value, UObject* Instigator)
--   bool GetWorldFlags(TArray<FName>& Out)
--   bool HasWorldFlagsLoaded()
-- plus a static UWorldFlagHandleFunctionLibrary (GetAllWorldFlagRowNames / RowHandles,
-- MakeWorldFlagRowHandle) shaped exactly like the weather library the reference mod already
-- drives. Trigger_WorldFlag_C is the in-game actor that flips a flag when the player walks into
-- it, and it goes through this same subsystem - so this is the game's own write path, not a
-- shortcut around it. NO published mod exercises it, so every call is pcall-guarded and the
-- first live run is what proves it (see docs/PROGRESS.md round 75).

local function worldFlagSubsystem()
    local ok, subsystem = pcall(function() return FindFirstOf("WorldFlagSubsystem") end)
    if ok and subsystem and subsystem:IsValid() then return subsystem end
    return nil
end

local function worldFlagLibrary()
    local ok, lib = pcall(function()
        return StaticFindObject("/Script/AbioticFactor.Default__WorldFlagHandleFunctionLibrary")
    end)
    if ok and lib and lib:IsValid() then return lib end
    return nil
end

local function currentWorldFlags()
    local set = {}
    local subsystem = worldFlagSubsystem()
    if subsystem then
        local names = outNames(function(out) subsystem:GetWorldFlags(out) end)
        for _, name in ipairs(names) do set[name] = true end
        if next(set) ~= nil then return set end
    end
    -- Fallback: the replicated AbioticGameState.WorldFlags array (native, from the usmap).
    local ok, gameState = pcall(function() return UEHelpers.GetGameStateBase() end)
    if ok and gameState and gameState:IsValid() then
        pcall(function()
            local flags = gameState.WorldFlags
            for i = 1, #flags do
                local okName, name = pcall(function() return flags[i]:ToString() end)
                if okName and name then set[name] = true end
            end
        end)
    end
    return set
end

handlers["flags.list"] = function(_, respond)
    runOnGameThread(function()
        local set = currentWorldFlags()
        local known = {}
        local lib = worldFlagLibrary()
        if lib then
            known = outNames(function(out) lib:GetAllWorldFlagRowNames(out) end)
        end
        local seen = {}
        local result = { __forceArray = true }
        for _, name in ipairs(known) do
            seen[name] = true
            table.insert(result, { name = name, isSet = set[name] == true })
        end
        for name, _ in pairs(set) do
            if not seen[name] then table.insert(result, { name = name, isSet = true }) end
        end
        return { flags = result, isHost = isHost() }
    end, respond)
end

-- Resolves a list of { name = "<flag row>", isSet = true/false } rows to their
-- FWorldFlagRowHandle and flips each one through the native subsystem. Factored out so
-- areas/story.lua (round 77: setting the story chapter IS setting/clearing its flags) can apply
-- the same edits flags.set does without a second round trip or a re-implementation. Must run
-- inside runOnGameThread; callers still do their own isHost() check first since the refusal
-- message differs per area ("...change quest flags" vs "...change the story chapter").
local function applyWorldFlagRows(rows)
    local subsystem = worldFlagSubsystem()
    local lib = worldFlagLibrary()
    if not subsystem or not lib then error("the quest flag system is not loaded (are you in a world?)") end
    local out = {}
    lib:GetAllWorldFlagRowHandles(out)
    local handles = {}
    for i = 1, #out do
        local okHandle, handle = pcall(function() return out[i]:get() end)
        if okHandle and handle then
            local okName, name = pcall(function() return handle.RowName:ToString() end)
            if okName and name then handles[name] = handle end
        end
    end
    local instigator = getMyPlayer()
    for i = 1, #rows do
        local row = rows[i]
        local handle = row.name and handles[row.name]
        if not handle then error("unknown quest flag " .. tostring(row.name)) end
        local value = row.isSet == true
        -- Same struct-as-table pattern the reference mod uses for TriggerWeatherEvent; the
        -- raw handle userdata is the fallback if the table form is rejected.
        local okCall = pcall(function()
            subsystem:SetWorldFlag({ RowName = handle.RowName, DataTablePath = handle.DataTablePath }, value, instigator)
        end)
        if not okCall then subsystem:SetWorldFlag(handle, value, instigator) end
    end
end

handlers["flags.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can change quest flags") end
        applyWorldFlagRows(payload.flags or {})
        return nil
    end, respond)
end

-- ===== Doors (SimpleDoor_ParentBP_C / SecurityDoor_C) =====
-- SimpleDoor: DoorState (byte, the same E_DoorStates the file editor writes: 0 closed, 1 open,
-- 2 locked, ...), OneWayDoor_HasBeenUnlocked, DoorDisabled, OnRep_DoorState, DoorUpdateState.
-- SecurityDoor: IsDoorOpen, OnRep_IsDoorOpen. Direct write + OnRep is the exact shape every other
-- confirmed area here uses (vitals, NPCs); DoorUpdateState is what the door's own blueprint
-- runs after a state change, so it is called too (pcall - no mod precedent).
--
-- REVIEWED (round 77): the class also exposes TryOpenOrUnlockDoor(CharacterToTest, DoorKick,
-- ForceDoor, ForceDoorState) and MarkOneWayDoorAsUnlocked(SkipSave) - confirmed real, 4 and 1
-- reflected inputs respectively, from a fresh class dump. TryOpenOrUnlockDoor looks tempting (it
-- even takes a ForceDoorState byte, so it could in principle BE the "set to this state" call),
-- but it was NOT adopted here: it needs a CharacterToTest actor reference with no evidenced
-- source for one (this mod has no "the player who is editing" actor handy inside a doors.set
-- call the way an interact prompt would), no installed mod calls it or MarkOneWayDoorAsUnlocked
-- at all (unlike DoorState/OnRep_DoorState, which at least match the pattern every other
-- confirmed area already uses), and its real behavior when ForceDoor is combined with
-- ForceDoorState on an ALREADY-unlocked or ALREADY-locked door is unverified - it may run lock
-- logic (sounds, kick-knockback, one-way checks) this protocol does not want on every plain
-- state edit. The existing direct-write shape is also the one round 75 already confirmed live
-- (state read back correctly on a real save, only the swing ANIMATION was left unwatched) - since
-- there is no working precedent for the alternative and the current shape has real-game evidence
-- behind it, this stays as direct DoorState + OnRep_DoorState + DoorUpdateState. Not changing the
-- write shape without evidence, per this round's own instructions.

local function doorRows()
    local result = { __forceArray = true }
    for _, door in ipairs(findAll("SimpleDoor_ParentBP_C")) do
        if door:IsValid() then
            local name = fullName(door)
            if name then
                local x, y, z = actorLocation(door)
                local okState, state = pcall(function() return door.DoorState end)
                local okOneWay, oneWay = pcall(function() return door.OneWayDoor_HasBeenUnlocked == true end)
                local okDisabled, disabled = pcall(function() return door.DoorDisabled == true end)
                table.insert(result, {
                    id = name, label = classLabel(name), kind = "simple",
                    state = okState and tonumber(state) or 0,
                    isOpen = okState and tonumber(state) == 1,
                    oneWayUnlocked = okOneWay and oneWay or false,
                    disabled = okDisabled and disabled or false,
                    x = x, y = y, z = z,
                })
            end
        end
    end
    for _, door in ipairs(findAll("SecurityDoor_C")) do
        if door:IsValid() then
            local name = fullName(door)
            if name then
                local x, y, z = actorLocation(door)
                local okOpen, open = pcall(function() return door.IsDoorOpen == true end)
                table.insert(result, {
                    id = name, label = classLabel(name), kind = "security",
                    state = (okOpen and open) and 1 or 0,
                    isOpen = okOpen and open or false,
                    oneWayUnlocked = false, disabled = false,
                    x = x, y = y, z = z,
                })
            end
        end
    end
    return result
end

handlers["doors.list"] = function(_, respond)
    runOnGameThread(function()
        return { doors = doorRows(), isHost = isHost() }
    end, respond)
end

-- BUG FOUND BY THE HARNESS (round 77, fixed here): a row whose id did not resolve to a live door
-- (unloaded, destroyed, or simply mistyped) used to be silently skipped, so the whole call
-- reported success even though nothing happened for that row - not a Lua crash, but not
-- "player-safe" either, since the player gets no indication anything was wrong. Every OTHER
-- resolvable row in the same call is still applied (an unloaded door two rows down should not
-- block editing the ones that ARE still loaded); only the final reply turns into an error naming
-- the first id that could not be found, once every row has had its chance.
handlers["doors.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can change doors") end
        local rows = payload.doors or {}
        local missingId = nil
        for i = 1, #rows do
            local row = rows[i]
            if row.kind == "security" then
                local door = row.id and findByFullName("SecurityDoor_C", row.id)
                if door then
                    if row.isOpen ~= nil then
                        door.IsDoorOpen = row.isOpen
                        pcall(function() door:OnRep_IsDoorOpen() end)
                    end
                else
                    missingId = missingId or row.id
                end
            else
                local door = row.id and findByFullName("SimpleDoor_ParentBP_C", row.id)
                if door then
                    if row.state ~= nil then
                        door.DoorState = row.state
                        pcall(function() door:OnRep_DoorState() end)
                        pcall(function() door:DoorUpdateState() end)
                    end
                    if row.oneWayUnlocked ~= nil then door.OneWayDoor_HasBeenUnlocked = row.oneWayUnlocked end
                    if row.disabled ~= nil then door.DoorDisabled = row.disabled end
                else
                    missingId = missingId or row.id
                end
            end
        end
        if missingId then error("door not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
        return nil
    end, respond)
end

-- ===== World containers (Deployed_Container_ParentBP_C) =====
-- Every storage crate/locker/cabinet in the world derives from this class and owns a
-- ContainerInventory (an Abiotic_InventoryComponent_C - the SAME component class the player
-- inventory area above already edits, with the same CurrentInventory slot structs and the same
-- hash-suffixed field names). OnRep_CurrentInventory exists on the component and is called after
-- a write so clients/UI refresh (pcall - no mod precedent for calling it directly).

-- skipMetadata defaults to false (full detail) because slotRow is shared with
-- inventory_transfer.lua's snapshot(), which genuinely needs instanceMetadata to correctly carry
-- a coating/pet-XP/tags across a move - only containers.list opts into skipping it, one slot pair
-- at a time is never the expensive case a bulk world listing is.
local function slotRow(slot, index, skipMetadata)
    local rowName = slotRowName(slot)
    local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
    -- "Empty" is the sentinel the game writes; "None" also shows up on loot-spill bags in a
    -- real world (confirmed live, round 75) and the file editor treats both as empty.
    local isEmpty = rowName == "" or rowName == "Empty" or rowName == "None"
    -- Skipped for an empty slot (see inventory.list's matching comment), and instance metadata
    -- is skipped too when the caller asked for it (see readItemDetails's own comment on
    -- skipMetadata) - both are the main levers on containers.list's freeze/timeout. Not the
    -- "isEmpty and nil or ..." idiom - see inventory.list's comment on why that silently never
    -- skips anything when the skipped value is nil.
    local details = nil
    if not isEmpty then details = readItemDetails(changeableData, slot, skipMetadata) end
    return {
        slotIndex = index,
        itemId = rowName,
        isEmpty = isEmpty,
        stack = changeableData and changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 or 0,
        durability = changeableData and changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 or 0,
        maxDurability = changeableData and changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B or 0,
        ammoInMagazine = changeableData and changeableData.CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D or 0,
        details = details,
    }
end

-- CurrentDurability/MaxDurability are plain (non-hash-suffixed) replicated DoubleProperty fields
-- on AbioticDeployed_ParentBP, the shared base every placed deployable (not just containers)
-- inherits from - verified against the game's own Blueprint exports. Omitted (nil) rather than
-- reported as 0/0 for a deployable that does not track durability at all (MaxDurability stays 0
-- for those), so the editor can tell "no health to show" apart from "destroyed".
--
-- Round 84: reading the raw fields alone was not enough - a live report showed every Void Chest
-- reading 0/42, which does not match a chest the player could see was only part-damaged in game.
-- CanLoseDurability() (a real, zero-argument, BlueprintCallable function on this same base class,
-- verified against the game's own Blueprint exports) is the actor's own answer to "does my
-- durability value mean anything at all", and is checked first.
--
-- Round 85: CanLoseDurability() alone was still not enough - restarted and re-tested, a live
-- report now shows CurrentDurability/MaxDurability for a Void Chest as something like 1200/600,
-- current nearly double the max, which cannot be genuine structural health under this game's own
-- rules (nothing else in this codebase's durability handling - item slot durability, vitals -
-- ever lets current exceed max). Traced CanLoseDurability()'s real bytecode (game export dump):
-- it looks up this actor's own DeconstructedItemData row (what item you get for scrapping this
-- deployable) and returns true whenever that row resolves at all, regardless of whether the
-- CurrentDurability/MaxDurability values that follow mean anything sensible for THIS class - a
-- Void Chest's DeconstructedItemData row still resolving to something valid does not mean its
-- durability fields were ever meant to be read as a literal 0-to-max health bar the way an
-- ordinary wood/metal crate's are (this could not be confirmed further live: the game only
-- accepts one client connection at a time, and the editor's own live session was holding it for
-- the whole of this investigation, so no direct read against the user's actual Void Chest was
-- possible this round either). Rather than keep guessing at what the raw numbers mean for this
-- specific class, treat a current-exceeds-maximum reading as self-evidently not real health and
-- omit it, the same as the "no health at all" case just above - a wrong number is worse than no
-- number.
local function containerHealth(container)
    local trackOk, tracksDurability = pcall(function() return container:CanLoseDurability() end)
    if not trackOk or tracksDurability ~= true then return nil, nil end
    local ok, current, maximum = pcall(function() return container.CurrentDurability, container.MaxDurability end)
    if ok and type(current) == "number" and type(maximum) == "number" and maximum > 0 and current >= 0 and current <= maximum then
        return current, maximum
    end
    return nil, nil
end

-- PlayerMadeString is a replicated (Net | RepNotify) StrProperty on AbioticDeployed_Furniture_ParentBP
-- - verified against the game's own Blueprint exports. It is the real, networked source of a
-- container's player-given label: the game's own DeliverString(String, FromSave) Blueprint event
-- sets it, then manually calls NewPlayerMadeString() (which AbioticDeployed_Furniture_ParentBP's
-- OnRep_PlayerMadeString also calls, so every client that receives the replicated change reaches
-- the same code) to copy it into AlternativeDisplayName and refresh the container's own 3D text
-- label. Two other name-shaped candidates were checked and rejected: AlternativeDisplayName
-- itself, and AlternativeObjectName (the field bases.lua's own deployable rename already reads
-- and writes) - both are "Edit | BlueprintVisible | DisableEditOnInstance" only, no Net flag at
-- all, so a direct Lua write to either would only ever be consistent on whichever machine touched
-- it. PlayerMadeString is the one candidate that is genuinely networked.
local function containerName(container)
    local ok, value = pcall(function() return container.PlayerMadeString end)
    -- textValue (defined above, shared with the per-slot PlayerMadeString_ field) already handles
    -- both shapes UE4SS hands back for a StrProperty - sometimes a plain Lua string, sometimes
    -- FString-like userdata needing :ToString() - so this does not assume either one.
    local text = ok and textValue(value)
    if not text or text == "" then return nil end
    return text
end

-- Round 85: also reports whether the returned inventory is SHARED (owned by something other
-- than this specific actor, e.g. a Void Chest's redirect to the world GameState's single pool)
-- rather than an ordinary per-actor one - see the two write handlers that read this for why:
-- forcing an immediate OnRep_CurrentInventory() refresh on a shared inventory a live report
-- showed freezing the game for a moment on write, plausibly because that inventory is watched by
-- every placed instance sharing it (four Void Chests' worth of UI/replication, not one crate's).
-- Detected by identity, not by class name: this actor's own (always-present, but for a Void
-- Chest always-empty) ContainerInventory property is compared against whatever
-- GetContainerInventory() actually returned, via GetFullName() (this Lua environment's objects do
-- not reliably support "==" for identity, so a real object hasn't been assumed equal to itself
-- through it) - genuinely the same object means ordinary per-actor storage; anything else means
-- GetContainerInventory() redirected somewhere shared.
local function containerInventory(container)
    -- GetContainerInventory() (a BlueprintPure function every container class exports, verified
    -- against the game's own Blueprint exports) is preferred over reading ContainerInventory/
    -- BenchInventory directly: a Void Chest overrides it to redirect to a single inventory owned
    -- by the world's GameState (Inventory_Void) instead of a per-actor component, since every
    -- placed Void Chest shares one storage pool. Reading its own ContainerInventory property
    -- directly (the old behavior here) found that per-actor component, which a Void Chest never
    -- actually stores anything in, so it always looked empty regardless of its real contents.
    local directOk, direct = pcall(function() return container.ContainerInventory end)
    local ok, inv = pcall(function() return container:GetContainerInventory() end)
    if not ok or not inv or not inv:IsValid() then ok, inv = pcall(function() return direct end) end
    if not ok or not inv or not inv:IsValid() then ok, inv = pcall(function() return container.BenchInventory end) end
    if ok and inv and inv:IsValid() then
        local shared = not (directOk and direct and direct:IsValid() and fullName(direct) == fullName(inv))
        return inv, shared
    end
    return nil, false
end

local CONTAINER_CLASSES = { "Deployed_Container_ParentBP_C", "Deployed_ProcessingBench_ParentBP_C" }
local function loadedContainers()
    local result, seen = {}, {}
    for _, class in ipairs(CONTAINER_CLASSES) do
        for _, actor in ipairs(findAll(class)) do
            local name = fullName(actor)
            if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
        end
    end
    return result
end
local function findContainer(name)
    for _, class in ipairs(CONTAINER_CLASSES) do
        local actor = findByFullName(class, name)
        if actor then return actor end
    end
end

require("inventory_transfer")({ handlers = handlers, runOnGameThread = runOnGameThread,
    isHost = isHost, findContainer = findContainer, containerInventory = containerInventory,
    resolvePlayer = resolvePlayer, inventoryComponent = inventoryComponent, slotRow = slotRow,
    prepareSlotWrite = prepareSlotWrite, applySlotWrite = applySlotWrite })

handlers["containers.list"] = function(_, respond)
    runOnGameThread(function()
        local result = { __forceArray = true }
        for _, container in ipairs(loadedContainers()) do
            -- pcall per container (not one pcall around the whole loop, matching the DELETE ALL
            -- SHOWN handler's own reasoning below): this walks every placed container/bench in the
            -- world, and slotRow/readItemDetails touch several ItemDataTable/ChangeableData fields
            -- unguarded - one actor in an unusual state (mid-destruction, a modded/DLC deployable
            -- with a different slot shape) used to raise an uncaught Lua error that failed this
            -- ENTIRE request, which the editor could only show as "not available", indistinguishable
            -- from no world being loaded at all. A container this still throws an uncaught error
            -- for (not just an unreadable inventory, see the inv/slots handling below for that
            -- narrower case) simply does not appear in the list - the same as a genuinely
            -- unloaded/destroyed one already looks like from the editor's side, and this handler
            -- has no per-item error channel (unlike dropped.remove's batch result) to report one
            -- through instead.
            pcall(function()
                if not container:IsValid() then return end
                local name = fullName(container)
                if not name then return end
                -- Round 84: a live report showed a Void Chest the player was standing right in
                -- front of missing from this list entirely (not "shown empty" - simply absent).
                -- The old code below dropped a container completely whenever containerInventory()
                -- came back nil/invalid, which folded "no such container" together with "this
                -- container exists but its inventory could not be resolved right now" - and a
                -- Void Chest's inventory is GetContainerInventory()'s own redirect to a single
                -- GameState-owned component (see that function's remarks), which is exactly the
                -- kind of lookup that can transiently fail for one specific client/session in a
                -- way a normal per-actor inventory does not. Now the container still gets a row
                -- (position included, so it is at least findable) with empty slots instead of
                -- vanishing outright - this could not be confirmed against a live repro of the
                -- original failure (this game's Lua hot-reload is off, so nothing here could be
                -- tested against the user's already-running game without a restart), but "visible
                -- with no readable contents" is a strictly more honest failure mode than "not
                -- listed at all" either way.
                local inv = containerInventory(container)
                local x, y, z = actorLocation(container)
                local slots = { __forceArray = true }
                if inv and inv.CurrentInventory then
                    for i = 1, #inv.CurrentInventory do
                        -- Same per-slot guard: one bad slot should not drop every other slot in an
                        -- otherwise-healthy container.
                        -- skipMetadata=true: this is the bulk world listing (see slotRow's own
                        -- comment) - inventory.transfer's own slotRow call keeps full detail.
                        local slotOk, row = pcall(slotRow, inv.CurrentInventory[i], i - 1, true)
                        if slotOk then table.insert(slots, row) end
                    end
                end
                local health, maxHealth = containerHealth(container)
                -- "label" is the auto-generated class-based name (e.g. "Storage Crate"), always
                -- present; "name" is the optional player-given label (see containerName's own
                -- remarks), nil when nothing has ever been typed.
                table.insert(result, { id = name, label = classLabel(name), x = x, y = y, z = z, slots = slots,
                    health = health, maxHealth = maxHealth, name = containerName(container) })
            end)
        end
        return { containers = result, isHost = isHost() }
    end, respond)
end

handlers["containers.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can change containers") end
        local container = payload.id and findContainer(payload.id)
        if not container then error("container not found (it may have been unloaded or destroyed)") end
        local inv, shared = containerInventory(container)
        if not inv or not inv.CurrentInventory then error("container has no inventory") end
        local rows, prepared = payload.edits or {}, {}
        -- Round 84: this used to only acquire the replication helper (and so only mark
        -- CurrentInventory dirty) when at least one edit carried full item .details - a plain
        -- item-id-and-stack write (the common case for just dropping something into a slot) never
        -- got marked at all. inventory.transfer (the other handler that writes a container's
        -- CurrentInventory) has always marked unconditionally - see its own remarks - and this now
        -- matches it. The gap mattered most for a Void Chest specifically: a live report showed an
        -- item added through the editor never appearing in the running game, and never showing up
        -- in the OTHER three Void Chests sharing the same pool either - GetContainerInventory()
        -- redirects every Void Chest to one inventory owned by the world's GameState (see that
        -- function's own remarks), and a GameState-owned component is exactly the kind of thing
        -- every connected client (including the host's own UI, which very plausibly reads it
        -- through the same replicated path everyone else does, unlike a normal per-actor-owned
        -- inventory the host can bind to directly) needs a real network update for. This could not
        -- be confirmed by re-testing this exact fix against the live game (this game's Lua
        -- hot-reload is off, so nothing here takes effect until a full restart), but it closes a
        -- real, confirmed gap in the marking logic that plain writes were falling through either way.
        local replication = require("replication")
        local helper = replication.requireHelper()
        for i = 1, #rows do
            local row = rows[i]
            if type(row.slotIndex) ~= "number" or row.slotIndex < 0 or row.slotIndex % 1 ~= 0 then
                error("invalid container slot index")
            end
            local slot = inv.CurrentInventory[row.slotIndex + 1]
            if not slot then error("container slot is unavailable; refresh and retry") end
            prepared[i] = prepareSlotWrite(slot, row)
        end
        for i = 1, #prepared do applySlotWrite(prepared[i]) end
        if payload.sort then
            -- SortInventory() is a real, ZERO-parameter function on Abiotic_InventoryComponent_C
            -- (LiveClassPropsProbe, fragment "Abiotic_InventoryComponent") - reorders
            -- CurrentInventory in place the same way the in-game "sort" button does. Not
            -- exercised by any mod, so pcall-guarded (round 77).
            local ok = pcall(function() inv:SortInventory() end)
            if not ok then error("could not sort this container on this game build") end
        end
        if #rows > 0 or payload.sort then replication.mark(helper, inv, "CurrentInventory") end
        -- Round 85: a live report showed the game freezing for a moment specifically when adding
        -- an item into a Void Chest through the editor. This manual OnRep_CurrentInventory() call
        -- exists only so the HOST's own view catches up immediately (a host never gets its own
        -- RepNotify - that only fires on receiving a replicated change from elsewhere - the same
        -- reasoning containers.rename's NewPlayerMadeString() call documents). For an ordinary
        -- per-actor container that is cheap. For a shared inventory (see containerInventory's own
        -- remarks) it is the one inventory every placed instance sharing it reads from, so forcing
        -- its refresh synchronously here plausibly does much more work than a single crate's own
        -- update ever would - this could not be measured directly against the user's actual game
        -- this round (the live-agent helper accepts only one connection at a time, and the
        -- editor's own session held it throughout this investigation), so this is a reasoned
        -- mitigation, not a proven fix. Skipped for a shared inventory: the mark above still queues
        -- the real network update everyone (host included) receives shortly after, just not
        -- synchronously inside this handler.
        if not shared then pcall(function() inv:OnRep_CurrentInventory() end) end
        return nil
    end, respond)
end

-- Sets a container's player-given name via PlayerMadeString - checked against the game's own
-- Blueprint exports specifically to find a property that is genuinely networked, not just any
-- name-shaped field: AbioticDeployed_Furniture_ParentBP declares it "Edit | BlueprintVisible |
-- Net | DisableEditOnInstance | RepNotify" with RepNotifyFunc "OnRep_PlayerMadeString", unlike
-- AlternativeDisplayName or the separate AlternativeObjectName property bases.lua's own rename
-- reads/writes (both plain "Edit | BlueprintVisible | DisableEditOnInstance" - no Net flag at
-- all on either). The game's own rename flow (DeliverString(String, FromSave), the Blueprint
-- event the container's in-world "type a name" prompt calls) sets this same PlayerMadeString,
-- then calls OnRep_PlayerMadeString -> NewPlayerMadeString to refresh AlternativeDisplayName and
-- the floating 3D label from it - the same two calls made below.
--
-- A direct Lua property write bypasses the generated code path that would normally flag a
-- replicated property as changed, so it needs an explicit push - this game (UE 5.4, push-model
-- replication) exposes exactly that through NetPushModelHelpers.MarkPropertyDirty, already used
-- elsewhere in this file for the same reason (see bases.lua's PaintedColor write, which marks
-- dirty after a direct property set for the identical cause). Without this mark, other already-
-- connected clients would not see the new name until something else caused this container to
-- replicate again - this mark is what makes it reach every connected player, not only the host.
handlers["containers.rename"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can rename containers") end
        local container = payload.id and findContainer(payload.id)
        if not container then error("container not found (it may have been unloaded or destroyed)") end
        local newName = tostring(payload.name or "")
        -- Try a plain string first (StrProperty, unlike bases.lua's FText-typed
        -- AlternativeObjectName - textValue above already shows a plain string is a real shape
        -- this field hands back), falling back to the FString(...) constructor some UE4SS builds
        -- require instead, the same defensive order bases.lua uses for its own text write.
        local ok = pcall(function() container.PlayerMadeString = newName end)
        if not ok then ok = pcall(function() container.PlayerMadeString = FString(newName) end) end
        if not ok then error("could not set this container's name on this game build") end
        local replication = require("replication")
        local markOk = pcall(function()
            local helper = replication.requireHelper()
            replication.mark(helper, container, "PlayerMadeString")
        end)
        -- Mirrors OnRep_PlayerMadeString -> NewPlayerMadeString, which every OTHER client already
        -- runs automatically once PlayerMadeString replicates to them (the mark above is what
        -- causes that to happen); the host itself never gets its own RepNotify (that only fires
        -- on receiving a replicated change), so this call is what makes the host's own view (and
        -- AlternativeDisplayName/the 3D label it derives) catch up immediately too.
        pcall(function() container:NewPlayerMadeString() end)
        if not markOk then
            error("the name was set but could not be confirmed as sent to other connected players on this game build")
        end
        -- Not the "cond and nil or x" idiom: when newName is "" that always evaluates to x
        -- (nil is falsy, so "or" falls through to it), silently defeating the empty-name case.
        local expected = newName
        if newName == "" then expected = nil end
        if containerName(container) ~= expected then
            error("the game did not retain the new name; refresh to see its current state")
        end
        return nil
    end, respond)
end

-- ===== Dropped items (Abiotic_Item_Dropped_C) =====
-- FindAllOf("Abiotic_Item_Dropped_C") + HasBeenPickedUp + InitDespawn()/OnItemDespawn() are the
-- reference mod's own "destroy all dropped items" command, verbatim (CommandsManager.lua:1464-
-- 1478, host-only there too). ItemDataRow/ChangeableData are on the blueprint class layout.

handlers["dropped.list"] = function(_, respond)
    runOnGameThread(function()
        local result = { __forceArray = true }
        for _, item in ipairs(findAll("Abiotic_Item_Dropped_C")) do
            if item:IsValid() then
                local name = fullName(item)
                local okPicked, picked = pcall(function() return item.HasBeenPickedUp == true end)
                if name and not (okPicked and picked) then
                    local x, y, z = actorLocation(item)
                    local okRow, rowName = pcall(function() return item.ItemDataRow.RowName:ToString() end)
                    local okStack, stack = pcall(function()
                        return item.ChangeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49
                    end)
                    table.insert(result, {
                        id = name,
                        itemId = (okRow and rowName) or classLabel(name),
                        stack = (okStack and stack) or 1,
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return { items = result, isHost = isHost() }
    end, respond)
end

handlers["dropped.remove"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can remove dropped items") end
        local ids = payload.ids or {}
        -- ONE scan of every dropped item in the world, not one per id: findByFullName does a
        -- fresh FindAllOf("Abiotic_Item_Dropped_C") walk on every call, and DELETE ALL SHOWN can
        -- send dozens of ids in a single request - that used to mean dozens of full-world rescans
        -- for one click, which on a long-played world (hundreds of loose items) was slow enough to
        -- freeze the game and blow past the helper's own response timeout, showing up as "delete
        -- all shown" silently doing nothing (the request never got an answer back at all, not even
        -- a partial one).
        local byName = {}
        for _, obj in ipairs(findAll("Abiotic_Item_Dropped_C")) do
            if obj:IsValid() then byName[fullName(obj)] = obj end
        end
        local removed = 0
        for i = 1, #ids do
            local item = byName[ids[i]]
            if item then
                -- pcall per item (not one pcall around the whole loop): one item raising here
                -- (already mid-pickup, an odd blueprint override, ...) used to abort the loop early
                -- and leave every later item in the batch undeleted with no sign why - matching a
                -- player report that "delete all shown" looked like it silently did nothing.
                local ok = pcall(function() item:InitDespawn() item:OnItemDespawn() end)
                if ok then removed = removed + 1 end
            end
        end
        return { removed = removed }
    end, respond)
end

-- ===== dropped.add: spawn a NEW item on the ground (round 77) =====
-- No SpawnDroppedItem/"give item" precedent exists anywhere in the reference mod - checked, and
-- there is no additem/spawnitem/give-style command in it at all, only "givexp" for skill XP (not
-- items). So this does not construct an Abiotic_Item_Dropped_C actor from scratch (guessing an
-- FTransform/spawn-params shape with no precedent is exactly the mistake this project already got
-- burned by once, GetMyPlayerController). Instead it chains two ALREADY-PROVEN mechanisms:
--   1. inventory.set's own writeSlot (proven live, round 74) puts the requested item into a free
--      slot of the target player's own inventory.
--   2. Abiotic_PlayerCharacter_C's Request_DropInventorySlot(Inventory: object, Index: int) - a
--      real function confirmed from the game's own class layout (LiveClassPropsProbe, fragment
--      "Abiotic_PlayerCharacter."), with exactly two simple parameters (an object reference and
--      an int) - the same action pressing "drop" on that slot performs in the inventory UI.
-- Not exercised by any mod, so pcall-guarded; the item lands wherever the game's own
-- FindBestItemDropLocation puts it (near the player), NOT at a caller-chosen position - unlike
-- the file editor's TryAddDroppedItem, which takes an explicit x/y/z. Genuinely unproven
-- end-to-end against the running game.
handlers["dropped.add"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can add dropped items") end
        if not payload.itemId or payload.itemId == "" then error("itemId is required") end
        local player = resolvePlayer(payload)
        if not player then error("player not found") end

        -- Hotbar first (smallest, closest to what a player would actually drop), then backpack.
        local targetInv, targetSlot, targetIndex
        for _, kind in ipairs({ "hotbar", "backpack" }) do
            local candidate = inventoryComponent(player, kind)
            if candidate and candidate.CurrentInventory then
                for i = 1, #candidate.CurrentInventory do
                    local rowName = slotRowName(candidate.CurrentInventory[i])
                    if rowName == "" or rowName == "Empty" or rowName == "None" then
                        targetInv, targetSlot, targetIndex = candidate, candidate.CurrentInventory[i], i - 1
                        break
                    end
                end
            end
            if targetSlot then break end
        end
        if not targetSlot then error("no free inventory slot to route the drop through") end

        writeSlot(targetSlot, {
            itemId = payload.itemId,
            dataTable = payload.dataTable,
            stack = payload.stack or 1,
            durability = payload.durability,
            maxDurability = payload.maxDurability,
        })
        pcall(function() targetInv:OnRep_CurrentInventory() end)

        local ok, err = pcall(function() player:Request_DropInventorySlot(targetInv, targetIndex) end)
        if not ok then error("could not drop this item on this game build: " .. tostring(err)) end
        return nil
    end, respond)
end

-- ===== inventory.drop: drop an item the player ALREADY has (round 79) =====
-- The counterpart to dropped.add above for the common case: the player's INVENTORY tab has a
-- DROP ITEM button for a slot that already holds something, so there is no need to route it
-- through a scratch slot first - this calls the exact same Request_DropInventorySlot(Inventory,
-- Index) RPC directly on the slot the caller names. Same not-host-gated reasoning as
-- inventory.set (a client editing their own pawn's own inventory has authority over it).
handlers["inventory.drop"] = function(payload, respond)
    runOnGameThread(function()
        -- Wire field is "slotIndex" (see LiveInventoryChannel.DropSlotAsync's DropWire record on
        -- the .NET side, camelCased by System.Text.Json) - NOT "index". Every call used to fail
        -- with "index is required" regardless of what was clicked, which is exactly why DROP ITEM
        -- looked enabled but did nothing: the request always errored before it ever reached
        -- Request_DropInventorySlot.
        if not payload.kind then error("kind is required") end
        if payload.slotIndex == nil then error("slotIndex is required") end
        local player = resolvePlayer(payload)
        if not player then error("player not found") end

        local inv = inventoryComponent(player, payload.kind)
        if not inv or not inv.CurrentInventory then error("no " .. tostring(payload.kind) .. " inventory on this pawn") end
        local slot = inv.CurrentInventory[payload.slotIndex + 1]
        if not slot then error("slot index out of range") end
        local rowName = slotRowName(slot)
        if rowName == "" or rowName == "Empty" or rowName == "None" then error("that slot is already empty") end

        local ok, err = pcall(function() player:Request_DropInventorySlot(inv, payload.slotIndex) end)
        if not ok then error("could not drop this item on this game build: " .. tostring(err)) end
        return nil
    end, respond)
end

-- ===== Area modules =====
-- Each live-editing area added after round 75 lives in its own file under Scripts/areas/ and is
-- listed in Scripts/areas/manifest.lua. A module is `return function(ctx) ... end` and registers
-- its handlers on ctx.handlers, using the shared helpers below instead of re-implementing them.
-- Kept as separate files so several areas can be developed at once without everyone editing
-- this file; see areas/README.md for the contract. A module that fails to load is logged and
-- skipped - one broken area never takes the whole mod down.
handlers["containers.setfull"] = handlers["containers.set"]
handlers["containers.setcomplete"] = handlers["containers.set"]

local ctx = {
    handlers = handlers,
    json = json,
    UEHelpers = UEHelpers,
    runOnGameThread = runOnGameThread,
    isHost = isHost,
    getMyPlayer = getMyPlayer,
    resolvePlayer = resolvePlayer,
    allPlayerStates = allPlayerStates,
    playerId = playerId,
    inventoryComponent = inventoryComponent,
    slotRowName = slotRowName,
    slotRow = slotRow,
    writeSlot = writeSlot,
    findAll = findAll,
    findByFullName = findByFullName,
    fullName = fullName,
    classLabel = classLabel,
    actorLocation = actorLocation,
    outNames = outNames,
    dayNightManager = dayNightManager,
    weatherLibrary = weatherLibrary,
    worldFlagSubsystem = worldFlagSubsystem,
    worldFlagLibrary = worldFlagLibrary,
    currentWorldFlags = currentWorldFlags,
    applyWorldFlagRows = applyWorldFlagRows,
    containerInventory = containerInventory,
    resolveDataTableRow = resolveDataTableRow,
    itemTableGlobal = ITEM_TABLE_GLOBAL,
}

local okManifest, areaModules = pcall(require, "areas.manifest")
if not okManifest or type(areaModules) ~= "table" then
    print("[AbioticEditorLiveAgentLua] no areas/manifest.lua (" .. tostring(areaModules) .. ")\n")
    areaModules = {}
end
for _, moduleName in ipairs(areaModules) do
    local okLoad, area = pcall(require, moduleName)
    if okLoad and type(area) == "function" then
        local okInit, initErr = pcall(area, ctx)
        if okInit then
            print("[AbioticEditorLiveAgentLua] area loaded: " .. moduleName .. "\n")
        else
            print("[AbioticEditorLiveAgentLua] area FAILED to initialise: " .. moduleName .. ": " .. tostring(initErr) .. "\n")
        end
    else
        print("[AbioticEditorLiveAgentLua] area FAILED to load: " .. moduleName .. ": " .. tostring(area) .. "\n")
    end
end

-- Exposed for the stub-environment test harness (live-agent/AbioticEditorLiveAgentLua/tests):
-- it loads this file under fake UE4SS globals and drives every handler directly. Harmless in
-- the real game - nothing else reads it.
AbioticEditorLiveAgentLua = { handlers = handlers, ctx = ctx }

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

local function respondToCurrentRequest(result, err, requestId)
    if err then
        writeResponseAtomic(json.encode({ requestId = requestId, ok = false, error = err }))
        return
    end
    -- A result that json.lua cannot encode (a raw UObject/FName/FText userdata left in a table
    -- by a handler) used to throw here, after the handler had already "succeeded" - so no reply
    -- was ever written and the editor only saw a timeout (found live in round 76 with two new
    -- areas). Turn that into an ok:false reply naming the problem instead.
    local okEncode, encoded = pcall(json.encode, { requestId = requestId, ok = true, result = result })
    if okEncode then
        writeResponseAtomic(encoded)
    else
        writeResponseAtomic(json.encode({ requestId = requestId, ok = false, error = "the mod produced a reply it could not encode: " .. tostring(encoded) }))
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
        writeResponseAtomic(json.encode({ requestId = request.requestId, ok = false, error = "unknown command '" .. request.cmd .. "'" }))
        return
    end

    -- The handler itself calls respondToCurrentRequest (immediately for "ping", or later via
    -- runOnGameThread's ExecuteInGameThread for anything that touches the game) - this call does
    -- NOT produce the response itself, unlike a plain synchronous-return design.
    -- Capture this request's id in the callback, never in a shared mutable current-id slot.
    local respond = function(result, err) respondToCurrentRequest(result, err, request.requestId) end
    local dispatchOk, dispatchErr = pcall(handler, request.payload or {}, respond)
    if not dispatchOk then
        -- The handler function itself raised before calling runOnGameThread/respond at all
        -- (a bug in the handler's own setup code, not inside the async game-thread work).
        writeResponseAtomic(json.encode({ requestId = request.requestId, ok = false, error = "dispatch error: " .. tostring(dispatchErr) }))
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
