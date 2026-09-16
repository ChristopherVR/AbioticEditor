-- Mirrors AddUpgrade's tag, replication, component refresh and save steps without
-- marshaling BenchUpgradeRowHandle through the native function bridge.
local M = {}
local replication = require("replication")
local SAVED_TAGS = "GameplayTags_45_1A018E824E25CC7BA608A6B2835209A1"
local function readable(container)
    return container and container.GameplayTags and container.ParentTags
end
function M.available(obj)
    local ok, value = pcall(function()
        return replication.available() and readable(obj.UpgradeTagContainer) ~= nil
            and readable(obj.ChangeableData[SAVED_TAGS]) ~= nil
    end)
    return ok and value == true
end
local function prepare(container, tag, installed)
    local values, seen = {}, {}
    for i = 1, #container.GameplayTags do
        local name = container.GameplayTags[i].TagName:ToString()
        if name ~= tag and not seen[name] then values[#values + 1] = name; seen[name] = true end
    end
    if installed then values[#values + 1] = tag end
    local tags, parents, parentSeen = {}, {}, {}
    for _, name in ipairs(values) do
        local fname = FName(name, EFindName.FNAME_Find)
        if fname:ToString() == "None" then error("unknown bench gameplay tag") end
        tags[#tags + 1] = { TagName = fname }
        local parent = name:match("^(.*)%.[^.]+$")
        while parent do
            if not parentSeen[parent] then
                local parentName = FName(parent, EFindName.FNAME_Find)
                if parentName:ToString() == "None" then error("unknown bench gameplay parent tag") end
                parentSeen[parent] = true
                parents[#parents + 1] = { TagName = parentName }
            end
            parent = parent:match("^(.*)%.[^.]+$")
        end
    end
    return { tags = tags, parents = parents }
end
function M.set(obj, row, installed)
    if not M.available(obj) then error("bench tag editing is unavailable on this runtime") end
    -- All three Item Transporter table rows share one gameplay tag in DT_BenchUpgrades.
    local tag = "BenchUpgrade." .. (row:match("^ItemTransporter") and "ItemTransporter" or row)
    local helper = replication.requireHelper()
    local saved = obj.ChangeableData[SAVED_TAGS]
    local currentPlan, savedPlan = prepare(obj.UpgradeTagContainer, tag, installed), prepare(saved, tag, installed)
    obj.UpgradeTagContainer.GameplayTags = currentPlan.tags
    obj.UpgradeTagContainer.ParentTags = currentPlan.parents
    saved.GameplayTags = savedPlan.tags
    saved.ParentTags = savedPlan.parents
    replication.mark(helper, obj, "UpgradeTagContainer")
    replication.mark(helper, obj, "ChangeableData")
    obj:FlushNetDormancy()
    obj:OnRep_UpgradeTagContainer()
    obj:SaveDeployable()
end
return M
