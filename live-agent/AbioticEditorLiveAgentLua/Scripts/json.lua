-- A deliberately minimal JSON encode/decode for exactly the shapes the live-editing protocol
-- sends: a flat object of string/number/boolean/null/nested-object values, or a flat array of
-- such objects. Mirrors live-agent/Shared/JsonLine.h's own scope-limiting reasoning: this avoids
-- depending on a bundled Lua JSON library that may or may not exist in every UE4SS install, for a
-- handful of known shapes this file fully controls.
--
-- Wire property names are camelCase (see docs/reference/live-editing-protocol.md), matching what
-- AbioticEditorLiveAgentHelper forwards verbatim from the .NET side.

local json = {}

-- ===== Encode =====

local function encodeString(s)
    local escaped = s:gsub('[%c"\\]', function(c)
        if c == '"' then return '\\"'
        elseif c == '\\' then return '\\\\'
        elseif c == '\n' then return '\\n'
        elseif c == '\r' then return '\\r'
        elseif c == '\t' then return '\\t'
        else return string.format('\\u%04x', string.byte(c))
        end
    end)
    return '"' .. escaped .. '"'
end

local function isArray(t)
    -- Checked first, and short-circuits the heuristic below: decodeArray always sets this, and
    -- without checking it first the marker key itself inflates the heuristic's own key count by
    -- one, making every decoded array (forced or not) fail the "t[i] present for i=1..count"
    -- check for its own last real element. Caught by live-agent's own Lua interpreter test.
    if t.__forceArray then return true end
    local count = 0
    for _ in pairs(t) do count = count + 1 end
    if count == 0 then return false end
    for i = 1, count do
        if t[i] == nil then return false end
    end
    return true
end

local encodeValue

local function encodeObject(t)
    local parts = {}
    for key, value in pairs(t) do
        if key ~= "__forceArray" then
            table.insert(parts, encodeString(tostring(key)) .. ":" .. encodeValue(value))
        end
    end
    return "{" .. table.concat(parts, ",") .. "}"
end

local function encodeArray(t)
    local parts = {}
    for _, value in ipairs(t) do
        table.insert(parts, encodeValue(value))
    end
    return "[" .. table.concat(parts, ",") .. "]"
end

encodeValue = function(value)
    local valueType = type(value)
    if value == nil then return "null"
    elseif valueType == "string" then return encodeString(value)
    elseif valueType == "number" then
        if value == math.floor(value) and math.abs(value) < 1e15 then
            return string.format("%d", value)
        end
        return tostring(value)
    elseif valueType == "boolean" then return tostring(value)
    elseif valueType == "table" then
        if isArray(value) then return encodeArray(value) else return encodeObject(value) end
    else
        error("json.encode: unsupported type " .. valueType)
    end
end

function json.encode(value)
    return encodeValue(value)
end

-- ===== Decode =====
-- A small recursive-descent parser, deliberately matching the shapes live-agent/Shared/JsonLine.h
-- parses: objects, arrays, strings, numbers, booleans, null - one level of nesting is all this
-- protocol ever sends.

local function skipWhitespace(s, pos)
    local _, newPos = s:find("^%s*", pos)
    return newPos + 1
end

local decodeValue

local function decodeString(s, pos)
    assert(s:sub(pos, pos) == '"', "expected '\"' at " .. pos)
    pos = pos + 1
    local parts = {}
    while s:sub(pos, pos) ~= '"' do
        local c = s:sub(pos, pos)
        if c == "" then error("unterminated string") end
        if c == "\\" then
            local escaped = s:sub(pos + 1, pos + 1)
            if escaped == "n" then table.insert(parts, "\n")
            elseif escaped == "r" then table.insert(parts, "\r")
            elseif escaped == "t" then table.insert(parts, "\t")
            else table.insert(parts, escaped)
            end
            pos = pos + 2
        else
            table.insert(parts, c)
            pos = pos + 1
        end
    end
    return table.concat(parts), pos + 1
end

local function decodeNumber(s, pos)
    local numberText, newPos = s:match("^([%-%+%d%.eE]+)()", pos)
    assert(numberText, "expected a number at " .. pos)
    return tonumber(numberText), newPos
end

local function decodeObject(s, pos)
    local result = {}
    pos = pos + 1 -- '{'
    pos = skipWhitespace(s, pos)
    if s:sub(pos, pos) == "}" then return result, pos + 1 end
    while true do
        pos = skipWhitespace(s, pos)
        local key
        key, pos = decodeString(s, pos)
        pos = skipWhitespace(s, pos)
        assert(s:sub(pos, pos) == ":", "expected ':' at " .. pos)
        pos = pos + 1
        local value
        value, pos = decodeValue(s, pos)
        result[key] = value
        pos = skipWhitespace(s, pos)
        local c = s:sub(pos, pos)
        if c == "," then pos = pos + 1
        elseif c == "}" then return result, pos + 1
        else error("expected ',' or '}' at " .. pos)
        end
    end
end

local function decodeArray(s, pos)
    local result = { __forceArray = true }
    pos = pos + 1 -- '['
    pos = skipWhitespace(s, pos)
    if s:sub(pos, pos) == "]" then return result, pos + 1 end
    local index = 1
    while true do
        local value
        value, pos = decodeValue(s, pos)
        result[index] = value
        index = index + 1
        pos = skipWhitespace(s, pos)
        local c = s:sub(pos, pos)
        if c == "," then pos = pos + 1
        elseif c == "]" then return result, pos + 1
        else error("expected ',' or ']' at " .. pos)
        end
    end
end

decodeValue = function(s, pos)
    pos = skipWhitespace(s, pos)
    local c = s:sub(pos, pos)
    if c == "{" then return decodeObject(s, pos)
    elseif c == "[" then return decodeArray(s, pos)
    elseif c == '"' then return decodeString(s, pos)
    elseif s:sub(pos, pos + 3) == "true" then return true, pos + 4
    elseif s:sub(pos, pos + 4) == "false" then return false, pos + 5
    elseif s:sub(pos, pos + 3) == "null" then return nil, pos + 4
    else return decodeNumber(s, pos)
    end
end

function json.decode(text)
    local value = decodeValue(text, 1)
    return value
end

return json
