-- Runs every case listed in tests/cases/manifest.lua against main.lua under the stub
-- environment in harness.lua. Exit code 0 when every check passes.
--
--   lua live-agent/AbioticEditorLiveAgentLua/tests/run.lua        (from the repo root)
--   lua run.lua                                                    (from this folder)

local thisFile = arg and arg[0] or "run.lua"
local testsDir = thisFile:match("^(.*)[/\\]run%.lua$") or "."
testsDir = testsDir .. "/"
local scriptsDir = testsDir .. "../Scripts/"

package.path = testsDir .. "?.lua;" .. package.path
local H = require("harness")
H.install()
H.hostSession()
H.load(scriptsDir)

-- Every module in areas/manifest.lua must have loaded cleanly; main.lua only logs a failure.
local newline = string.char(10)
for _, line in ipairs(H.printed) do
    if line:find("FAILED", 1, true) then H.check(false, line) end
    if line:find("area loaded", 1, true) then io.write(line .. newline) end
end

local manifest = dofile(testsDir .. "cases/manifest.lua")
for _, name in ipairs(manifest) do
    io.write("== " .. name .. newline)
    local case = dofile(testsDir .. "cases/" .. name .. ".lua")
    local ok, err = pcall(case, H)
    if not ok then H.check(false, name .. " raised: " .. tostring(err)) end
end

local allPassed = H.summary()
os.exit(allPassed and 0 or 1)
