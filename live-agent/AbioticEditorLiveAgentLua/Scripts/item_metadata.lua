-- Exact fields from Abiotic_InventoryChangeableDataStruct and native FDynamicProperty.
-- UE4SS push_structproperty/push_arrayproperty convert Lua tables using reflected types:
-- https://github.com/UE4SS-RE/RE-UE4SS/blob/main/UE4SS/src/LuaType/LuaUObject.cpp
-- Snapshot scalar values before any batch writes. Keeping UScriptStruct/TArray references
-- would alias the source and destroy the second half of a swap when the first is written.
local M = {}
local DYNAMIC = "DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7"
local TAGS = "GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1"
local ITEM = "ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B"
local VARIANT = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636"
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
function M.read(data, slot)
    if not data or not data[DYNAMIC] or not data[TAGS] then return nil end
    local properties = { __forceArray = true }
    for i = 1, #data[DYNAMIC] do
        local entry = data[DYNAMIC][i]
        local name = type(entry.Key) == "number" and text(enum():GetNameByValue(entry.Key)) or text(entry.Key)
        properties[#properties + 1] = { key = name, value = entry.Value }
    end
    local tags = data[TAGS]
    if not tags.GameplayTags or not tags.ParentTags then return nil end
    return { dynamicProperties = properties, gameplayTags = readTags(tags.GameplayTags),
        parentGameplayTags = readTags(tags.ParentTags),
        itemDataTable = slot and path(slot[ITEM].DataTable),
        variantDataTable = data[VARIANT] and path(data[VARIANT].DataTable) }
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
        or not data[DYNAMIC] or not data[TAGS] then error("complete item metadata is unavailable") end
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
    data[DYNAMIC] = prepared.properties
    data[TAGS].GameplayTags = prepared.tags
    data[TAGS].ParentTags = prepared.parents
end
function M.clear(data)
    if data[DYNAMIC] then data[DYNAMIC] = {} end
    if data[TAGS] then
        data[TAGS].GameplayTags = {}
        data[TAGS].ParentTags = {}
    end
end
return M
