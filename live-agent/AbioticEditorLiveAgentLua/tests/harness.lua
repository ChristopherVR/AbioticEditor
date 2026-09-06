-- Stub UE4SS environment for exercising main.lua and every areas/ module WITHOUT the game.
--
-- What this proves: that a handler resolves the objects it expects, reads the fields it expects,
-- converts values into something json.lua can send, and honours host gating - the exact class of
-- bug found live in round 76 (an FString userdata left in a reply, a constructor that is not a
-- UE4SS global, a five-parameter blueprint function called with four). What it cannot prove: that
-- a property or function actually exists on the real class - fake objects here are built from the
-- class dumps in tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, so keep them honest.
--
-- Usage (from the repo root, with a Lua 5.4 interpreter):
--   lua live-agent/AbioticEditorLiveAgentLua/tests/run.lua
--
-- Fake object model: `H.object(className, fields, methods)` returns a table that answers
-- IsValid()/GetFullName() like a UE4SS UObject, exposes `fields` as properties, and `methods` as
-- callable functions (any OTHER method name is nil, so a handler calling a function the fake does
-- not declare fails loudly - mirroring "attempt to call a nil value" / "UFunction expected" live).
-- Register objects with H.world.add(obj) so FindAllOf/FindFirstOf can see them; register
-- StaticFindObject targets with H.world.static(path, obj).

local H = {}

-- ---------- fake engine value types ----------

local FNameMeta = {}
FNameMeta.__index = FNameMeta
function FNameMeta:ToString() return self.__name end
function FNameMeta:GetComparisonIndex() return self.__name == "None" and 0 or 1 end
FNameMeta.__eq = function(a, b) return a.__name == b.__name end
FNameMeta.__tostring = function(self) return "FName(" .. self.__name .. ")" end

function H.fname(text)
    return setmetatable({ __name = tostring(text) }, FNameMeta)
end

-- FString/FText userdata: also needs :ToString(), and is deliberately NOT a plain string so a
-- handler that forgets to convert it hands json.lua a table it refuses (like the real userdata).
local FStringMeta = {}
FStringMeta.__index = FStringMeta
function FStringMeta:ToString() return self.__text end
FStringMeta.__tostring = function(self) return "FString(" .. self.__text .. ")" end
FStringMeta.__eq = function(a, b) return a.__text == b.__text end

function H.fstring(text)
    return setmetatable({ __text = tostring(text), __fstring = true }, FStringMeta)
end

-- An out-param array element as UE4SS hands it back: `param:get()` is the value.
function H.outParam(value)
    return { get = function() return value end }
end

function H.vector(x, y, z) return { X = x, Y = y, Z = z } end
function H.rotator(p, y, r) return { Pitch = p, Yaw = y, Roll = r } end

-- ---------- fake UObjects ----------

local ObjectMeta = {}
function ObjectMeta.__index(self, key)
    local fields = rawget(self, "__fields")
    if fields[key] ~= nil then return fields[key] end
    local methods = rawget(self, "__methods")
    if methods[key] ~= nil then return methods[key] end
    if key == "IsValid" then return function() return rawget(self, "__valid") ~= false end end
    if key == "GetFullName" then return function() return rawget(self, "__fullName") end end
    if key == "IsA" then return function(_, cls) return cls == rawget(self, "__class") end end
    return nil
end
function ObjectMeta.__newindex(self, key, value)
    rawget(self, "__fields")[key] = value
    rawget(self, "__writes")[key] = (rawget(self, "__writes")[key] or 0) + 1
end

local objectCounter = 0
function H.object(className, fields, methods)
    objectCounter = objectCounter + 1
    local obj = {
        __class = className,
        __fullName = className .. " /Game/Maps/Facility.Facility:PersistentLevel." .. className .. "_" .. objectCounter,
        __fields = fields or {},
        __methods = methods or {},
        __writes = {},
        __calls = {},
        __valid = true,
    }
    -- Every declared method is wrapped so tests can assert it was called.
    for name, fn in pairs(obj.__methods) do
        obj.__methods[name] = function(self, ...)
            local calls = rawget(obj, "__calls")
            calls[name] = (calls[name] or 0) + 1
            return fn(self, ...)
        end
    end
    return setmetatable(obj, ObjectMeta)
