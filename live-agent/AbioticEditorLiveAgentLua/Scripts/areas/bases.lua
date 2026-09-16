-- Bases and deployables. Bench state reads replicated GameplayTags directly.
-- Never call Has Upgrade/AddUpgrade with Lua-built row handles: both fabricated and
-- real-enumerated-handle copies crashed the native bridge during Cascade verification.
return function(ctx)
    local benchTags = require("bench_tags")
    local replication = require("replication")

    -- EPaintColor::None (see AbioticEditor.Core.WorldSaves.DeployablePaintCatalog.NoneValue) -
    -- the CDO default, meaning "unpainted". Paintability itself is decided client-side from the
    -- deployable's class name, the same DeployablePaintCatalog the file editor uses, so this file
    -- only ever reports/writes the raw colour value.
    local PAINT_NONE = 12
    -- The 11 known upgrade rows (DT_BenchUpgrades), matching
    -- AbioticEditor.Core.WorldSaves.BenchUpgradeCatalog.All row-for-row so live and file report
    -- the same catalog. Kept here rather than fetched live since there is no enumeration
    -- function for this table (see header comment).
    local BENCH_UPGRADE_ROWS = {
        "ItemTransporter", "TougherBench", "BenchWarmer", "Dioxohealer", "PortalSuppression",
        "MatterSynthesizer", "MetabolicField", "BenchTurret", "Cheffigy",
        "ItemTransporter_ChefStation", "ItemTransporter_UpgradeBench",
    }

    local function benchSupportsUpgrades(obj)
        local ok, supports = pcall(function() return obj.SupportsUpgrades == true end)
        return ok and supports
    end

    -- Read the actual replicated tags without invoking Has Upgrade. Even real native
    -- handles passed through a Lua table caused a fatal native error in Cascade.
    local function benchInstalledUpgrades(obj)
        local result = { __forceArray = true }
        if not benchSupportsUpgrades(obj) then return result end
        local ok, tags = pcall(function() return obj.UpgradeTagContainer.GameplayTags end)
        if not ok or not tags then return result end
        for i = 1, #tags do
            local tag = tags[i].TagName:ToString()
            local row = tag:match("^BenchUpgrade%.(.+)$")
            if row then table.insert(result, row) end
        end
        return result
    end

    -- PaintedColor is a plain top-level EPaintColor property on AbioticDeployed_ParentBP_C (no
    -- hash suffix - confirmed via the probe in
    -- tests/AbioticEditor.Probes/DeployablePaintProbeTests.cs), read here purely for display; the
    -- save's own paint field lives elsewhere (ChangableData_.DynamicProperties_), so this value is
    -- NOT expected to match a freshly-loaded save until the game itself round-trips it.
    -- The saved side of a paint colour: {Key = EDynamicProperty::PaintColor, Value = colour} in
    -- the deployable's ChangeableData dynamic-property array (same struct and field names as an
    -- inventory item's ChangeableData - see item_metadata.lua and DeployablePaintCatalog).
    local SAVED_DYNAMIC = "DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7"
    local savedPaint = {}
    local function paintKey()
        local enum = StaticFindObject("/Script/AbioticFactor.EDynamicProperty")
        if not enum or not enum:IsValid() then return nil end
        local key
        enum:ForEachName(function(name, value)
            local text = type(name) == "string" and name or name:ToString()
            if text == "EDynamicProperty::PaintColor" then key = value end
        end)
        return key
    end
    -- Returns true when the saved entry was updated, false when this object has no saved
    -- dynamic-property array to update (an older build, or a class that never saves one).
    function savedPaint.write(obj, colour)
        local okData, data = pcall(function() return obj.ChangeableData end)
        if not okData or not data then return false end
        local okArray, array = pcall(function() return data[SAVED_DYNAMIC] end)
        if not okArray or not array then return false end
        local key = paintKey()
        if key == nil then error("dynamic property enum is unavailable") end
        local entries, replaced = {}, false
        for i = 1, #array do
            local entry = array[i]
            local entryKey = tonumber(entry.Key) or tonumber(tostring(entry.Key))
            if entryKey == key then
                if not replaced then entries[#entries + 1] = { Key = key, Value = colour }; replaced = true end
            else
                entries[#entries + 1] = { Key = entry.Key, Value = entry.Value }
            end
        end
        if not replaced then entries[#entries + 1] = { Key = key, Value = colour } end
        data[SAVED_DYNAMIC] = entries
        return true
    end

    local function deployablePaintColor(obj)
        local ok, value = pcall(function() return obj.PaintedColor end)
        if not ok or value == nil then return nil end
        local numeric = tonumber(value)
        if numeric == nil then
            local okStr, str = pcall(function() return tostring(value) end)
            numeric = okStr and tonumber(str) or nil
        end
        if numeric == nil or numeric == PAINT_NONE then return nil end
        return numeric
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
                            local rowName = ctx.slotRowName(inv.CurrentInventory[i])
                            if rowName ~= "" and rowName ~= "None" and rowName ~= "Empty" then
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
                        canEditUpgrades = benchSupportsUpgrades(obj) and benchTags.available(obj),
                        installedUpgrades = benchInstalledUpgrades(obj),
                        paintColor = deployablePaintColor(obj),
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["bases.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            local rows, available = deployableRows(), false
            for _, row in ipairs(rows) do if row.canEditUpgrades then available = true break end end
            return { deployables = rows, isHost = ctx.isHost(), supportsBenchUpgrades = available,
                supportsBenchUpgradeRemoval = available }
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
                if not benchSupportsUpgrades(obj) then error("this deployable does not support upgrades") end
                local found = false
                for _, row in ipairs(BENCH_UPGRADE_ROWS) do
                    if row == payload.upgradeRow then found = true break end
                end
                if not found then error("unknown bench upgrade row") end
                if not benchTags.available(obj) then error("editing a bench upgrade isn't supported by this runtime") end
                benchTags.set(obj, payload.upgradeRow, payload.upgradeInstalled ~= false)
            end
            if payload.paintColor ~= nil then
                -- Plain replicated enum property with its own OnRep (see the probe cited above),
                -- so this follows the same set-then-notify shape as every other direct-property
                -- write in this file - never SetPaintColor itself, which is a Blueprint function
                -- and untested from Lua. The save keeps paint in the deployable's own
                -- ChangeableData dynamic-property array (the field the file editor writes), so
                -- that entry is updated too, the same both-sides approach bench_tags.lua takes for
                -- upgrade tags; without it a live repaint could be lost on the next world save.
                -- Awaiting in-game verification.
                local ok, err = pcall(function()
                    local helper = replication.requireHelper()
                    obj.PaintedColor = payload.paintColor
                    pcall(function() obj:OnRep_PaintedColor() end)
                    replication.mark(helper, obj, "PaintedColor")
                    if savedPaint.write(obj, payload.paintColor) then
                        replication.mark(helper, obj, "ChangeableData")
                        pcall(function() obj:SaveDeployable() end)
                    end
                end)
                if not ok then error("could not set this object's paint colour on this game build: " .. tostring(err)) end
            end
            return nil
        end, respond)
    end
end
