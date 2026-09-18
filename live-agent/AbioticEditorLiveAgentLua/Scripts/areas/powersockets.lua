-- Live editing area: power sockets (PowerSocketMap, round 103). The live twin of
-- Core/WorldSaves/Services/WorldMapFeatures/PowerSocketMapFeature.cs (the save's PowerSocketMap;
-- editable "hasTimer", read-only "socketId"/"pluggedInDevice"/"timerMode"/"extraDevices").
--
-- Everything below is read straight from the coordinator's own CUE4Parse class dump plus the full
-- blueprint bytecode (ScriptBytecode JSON) of PowerSocket_ParentBP_C's own GetPowerSocketID,
-- Update_SaveData and SavePowerSocketToWorldSave - not guessed. See docs/PROGRESS.md's Round-103
-- entry and docs/reference/live-editing-protocol.md for the full citation trail.
--
-- CLASS DISCOVERY: PowerSocket_ParentBP_C (super=AbioticActor_C) is the one class every placed
-- socket derives from, confirmed from each concrete socket asset's own `super=` in the dump
-- (PowerSocket_MgtCore_C, PowerSocket_ORDER_C, PowerSocket_VWinter_C, PowerSocket_XMAS25_C).
-- FindAllOf is hierarchy-inclusive (same idiom buttons.lua/elevators.lua/pets.lua already rely on),
-- so a single FindAllOf("PowerSocket_ParentBP_C") already returns every one of those subclasses'
-- instances and any future one the game adds, with no class name hardcoded anywhere below. A
-- second, currently-empty table exists for a socket-shaped class that does NOT chain up to
-- PowerSocket_ParentBP_C (data, not logic, mirroring buttons.lua's ADDITIONAL_ROOT_CLASSES) -
-- nothing qualifies today; every confirmed socket asset chains to the one parent.
--
-- IDENTITY: PowerSocket_ParentBP_C:GetPowerSocketID() is BlueprintPure and its bytecode is exactly
-- `BreakSoftObjectPath(MakeSavedObjectPath(Self)).PathString` - the same "actor's own soft path"
-- shape the save's PowerSocket_<hash> leaf stores (PowerSocketMapFeature's own doc comment: this
-- value "matches the map entry key"). Read live via a plain pcall'd `socket:GetPowerSocketID()`
-- call and surfaced as the read-only `socketId` field, matching the offline id space exactly - the
-- wire `id` itself is still the usual ctx.fullName(actor) (GetFullName(), the same convention every
-- other area here uses; this live session never merges against an offline-loaded save so the two
-- id spaces never need to match bit for bit).
--
-- FIELD MAPPING, read straight off Update_SaveData's own bytecode (the ONLY place in this class
-- that writes LatestSaveData's socket-scoped leaves, confirmed by an exhaustive search of every
-- hash-suffixed struct-member write in the dump - see the header note on the `hasTimer`/`timerMode`
-- read-only verdict below):
--   LatestSaveData.PowerSocket_23_0F6755A14AF8031F65674CB6F8553D7A = GetPowerSocketID()
--   LatestSaveData.PluggedInDeviceAssetID_20_EDF3C1474C00B1AC29424D8B05460265 = <device changeable-
--     data AssetID, or "-1"-shaped literal when nothing is plugged in - matches
--     PowerSocketMapFeature's own "-1" = nothing convention>
--   LatestSaveData.HasTimer_9_3EE13A6A4C191C0D16A106BF214E9F81 = False        -- UNCONDITIONALLY
--   LatestSaveData.TimerMode_8_B4F1798A4364D14CC93168AD9E666B81 = 0 (NewEnumerator0) -- UNCONDITIONALLY
-- Both of the last two lines appear, unchanged, in BOTH branches of Update_SaveData's own
-- Attach/detach if-else (traced statement by statement) - there is no branch, no gate, no other
-- write site anywhere in this class that ever sets HasTimer_/TimerMode_ to anything else, and no
-- read of either leaf exists in this class either (a plain grep of every hash-suffixed name the
-- dump shows finds exactly these two write sites and nothing else). SavePowerSocketToWorldSave
-- (the actor's own "persist me now" entry point, gated on IsServer + IsSaveableGame) itself calls
-- Update_SaveData before handing off to the game mode's UpdateActorToWorldSave, so there is no way
-- to trigger a persist without also re-running this reset. This means: whatever a save file
-- carries for HasTimer_/TimerMode_ before the game's own logic next touches this socket is exactly
-- what OnLoadedFromSave-style code restores it to at load time (not traced here, inferred by
-- symmetry with every other area's load/save pairing), but the very next time ANYTHING calls
-- SavePowerSocketToWorldSave on it (plugging/unplugging a device, or any other save-triggering
-- event) both leaves are forced back to false/0 regardless of what a player or this module wrote.
-- `hasTimer`/`timerMode` are therefore READ-ONLY here: readable (LatestSaveData is a struct VALUE
-- embedded on the actor, indexed by its exact hash-suffixed leaf name exactly like
-- ButtonSaveData/SKILL_XP_FIELD elsewhere in this mod), but `powersockets.set` refuses any request
-- to change either, by name, rather than silently no-op or write something that the game's own
-- code would immediately discard. The full E_PowerTimerModes enum dump (9 real values plus E_MAX)
-- shows every enumerator is still an auto-generated "NewEnumeratorN" name with no meaningful
-- English label and, per the above, no evidenced way to ever observe anything but value 0 live -
-- so this module reports the raw byte and leaves interpretation to the caller rather than inventing
-- friendly names for modes nothing here can ever produce.
--
-- pluggedInDevice is read from the live PluggedInDevice ObjectProperty directly (an actor
-- reference, not an asset-id string) - when set, this module reports the plugged actor's own real
-- class name (ctx.classLabel), which needs no game-data catalog and is never wrong, unlike
-- resolving the save's stored asset id to a friendly name (PowerSocketDeviceResolver's job
-- offline). `powered` is a bonus read-only field off the confirmed `IsPowered()` function (present
-- on PowerSocket_ParentBP_C itself, per the dump).
return function(ctx)
    local PARENT_CLASS = "PowerSocket_ParentBP_C"
    -- Data-only fallback root list (see header) - empty today; every confirmed socket asset chains
    -- to PARENT_CLASS.
    local ADDITIONAL_ROOT_CLASSES = {}
    local ROOT_CLASSES = { PARENT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local HAS_TIMER_LEAF = "HasTimer_9_3EE13A6A4C191C0D16A106BF214E9F81"
    local TIMER_MODE_LEAF = "TimerMode_8_B4F1798A4364D14CC93168AD9E666B81"

    local function allSockets()
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
            end
        end
        return result
    end

    local function findSocket(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- Same explicit-if discipline buttons.lua documents (an `and/or` ternary silently loses a real
    -- `false` reading) - never collapsed to a one-liner.
    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    -- Struct-nested leaf read, same shape as buttons.lua's readPressedOnce: LatestSaveData is a
    -- struct VALUE on the actor, not a separate UObject, so it is indexed by the leaf's exact
    -- hash-suffixed name once the struct itself resolves.
    local function readSaveDataLeaf(socket, leaf)
        local okData, data = pcall(function() return socket.LatestSaveData end)
        if not okData or data == nil then return nil end
        local ok, value = pcall(function() return data[leaf] end)
        return ok, value
    end

    local function describeDevice(socket)
        local okDevice, device = pcall(function() return socket.PluggedInDevice end)
        if not okDevice or device == nil then return "nothing plugged in" end
        local okValid, valid = pcall(function() return device:IsValid() end)
        if not okValid or not valid then return "nothing plugged in" end
        local name = ctx.fullName(device)
        return name and ctx.classLabel(name) or "plugged in (unknown device)"
    end

    local function socketRows()
        local result = { __forceArray = true }
        for _, socket in ipairs(allSockets()) do
            if socket:IsValid() then
                local name = ctx.fullName(socket)
                if name then
                    local x, y, z = ctx.actorLocation(socket)
                    local okId, socketId = pcall(function() return socket:GetPowerSocketID() end)
                    local okHasTimer, hasTimerRaw = readSaveDataLeaf(socket, HAS_TIMER_LEAF)
                    local okTimerMode, timerModeRaw = readSaveDataLeaf(socket, TIMER_MODE_LEAF)
                    local okPowered, powered = pcall(function() return socket:IsPowered() end)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name),
                        socketId = (okId and type(socketId) == "string") and socketId or nil,
                        pluggedInDevice = describeDevice(socket),
                        hasTimer = boolOrNil(okHasTimer, hasTimerRaw),
                        timerMode = (okTimerMode and tonumber(timerModeRaw)) or nil,
                        powered = boolOrNil(okPowered, powered),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["powersockets.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { sockets = socketRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- No field on this feature has an evidenced live-settable path (see the header note on
    -- hasTimer/timerMode) - every request is refused, by name, rather than silently no-op'd.
    -- Kept as a real handler (not simply absent) so a future confirmed write path only needs a
    -- Lua change here, and so the reply is an honest, specific error rather than a generic
    -- "unknown command".
    ctx.handlers["powersockets.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change power sockets") end
            local rows = payload.sockets or {}
            local missingId, requestedId = nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local socket = row.id and findSocket(row.id)
                if socket then
                    if row.hasTimer ~= nil then requestedId = requestedId or row.id end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("power socket not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if requestedId then
                error("a socket's timer cannot be armed live - PowerSocket_ParentBP_C's only save "
                    .. "path (Update_SaveData) unconditionally resets HasTimer/TimerMode to false/0 "
                    .. "every time it runs, in both branches of its own bytecode, so nothing written "
                    .. "here would survive the next save; edit the save file directly instead: "
                    .. tostring(requestedId))
            end
            return nil
        end, respond)
    end
end
