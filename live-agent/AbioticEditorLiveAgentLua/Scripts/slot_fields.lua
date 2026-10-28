-- Item slot and item-instance field names. Community Update 5 made the slot structs native
-- (AbioticInventoryItemSlot / AbioticInventoryChangeableData) with plain field names; older game
-- builds used Blueprint structs whose fields carry compiler hash suffixes. Each field resolves to
-- whichever spelling the running game has, native first, and the choice is cached: one game build
-- uses one layout, and container lists touch these fields for every slot in the world.
local M = {}

local LEGACY = {
    ItemDataTable = "ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B",
    ChangeableData = "ChangeableData_12_2B90E1F74F648135579D39A49F5A2313",
    CurrentStack = "CurrentStack_9_D443B69044D640B0989FD8A629801A49",
    CurrentAmmoInMagazine = "CurrentAmmoInMagazine_12_D68C190F4B2FA78A4B1D57835B95C53D",
    CurrentItemDurability = "CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8",
    MaxItemDurability = "MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B",
    CurrentLiquid = "CurrentLiquid_19_3E1652F448223AAE5F405FB510838109",
    LiquidLevel = "LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A",
    PlayerMadeString = "PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9",
    AssetID = "AssetID_25_06DB7A12469849D19D5FC3BA6BEDEEAB",
    DynamicState = "DynamicState_39_7597AC6549E292B931C61BB13C9E42EB",
    TextureVariantRow = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636",
    DynamicProperties = "DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7",
    GameplayTags = "GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1",
}
M.LEGACY = LEGACY

local chosen = {}
local legacyLayout = nil

local function present(struct, name)
    local ok, value = pcall(function() return struct[name] end)
    return ok and value ~= nil
end

---The spelling of `field` the running game uses on `struct`. A field found under neither spelling
---follows the layout already seen on other fields (native until one is seen).
function M.name(struct, field)
    local cached = chosen[field]
    if cached then return cached end
    local legacy = LEGACY[field]
    if struct ~= nil then
        if present(struct, field) then chosen[field] = field; legacyLayout = false; return field end
        if legacy and present(struct, legacy) then chosen[field] = legacy; legacyLayout = true; return legacy end
    end
    if legacyLayout and legacy then return legacy end
    return field
end

---Reads `field` from `struct`; nil when the struct has neither spelling.
function M.get(struct, field)
    if struct == nil then return nil end
    local name = M.name(struct, field)
    local ok, value = pcall(function() return struct[name] end)
    if ok then return value end
    return nil
end

---Writes `field` on `struct` under the spelling the running game uses.
function M.set(struct, field, value)
    struct[M.name(struct, field)] = value
end

---True when a row handle still carries a DataTable reference (older builds); native typed
---handles have only RowName.
function M.handleHasTable(handle)
    return present(handle, "DataTable")
end

---Forgets the cached layout (tests switch between fixtures of both layouts).
function M.reset()
    chosen = {}
    legacyLayout = nil
end

return M
