-- HumanCustomizationComponent's exported Server_ApplyCustomizationChange assigns
-- these FNames and marks their replicated properties. Use the same field writes on
-- the host, then call their exact RepNotify so the local meshes refresh as well.
return function(ctx)
    local replication = require("replication")
    local fields = {
        Customization_Head = {"Customization_Head", "DT_Customization_Head"},
        Customization_HeadAccessory = {"Customization_HeadAccessory", "DT_Customization_HeadAccessory"},
        Customization_Wristwatch = {"Customization_Watch", "DT_Customization_Watch"},
        Customization_Tie = {"Customization_Tie", "DT_Customization_Tie"},
        Customization_UpperBody = {"Customization_UpperBody", "DT_Customization_UpperBody"},
        Customization_LowerBody = {"Customization_LowerBody", "DT_Customization_LowerBody"},
        Customization_HairStyle = {"Customization_HairStyle", "DT_Customization_HairStyle"},
        Customization_HairColor = {"Customization_HairColor", "DT_Customization_HairColor"},
        Customization_ShirtColor = {"Customization_ShirtColor", "DT_Customization_ShirtColor"},
        Customization_Shoes = {"Customization_Shoes", "DT_Customization_Shoes"},
        Customization_Belt = {"Customization_Belt", "DT_Customization_Belt"},
        customization_beard = {"Customization_FacialTrait", "DT_Customization_Beards"},
        Customization_IDCard = {"Customization_IDCard", "DT_Customization_IDCard"},
    }
    local function componentFor(payload)
        local player = ctx.resolvePlayer(payload)
        if not player then error("no player found") end
        local class = StaticFindObject("/Game/Blueprints/Characters/HumanCustomizationComponent.HumanCustomizationComponent_C")
        if not class or not class:IsValid() then error("appearance component is unavailable") end
        local component = player:GetComponentByClass(class)
        if not component or not component:IsValid() then error("no appearance component found") end
        return component
    end
    local function profileFor(payload)
        if not ctx.isHost() then error("appearance profile saving requires host authority") end
        local player, localPlayer = ctx.resolvePlayer(payload), ctx.getMyPlayer()
        if not player or not localPlayer or ctx.fullName(player) ~= ctx.fullName(localPlayer) then
            error("only this computer's local character profile can be saved")
        end
        local instance = ctx.UEHelpers.GetGameInstance()
        if not instance or not instance:IsValid() then error("game instance is unavailable") end
        local profile = instance.CurrentCustomizationSave
        if not profile or not profile:IsValid() then error("local appearance profile is unavailable") end
        return instance, profile
    end
    local function saveProperty(id) return id == "customization_beard" and "Customization_Beard" or id end
    ctx.handlers["appearance.save"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local instance, profile = profileFor(payload)
            local component = componentFor(payload)
            local library = StaticFindObject("/Script/AbioticFactor.Default__AbioticFunctionLibrary")
            local gameplay = ctx.UEHelpers.GetGameplayStatics()
            if not library or not library:IsValid() or not gameplay or not gameplay:IsValid() then
                error("appearance profile saving is unavailable")
            end
            local slot = library:GetSaveSubfolderName():ToString() .. "/" .. instance.CustomizationSavePrefix:ToString()
                .. tostring(instance.SelectedCustomizationPreset)
            local old, replacement = {}, {}
            for id, field in pairs(fields) do
                local property = saveProperty(id)
                old[property] = FName(profile[property]:ToString(), EFindName.FNAME_Find)
                replacement[property] = FName(component[field[1]]:ToString(), EFindName.FNAME_Find)
            end
            -- Back up through the game's own save API so its storage provider also handles
            -- platform-specific profiles. The backup slot ends in .bak (typically .bak.sav).
            local original = gameplay:LoadGameFromSlot(slot, 0)
            if original and original:IsValid() and not gameplay:SaveGameToSlot(original, slot .. ".bak", 0) then
                error("could not back up the local appearance profile")
            end
            local ok, err = pcall(function()
                for property, name in pairs(replacement) do profile[property] = name end
                if not gameplay:SaveGameToSlot(profile, slot, 0) then error("could not save the local appearance profile") end
            end)
            if not ok then
                for property, name in pairs(old) do pcall(function() profile[property] = name end) end
                error(err)
            end
            return nil
        end, respond)
    end
    ctx.handlers["appearance.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = componentFor(payload)
            local result = {}
            for id, field in pairs(fields) do result[id] = component[field[1]]:ToString() end
            local canSave, _, profile = pcall(profileFor, payload)
            local profileChanges = false
            if canSave then
                for id, value in pairs(result) do
                    if profile[saveProperty(id)]:ToString():lower() ~= value:lower() then profileChanges = true end
                end
            end
            return {fields = result, canEdit = ctx.isHost() and replication.available(),
                canSaveProfile = canSave, hasProfileChanges = profileChanges}
        end, respond)
    end
    ctx.handlers["appearance.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("appearance editing requires host authority") end
            local helper = replication.requireHelper()
            local field = fields[payload.propertyName]
            if not field then error("unknown appearance field") end
            if type(payload.rowName) ~= "string" or payload.rowName == "" then error("appearance row name is required") end
            local component = componentFor(payload)
            local tablePath = "/Game/Blueprints/DataTables/Customization/" .. field[2] .. "." .. field[2]
            local dataTable = StaticFindObject(tablePath)
            local library = StaticFindObject("/Script/Engine.Default__DataTableFunctionLibrary")
            if not dataTable or not dataTable:IsValid() or not library or not library:IsValid() then
                error("appearance options are unavailable")
            end
            local row = FName(payload.rowName, EFindName.FNAME_Find)
            if not library:DoesDataTableRowExist(dataTable, row) then error("unknown appearance row: " .. payload.rowName) end
            local property = field[1]
            local original = FName(component[property]:ToString(), EFindName.FNAME_Find)
            if original:ToString() == row:ToString() then return nil end
            local notify = "OnRep_" .. property
            local ok, err = pcall(function()
                component[property] = row
                replication.mark(helper, component, property)
                component[notify](component)
            end)
            if not ok then
                pcall(function()
                    component[property] = original
                    replication.mark(helper, component, property)
                    component[notify](component)
                end)
                error(err)
            end
            return nil
        end, respond)
    end
end
