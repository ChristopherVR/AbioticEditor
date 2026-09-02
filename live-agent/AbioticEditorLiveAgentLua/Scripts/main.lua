-- AbioticEditorLiveAgentLua: the game-interaction half of live editing's hybrid design (see
-- ../../README.md "The Lua+helper hybrid"). This mod does ALL live UObject property access
-- (public UE4SS Lua API, no build step, no SDK access needed) and talks to
-- AbioticEditorLiveAgentHelper.exe (plain Winsock, no UE4SS dependency) over a pair of files in
-- %LOCALAPPDATA%\AbioticEditorLiveAgent\ipc\ - see ../../Shared/LiveAgentServer.h and
-- AbioticEditorLiveAgentHelper's FileMailbox.h for the other side of this bridge, and
-- docs/reference/live-editing-protocol.md for the wire shapes both ultimately carry.
--
-- STATUS: the JSON module (json.lua) and this file's request/response file handling were tested
-- against a real Lua 5.4 interpreter (see live-agent/README.md). The property names below
-- (Hunger_, CurrentSkillXP_, ...) and the FindLocalPlayerState class name are NOT verified
-- against a real running game - same caveat as VitalsCommands.cpp/SkillsCommands.cpp in the
-- (currently blocked) pure-C++-mod approach. Confirm them against a real property dump before
-- trusting an edit.

local json = require("json")

local ipcDir = (os.getenv("LOCALAPPDATA") or "") .. "\\AbioticEditorLiveAgent\\ipc"
local requestPath = ipcDir .. "\\request.json"
local responsePath = ipcDir .. "\\response.json"
local responseTempPath = responsePath .. ".tmp"

-- Mirrors PropertyTagExtensions.FindByPrefix on the .NET side (Serialization/Gvas) and
-- VitalsCommands.cpp's FindPropertyByPrefix: live reflection properties carry the same
-- blueprint-compiler hash suffix the save file's properties do (both come from the same compiled
-- blueprint class), so an exact name would break on the next game patch the same way a save
-- writer's exact-name lookup would - this has to search by prefix, not guess a full name.
local function findPropertyNameByPrefix(object, prefix)
    local found = nil
    object:GetClass():ForEachProperty(function(property)
        local name = property:GetFName():ToString()
        if name:sub(1, #prefix) == prefix then
            found = name
            return true -- stop iterating
        end
        return false
    end)
    return found
end

local function getByPrefix(object, prefix, fallback)
    local name = findPropertyNameByPrefix(object, prefix)
    if not name then return fallback end
    local ok, value = pcall(function() return object:GetPropertyValue(name) end)
    if not ok then return fallback end
    return value
end

local function setByPrefix(object, prefix, value)
    local name = findPropertyNameByPrefix(object, prefix)
    if not name then return false end -- Unknown on this game build: leave it alone, do not guess.
    local ok = pcall(function() object:SetPropertyValue(name, value) end)
    return ok
end

-- See VitalsCommands.cpp's FindLocalPlayerState for the same "no player-selection yet, one
-- process = one player" simplification this Phase-0/1 scope accepts.
local function findLocalPlayerState()
    return FindFirstOf("AbioticCharacterPlayerState")
end

local handlers = {}

handlers["vitals.get"] = function(_)
    local playerState = findLocalPlayerState()
    if not playerState then return nil, "no local player state found" end
    local result = {
        hunger = getByPrefix(playerState, "Hunger_", 100),
        thirst = getByPrefix(playerState, "Thirst_", 100),
        sanity = getByPrefix(playerState, "Sanity_", 100),
        fatigue = getByPrefix(playerState, "Fatigue_", 0),
        continence = getByPrefix(playerState, "Continence_", 100),
        money = getByPrefix(playerState, "Money_", 0),
        head = getByPrefix(playerState, "Head_", 100),
        torso = getByPrefix(playerState, "Torso_", 100),
        leftArm = getByPrefix(playerState, "LeftArm_", 100),
        rightArm = getByPrefix(playerState, "RightArm_", 100),
        leftLeg = getByPrefix(playerState, "LeftLeg_", 100),
        rightLeg = getByPrefix(playerState, "RightLeg_", 100),
    }
    return result, nil
end

handlers["vitals.set"] = function(payload)
    local playerState = findLocalPlayerState()
    if not playerState then return nil, "no local player state found" end
    local fields = {
        hunger = "Hunger_", thirst = "Thirst_", sanity = "Sanity_", fatigue = "Fatigue_",
        continence = "Continence_", money = "Money_", head = "Head_", torso = "Torso_",
        leftArm = "LeftArm_", rightArm = "RightArm_", leftLeg = "LeftLeg_", rightLeg = "RightLeg_",
    }
    for key, prefix in pairs(fields) do
        if payload[key] ~= nil then setByPrefix(playerState, prefix, payload[key]) end
    end
    return nil, nil
end

-- Mirrors PlayerSaveReader.ReadSkills/PlayerSaveWriter.ApplySkills: the Skills_ array is a fixed
-- list of structs, one per skill, matched by ARRAY INDEX (skill structs are not individually
-- named properties) - see SkillsCommands.cpp for the same shape in the blocked C++-mod approach.
handlers["skills.get"] = function(_)
    local playerState = findLocalPlayerState()
    if not playerState then return nil, "no local player state found" end
    local skillsArrayName = findPropertyNameByPrefix(playerState, "Skills_")
    if not skillsArrayName then return nil, "no Skills_ array property found" end
    local skillsArray = playerState:GetPropertyValue(skillsArrayName)

    local result = { __forceArray = true }
    for i = 1, #skillsArray do
        local element = skillsArray[i]
        result[i] = {
            index = i - 1,
            xp = getByPrefix(element, "CurrentSkillXP_", 0),
            xpMultiplier = getByPrefix(element, "CurrentXPMultiplier_", 1),
        }
    end
    return result, nil
end

handlers["skills.set"] = function(payload)
    local playerState = findLocalPlayerState()
    if not playerState then return nil, "no local player state found" end
    local skillsArrayName = findPropertyNameByPrefix(playerState, "Skills_")
    if not skillsArrayName then return nil, "no Skills_ array property found" end
    local skillsArray = playerState:GetPropertyValue(skillsArrayName)

    for i = 1, #payload do
        local row = payload[i]
        local index = row.index
        if index ~= nil and index >= 0 and index < #skillsArray then
            local element = skillsArray[index + 1]
            if row.xp ~= nil then setByPrefix(element, "CurrentSkillXP_", row.xp) end
            if row.xpMultiplier ~= nil then setByPrefix(element, "CurrentXPMultiplier_", row.xpMultiplier) end
        end
    end
    return nil, nil
end

-- ===== The file-mailbox poll loop =====
-- Atomic publish: write to a temp file, then rename over the real path, so the helper's reader
-- never observes a half-written response (matches FileMailbox::WriteAtomic on the helper side).
local function writeResponseAtomic(text)
    local file = io.open(responseTempPath, "wb")
    if not file then return end
    file:write(text)
    file:close()
    os.remove(responsePath) -- os.rename does not overwrite an existing file on Windows.
    os.rename(responseTempPath, responsePath)
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

    local success, result, err = pcall(handler, request.payload or {})
    if not success then
        -- `result` here is pcall's error value (the handler raised instead of returning).
        writeResponseAtomic(json.encode({ ok = false, error = "handler error: " .. tostring(result) }))
    elseif err then
        writeResponseAtomic(json.encode({ ok = false, error = err }))
    else
        writeResponseAtomic(json.encode({ ok = true, result = result }))
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
