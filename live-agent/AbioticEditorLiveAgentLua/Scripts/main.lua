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
-- Also unconfirmed: whether an inventory write needs an OnRep-style call to refresh the HUD (no
-- OnRep_*Inventory* or similar exists anywhere in the reference mod, unlike vitals'
-- OnRep_CurrentHealth or NPCs' OnRep_IsDead) - tested live to find out, see docs/PROGRESS.md.
--
-- NAME_None (real global, confirmed used the same way at AFUtils.lua:558) marks an empty slot.
-- FName(string, EFindName.FNAME_Find) (real, used by the ACTIVE SetNextWeatherEvent command at
-- AFUtils.lua:587) converts a row-name string into the FName these fields need - FNAME_Find only
-- finds an FName already interned somewhere in the running game, which every real item row name
-- already is (the item data table itself references it), so this can never silently fabricate a
-- bogus new name.

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
                    table.insert(result, {
                        kind = kind,
                        slotIndex = i - 1,
                        itemId = rowName,
                        -- "Empty" (confirmed real, capitalized) is this game's own empty-slot
                        -- sentinel string - not "None", which NAME_None:ToString() never actually
                        -- produces for this field (confirmed live: an untouched slot's RowName
                        -- prints "Empty", not "None"). "" is kept as a defensive fallback only.
                        isEmpty = rowName == "" or rowName == "Empty",
                        stack = changeableData and changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 or 0,
                        durability = changeableData and changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 or 0,
                        maxDurability = changeableData and changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B or 0,
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
        for i = 1, #rows do
            local row = rows[i]
            local inv = row.kind and inventoryComponent(player, row.kind)
            local slot = inv and inv.CurrentInventory and row.slotIndex ~= nil
                and inv.CurrentInventory[row.slotIndex + 1]
            if slot then
                local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
                if row.clear then
                    -- "Empty" (confirmed live), not NAME_None - see inventory.list's isEmpty
                    -- comment above for why: this game's own empty-slot sentinel is the literal
                    -- interned name "Empty", not the engine's generic none-name.
                    slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName =
                        FName("Empty", EFindName.FNAME_Find)
                    changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0
                    changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0
                    changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0
                else
                    if row.itemId ~= nil and row.itemId ~= "" then
                        slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName =
                            FName(row.itemId, EFindName.FNAME_Find)
                    end
                    if row.stack ~= nil then
                        changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = row.stack
                    end
                    if row.durability ~= nil then
                        changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = row.durability
                    end
                    if row.maxDurability ~= nil then
                        changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = row.maxDurability
                    end
                end
            end
        end
        return nil
    end, respond)
end

-- ===== Shared world helpers for the areas below =====
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

local function slotRow(slot, index)
    local rowName = slotRowName(slot)
    local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
    return {
        slotIndex = index,
        itemId = rowName,
        -- "Empty" is the sentinel the game writes; "None" also shows up on loot-spill bags in a
        -- real world (confirmed live, round 75) and the file editor treats both as empty.
        isEmpty = rowName == "" or rowName == "Empty" or rowName == "None",
        stack = changeableData and changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 or 0,
        durability = changeableData and changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 or 0,
        maxDurability = changeableData and changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B or 0,
    }
end

local function writeSlot(slot, row)
    local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
    if row.clear then
        slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName = FName("Empty", EFindName.FNAME_Find)
        changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0
        changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0
        changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0
        return
    end
    if row.itemId ~= nil and row.itemId ~= "" then
        slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName = FName(row.itemId, EFindName.FNAME_Find)
    end
    if row.stack ~= nil then changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = row.stack end
    if row.durability ~= nil then changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = row.durability end
    if row.maxDurability ~= nil then changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = row.maxDurability end
end

local function containerInventory(container)
    local ok, inv = pcall(function() return container.ContainerInventory end)
    if ok and inv and inv:IsValid() then return inv end
    return nil
end

handlers["containers.list"] = function(_, respond)
    runOnGameThread(function()
        local result = { __forceArray = true }
        for _, container in ipairs(findAll("Deployed_Container_ParentBP_C")) do
            if container:IsValid() then
                local name = fullName(container)
                local inv = containerInventory(container)
                if name and inv and inv.CurrentInventory then
                    local x, y, z = actorLocation(container)
                    local slots = { __forceArray = true }
                    for i = 1, #inv.CurrentInventory do
                        table.insert(slots, slotRow(inv.CurrentInventory[i], i - 1))
                    end
                    table.insert(result, { id = name, label = classLabel(name), x = x, y = y, z = z, slots = slots })
                end
            end
        end
        return { containers = result, isHost = isHost() }
    end, respond)
end

handlers["containers.set"] = function(payload, respond)
    runOnGameThread(function()
        if not isHost() then error("only the host can change containers") end
        local container = payload.id and findByFullName("Deployed_Container_ParentBP_C", payload.id)
        if not container then error("container not found (it may have been unloaded or destroyed)") end
        local inv = containerInventory(container)
        if not inv or not inv.CurrentInventory then error("container has no inventory") end
        local rows = payload.edits or {}
        for i = 1, #rows do
            local row = rows[i]
            local slot = row.slotIndex ~= nil and inv.CurrentInventory[row.slotIndex + 1]
            if slot then writeSlot(slot, row) end
        end
        pcall(function() inv:OnRep_CurrentInventory() end)
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
        local removed = 0
        for i = 1, #ids do
            local item = findByFullName("Abiotic_Item_Dropped_C", ids[i])
            if item then
                item:InitDespawn()
                item:OnItemDespawn()
                removed = removed + 1
            end
        end
        return { removed = removed }
    end, respond)
end

-- ===== Area modules =====
-- Each live-editing area added after round 75 lives in its own file under Scripts/areas/ and is
-- listed in Scripts/areas/manifest.lua. A module is `return function(ctx) ... end` and registers
-- its handlers on ctx.handlers, using the shared helpers below instead of re-implementing them.
-- Kept as separate files so several areas can be developed at once without everyone editing
-- this file; see areas/README.md for the contract. A module that fails to load is logged and
-- skipped - one broken area never takes the whole mod down.
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

local function respondToCurrentRequest(result, err)
    if err then
        writeResponseAtomic(json.encode({ ok = false, error = err }))
        return
    end
    -- A result that json.lua cannot encode (a raw UObject/FName/FText userdata left in a table
    -- by a handler) used to throw here, after the handler had already "succeeded" - so no reply
    -- was ever written and the editor only saw a timeout (found live in round 76 with two new
    -- areas). Turn that into an ok:false reply naming the problem instead.
    local okEncode, encoded = pcall(json.encode, { ok = true, result = result })
    if okEncode then
        writeResponseAtomic(encoded)
    else
        writeResponseAtomic(json.encode({ ok = false, error = "the mod produced a reply it could not encode: " .. tostring(encoded) }))
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
