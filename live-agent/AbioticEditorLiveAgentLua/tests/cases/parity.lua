return function(H)
    local pawn = H.hostSession()
    local component = pawn.CharacterProgressionComponent
    local dirty = {}
    local replicationPath = "/Script/Engine.Default__NetPushModelHelpers"
    H.world.static(replicationPath, H.object("NetPushModelHelpers", {}, {
        MarkPropertyDirty = function(_, object, property) dirty[property:ToString()] = object end,
    }))
    -- These no-argument RepNotify functions exist in the exported component layout.
    for _, name in ipairs({ "OnRep_RecipesUnlockedArray", "OnRep_CraftedItems", "OnRep_JournalEntries",
        "OnRep_FishCaughtArray", "OnRep_Compendium_EmailSections", "OnRep_Compendium_ExplorationSections",
        "OnRep_Compendium_NarrativeNPCSections" }) do
        rawget(component, "__methods")[name] = function() end
    end
    component.RecipesUnlockedArray = { H.fname("recipe_a"), H.fname("recipe_b") }
    H.eq(H.ok(H.dispatch("recipes.get")).canLock, true, "host can relock")
    H.ok(H.dispatch("recipes.set", { lockIds = { "recipe_a" } }), "relock recipe")
    local recipes = H.ok(H.dispatch("recipes.get")).unlockedIds
    H.eq(#recipes, 1, "only requested recipe removed")
    H.eq(recipes[1], "recipe_b", "other recipe retained")
    H.eq(dirty.RecipesUnlockedArray, component, "recipe replication notified")
    component.CraftedItems = { H.fname("existing") }
    H.ok(H.dispatch("general.set", { itemsCrafted = { "new", "new", "existing" } }), "discover crafted")
    H.eq(#H.ok(H.dispatch("general.get")).itemsCrafted, 2, "crafted discovery deduplicates")
    H.eq(dirty.CraftedItems, component, "crafted replication notified")
    component.EmailsRead = { H.fname("email_a"), H.fname("email_b") }
    H.ok(H.dispatch("codex.set", { clear = { section = "emails", ids = { "email_a" } } }), "clear known email")
    H.eq(H.ok(H.dispatch("codex.get")).emails[1], "email_b", "other email preserved")
    H.eq(dirty.EmailsRead, component, "email replication notified")
    component.Compendium_EmailSections = { H.fname("lore") }
    component.Compendium_ExplorationSections = { H.fname("lore"), H.fname("keep") }
    component.Compendium_NarrativeNPCSections = {}
    H.ok(H.dispatch("codex.set", { clear = { section = "compendium", ids = { "lore" } } }), "clear compendium sections")
    H.eq(H.ok(H.dispatch("codex.get")).compendium[1], "keep", "all matching sections removed")

    -- Model the documented TSet API, deliberately without numeric indexing.
    local function set(ids)
        local values = {}; for _, id in ipairs(ids) do values[id] = true end
        return { Add = function(_, name) values[name:ToString()] = true end,
            Remove = function(_, name) values[name:ToString()] = nil end,
            ForEach = function(_, callback) for id in pairs(values) do callback(H.outParam(H.fname(id))) end end }
    end
    H.gameState.GlobalRecipesUnlocked = set({ "old" })
    H.gameState.GlobalRecipesResearched = set({})
    H.eq(H.ok(H.dispatch("worldunlocks.get")).canEditRecipes, true, "TSet capability detected")
    H.ok(H.dispatch("worldunlocks.set", { recipes = { { id = "new", unlocked = true }, { id = "old", unlocked = false } } }), "edit global recipes")
    local world = H.ok(H.dispatch("worldunlocks.get"))
    H.eq(world.recipesUnlocked[1], "new", "world unlock changed")
    H.eq(world.recipesResearched[1], "new", "research follows unlock")
    H.eq(#world.recipesUnlocked, 1, "old world recipe removed")
    H.eq(dirty.GlobalRecipesUnlocked, H.gameState, "world unlock replication notified")
    H.eq(dirty.GlobalRecipesResearched, H.gameState, "world research replication notified")

    local key = "ChangeableData_12_2B90E1F74F648135579D39A49F5A2313"
    local data = pawn.CharacterInventory.CurrentInventory[1][key]
    local enumPath = "/Game/Blueprints/Data/E_LiquidType.E_LiquidType"
    local liquids = { [0] = "E_LiquidType::NewEnumerator0", [3] = "E_LiquidType::NewEnumerator13" }
    H.world.static(enumPath, H.object("UEnum", {}, {
        GetNameByValue = function(_, value) return H.fname(liquids[value]) end,
        ForEachName = function(_, callback) for value, name in pairs(liquids) do if callback(H.fname(name), value) then break end end end,
    }))
    local variantKey = "TextureVariantRow_28_1C7CF7A0441335E8AC4EA7B5CA91F636"
    local tablePath = "/Game/Blueprints/DataTables/Customization/DT_TextureVariants.DT_TextureVariants"
    local variants = H.itemTable(tablePath, { Poster_Art = true })
    data[variantKey] = { RowName = H.fname("None") }
    local details = { liquidLevel = 25, liquidType = liquids[3], dynamicState = true,
        playerMadeString = "My item", assetId = "instance-guid", variantRowName = "Poster_Art" }
    H.ok(H.dispatch("inventory.setfull", { edits = { { kind = "backpack", slotIndex = 0, details = details } } }), "write instance details")
    H.eq(data["CurrentLiquid_19_3E1652F448223AAE5F405FB510838109"], 3, "enum uses actual value, not suffix 13")
    H.eq(data[variantKey].DataTable, variants, "variant table is paired with row")
    local inventory = H.ok(H.dispatch("inventory.list"))
    local found; for _, row in ipairs(inventory) do if row.kind == "backpack" and row.slotIndex == 0 then found = row end end
    H.eq(found.details.liquidType, liquids[3], "liquid name round trips")
    H.eq(found.details.playerMadeString, "My item", "custom text round trips")
    H.eq(found.details.assetId, "instance-guid", "instance identity round trips")
    H.eq(dirty.CurrentInventory, pawn.CharacterInventory, "inventory replication notified")
    H.eq(found.details.variantRowName, "Poster_Art", "variant round trips")
    H.fails(H.dispatch("inventory.setfull", { edits = {
        { kind = "backpack", slotIndex = 0, details = { playerMadeString = "wrong" } },
        { kind = "backpack", slotIndex = 1, details = { liquidType = "invalid" } },
    } }), "unknown liquid type", "invalid metadata rejects whole batch")
    H.eq(data["PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9"], "My item", "failed batch preserved first item")
    H.ok(H.dispatch("inventory.setfull", { edits = { { kind = "backpack", slotIndex = 0,
        details = { playerMadeString = "", variantRowName = "" } } } }), "clear text and override")
    H.eq(data[variantKey].RowName:ToString(), "None", "default variant restored")

    local oldHost = AbioticEditorLiveAgentLua.ctx.isHost
    AbioticEditorLiveAgentLua.ctx.isHost = function() return false end
    H.fails(H.dispatch("recipes.set", { lockIds = { "recipe_b" } }), "host authority", "client cannot relock")
    H.fails(H.dispatch("general.set", { itemsCrafted = { "client" } }), "host authority", "client cannot discover crafted")
    H.fails(H.dispatch("codex.set", { clear = { section = "emails", ids = { "email_b" } } }), "host authority", "client cannot clear codex")
    H.fails(H.dispatch("worldunlocks.set", { recipes = { { id = "client", unlocked = true } } }), "host authority", "client cannot edit world unlocks")
    AbioticEditorLiveAgentLua.ctx.isHost = oldHost
    H.world.static(replicationPath, nil)
    H.eq(H.ok(H.dispatch("recipes.get")).canLock, false, "missing replication helper disables relocking")
    H.fails(H.dispatch("recipes.set", { lockIds = { "recipe_b" } }), "replication notification", "missing helper rejects edit")
    H.eq(component.RecipesUnlockedArray[1]:ToString(), "recipe_b", "missing helper preserves recipe")
end
