-- AbioticEditorLiveAgentLua: the game-interaction half of live editing's hybrid design (see
-- ../../README.md "The Lua+helper hybrid"). This mod does ALL live UObject property access
-- (public UE4SS Lua API, no build step, no SDK access needed) and talks to
-- AbioticEditorLiveAgentHelper.exe (plain Winsock, no UE4SS dependency) over a pair of files in
-- %LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\ - see ../../Shared/LiveAgentServer.h and
-- AbioticEditorLiveAgentHelper's FileMailbox.h for the other side of this bridge, and
-- docs/reference/live-editing-protocol.md for the wire shapes both ultimately carry.
--
-- STATUS: extensively tested against a real running game this round (see live-agent/README.md
-- "Real-game debugging session" for the full log). What is CONFIRMED:
--   - The mod loads and runs with zero errors (require, os.getenv, LoopAsync, file mailbox).
--   - FindFirstOf("AbioticCharacterPlayerState") is the correct class name - it returns a real
--     object, fast, every time.
--   - Calling GetClass() from LoopAsync's own callback (i.e. off the game thread) FROZE THE WHOLE
--     GAME. ExecuteInGameThread genuinely dispatches to the game thread fast (~5ms) and prevents
--     that catastrophic freeze - confirmed via debug timestamps.
--   - The first version's real bug (not a freeze, a budget problem): it re-scanned every property
--     from scratch once per field (12 full scans for vitals.get alone) - fixed by
--     collectPropertyNames scanning once and reusing the result (see MAX_PROPERTIES_TO_SCAN).
-- What is NOT yet resolved: even from inside ExecuteInGameThread, on the confirmed-correct
-- object, GetClass() itself still does not return within the 5s round-trip budget - and this
-- happens without freezing the game this time (ping keeps working throughout), so it is not the
-- same catastrophic issue as before. A real published UE4SS mod
-- (github.com/Matraweber/PalWorkPriority, Scripts/icons.lua) uses the identical
-- `object:GetClass():ForEachProperty(...)` pattern successfully, so the API usage itself is not
-- wrong - the likely next thing to try is calling this from a `RegisterHook` on a naturally
-- recurring, already-game-thread-bound function (the pattern most working mods actually use for
-- per-frame reflection access) instead of ExecuteInGameThread, or attaching a native debugger to
-- see exactly what GetClass() is blocked on. Needs a fresh live-game session to continue -
-- MAX_PROPERTIES_TO_SCAN below is currently set low (30) for that next diagnostic round; raise it
-- back once ForEachProperty is confirmed to complete cleanly. The individual property prefixes
-- (Hunger_, CurrentSkillXP_, ...) remain unconfirmed guesses until GetClass() itself works.

local json = require("json")

local ipcDir = (os.getenv("LOCALAPPDATA") or "") .. "\\AbioticEditorLiveAgent\\ipc"
local requestPath = ipcDir .. "\\request.json"
local responsePath = ipcDir .. "\\response.json"
local responseTempPath = responsePath .. ".tmp"

