-- Carried pets / companions (round 76): a pet is just an Item.Pet row sitting in the same
-- backpack/equip/hotbar inventory arrays inventory.list/inventory.set (main.lua, round 74)
-- already read/write - same UAbiotic_InventoryComponent_C, same FAbiotic_InventoryItemSlotStruct,
-- same hash-suffixed ChangeableData fields. This module reuses ctx.inventoryComponent/
-- ctx.slotRowName and adds two more fields those handlers don't surface: the pet's custom name
-- (PlayerMadeString_, the SAME field inventory.list's slot already carries, just not read there)
-- and its XP / mutation progress.
--
-- XP / mutation progress are a NEW access path this round found in the game's own class layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs dumping Abiotic_InventoryChangeableDataStruct):
-- DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7, an array of {Key: EDynamicProperty,
-- Value: int} structs - the exact same array PlayerSaveWriter.Pets.cs / PetDynamicProperties.cs
-- already read/write in the FILE format, using the identical enum tail strings ("XP",
-- "MutationProgress", "PetMutation"). NO reference-mod command reads or writes this array over
-- UE4SS Lua, so every access to it here is genuinely new and pcall-guarded; this is the honest
-- caveat to check first if XP/mutation come back wrong or fail to apply.
--
-- The Lua side has no item-data-table catalog of its own, so companions.list returns every
-- occupied slot (like inventory.list does) plus the extra pet fields; filtering down to which
-- rows are actually pets happens on the .NET side (PetItemCatalog.IsPetItem), same division of
-- labor the file reader already uses (game-data catalogs live in Core, not in the mod).
--
-- Round-78 bug fix (reported live: "removing a pet from a player leaves the pet standing next to
-- them, unable to be picked up"): equip slot 12 is the active Companion slot - the ONE slot the
-- game visibly spawns a live, in-world follower actor for (see PlayerCompanions.cs's own remarks).
-- Clearing that slot used to only ever write the inventory struct back to "Empty", which desyncs
-- the follower actor from its now-empty backing item instead of despawning it - the actor stays
-- there, still walking around, uninteractable. LiveClassPropsProbe's class dump
-- (NPC_Monster_Pest.uasset) found the fix: NPC_Monster_Pest_C (and its subclass
-- NPC_Skink_Basic_C) carries its own `FollowingOwner : FObjectProperty` - a direct reference to
-- the player it is currently following - so clearing the Companion slot can now find the matching
-- live follower (by comparing GetFullName() strings, the same object-identity technique
-- ctx.findByFullName already uses) and destroy it with `K2_DestroyActor()`, the same standard
-- AActor function the reference CheatConsoleCommands mod's own "deleteobject" console command
-- already uses on an arbitrary world actor (CommandsManager.lua). FollowingOwner only exists on
-- the Pest/Skink family (same limitation pets.lua's own research already documents), so a
-- Peccary/WinterSprite companion still can't be matched and may still be left behind - a known,
-- pre-existing gap, not something this fix makes worse. Scoped to slot 12 specifically (not every
-- cleared slot) so a hotbar/backpack pet - never a live follower - can never trigger a search.
return function(ctx)
    local PET_KINDS = { "equip", "hotbar", "backpack" }
    local COMPANION_SLOT_KIND, COMPANION_SLOT_INDEX = "equip", 12
    local FOLLOWER_FAMILY_CLASS = "NPC_Monster_Pest_C" -- hierarchy-inclusive: also finds NPC_Skink_Basic_C.

    -- Finds the live follower actor for `player` among Pest/Skink-family pets (the only family
    -- that exposes FollowingOwner) and destroys it. Best-effort and silent: no match (a
    -- Peccary/WinterSprite companion, or no live actor at all) just returns false so the caller
    -- still clears the inventory slot as before.
    local function despawnFollowerFor(player)
        if not player then return false end
        local playerName = ctx.fullName(player)
        if not playerName then return false end
        for _, candidate in ipairs(ctx.findAll(FOLLOWER_FAMILY_CLASS)) do
            if candidate:IsValid() then
                local ok, owner = pcall(function() return candidate.FollowingOwner end)
                if ok and owner and owner:IsValid() and ctx.fullName(owner) == playerName then
                    if pcall(function() candidate:K2_DestroyActor() end) then return true end
                end
            end
        end
        return false
    end

    -- Unverified against the real game (no mod precedent) - reads one int keyed by an
    -- EDynamicProperty enum tail, matching PlayerSaveReader.ReadSlotDynamicInt's own "ends with
    -- ::<suffix>" match against the enum's ToString().
    local function dynamicInt(changeableData, keySuffix)
        local ok, array = pcall(function() return changeableData.DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 end)
        if not ok or not array then return 0 end
        for i = 1, #array do
            local okEntry, key, value = pcall(function()
                local entry = array[i]
                local keyValue = entry.Key
                local keyString = keyValue.ToString and keyValue:ToString() or tostring(keyValue)
                return keyString, entry.Value
            end)
            if okEntry and key and tostring(key):match(keySuffix .. "$") then return value or 0 end
        end
        return 0
    end

    -- Sets one int in place; does nothing when the slot has no existing entry for that key -
    -- matching the file writer's own refusal to fabricate a new DynamicProperties array element
    -- from scratch with no template to clone (PetDynamicProperties.cs) - a live struct offers no
    -- safer template-cloning trick than the file format already needed one for.
    local function setDynamicInt(changeableData, keySuffix, value)
        local ok, array = pcall(function() return changeableData.DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7 end)
        if not ok or not array then return false end
        for i = 1, #array do
            local okEntry, matched = pcall(function()
                local entry = array[i]
                local keyValue = entry.Key
                local keyString = keyValue.ToString and keyValue:ToString() or tostring(keyValue)
                if tostring(keyString):match(keySuffix .. "$") then
                    entry.Value = value
                    return true
                end
                return false
            end)
            if okEntry and matched then return true end
        end
        return false
    end

    ctx.handlers["companions.list"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local player = ctx.resolvePlayer(payload)
            if not player then error("player not found") end

            local result = { __forceArray = true }
            for _, kind in ipairs(PET_KINDS) do
                local inv = ctx.inventoryComponent(player, kind)
                if inv and inv.CurrentInventory then
                    for i = 1, #inv.CurrentInventory do
                        local slot = inv.CurrentInventory[i]
                        local rowName = ctx.slotRowName(slot)
                        if rowName ~= "" and rowName ~= "Empty" and rowName ~= "None" then
                            local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
                            -- PlayerMadeString is an FString userdata - convert it, or json.encode
                            -- rejects the whole reply (found live, round 76).
                            local okName, name = pcall(function()
                                local value = changeableData.PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9
                                return value and value:ToString() or ""
                            end)
                            table.insert(result, {
                                kind = kind,
                                slotIndex = i - 1,
                                itemId = rowName,
                                name = (okName and name ~= "" and name) or nil,
                                health = changeableData and changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 or 0,
                                maxHealth = changeableData and changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B or 0,
                                xp = dynamicInt(changeableData, "XP"),
                                mutationProgress = dynamicInt(changeableData, "MutationProgress"),
                                petMutation = dynamicInt(changeableData, "PetMutation"),
                            })
                        end
                    end
                end
            end
            return { pets = result, isHost = ctx.isHost() }
        end, respond)
    end

    -- One row at a time (unlike inventory.set's array of edits): LivePlayerCompanionsSession
    -- applies a single carried pet's full field set per call, the same shape ApplyCarriedPet
    -- (PlayerSaveWriter.Pets.cs) writes in the file format.
    ctx.handlers["companions.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local player = ctx.resolvePlayer(payload)
            if not player then error("player not found") end

            local inv = payload.kind and ctx.inventoryComponent(player, payload.kind)
            local slot = inv and inv.CurrentInventory and payload.slotIndex ~= nil
                and inv.CurrentInventory[payload.slotIndex + 1]
            if not slot then error("slot not found") end

            local changeableData = slot.ChangeableData_12_2B90E1F74F648135579D39A49F5A2313
            if payload.clear then
                -- "Empty" (confirmed live in round 74's inventory.set), not NAME_None.
                slot.ItemDataTable_18_BF1052F141F66A976F4844AB2B13062B.RowName = FName("Empty", EFindName.FNAME_Find)
                changeableData.CurrentStack_9_D443B69044D640B0989FD8A629801A49 = 0
                changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = 0
                changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = 0
                -- Round 78: clearing the active Companion slot also despawns its matching live
                -- follower actor, when one can be found - see this file's own header comment.
                local despawnedFollower = false
                if payload.kind == COMPANION_SLOT_KIND and payload.slotIndex == COMPANION_SLOT_INDEX then
                    despawnedFollower = despawnFollowerFor(player)
                end
                return { despawnedFollower = despawnedFollower }
            end

            ctx.writeSlot(slot, { itemId = payload.itemId, dataTable = payload.dataTable })
            if payload.health ~= nil then changeableData.CurrentItemDurability_4_24B4D0E64E496B43FB8D3CA2B9D161C8 = payload.health end
            if payload.maxHealth ~= nil then changeableData.MaxItemDurability_6_F5D5F0D64D4D6050CCCDE4869785012B = payload.maxHealth end
            if payload.name ~= nil then
                pcall(function() changeableData.PlayerMadeString_42_CC0B72B24DBEAB2CC04454AAFFD4BBE9 = payload.name end)
            end
            if payload.xp ~= nil then setDynamicInt(changeableData, "XP", math.floor(payload.xp)) end
            if payload.mutationProgress ~= nil then setDynamicInt(changeableData, "MutationProgress", math.floor(payload.mutationProgress)) end
            if payload.petMutation ~= nil then setDynamicInt(changeableData, "PetMutation", math.floor(payload.petMutation)) end

            return nil
        end, respond)
    end
end
