-- ===== World bases / deployables (AbioticDeployed_ParentBP_C) =====
-- Round 76. Every player-placed object (benches, furniture, defenses, containers) derives from
-- this one blueprint class - confirmed from the game's own pak layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragment "AbioticDeployed_ParentBP"):
--   AlternativeObjectName : FTextProperty   -- the player-given custom name (file: CustomTextDisplay_)
--   CurrentDurability / MaxDurability : FDoubleProperty, with OnRep_CurrentDurability/OnRep_MaxDurability
--   DestroyDeployable(), GetItemNameText() functions
-- FindAllOf("AbioticDeployed_ParentBP_C") is hierarchy-inclusive (confirmed live already by
-- containers.list, which finds every Deployed_Container_* subclass through the narrower
-- Deployed_Container_ParentBP_C the same way), so this one scan covers every deployable a base
-- is built from - no per-subclass enumeration needed.
--
-- ===== Bench upgrades (round 77, closing the round-76 gap) =====
-- Round 76 reported supportsBenchUpgrades = false because no function library could enumerate a
-- real BenchUpgradeRowHandle the way flags.lua/world.lua enumerate WorldFlagRowHandle/
-- WeatherEventRowHandle. Re-checked this round against AbioticDeployed_CraftingBench_ParentBP_C's
-- own class layout (LiveClassPropsProbe, fragment "BenchUpgrade") instead of a function library,
-- and it carries everything needed directly:
--   SupportsUpgrades : FBoolProperty        -- whether THIS deployable can take upgrades at all
--   UpgradeTagContainer : FStructProperty   -- read via the functions below, not parsed directly
--   func AddUpgrade(Upgrade: <RowHandle struct>)          -- installs one upgrade module
--   func "Has Upgrade"(Upgrade: <RowHandle struct>) : bool -- NOTE THE LITERAL SPACE in this
--     function's own compiled name (confirmed in the dump: "func Has Upgrade", not
--     "func HasUpgrade") - UE4SS Lua's `obj:HasUpgrade()` sugar would look up a member that does
--     not exist, so this module calls it as `obj["Has Upgrade"](obj, ...)` instead. Worth
--     flagging plainly since every other function this project has called so far happened to have
--     a space-free name.
--   func OnRep_UpgradeTagContainer                          -- called after AddUpgrade, best-effort
-- There is no "RemoveUpgrade"/"Server_RemoveUpgrade" anywhere in this class's ~90 functions, so
-- removing an installed upgrade still has no evidenced live path - bases.set rejects a removal
-- request with a clear error instead of guessing at a raw GameplayTagContainer edit (the mistake
-- this project got burned by once already, GetMyPlayerController).
--
-- The remaining unknown: AddUpgrade's own "Upgrade" parameter is a row-handle struct
-- ({RowName, DataTablePath}, the same two-field shape as WorldFlagRowHandle/WeatherEventRowHandle
-- - all three are the engine's own FDataTableRowHandle under a game-specific type alias) but,
-- unlike flags/weather, there is no enumeration function anywhere to fetch a REAL handle from -
-- every previous use of a row handle in this project (flags.lua, main.lua's world.set) copied one
-- straight from a live GetAll*RowHandles() call, never built one from scratch. DataTablePath here
-- is reconstructed from the pak's own asset location instead
-- (Content/Blueprints/DataTables/DT_BenchUpgrades.uasset -> the standard UE soft-object-path form
-- "/Game/Blueprints/DataTables/DT_BenchUpgrades.DT_BenchUpgrades", the same
-- package-path-plus-object-name shape every enumerated handle in this project already carries) -
-- plausible and grounded in the pak layout, but genuinely UNVERIFIED against the running game
-- (AddUpgrade could silently no-op if this path is wrong, since UE4SS gives no error for a
-- soft-reference that resolves to nothing). Wrapped in pcall; a caller should re-read bases.list
-- afterward to confirm the row now reports installed = true rather than trust the call succeeding.
return function(ctx)
    -- The 11 known upgrade rows (DT_BenchUpgrades), matching
    -- AbioticEditor.Core.WorldSaves.BenchUpgradeCatalog.All row-for-row so live and file report
    -- the same catalog. Kept here rather than fetched live since there is no enumeration
    -- function for this table (see header comment).
    local BENCH_UPGRADE_ROWS = {
        "ItemTransporter", "TougherBench", "BenchWarmer", "Dioxohealer", "PortalSuppression",
        "MatterSynthesizer", "MetabolicField", "BenchTurret", "Cheffigy",
        "ItemTransporter_ChefStation", "ItemTransporter_UpgradeBench",
    }
    local BENCH_UPGRADE_DATA_TABLE_PATH = "/Game/Blueprints/DataTables/DT_BenchUpgrades.DT_BenchUpgrades"

    local function rowHandle(row)
        return { RowName = FName(row, EFindName.FNAME_Find), DataTablePath = BENCH_UPGRADE_DATA_TABLE_PATH }
    end

    local function benchSupportsUpgrades(obj)
        local ok, supports = pcall(function() return obj.SupportsUpgrades == true end)
        return ok and supports
    end

    local function benchInstalledUpgrades(obj)
        local installed = { __forceArray = true }
        if not benchSupportsUpgrades(obj) then return installed end
        for _, row in ipairs(BENCH_UPGRADE_ROWS) do
            -- "Has Upgrade" has a literal space in its compiled name - see header comment.
            local ok, has = pcall(function() return obj["Has Upgrade"](obj, rowHandle(row)) end)
            if ok and has then table.insert(installed, row) end
        end
        return installed
    end

    local function deployableRows()
        local result = { __forceArray = true }
        for _, obj in ipairs(ctx.findAll("AbioticDeployed_ParentBP_C")) do
            if obj:IsValid() then
                local name = ctx.fullName(obj)
                if name then
                    local x, y, z = ctx.actorLocation(obj)
                    local okName, customName = pcall(function() return obj.AlternativeObjectName:ToString() end)
                    local inv = ctx.containerInventory(obj)
                    local hasInventory = inv ~= nil and inv.CurrentInventory ~= nil
                    local stored = 0
                    if hasInventory then
                        for i = 1, #inv.CurrentInventory do
                            if not ctx.slotRow(inv.CurrentInventory[i], i - 1).isEmpty then
                                stored = stored + 1
                            end
                        end
                    end
                    table.insert(result, {
                        id = name,
                        className = ctx.classLabel(name),
                        x = x, y = y, z = z,
                        customName = (okName and customName ~= "" and customName ~= nil) and customName or nil,
                        hasInventory = hasInventory,
                        storedItemCount = stored,
                        supportsUpgrades = benchSupportsUpgrades(obj),
                        installedUpgrades = benchInstalledUpgrades(obj),
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["bases.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { deployables = deployableRows(), isHost = ctx.isHost(), supportsBenchUpgrades = true,
                supportsBenchUpgradeRemoval = false }
        end, respond)
    end

    -- Host-only, matching every other shared-world-object write (containers.set, doors.set): a
    -- deployable belongs to the world, not to whichever client happens to be editing it.
    ctx.handlers["bases.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change deployables") end
            local obj = payload.id and ctx.findByFullName("AbioticDeployed_ParentBP_C", payload.id)
            if not obj then error("deployable not found (it may have been unloaded or destroyed)") end
            if payload.customName ~= nil then
                local text = payload.customName
                -- No precedent anywhere in the reference mod for writing an FText property from
                -- Lua. FText(...) is UE4SS's own documented constructor but nothing here has
                -- exercised it before. Try it, then fall back to a plain string assignment (some
                -- UE4SS builds coerce a string into an FText field), and only then report failure
                -- instead of silently doing nothing.
                local ok = pcall(function() obj.AlternativeObjectName = FText(text) end)
                if not ok then ok = pcall(function() obj.AlternativeObjectName = text end) end
                if not ok then error("could not set this object's custom name on this game build") end
            end
            if payload.upgradeRow ~= nil then
                if payload.upgradeInstalled == false then
                    -- No RemoveUpgrade/Server_RemoveUpgrade exists anywhere on this class (see
                    -- header comment) - refuse rather than guess at a raw tag-container edit.
                    error("removing an installed bench upgrade isn't supported on this game build " ..
                        "(no game function does it) - edit the save file instead")
                end
                if not benchSupportsUpgrades(obj) then error("this deployable does not support upgrades") end
                local found = false
                for _, row in ipairs(BENCH_UPGRADE_ROWS) do
                    if row == payload.upgradeRow then found = true break end
                end
                if not found then error("unknown bench upgrade row") end
                local ok = pcall(function() obj:AddUpgrade(rowHandle(payload.upgradeRow)) end)
                if not ok then error("could not install this upgrade on this game build") end
                pcall(function() obj:OnRep_UpgradeTagContainer() end)
            end
            return nil
        end, respond)
    end
end