-- Every call that touches a live UObject (FindFirstOf, GetClass, ForEachProperty,
-- Get/SetPropertyValue) MUST run on the game thread - LoopAsync's own callback does not run on
-- it, and calling these off-thread deadlocked the whole game the first time this mod was tested
-- live (see the STATUS comment above). ExecuteInGameThread is fire-and-forget/async with no
-- synchronous return (confirmed against UE4SS's own docs), so `work` reports its own outcome by
-- calling `respond(result, err)` itself instead of returning a value.
local function runOnGameThread(work, respond)
    print("[AbioticEditorLiveAgentLua] DEBUG runOnGameThread: calling ExecuteInGameThread\n")
    ExecuteInGameThread(function()
        print("[AbioticEditorLiveAgentLua] DEBUG ExecuteInGameThread callback FIRING\n")
        local ok, result, err = pcall(work)
        print("[AbioticEditorLiveAgentLua] DEBUG work() returned ok=" .. tostring(ok) .. "\n")
        if not ok then
            respond(nil, "handler error: " .. tostring(result))
        else
            respond(result, err)
        end
    end)
    print("[AbioticEditorLiveAgentLua] DEBUG runOnGameThread: ExecuteInGameThread call returned (queued)\n")
end

-- Mirrors PropertyTagExtensions.FindByPrefix on the .NET side (Serialization/Gvas) and
-- VitalsCommands.cpp's FindPropertyByPrefix: live reflection properties carry the same
-- blueprint-compiler hash suffix the save file's properties do (both come from the same compiled
-- blueprint class), so an exact name would break on the next game patch the same way a save
-- writer's exact-name lookup would - this has to search by prefix, not guess a full name.
--
-- MAX_PROPERTIES_TO_SCAN is a hard circuit breaker in case a broken callback (e.g. one that
-- errors before reaching its `return true`) would otherwise make ForEachProperty call it forever
-- instead of stopping. Generous (a real PlayerState-shaped blueprint class can easily carry
-- several hundred properties across its whole inheritance chain) - collectPropertyNames below
-- scans ONCE per object regardless of how many prefixes are looked up against the result, so a
-- larger cap here is cheap.
--
-- IMPORTANT (found by real-game timing, not by inspection): the first version of this scanned
-- ONCE PER FIELD (12 separate full ForEachProperty passes for vitals.get alone) and blew past the
-- 5s round-trip budget - not a freeze, just real native-call cost multiplied by 12. Every caller
-- must now collect names ONCE per object with collectPropertyNames and reuse the result.
local MAX_PROPERTIES_TO_SCAN = 30 -- TEMPORARILY small for this diagnostic round - raise once ForEachProperty is confirmed to complete cleanly on the game thread.

