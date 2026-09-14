-- Host-side FName array edits. UE4SS LuaUObject.cpp push_arrayproperty accepts Lua
-- tables and copies each FName into a new native TArray. Snapshot names first so
-- assigning the destination never invalidates values still needed from the old array.
local M = {}
function M.prepare(current, additions, removals)
    local remove, seen, result = {}, {}, {}
    local function checkedName(id)
        if type(id) ~= "string" or id == "" then error("row names must be non-empty strings") end
        local name = FName(id, EFindName.FNAME_Find)
        if name:ToString() == "None" then error("unknown row name: " .. id) end
        return name
    end
    for _, id in ipairs(removals or {}) do checkedName(id); remove[id] = true end
    for i = 1, #current do
        local id = current[i]:ToString()
        if not remove[id] and not seen[id] then
            seen[id] = true; result[#result + 1] = FName(id, EFindName.FNAME_Find)
        end
    end
    for _, id in ipairs(additions or {}) do
        local name = checkedName(id)
        if not seen[id] then seen[id] = true; result[#result + 1] = name end
    end
    return result
end
return M
