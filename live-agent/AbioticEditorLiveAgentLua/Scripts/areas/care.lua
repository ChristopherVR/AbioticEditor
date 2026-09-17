-- Deployed care controls use the game's own save-aware functions, exported by
-- GardenPlot_ParentBP, FarmingPlot_BP and RechargeableComponent. No native row handles
-- are constructed here. Each request changes one field and checks the immediate readback.
return function(ctx)
    local stages = { "Sprout", "Budding", "Juvenile", "Flowering", "Grown", "Harvested", "Regrowing", "Dead" }
    -- Mirrors DeployedCareFeatures.cs's GardenPlotsFeature.CropRows (kept in sync by hand; see
    -- that list's own comment for how it was derived). A planted row missing from this list
    -- still shows correctly, it just cannot be chosen as a new selection here.
    local cropRows = {
        "Plant_Corn", "Plant_Tomato", "Plant_Wheat", "Plant_Greyeb", "Plant_Nyxshade", "Plant_Super_Tomato",
        "Plant_RopePlant", "Plant_Egg", "Plant_SpaceLettuce", "Plant_VinePlant", "Plant_Potato", "Plant_Rice",
        "Plant_Antelight", "Plant_Antelight_GRN", "Plant_Antelight_pink", "Plant_Antelight_red",
        "Plant_Antelight_orange", "Plant_Antelight_blue", "Plant_Antelight_RGB", "Plant_Antelight_space",
        "Plant_Pumpkin", "Plant_GlowTulip", "Plant_Shadowberry", "Plant_Carrot",
    }
    local classes = {
        ["garden-plots"] = "GardenPlot_ParentBP_C",
        ["power-chairs"] = "Deployed_Furniture_Chair_PowerChair_C",
        ["chemistry-benches"] = "Deployed_ChemistryBench_C",
    }
    local function valid(obj) return obj and obj:IsValid() end
    local function number(value, maximum)
        if type(value) ~= "number" or value ~= value or value < 0 or value > maximum or value % 1 ~= 0 then
            error("value must be a whole number between 0 and " .. maximum)
        end
        return value
    end
    local function text(value)
        if value == nil then return "" end
        local ok, result = pcall(function() return value:ToString() end)
        return ok and result or tostring(value)
    end
    local function field(id, label, value, kind, editable, options, maximum)
        return { id = id, label = label, value = text(value), kind = kind or "text",
            editable = editable == true, options = options, maximum = maximum }
    end
    local function capacity(obj)
        return obj.Liquid_MaxFill
    end
    local function spots(obj)
        local result = {}
        for i = 1, #obj.FarmingPlots do
            local spot = obj.FarmingPlots[i]
            if valid(spot) then result[spot.PlotIndex] = spot end
        end
        return result
    end
    local function charge(obj)
        return obj.ChangeableData.LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A
    end
    local function fieldsFor(feature, obj, host)
        local fields = { __forceArray = true }
        if feature == "garden-plots" then
            table.insert(fields, field("water", "Water stored", obj.Liquid_FillLevel, "integer", host, nil, capacity(obj)))
            for index, spot in pairs(spots(obj)) do
                table.insert(fields, field("fertilizer:" .. index, "Spot " .. (index + 1) .. " fertilizer",
                    spot.VisualFertilizeQuality, "integer", host, nil, 10000))
                if spot:HasPlant() then
                    local stage = spot:GetCurrentGrowthStage()
                    local stageName = type(stage) == "number" and stages[stage + 1] or text(stage):match("([^:]+)$")
                    -- The item-table row name (e.g. "Plant_Corn"), not a display label, so it
                    -- round-trips against cropRows the same way file-mode's crop field does.
                    local cropRow = valid(spot.PlantProxy) and text(spot.PlantProxy.ItemRow.RowName) or ""
                    table.insert(fields, field("crop:" .. index, "Spot " .. (index + 1) .. " crop", cropRow, "enum", host, cropRows))
                    table.insert(fields, field("stage:" .. index, "Spot " .. (index + 1) .. " stage", stageName, "enum", host, stages))
                    table.insert(fields, field("growth:" .. index, "Spot " .. (index + 1) .. " growth", spot:GetCurrentGrowthProgress(), "integer", host, nil, 10000))
                end
            end
        elseif feature == "power-chairs" then
            table.insert(fields, field("charge", "Battery charge", charge(obj), "integer", host, nil, 200))
        else
            local inv = obj.BenchInventory
            if inv and inv.CurrentInventory then
                for i = 1, math.min(4, #inv.CurrentInventory) do
                    -- The output (slot 4) is computed by the game's own mixing logic and stays
                    -- read-only here; the three inputs are host-editable "item" fields so the
                    -- editor can offer a picker instead of a plain text box.
                    local isOutput = i == 4
                    table.insert(fields, field("flask:" .. (i - 1), isOutput and "Output" or "Input " .. i,
                        ctx.slotRowName(inv.CurrentInventory[i]), "item", not isOutput and host))
                end
            end
        end
        return fields
    end
    ctx.handlers["care.list"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local feature = payload.featureId
            local class = classes[feature]
            if not class then error("unknown deployed care feature") end
            local entries = { __forceArray = true }
            local host = ctx.isHost()
            for _, obj in ipairs(ctx.findAll(class)) do
                if valid(obj) then
                    local id = ctx.fullName(obj)
                    if id then
                        -- Round 89: chemistry benches want to list closest-to-the-player first,
                        -- the same "nearby" convenience containers/dropped items already have -
                        -- x/y/z were never reported here before since none of these three
                        -- features previously needed a real world position.
                        local x, y, z = ctx.actorLocation(obj)
                        table.insert(entries, { id = id, label = ctx.classLabel(id), fields = fieldsFor(feature, obj, host),
                            containerId = feature == "chemistry-benches" and id or nil, x = x, y = y, z = z })
                    end
                end
            end
            return { entries = entries, isHost = host }
        end, respond)
    end
    ctx.handlers["care.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change deployed objects") end
            local feature = payload.featureId
            local class = classes[feature]
            if not class then error("unknown deployed care feature") end
            local obj = payload.id and ctx.findByFullName(class, payload.id)
            if not valid(obj) then error("object not found (it may have been unloaded)") end
            local id, value = payload.fieldId, payload.value
            if feature == "garden-plots" then
                if id == "water" then
                    value = number(value, capacity(obj))
                    obj:Server_ModifyFillState(obj.ChangeableData.CurrentLiquid_19_3E1652F448223AAE5F405FB510838109, value, false, nil)
                else
                    local kind, index = tostring(id):match("^(%a+):(%d+)$")
                    local spot = index and spots(obj)[tonumber(index)]
                    if not spot then error("planting spot not found") end
                    if kind == "fertilizer" then
                        spot:SetPlantFertilized(number(value, 10000) / 1000, false)
                    elseif kind == "growth" then
                        if not spot:HasPlant() then error("this spot has no crop") end
                        spot:SetCurrentGrowthProgress(number(value, 10000))
                        spot:SavePlot()
                    elseif kind == "stage" then
                        if not spot:HasPlant() then error("this spot has no crop") end
                        local stage
                        for i, name in ipairs(stages) do if name == value then stage = i - 1 break end end
                        if stage == nil then error("unknown growth stage") end
                        spot:SetCurrentGrowthStage(stage, false)
                    elseif kind == "crop" then
                        if not spot:HasPlant() then error("this spot has no crop") end
                        local wanted = tostring(value)
                        local known = false
                        for _, row in ipairs(cropRows) do if row == wanted then known = true break end end
                        if not known then error("unknown crop: " .. wanted) end
                        local dataTable, name = ctx.resolveDataTableRow(ctx.itemTableGlobal, wanted)
                        -- Uproot the current plant before spawning a replacement, mirroring how
                        -- the game's own planting flow always pairs these two functions: a spot
                        -- that already HasPlant() keeps its old PlantProxy actor otherwise.
                        spot:ClearPlant()
                        local planted = spot:SetPlantFromItemData({ DataTable = dataTable, RowName = name }, {}, false)
                        if planted ~= true then error("the game rejected the new crop") end
                        -- Fresh planting: reset back to the very start, same as file-mode edits.
                        spot:SetCurrentGrowthStage(0, false)
                        spot:SetCurrentGrowthProgress(0)
                        spot:SavePlot()
                    else error("unknown garden field") end
                end
            elseif feature == "power-chairs" and id == "charge" then
                value = number(value, 200)
                local component = obj.RechargeableComponent
                if not valid(component) then error("chair battery is unavailable") end
                component:Server_ModifyBattery(value - charge(obj), true)
            elseif feature == "chemistry-benches" then
                local kind, index = tostring(id):match("^(%a+):(%d+)$")
                if kind ~= "flask" then error("this field is read-only") end
                index = tonumber(index)
                if index == 3 then error("the output flask is computed by the game and cannot be set directly") end
                if index < 0 or index > 2 then error("bench slot not found") end
                local inv = obj.BenchInventory
                if not inv or not inv.CurrentInventory then error("this bench has no inventory") end
                local slot = inv.CurrentInventory[index + 1]
                if not slot then error("bench slot is unavailable; refresh and retry") end
                -- writeSlot's own contract (see prepareSlotWrite) rejects itemId = "Empty"/"None"
                -- outright and requires the explicit clear = true path to empty a slot instead.
                if value == nil or value == "" or value == "Empty" or value == "None" then
                    ctx.writeSlot(slot, { clear = true })
                    value = "Empty"
                else
                    ctx.writeSlot(slot, { itemId = value, stack = 1 })
                end
                pcall(function() inv:OnRep_CurrentInventory() end)
            else error("this field is read-only") end
            local expected = tostring(value)
            for _, f in ipairs(fieldsFor(feature, obj, true)) do
                if f.id == id and (f.value == expected or type(value) == "number" and tonumber(f.value) == value) then return nil end
            end
            error("the game did not retain the requested value; refresh to see its current state")
        end, respond)
    end
end