end

function H.calls(obj, name) return rawget(obj, "__calls")[name] or 0 end
function H.writes(obj, name) return rawget(obj, "__writes")[name] or 0 end
function H.field(obj, name) return rawget(obj, "__fields")[name] end

-- ---------- fake world (what FindAllOf/FindFirstOf/StaticFindObject see) ----------

H.world = { objects = {}, statics = {} }

function H.world.reset() H.world.objects = {}; H.world.statics = {} end
function H.world.add(obj) table.insert(H.world.objects, obj); return obj end
function H.world.static(path, obj) H.world.statics[path] = obj; return obj end

local function matches(obj, className)
    local cls = rawget(obj, "__class")
    if cls == className then return true end
    local bases = rawget(obj, "__fields").__bases
    if bases then
        for _, b in ipairs(bases) do if b == className then return true end end
    end
    return false
end

-- ---------- UE4SS globals ----------

function H.install()
    _G.FindAllOf = function(className)
        local found = {}
        for _, obj in ipairs(H.world.objects) do
            if matches(obj, className) then table.insert(found, obj) end
        end
        if #found == 0 then return nil end
        return found
    end
    _G.FindFirstOf = function(className)
        for _, obj in ipairs(H.world.objects) do
            if matches(obj, className) then return obj end
        end
        return nil
    end
    _G.StaticFindObject = function(path) return H.world.statics[path] end
    _G.ExecuteInGameThread = function(fn) fn() end
    _G.LoopAsync = function() end
    _G.RegisterHook = function() end
    _G.NAME_None = H.fname("None")
    _G.EFindName = { FNAME_Find = 0, FNAME_Add = 1 }
    _G.FName = function(text) return H.fname(text) end
    -- FText(string), unlike FVector()/FRotator(), IS a real UE4SS Lua global (bases.lua relies on
    -- it, with a plain-string fallback for older UE4SS builds) - the resulting value needs
    -- :ToString() same as FString, so it reuses the same fake userdata type.
    _G.FText = function(text) return H.fstring(text) end
    _G.CreateInvalidObject = function() return H.object("Invalid", {}, {}); end
    _G.IsValid = function(obj) return obj ~= nil and obj:IsValid() end
    -- The ONLY globals a real UE4SS mod has for structs are the ones above: there is no FVector()
    -- or FRotator() constructor (found live, round 76), so those stay undefined on purpose.
    H.printed = {}
    _G.print = function(...) local parts = {} for i = 1, select("#", ...) do parts[i] = tostring(select(i, ...)) end table.insert(H.printed, table.concat(parts, " ")) end

    -- UEHelpers (UE4SS's bundled shared module) - the pieces main.lua uses.
    H.playerStates = {}
    H.playerController = nil
    H.gameState = nil
    H.gameMode = nil
    package.preload["UEHelpers"] = function()
        return {
            GetPlayerController = function() return H.playerController or H.object("PlayerController", { __valid = false }) end,
            GetAllPlayerStates = function() return H.playerStates end,
            GetWorld = function()
                return H.object("World", { AuthorityGameMode = H.gameMode, GameState = H.gameState })
            end,
            GetGameStateBase = function() return H.gameState end,
            GetGameModeBase = function() return H.gameMode end,
        }
    end
    -- os.getenv is only used to build the ipc path; keep it deterministic.
    local realGetenv = os.getenv
    os.getenv = function(name) if name == "LOCALAPPDATA" then return "." end return realGetenv(name) end
end

-- ---------- loading the mod ----------

function H.load(scriptsDir)
    package.path = scriptsDir .. "?.lua;" .. package.path
    package.loaded["json"] = nil
    dofile(scriptsDir .. "main.lua")
    H.mod = _G.AbioticEditorLiveAgentLua
    assert(H.mod and H.mod.handlers, "main.lua did not export AbioticEditorLiveAgentLua.handlers")
    H.json = require("json")
    return H.mod
end

-- ---------- driving handlers ----------

-- Calls a handler exactly the way the mailbox loop does and returns the decoded reply table
-- ({ ok = true, result = ... } or { ok = false, error = "..." }). The reply is round-tripped
-- through json.lua so an unencodable value fails here the way it fails live.
function H.dispatch(cmd, payload)
    local handler = H.mod.handlers[cmd]
    if not handler then return { ok = false, error = "unknown command '" .. cmd .. "'" } end
    local reply
    local function respond(result, err)
        if err then
            reply = { ok = false, error = tostring(err) }
            return
        end
        local okEncode, encoded = pcall(H.json.encode, { ok = true, result = result })
        if okEncode then
            reply = H.json.decode(encoded)
        else
            reply = { ok = false, error = "unencodable reply: " .. tostring(encoded) }
        end
    end
    local ok, err = pcall(handler, payload or {}, respond)
    if not ok then return { ok = false, error = "dispatch error: " .. tostring(err) } end
    assert(reply ~= nil, cmd .. " never called respond()")
    return reply
end

-- ---------- assertions ----------

local failures = 0
local passes = 0

function H.check(condition, message)
    if condition then
        passes = passes + 1
    else
        failures = failures + 1
        io.write("  FAIL: " .. tostring(message) .. "\n")
    end
end

function H.eq(actual, expected, message)
    H.check(actual == expected, (message or "") .. " (expected " .. tostring(expected) .. ", got " .. tostring(actual) .. ")")
end

function H.ok(reply, message)
    H.check(reply.ok == true, (message or "reply ok") .. (reply.ok and "" or (": " .. tostring(reply.error))))
    return reply.result
end

function H.fails(reply, needle, message)
    local text = tostring(reply.error or "")
    H.check(reply.ok == false and text:find(needle, 1, true) ~= nil,
        (message or "expected failure") .. " containing '" .. needle .. "', got ok=" .. tostring(reply.ok) .. " error=" .. text)
end

function H.summary()
    io.write(string.format("%d checks passed, %d failed\n", passes, failures))
    return failures == 0
end

-- ---------- common fixtures ----------

-- A hosting session: one local player with a pawn carrying the stats/inventory/progression
-- components every player area expects. Returns the pawn.
function H.hostSession()
    H.world.reset()
    local function inventory(count, kind, extraFields, extraMethods)
        local slots = {}
        for i = 1, count do
            slots[i] = {
                ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
                ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = {
                    CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0,
                    CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0,
                    MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0,
                    PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9 = H.fstring(""),
                    DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = {},
                },
            }
        end
        local fields = { CurrentInventory = slots, __kind = kind }
        if extraFields then for k, v in pairs(extraFields) do fields[k] = v end end
        local methods = { OnRep_CurrentInventory = function() end }
        if extraMethods then for k, v in pairs(extraMethods) do methods[k] = v end end
        return H.object("Abiotic_InventoryComponent_C", fields, methods)
    end
    local progression = H.object("Abiotic_CharacterProgressionComponent_C", {
        CharacterSkills_Keys = {}, CharacterSkills_Values = {},
        RecipesUnlockedArray = { H.fname("recipe_bandage") },
        EmailsRead = { H.fname("Email_Crossbow") }, JournalEntries = {}, FishCaughtArray = {},
        ItemsPickedUpArray = { H.fname("scrap_metal") }, CraftedItems = {}, CurrentMaps = { H.fname("map_office3") },
        Local_AllCompendiumEntries = {},
        Compendium_ExplorationSections = { H.fname("Compendium_Office") },
        Compendium_EmailSections = {},
        Compendium_NarrativeNPCSections = {},
        -- Round 77: read-only through general.get - see general.lua's header comment.
        Traits = { H.fname("Trait_Chef") },
    }, {
        Request_UnlockNewRecipe = function() end,
        Server_AddEmailToReadList = function() end,
        Server_AddNoteToJournal = function() end,
        Request_UnlockNewFish = function() end,
        Server_CheckNewItemPickedUp = function() end,
        Server_AddMapToJournal = function() end,
        Request_UnlockCompendiumSection = function() end,
    })
    -- Round 77: TransmogVisibility/DisableTransmogArray + the two Request_ RPCs, confirmed real
    -- on Abiotic_TransmogInventoryComp_C (see areas/transmog.lua's header comment).
    local tmog = inventory(6, "transmog", {
        TransmogVisibility = { true, true, true, true, true, true, true, true, true, true, true, true },
        DisableTransmogArray = { true, true, true, true, true, true, true, true, true, true, true, true, true },
    }, {
        Request_ChangeTransmogVisibilityFlag = function(self, index, item)
            rawget(self, "__fields").TransmogVisibility[index + 1] = item
        end,
        Request_ChangeDisableTransmogArray = function(self, index, item)
            rawget(self, "__fields").DisableTransmogArray[index + 1] = item
        end,
    })
    local pawn = H.object("Abiotic_PlayerCharacter_C", {
        CurrentHunger = 50, CurrentThirst = 60, CurrentSanity = 100, CurrentFatigue = 10, CurrentContinence = 75,
        CurrentMoney = 1000, CurrentHealth_Head = 80, CurrentHealth_Torso = 100, CurrentHealth_LeftArm = 100,
        CurrentHealth_RightArm = 100, CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
        CharacterInventory = inventory(30, "backpack"),
        CharacterEquipSlotInventory = inventory(13, "equip"),
        CharacterHotbarInventory = inventory(8, "hotbar"),
        TmogInventory = tmog,
        CharacterProgressionComponent = progression,
    }, {
        HasAuthority = function() return true end,
        OnRep_CurrentHealth = function() end,
        Request_ModifyMoney = function() end,
        K2_GetActorLocation = function() return H.vector(100, 200, 300) end,
        K2_GetActorRotation = function() return H.rotator(0, 90, 0) end,
        K2_TeleportTo = function(self, location) rawget(self, "__methods").K2_GetActorLocation = function() return { X = location.X, Y = location.Y, Z = location.Z } end return true end,
    })
    local state = H.object("Abiotic_PlayerState_C", {
        PawnPrivate = pawn, PlayerNamePrivate = H.fstring("Tribbes"), UniquePlayerID = H.fstring("76561197993781479"),
        -- Round 77: general.lua reads/writes this directly (no OnRep_PhD exists) for BACKGROUND.
        PhD = H.fname("PhD_HumanBio"),
    })
    -- APawn.PlayerState (base-engine): general.lua's getPlayerState() reads this off the PAWN for
    -- the "no playerId given" (local player) case, the same property main.lua's own
    -- localPlayerId() already reads off the CONTROLLER for a different purpose.
    pawn.PlayerState = state
    H.playerStates = { state }
    H.playerController = H.object("Abiotic_PlayerController_C", {
        MyPlayerCharacter = pawn, PlayerState = state,
        ActiveLevelName = H.fname("Facility"), TerminalRespawnID = H.fname("E57CB02C4853F46D2BB7CA80303EB6A3"),
    })
    H.gameMode = H.object("Abiotic_Survival_GameMode_C", { AI_Director = nil })
    H.gameState = H.object("Abiotic_Survival_GameState_C", {
        PlayerArray = { state }, WorldFlags = { H.fname("Office_PowerOn") },
        CurrentQuest = { RowName = H.fname("quest_RES_EndInterlude") },
        GlobalRecipesUnlocked = { H.fname("recipe_bandage") },
        GlobalRecipesResearched = {},
        GlobalItemsPickedUp = { H.fname("scrap_metal") },
        GlobalEmailsRead = { H.fname("Email_Crossbow") },
        GlobalJournalEntries = {},
        GlobalCompendiumEmail = {},
        GlobalCompendiumNarrative = {},
        GlobalCompendiumExploration = { H.fname("Compendium_Office") },
    }, { OnRep_CurrentQuest = function() end })
    H.world.add(pawn)
    H.world.add(H.gameState)
    return pawn
end

-- The same session as seen from a joined client: no authority anywhere.
function H.clientSession()
    local pawn = H.hostSession()
    rawget(pawn, "__methods").HasAuthority = function() return false end
    H.gameMode = nil
    return pawn
end

return H