local function collectPropertyNames(object)
    print("[AbioticEditorLiveAgentLua] DEBUG collectPropertyNames: calling GetClass()\n")
    local class = object:GetClass()
    print("[AbioticEditorLiveAgentLua] DEBUG collectPropertyNames: GetClass() returned, hasClass="
        .. tostring(class ~= nil) .. "\n")
    if not class then return {} end
    local names = {}
    local count = 0
    print("[AbioticEditorLiveAgentLua] DEBUG collectPropertyNames: calling ForEachProperty\n")
    class:ForEachProperty(function(property)
        count = count + 1
        table.insert(names, property:GetFName():ToString())
        if count % 5 == 0 then
            print("[AbioticEditorLiveAgentLua] DEBUG collectPropertyNames: scanned " .. count
                .. " so far, last=" .. names[#names] .. "\n")
        end
        if count >= MAX_PROPERTIES_TO_SCAN then return true end -- circuit breaker
        return false
    end)
    print("[AbioticEditorLiveAgentLua] DEBUG collectPropertyNames: ForEachProperty returned, total="
        .. count .. "\n")
    return names
end

local function findInNames(names, prefix)
    for _, name in ipairs(names) do
        if name:sub(1, #prefix) == prefix then return name end
    end
    return nil
end

local function getByPrefix(object, names, prefix, fallback)
    local name = findInNames(names, prefix)
    if not name then return fallback end
    return object:GetPropertyValue(name)
end

local function setByPrefix(object, names, prefix, value)
    local name = findInNames(names, prefix)
    if not name then return end -- Unknown on this game build: leave it alone, do not guess.
    object:SetPropertyValue(name, value)
end

-- See VitalsCommands.cpp's FindLocalPlayerState for the same "no player-selection yet, one
-- process = one player" simplification this Phase-0/1 scope accepts. Must only ever be called
-- from inside runOnGameThread's `work`.
local function findLocalPlayerState()
    return FindFirstOf("AbioticCharacterPlayerState")
end

local handlers = {}

-- Touches ZERO UE4SS/game APIs - a safe baseline to confirm the dispatch/mailbox loop itself is
-- healthy before calling anything that reaches into the game. Responds synchronously (no game
-- thread needed for this one).
handlers["ping"] = function(_, respond)
    respond({ pong = true }, nil)
end

-- Calls ONLY FindFirstOf, nothing else - a minimal real-game-touching diagnostic.
handlers["diag.findplayer"] = function(_, respond)
    runOnGameThread(function()
        return { found = findLocalPlayerState() ~= nil }
    end, respond)
end

handlers["vitals.get"] = function(_, respond)
    runOnGameThread(function()
        local playerState = findLocalPlayerState()
        if not playerState then error("no local player state found") end
        local names = collectPropertyNames(playerState) -- ONE scan, reused for all 12 fields below.
        return {
            hunger = getByPrefix(playerState, names, "Hunger_", 100),
            thirst = getByPrefix(playerState, names, "Thirst_", 100),
            sanity = getByPrefix(playerState, names, "Sanity_", 100),
            fatigue = getByPrefix(playerState, names, "Fatigue_", 0),
            continence = getByPrefix(playerState, names, "Continence_", 100),
            money = getByPrefix(playerState, names, "Money_", 0),
            head = getByPrefix(playerState, names, "Head_", 100),
            torso = getByPrefix(playerState, names, "Torso_", 100),
            leftArm = getByPrefix(playerState, names, "LeftArm_", 100),
            rightArm = getByPrefix(playerState, names, "RightArm_", 100),
            leftLeg = getByPrefix(playerState, names, "LeftLeg_", 100),
            rightLeg = getByPrefix(playerState, names, "RightLeg_", 100),
        }
    end, respond)
end

handlers["vitals.set"] = function(payload, respond)
    runOnGameThread(function()
        local playerState = findLocalPlayerState()
        if not playerState then error("no local player state found") end
        local names = collectPropertyNames(playerState)
        local fields = {
            hunger = "Hunger_", thirst = "Thirst_", sanity = "Sanity_", fatigue = "Fatigue_",
            continence = "Continence_", money = "Money_", head = "Head_", torso = "Torso_",
            leftArm = "LeftArm_", rightArm = "RightArm_", leftLeg = "LeftLeg_", rightLeg = "RightLeg_",
        }
        for key, prefix in pairs(fields) do
            if payload[key] ~= nil then setByPrefix(playerState, names, prefix, payload[key]) end
        end
        return nil
    end, respond)
end

-- Mirrors PlayerSaveReader.ReadSkills/PlayerSaveWriter.ApplySkills: the Skills_ array is a fixed
-- list of structs, one per skill, matched by ARRAY INDEX (skill structs are not individually
-- named properties) - see SkillsCommands.cpp for the same shape in the blocked C++-mod approach.
handlers["skills.get"] = function(_, respond)
    runOnGameThread(function()
        local playerState = findLocalPlayerState()
        if not playerState then error("no local player state found") end
        local skillsArrayName = findInNames(collectPropertyNames(playerState), "Skills_")
        if not skillsArrayName then error("no Skills_ array property found") end
        local skillsArray = playerState:GetPropertyValue(skillsArrayName)

        local result = { __forceArray = true }
        for i = 1, #skillsArray do
            local element = skillsArray[i]
            local elementNames = collectPropertyNames(element) -- one scan per skill struct, reused for 2 fields.
            result[i] = {
                index = i - 1,
                xp = getByPrefix(element, elementNames, "CurrentSkillXP_", 0),
                xpMultiplier = getByPrefix(element, elementNames, "CurrentXPMultiplier_", 1),
            }
        end
        return result
    end, respond)
end

handlers["skills.set"] = function(payload, respond)
    runOnGameThread(function()
        local playerState = findLocalPlayerState()
        if not playerState then error("no local player state found") end
        local skillsArrayName = findInNames(collectPropertyNames(playerState), "Skills_")
        if not skillsArrayName then error("no Skills_ array property found") end
        local skillsArray = playerState:GetPropertyValue(skillsArrayName)

        for i = 1, #payload do
            local row = payload[i]
            local index = row.index
            if index ~= nil and index >= 0 and index < #skillsArray then
                local element = skillsArray[index + 1]
                local elementNames = collectPropertyNames(element)
                if row.xp ~= nil then setByPrefix(element, elementNames, "CurrentSkillXP_", row.xp) end
                if row.xpMultiplier ~= nil then
                    setByPrefix(element, elementNames, "CurrentXPMultiplier_", row.xpMultiplier)
                end
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
    -- NOT produce the response itself, unlike the old synchronous-return design.
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
