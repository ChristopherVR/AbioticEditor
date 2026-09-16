-- Deployed care controls use the game's own save-aware functions, exported by
-- GardenPlot_ParentBP, FarmingPlot_BP and RechargeableComponent. No native row handles
-- are constructed here. Each request changes one field and checks the immediate readback.
return function(ctx)
    local stages = { "Sprout", "Budding", "Juvenile", "Flowering", "Grown", "Harvested", "Regrowing", "Dead" }
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
                    table.insert(fields, field("crop:" .. index, "Spot " .. (index + 1) .. " crop", ctx.classLabel(ctx.fullName(spot.PlantProxy))))
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
                    table.insert(fields, field("flask:" .. (i - 1), i == 4 and "Output" or "Input " .. i,
                        ctx.slotRowName(inv.CurrentInventory[i])))
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
                        table.insert(entries, { id = id, label = ctx.classLabel(id), fields = fieldsFor(feature, obj, host),
                            containerId = feature == "chemistry-benches" and id or nil })
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
                    else error("unknown garden field") end
                end
            elseif feature == "power-chairs" and id == "charge" then
                value = number(value, 200)
                local component = obj.RechargeableComponent
                if not valid(component) then error("chair battery is unavailable") end
                component:Server_ModifyBattery(value - charge(obj), true)
            else error("this field is read-only") end
            local expected = tostring(value)
            for _, f in ipairs(fieldsFor(feature, obj, true)) do
                if f.id == id and (f.value == expected or type(value) == "number" and tonumber(f.value) == value) then return nil end
            end
            error("the game did not retain the requested value; refresh to see its current state")
        end, respond)
    end
end
