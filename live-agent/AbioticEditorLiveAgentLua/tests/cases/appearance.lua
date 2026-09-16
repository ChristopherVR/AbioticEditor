return function(H)
    require("areas.appearance")(AbioticEditorLiveAgentLua.ctx)
    local pawn = H.hostSession()
    local fields = {Customization_Head = "Head", Customization_HeadAccessory = "Glasses", Customization_Watch = "Watch",
        Customization_Tie = "Tie", Customization_UpperBody = "Torso", Customization_LowerBody = "Pants",
        Customization_HairStyle = "Hair", Customization_HairColor = "Brown", Customization_ShirtColor = "Blue",
        Customization_Shoes = "Shoes", Customization_Belt = "Belt", Customization_FacialTrait = "Beard", Customization_IDCard = "ID"}
    local notified = 0
    local methods = {}
    for property, value in pairs(fields) do
        fields[property] = H.fname(value)
        methods["OnRep_" .. property] = function() notified = notified + 1 end
    end
    local appearance = H.object("HumanCustomizationComponent_C", fields, methods)
    local class = H.object("Class")
    H.world.static("/Game/Blueprints/Characters/HumanCustomizationComponent.HumanCustomizationComponent_C", class)
    rawget(pawn, "__methods").GetComponentByClass = function(_, requested)
        if requested == class then return appearance end
    end
    local tableObject = H.object("DataTable")
    H.world.static("/Game/Blueprints/DataTables/Customization/DT_Customization_HairColor.DT_Customization_HairColor", tableObject)
    H.world.static("/Script/Engine.Default__DataTableFunctionLibrary", H.object("Library", {}, {
        DoesDataTableRowExist = function(_, tableArg, name) return tableArg == tableObject and name:ToString() == "Red" end,
    }))
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, {MarkPropertyDirty = function() end}))
    local profileFields = {}
    local saveNames = {Customization_Watch = "Customization_Wristwatch", Customization_FacialTrait = "Customization_Beard"}
    for property, name in pairs(fields) do profileFields[saveNames[property] or property] = H.fname(name:ToString()) end
    local profile = H.object("CustomizationSave", profileFields)
    local original = H.object("OriginalSave")
    local instance = H.object("GameInstance", {CurrentCustomizationSave = profile,
        CustomizationSavePrefix = H.fstring("ScientistCustomization_"), SelectedCustomizationPreset = 2})
    local saves = {}
    local failSave = false
    local gameplay = H.object("GameplayStatics", {}, {
        LoadGameFromSlot = function(_, slot) H.eq(slot, "account/ScientistCustomization_2", "profile slot from game selection"); return original end,
        SaveGameToSlot = function(_, object, slot)
            saves[#saves + 1] = {object = object, slot = slot}
            return not failSave or slot:find(".bak", 1, true) ~= nil
        end,
    })
    local helpers = AbioticEditorLiveAgentLua.ctx.UEHelpers
    local oldInstance, oldGameplay = helpers.GetGameInstance, helpers.GetGameplayStatics
    helpers.GetGameInstance = function() return instance end
    helpers.GetGameplayStatics = function() return gameplay end
    H.world.static("/Script/AbioticFactor.Default__AbioticFunctionLibrary", H.object("AbioticFunctionLibrary", {}, {
        GetSaveSubfolderName = function() return H.fstring("account") end,
    }))
    local result = H.ok(H.dispatch("appearance.get"))
    H.eq(result.canEdit, true, "appearance editable for host")
    H.eq(result.canSaveProfile, true, "local profile may be saved")
    H.eq(result.hasProfileChanges, false, "unchanged local profile")
    H.eq(result.fields.Customization_Wristwatch, "Watch", "offline wristwatch maps to live Watch")
    H.eq(result.fields.customization_beard, "Beard", "offline beard maps to live FacialTrait")
    H.fails(H.dispatch("appearance.set", {propertyName = "Customization_HairColor", rowName = "Unknown"}), "unknown appearance row", "invalid row rejected")
    H.eq(appearance.Customization_HairColor:ToString(), "Brown", "invalid edit leaves old color")
    H.ok(H.dispatch("appearance.set", {propertyName = "Customization_HairColor", rowName = "Red"}), "change appearance")
    H.eq(appearance.Customization_HairColor:ToString(), "Red", "appearance field changed")
    H.eq(notified, 1, "local mesh notification once")
    H.ok(H.dispatch("appearance.set", {propertyName = "Customization_HairColor", rowName = "Red"}), "unchanged appearance")
    H.eq(notified, 1, "unchanged appearance avoids mesh rebuild")
    H.eq(profile.Customization_HairColor:ToString(), "Brown", "live edit waits for explicit profile save")
    H.eq(H.ok(H.dispatch("appearance.get")).hasProfileChanges, true, "profile pending changes reported")
    failSave = true
    H.fails(H.dispatch("appearance.save"), "could not save", "profile save failure reported")
    H.eq(profile.Customization_HairColor:ToString(), "Brown", "failed save restores cached profile")
    failSave = false
    H.ok(H.dispatch("appearance.save"), "save local appearance profile")
    H.eq(saves[3].object, original, "backup uses original disk profile")
    H.eq(saves[3].slot, "account/ScientistCustomization_2.bak", "separate engine-managed backup slot")
    H.eq(profile.Customization_HairColor:ToString(), "Red", "profile stores current appearance")
    H.eq(H.ok(H.dispatch("appearance.get")).hasProfileChanges, false, "profile changes cleared after successful save")
    local ctx = AbioticEditorLiveAgentLua.ctx
    local oldResolve = ctx.resolvePlayer
    local remote = H.object("OtherPlayer")
    ctx.resolvePlayer = function(payload) return payload and payload.playerId == "other" and remote or oldResolve(payload) end
    H.fails(H.dispatch("appearance.save", {playerId = "other"}), "local character profile", "host cannot overwrite a remote player's local profile")
    ctx.resolvePlayer = oldResolve
    H.clientSession()
    H.fails(H.dispatch("appearance.set", {propertyName = "Customization_HairColor", rowName = "Red"}), "host authority", "appearance host authority required")
    H.fails(H.dispatch("appearance.save"), "host authority", "profile save cannot cross host boundary")
    helpers.GetGameInstance, helpers.GetGameplayStatics = oldInstance, oldGameplay
end
