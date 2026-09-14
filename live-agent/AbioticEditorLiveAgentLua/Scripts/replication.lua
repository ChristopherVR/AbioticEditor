-- Direct writes must notify push-model replication as well as local RepNotify.
-- UNetPushModelHelpers.MarkPropertyDirty takes an object and its property FName.
local M = {}
function M.requireHelper()
    local helper = StaticFindObject("/Script/Engine.Default__NetPushModelHelpers")
    if not helper or not helper:IsValid() then error("replication notification is unavailable") end
    return helper
end
function M.available()
    return pcall(M.requireHelper)
end
function M.mark(helper, object, property)
    helper:MarkPropertyDirty(object, FName(property, EFindName.FNAME_Find))
end
return M
