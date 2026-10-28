-- Fields of the item-instance data (native AbioticInventoryChangeableData since Community Update 5,
-- Abiotic_InventoryChangeableDataStruct before; see slot_fields.lua) and native FDynamicProperty.
-- UE4SS push_structproperty/push_arrayproperty convert Lua tables using reflected types:
-- https://github.com/UE4SS-RE/RE-UE4SS/blob/main/UE4SS/src/LuaType/LuaUObject.cpp
-- Snapshot scalar values before any batch writes. Keeping UScriptStruct/TArray references
-- would alias the source and destroy the second half of a swap when the first is written.
local M = {}
local fields = require("slot_fields")
local function text(value)
    if type(value) == "string" then return value end
    return value:ToString()
end
local function enum()
    local value = StaticFindObject("/Script/AbioticFactor.EDynamicProperty")
    if not value or not value:IsValid() then error("dynamic property enum is unavailable") end
    return value
end
local function path(object)
    if not object or not object:IsValid() then return nil end
    return object:GetFullName():match("^%S+%s+(.+)$")
end
local function readTags(array)
    local result = { __forceArray = true }
    for i = 1, #array do result[#result + 1] = text(array[i].TagName) end
    return result
end
local function tablePath(handle)
    -- Native typed row handles (Community Update 5) carry no DataTable reference.
    if not handle or not fields.handleHasTable(handle) then return nil end
    return path(handle.DataTable)
end
function M.read(data, slot)
    local dynamic, tags = fields.get(data, "DynamicProperties"), fields.get(data, "GameplayTags")
    if not data or not dynamic or not tags then return nil end
    local properties = { __forceArray = true }
    for i = 1, #dynamic do
        local entry = dynamic[i]
        local name = type(entry.Key) == "number" and text(enum():GetNameByValue(entry.Key)) or text(entry.Key)
        properties[#properties + 1] = { key = name, value = entry.Value }
    end
    if not tags.GameplayTags or not tags.ParentTags then return nil end
    return { dynamicProperties = properties, gameplayTags = readTags(tags.GameplayTags),
        parentGameplayTags = readTags(tags.ParentTags),
        itemDataTable = slot and tablePath(fields.get(slot, "ItemDataTable")),
        variantDataTable = tablePath(fields.get(data, "TextureVariantRow")) }
end
local function prepareTags(values)
    if type(values) ~= "table" then error("invalid item gameplay tags") end
    local result = {}
    for _, value in ipairs(values) do
        if type(value) ~= "string" or value == "" then error("invalid item gameplay tag") end
        local name = FName(value, EFindName.FNAME_Find)
        if text(name) == "None" then error("unknown item gameplay tag: " .. value) end
        result[#result + 1] = { TagName = name }
    end
    return result
end
function M.prepare(data, metadata)
    if metadata == nil then return nil end
    if type(metadata) ~= "table" or type(metadata.dynamicProperties) ~= "table"
        or not fields.get(data, "DynamicProperties") or not fields.get(data, "GameplayTags") then
        error("complete item metadata is unavailable")
    end
    local properties, enumValues, seen = {}, {}, {}
    if #metadata.dynamicProperties > 0 then
        enum():ForEachName(function(name, value) enumValues[text(name)] = value end)
    end
    for _, entry in ipairs(metadata.dynamicProperties) do
        local value = entry.value
        if type(entry.key) ~= "string" or enumValues[entry.key] == nil then error("unknown dynamic item property") end
        if seen[entry.key] then error("duplicate dynamic item property") end
        if type(value) ~= "number" or value % 1 ~= 0 or value < -2147483648 or value > 2147483647 then error("invalid dynamic item value") end
        if entry.key == "EDynamicProperty::WeaponCoating" and value < -1 then error("invalid weapon coating") end
        if entry.key == "EDynamicProperty::CoatingDurability" and value < 0 then error("invalid coating durability") end
        seen[entry.key] = true
        properties[#properties + 1] = { Key = enumValues[entry.key], Value = value }
    end
    for _, field in ipairs({ "itemDataTable", "variantDataTable" }) do
        if metadata[field] ~= nil and (type(metadata[field]) ~= "string" or metadata[field]:sub(1, 1) ~= "/") then
            error("invalid " .. field)
        end
    end
    return { properties = properties, tags = prepareTags(metadata.gameplayTags),
        parents = prepareTags(metadata.parentGameplayTags or {}) }
end
function M.apply(data, prepared)
    if not prepared then return end
    fields.set(data, "DynamicProperties", prepared.properties)
    local tags = fields.get(data, "GameplayTags")
    tags.GameplayTags = prepared.tags
    tags.ParentTags = prepared.parents
end
function M.clear(data)
    if fields.get(data, "DynamicProperties") then fields.set(data, "DynamicProperties", {}) end
    local tags = fields.get(data, "GameplayTags")
    if tags then
        tags.GameplayTags = {}
        tags.ParentTags = {}
    end
end
return M
