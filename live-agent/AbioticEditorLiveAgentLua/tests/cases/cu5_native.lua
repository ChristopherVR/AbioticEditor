-- Community Update 5 (native 1.5) object layouts: plain field names on the native item-slot
-- structs, typed row handles with no DataTable, the native ELiquidType enum (renumbered), native
-- skill structs, trait/background/appearance row handles, the AnyRecipeRowHandle unlock argument,
-- native save structs on power sockets and buttons, and Leyak directors found by class. Each
-- fixture below is shaped from the installed game's own class and struct definitions.
return function(H)
    local slotFields = require("slot_fields")
    slotFields.reset()

    local pawn = H.hostSession()
    local function nativeSlots(count)
        local slots = {}
        for i = 1, count do
            slots[i] = {
                ItemDataTable = { RowName = H.fname("Empty") },
                ChangeableData = {
                    CurrentStack = 0, CurrentItemDurability = 0, MaxItemDurability = 0, CurrentAmmoInMagazine = 0,
                    LiquidLevel = -1, CurrentLiquid = 0, PlayerMadeString = H.fstring(""), AssetID = H.fstring("-1"),
                    DynamicState = false, TextureVariantRow = { RowName = H.fname("None") }, DynamicProperties = {},
                    GameplayTags = { GameplayTags = {}, ParentTags = {} },
                },
            }
        end
        return slots
    end
    for _, name in ipairs({ "CharacterInventory", "CharacterEquipSlotInventory", "CharacterHotbarInventory", "TmogInventory" }) do
        local inventory = pawn[name]
        inventory.CurrentInventory = nativeSlots(#inventory.CurrentInventory)
    end
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("NetPushModelHelpers", {}, {
        MarkPropertyDirty = function() end,
    }))

    local native = { [0] = "ELiquidType::Empty", [1] = "ELiquidType::Water", [5] = "ELiquidType::Fuel", [8] = "ELiquidType::Blood" }
    H.world.static("/Script/AbioticFactor.ELiquidType", H.object("UEnum", {}, {
        GetNameByValue = function(_, value) return H.fname(native[value]) end,
        ForEachName = function(_, callback) for value, name in pairs(native) do if callback(H.fname(name), value) then break end end end,
    }))

    -- Item slots: native field names, and a typed item handle that has no DataTable to set.
    H.eq(H.ok(H.dispatch("inventory.list"))[1].isEmpty, true, "native empty slot recognised")
    H.ok(H.dispatch("inventory.set", { edits = { { kind = "backpack", slotIndex = 0, itemId = "scrap_metal", stack = 3 } } }), "native inventory.set")
    local slot = pawn.CharacterInventory.CurrentInventory[1]
    H.eq(slot.ItemDataTable.RowName:ToString(), "scrap_metal", "item row written to the native handle")
    H.eq(rawget(slot.ItemDataTable, "DataTable"), nil, "no DataTable written to a typed handle")
    H.eq(slot.ChangeableData.CurrentStack, 3, "stack written to the native field")
    H.eq(rawget(slot.ChangeableData, slotFields.LEGACY.CurrentStack), nil, "no legacy field created")
    local listed = H.ok(H.dispatch("inventory.list"))
    H.eq(listed[1].itemId, "scrap_metal", "native item listed")
    H.eq(listed[1].stack, 3, "native stack listed")

    -- Liquids: the editor's E_LiquidType::NewEnumeratorN names map by NAME to native ELiquidType,
    -- whose numbers differ (Fuel was 6, is 5).
    H.ok(H.dispatch("inventory.setfull", { edits = { { kind = "backpack", slotIndex = 0, itemId = "scrap_metal",
        details = { liquidLevel = 10, liquidType = "E_LiquidType::NewEnumerator6" } } } }), "native liquid write")
    H.eq(slot.ChangeableData.CurrentLiquid, 5, "legacy Fuel name lands on native Fuel's number")
    local found
    for _, row in ipairs(H.ok(H.dispatch("inventory.list"))) do
        if row.kind == "backpack" and row.slotIndex == 0 then found = row end
    end
    H.eq(found.details.liquidType, "E_LiquidType::NewEnumerator6", "native liquid reported in the editor's form")
    H.ok(H.dispatch("inventory.set", { edits = { { kind = "backpack", slotIndex = 0, clear = true } } }), "native clear")
    H.eq(slot.ChangeableData.CurrentLiquid, 0, "cleared slot holds native Empty")
    H.eq(slot.ItemDataTable.RowName:ToString(), "Empty", "cleared slot marked Empty")

    -- Skills: native AbioticCharacterSkill { Skill, CurrentXP, XPMultiplier }.
    local progression = pawn.CharacterProgressionComponent
    progression.CharacterSkills_Keys = { 1 }
    progression.CharacterSkills_Values = { { Skill = { RowName = H.fname("Sprinting") }, CurrentXP = 1234.5, XPMultiplier = 1.5 } }
    local skills = H.ok(H.dispatch("skills.get"), "native skills.get")
    H.eq(skills[1].xp, 1234.5, "native CurrentXP read")
    H.eq(skills[1].xpMultiplier, 1.5, "native XPMultiplier read")

    -- Traits: AnyTraitRowHandle entries, buff rows checked against the game's own buff list.
    progression.Traits = { { RowName = H.fname("Trait_Chef") } }
    local class = H.object("Class")
    H.world.static("/Script/AbioticFactor.CharacterBuffComponent", class)
    local added = {}
    local buffs = H.object("CharacterBuffComponent", {}, {
        Server_AddTraitBuff = function(_, handle) added[#added + 1] = handle.RowName:ToString() end,
        Server_RemoveTraitBuff = function() end,
    })
    rawget(pawn, "__methods").GetComponentByClass = function() return buffs end
    H.world.static("/Script/AbioticFactor.Default__BuffDebuffHandleFunctionLibrary", H.object("BuffLibrary", {}, {
        MakeBuffDebuffRowHandle = function(_, name) return { RowName = name } end,
        GetAllBuffDebuffRowNames = function(_, out) out[1] = H.outParam(H.fname("Buff_Trait_Strong")) end,
    }))
    local general = H.ok(H.dispatch("general.get"), "native general.get")
    H.eq(general.traits[1], "Trait_Chef", "trait handle read by row name")
    H.eq(general.background, "PhD_HumanBio", "background read")
    H.ok(H.dispatch("general.trait.set", { id = "Trait_Strong", enabled = true, buffRowName = "Buff_Trait_Strong" }), "native trait add")
    H.eq(added[1], "Buff_Trait_Strong", "trait buff applied")
    H.eq(progression.Traits[2].RowName:ToString(), "Trait_Strong", "trait stored as a row handle")
    H.eq(type(progression.Traits[1]), "table", "existing trait kept as a row handle")
    H.fails(H.dispatch("general.trait.set", { id = "Trait_Strong", enabled = false, buffRowName = "NotABuff" }),
        "unknown trait buff row", "buff row outside the game's buff list is refused")

    -- Background: PhDRowHandle.
    H.playerStates[1].PhD = { RowName = H.fname("PhD_HumanBio") }
    H.eq(H.ok(H.dispatch("general.get")).background, "PhD_HumanBio", "background handle read")
    H.ok(H.dispatch("general.set", { background = "PhD_Physics" }), "background handle write")
    H.eq(H.playerStates[1].PhD.RowName:ToString(), "PhD_Physics", "background written as a row handle")

    -- Recipes (non-host path): Request_UnlockNewRecipe takes an AnyRecipeRowHandle.
    local unlocked = {}
    local client = H.clientSession()
    rawget(client.CharacterProgressionComponent, "__methods").Request_UnlockNewRecipe = function(_, row)
        if type(row) ~= "table" then error("expected an AnyRecipeRowHandle") end
        unlocked[#unlocked + 1] = row.Table .. ":" .. row.RowName:ToString()
    end
    H.ok(H.dispatch("recipes.set", { unlockIds = { "recipe_bandage", "srecipe_stew", "crecipe_acid" } }), "native recipe unlock")
    H.eq(unlocked[1], "0:recipe_bandage", "crafting recipe table")
    H.eq(unlocked[2], "1:srecipe_stew", "soup recipe table")
    H.eq(unlocked[3], "2:crecipe_acid", "chemistry recipe table")

    -- Leyak containment: the AI director no longer has named Leyak/Krasue properties, so the
    -- directors are found by component class (Krasue derives from Leyak).
    H.hostSession()
    local function director(className, bases)
        return H.world.add(H.object(className, { ActiveLeyakContainmentID = "", __bases = bases }, {
            SetLeyakContainmentID = function(self, newId) self.ActiveLeyakContainmentID = newId end,
        }))
    end
    local leyak = director("LeyakDirectorComponent_C")
    local krasue = director("KrasueDirectorComponent_C", { "LeyakDirectorComponent_C" })
    H.gameMode.AI_Director = H.object("Abiotic_AIDirector_C", {}, {})
    local unit = H.world.add(H.object("Deployed_LeyakContainment_C", {
        ContainsLeyak = H.fname("None"), ["Stability Level"] = 40, MaxStability = 100,
        DeployableDestroyed = false, SpawnedAssetID = H.fstring("UnitCU5"),
    }, {
        TrapLeyak = function(self, rowName) self.ContainsLeyak = rowName end,
        ["Free Leyak"] = function(self) self.ContainsLeyak = H.fname("None") end,
        ServerUpdateStabilityLevel = function() end,
        OnRep_ContainsLeyak = function() end,
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))
    local unitId = H.ok(H.dispatch("containment.list")).units[1].id
    H.ok(H.dispatch("containment.set", { action = "assign", unitId = unitId, creature = "Krasue" }), "assign Krasue via class lookup")
    H.eq(unit.ContainsLeyak:ToString(), "Krasue", "Krasue trapped")
    H.eq(krasue.ActiveLeyakContainmentID, "UnitCU5", "Krasue director found by class")
    H.eq(leyak.ActiveLeyakContainmentID, "", "Leyak director untouched by a Krasue assignment")

    -- Pets: IsTamedPet lives in PetFunctionLibrary, and SpawnPet takes an NPC row handle, so a
    -- species change (which would pass a class) is refused rather than risk a native crash.
    H.hostSession()
    H.world.static("/Script/AbioticFactor.NPCRowHandle", H.object("ScriptStruct"))
    local tamed = H.world.add(H.object("NPC_Peccary_C", { IsDead = false, __bases = { "NPC_Base_ParentBP_C" } }, {
        K2_GetActorLocation = function() return H.vector(0, 0, 0) end,
    }))
    H.world.static("/Script/AbioticFactor.Default__PetFunctionLibrary", H.object("PetFunctionLibrary", {}, {
        IsTamedPet = function(_, npc) return npc == tamed end,
    }))
    local pets = H.ok(H.dispatch("pets.list"), "native pets.list")
    H.eq(pets.supportsSpeciesChange, false, "species change not offered when SpawnPet takes an NPC row")
    local tamedListed = false
    for _, row in ipairs(pets.pets) do if row.matched == false then tamedListed = true end end
    H.check(tamedListed, "tamed creature found through PetFunctionLibrary")

    -- Appearance: every customization field is a typed row handle.
    local pawnAppearance = H.hostSession()
    local handles, methods = {}, {}
    for _, property in ipairs({ "Customization_Head", "Customization_HeadAccessory", "Customization_Watch", "Customization_Tie",
        "Customization_UpperBody", "Customization_LowerBody", "Customization_HairStyle", "Customization_HairColor",
        "Customization_ShirtColor", "Customization_Shoes", "Customization_Belt", "Customization_FacialTrait", "Customization_IDCard" }) do
        handles[property] = { RowName = H.fname(property .. "_Row") }
        methods["OnRep_" .. property] = function() end
    end
    local appearance = H.object("HumanCustomizationComponent_C", handles, methods)
    local appearanceClass = H.object("Class")
    H.world.static("/Game/Blueprints/Characters/HumanCustomizationComponent.HumanCustomizationComponent_C", appearanceClass)
    rawget(pawnAppearance, "__methods").GetComponentByClass = function(_, requested)
        if requested == appearanceClass then return appearance end
    end
    local hairTable = H.object("DataTable")
    H.world.static("/Game/Blueprints/DataTables/Customization/DT_Customization_HairColor.DT_Customization_HairColor", hairTable)
    H.world.static("/Script/Engine.Default__DataTableFunctionLibrary", H.object("Library", {}, {
        DoesDataTableRowExist = function(_, tableArg, name) return tableArg == hairTable and name:ToString() == "Red" end,
    }))
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, { MarkPropertyDirty = function() end }))
    local look = H.ok(H.dispatch("appearance.get"), "native appearance.get")
    H.eq(look.fields.Customization_HairColor, "Customization_HairColor_Row", "appearance handle read")
    H.ok(H.dispatch("appearance.set", { propertyName = "Customization_HairColor", rowName = "Red" }), "native appearance.set")
    H.eq(appearance.Customization_HairColor.RowName:ToString(), "Red", "appearance written as a row handle")

    slotFields.reset()
end
