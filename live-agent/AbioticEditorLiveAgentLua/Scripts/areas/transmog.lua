-- Live transmog VISIBILITY editing (round 77): the per-slot "hide this armor piece" eye toggle
-- the offline editor calls TransmogVisibility on PlayerTransmogTab. Previously reported as
-- read-only live ("no confirmed live property to write these", round 76) because nobody had
-- looked past the SAVE-file property name yet. LiveClassPropsProbe's dump of
-- Content/Blueprints/Characters/Abiotic_TransmogInventoryComp.uasset - the EXACT class
-- ctx.inventoryComponent(player, "transmog") already returns for inventory.list/set (it reads
-- Abiotic_PlayerCharacter_C.TmogInventory, confirmed in main.lua's own INVENTORY_PROPERTY_BY_KIND
-- table) - carries:
--   prop TransmogVisibility : FArrayProperty        -- 12 bools, one per equipment slot
--   prop DisableTransmogArray : FArrayProperty       -- 13 bools (patch-drifted, see
--                                                        docs/reference/research/research-transmog-appearance.md)
--   func OnRep_TransmogVisibility / OnRep_DisableTransmogArray
--   func Request_ChangeTransmogVisibilityFlag(Index: int, Item: bool)
--   func Request_ChangeDisableTransmogArray(Index: int, Item: bool)
-- Both Request_ functions are genuine client -> server RPCs declared directly on this component
-- (the "Request_" naming convention this codebase's other confirmed writes already use, e.g.
-- recipes.lua's Request_UnlockNewRecipe, codex.lua's Request_UnlockNewFish) - this is a real write
-- path, not a guessed direct-field assignment. Not host-gated, same reasoning as inventory.set:
-- this component belongs to a specific player's own pawn, the same "player-owned data" category
-- vitals.set/inventory.set already write without an isHost() check.
--
-- Only the first six flags are exposed here: the offline PlayerTransmogTab only shows the six
-- visual gear roles (CHEST/HEAD/LEGS/BACK/ARMS/SUIT) - see research-transmog-appearance.md's
-- "Editor guidance" - the remaining stored flags round-trip untouched because this area never
-- writes past index 5.
return function(ctx)
    local VISIBLE_SLOTS = 6

    ---@return userdata? transmogComponent
    local function getTransmogComponent(payload)
        local player = ctx.resolvePlayer(payload)
        if not player then return nil end
        return ctx.inventoryComponent(player, "transmog")
    end

    ctx.handlers["transmog.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getTransmogComponent(payload)
            if not component then error("no transmog inventory component found") end
            local result = { __forceArray = true }
            local ok, visibility = pcall(function() return component.TransmogVisibility end)
            if ok and visibility then
                for i = 1, math.min(VISIBLE_SLOTS, #visibility) do
                    table.insert(result, { index = i - 1, isVisible = visibility[i] and true or false })
                end
            end
            return { visibility = result }
        end, respond)
    end

    ctx.handlers["transmog.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getTransmogComponent(payload)
            if not component then error("no transmog inventory component found") end
            local edits = payload.visibility or {}
            for i = 1, #edits do
                local edit = edits[i]
                if edit.index ~= nil and edit.isVisible ~= nil and edit.index >= 0 and edit.index < VISIBLE_SLOTS then
                    -- Full two-argument call - a blueprint function called with fewer than its
                    -- real parameter count is refused by UE4SS ("UFunction expected").
                    pcall(function()
                        component:Request_ChangeTransmogVisibilityFlag(edit.index, edit.isVisible and true or false)
                    end)
                end
            end
            return nil
        end, respond)
    end
end
