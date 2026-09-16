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
-- already uses on an arbitrary world actor (CommandsManager.lua). FollowingOwner was only
-- confirmed live on the Pest/Skink family at the time, so a Peccary/WinterSprite companion could
-- not be matched and might still be left behind.
--
-- Round-79: re-checked whether that gap could be widened, against the installed game's own class
-- data (LiveClassPropsProbe, run 2026-09-16 with LIVE_CLASS_PROBE_OUT against the mounted paks -
-- see tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragments "NPC_Monster_Peccary.",
-- "NPC_Monster_WinterSprite"). The dump is conclusive, not a guess: both
-- NPC_Monster_Peccary_C and NPC_Monster_WinterSprite_C declare `super=NPC_Base_ParentBP_C`
-- directly (NOT a subclass of NPC_Monster_Pest_C, unlike Skink), and NEITHER their own declared
-- properties NOR NPC_Base_ParentBP_C's ~150 inherited properties include FollowingOwner, Guid,
-- PetName, or DynamicProperties, or any other player-identity reference. There is no live object-
-- identity path to a Peccary/Lamogi companion's owning player anywhere in the class hierarchy
-- today - this is a confirmed, hard limit of the current game build, not an unexplored one. A
-- future safe approach would need the game itself to add an equivalent owner reference to those
-- classes (matching pets.lua's own note on why this project refuses to guess at constructing one).
--
-- What DID change this round: FOLLOWER_FAMILY_CLASSES below now lists NPC_Skink_Basic_C
-- explicitly alongside NPC_Monster_Pest_C. NPC_Monster_Pest_C already hierarchy-matches every
-- Pest variant AND NPC_Skink_Basic_C (FindAllOf is hierarchy-inclusive - confirmed above and by
-- bases.lua/containers.list scanning the same way, and by this same probe run: NPC_Skink_Basic_C
-- declares `super=NPC_Monster_Pest_C`), so this makes no functional difference today - it only
-- protects against that one hierarchy fact ever changing, and is covered by its own Lua harness
-- case (tests/cases/companions.lua) matching a Skink actor by its own class name rather than by
-- falling through the Pest search.
return function(ctx)
    local PET_KINDS = { "equip", "hotbar", "backpack" }
    local COMPANION_SLOT_KIND, COMPANION_SLOT_INDEX = "equip", 12
    local FOLLOWER_FAMILY_CLASSES = {
        "NPC_Monster_Pest_C", -- Pest family root; hierarchy-inclusive also finds every Pest
                               -- variant and NPC_Skink_Basic_C (see header above).
        "NPC_Skink_Basic_C",  -- Skink family root, listed explicitly for robustness against that
                               -- inheritance relationship ever changing (redundant today).
        -- Peccary and Lamogi/WinterSprite are deliberately NOT listed: confirmed this round
        -- (see header above) to expose no owner-identity field at all, so searching their
        -- classes here could never find a match - it would just be dead code dressed up as a fix.
    }

    -- Finds the live follower actor for `player` across every known pet family root
    -- (FOLLOWER_FAMILY_CLASSES above) and destroys it. Best-effort and silent: no match (a family
    -- that turns out not to expose FollowingOwner, or no live actor at all) just returns false so
    -- the caller still clears the inventory slot as before.
    local function despawnFollowerFor(player)
        if not player then return false end
        local playerName = ctx.fullName(player)
        if not playerName then return false end
        for _, familyClass in ipairs(FOLLOWER_FAMILY_CLASSES) do
            for _, candidate in ipairs(ctx.findAll(familyClass)) do
                if candidate:IsValid() then
                    local ok, owner = pcall(function() return candidate.FollowingOwner end)
                    if ok and owner and owner:IsValid() and ctx.fullName(owner) == playerName then
                        if pcall(function() candidate:K2_DestroyActor() end) then return true end
                    end
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
