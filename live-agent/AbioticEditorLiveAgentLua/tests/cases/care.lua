return function(H)
    H.hostSession()
    local progress, stage = 123, 1
    local crop = H.object("Plant_Proxy_Tomato_C")
    local spot = H.object("FarmingPlot_BP_C", { PlotIndex = 0, VisualFertilizeQuality = 1000, PlantProxy = crop }, {
        HasPlant = function() return true end,
        GetCurrentGrowthProgress = function() return progress end,
        GetCurrentGrowthStage = function() return stage end,
        SetCurrentGrowthProgress = function(_, value) progress = value end,
        SetCurrentGrowthStage = function(_, value, skip) H.eq(skip, false, "stage saves crop"); stage = value end,
        SetPlantFertilized = function(self, value, skip) H.eq(skip, false, "fertilizer saves crop"); self.VisualFertilizeQuality = value * 1000 end,
        SavePlot = function() end,
    })
    local garden = H.world.add(H.object("GardenPlot_ParentBP_C", {
        FarmingPlots = { spot }, Liquid_FillLevel = 10, Liquid_MaxFill = 400,
        ChangeableData = { CurrentLiquid_19_3E1652F448223AAE5F405FB510838109 = 1 },
    }, { Server_ModifyFillState = function(self, liquid, level, skip, instigator)
        H.eq(liquid, 1, "water keeps existing liquid type")
        H.eq(skip, false, "water saves plot")
        H.eq(instigator, nil, "editing water does not grant player XP")
        self.Liquid_FillLevel = level
    end }))
    local listing = H.ok(H.dispatch("care.list", {featureId="garden-plots"}))
    H.eq(#listing.entries, 1, "garden listed")
    H.eq(#listing.entries[1].fields, 5, "garden care fields listed")
    local id = garden:GetFullName()
    local function set(field, value) return H.dispatch("care.set", {featureId="garden-plots", id=id, fieldId=field, value=value}) end
    H.ok(set("water", 300), "water set with readback")
    H.fails(set("water", 401), "whole number", "over-capacity water rejected")
    H.eq(garden.Liquid_FillLevel, 300, "rejected water has no mutation")
    H.ok(set("growth:0", 999), "growth set")
    H.eq(H.calls(spot, "SavePlot"), 1, "growth explicitly saves plot")
    H.ok(set("stage:0", "Grown"), "growth stage set")
    H.eq(stage, 4, "native growth enum ordinal passed")
    H.ok(set("fertilizer:0", 2500), "fertilizer multiplier converted")
    H.eq(spot.VisualFertilizeQuality, 2500, "fertilizer readback")
    H.fails(set("stage:0", "Unknown"), "unknown growth stage", "invalid stage rejected")
    H.fails(set("growth:1", 50), "spot not found", "foreign spot rejected")
    H.fails(set("crop:0", "Seed"), "unknown garden field", "crop identity is read-only")
    local data = { LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A = 25 }
    local component = H.object("RechargeableComponent_C", {}, { Server_ModifyBattery = function(_, delta, notOwned)
        H.eq(notOwned, true, "placed chair uses deployed battery path")
        data.LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A = data.LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A + delta
    end })
    local chair = H.world.add(H.object("Deployed_Furniture_Chair_PowerChair_C", {ChangeableData=data, RechargeableComponent=component}))
    H.ok(H.dispatch("care.set", {featureId="power-chairs",id=chair:GetFullName(),fieldId="charge",value=200}), "chair charge set")
    H.eq(data.LiquidLevel_46_D6414A6E49082BC020AADC89CC29E35A, 200, "chair charge delta applied")
    local benchInv = H.object("Abiotic_InventoryComponent_C", { CurrentInventory = {
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0, DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = {} } },
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0, DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = {} } },
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0, DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = {} } },
        { ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B = { RowName = H.fname("Empty") },
          ChangeableData_12_2B90E1F74F648135579D39A49F5A2313 = { CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0, DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 = {} } },
    } }, { OnRep_CurrentInventory = function() end })
    local bench = H.world.add(H.object("Deployed_ChemistryBench_C", { BenchInventory = benchInv }))
    local benchId = bench:GetFullName()
    local benchListing = H.ok(H.dispatch("care.list", {featureId="chemistry-benches"}))
    H.eq(#benchListing.entries[1].fields, 4, "bench flask fields listed")
    H.eq(benchListing.entries[1].fields[1].editable, true, "bench input slot is editable for the host")
    H.eq(benchListing.entries[1].fields[4].editable, false, "bench output slot is always read-only")
    H.eq(benchListing.entries[1].containerId, benchId, "bench still exposes a container id for the deep link")
    local function setBench(field, value) return H.dispatch("care.set", {featureId="chemistry-benches", id=benchId, fieldId=field, value=value}) end
    H.ok(setBench("flask:0", "bandage"), "bench input slot set")
    H.eq(benchInv.CurrentInventory[1].ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName:ToString(), "bandage", "bench slot readback")
    H.ok(setBench("flask:0", ""), "bench input slot cleared")
    H.eq(benchInv.CurrentInventory[1].ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName:ToString(), "Empty", "cleared bench slot reads back empty")
    H.fails(setBench("flask:3", "bandage"), "computed by the game", "bench output slot rejects writes")

    H.clientSession()
    H.world.add(garden)
    local clientList = H.ok(H.dispatch("care.list", {featureId="garden-plots"}))
    H.eq(clientList.entries[1].fields[1].editable, false, "clients see read-only care fields")
    H.fails(set("water", 20), "only the host", "client write refused")
end
