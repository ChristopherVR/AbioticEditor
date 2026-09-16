-- Resolve and snapshot both sides on one game-thread callback. No client-side read/write
-- sequence, so a failed validation cannot duplicate an item in the other inventory.
return function(ctx)
    ctx.handlers["inventory.transfer"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can transfer items between inventories") end
            local replication = require("replication")
            local helper = replication.requireHelper()
            local function resolve(endpoint)
                if type(endpoint) ~= "table" or type(endpoint.slotIndex) ~= "number"
                    or endpoint.slotIndex < 0 or endpoint.slotIndex % 1 ~= 0 then error("invalid transfer slot") end
                local inv
                if endpoint.containerId then
                    local actor = ctx.findContainer(endpoint.containerId)
                    if actor then inv = ctx.containerInventory(actor) end
                else
                    local player = ctx.resolvePlayer({ playerId = endpoint.playerId })
                    if player then inv = ctx.inventoryComponent(player, endpoint.kind) end
                end
                local slot = inv and inv.CurrentInventory and inv.CurrentInventory[endpoint.slotIndex + 1]
                if not slot then error("transfer slot is unavailable; refresh and retry") end
                return inv, slot
            end
            local firstInv, first = resolve(payload.first)
            local secondInv, second = resolve(payload.second)
            if firstInv == secondInv and payload.first.slotIndex == payload.second.slotIndex then return nil end
            local function snapshot(slot)
                local row = ctx.slotRow(slot, 0)
                if row.isEmpty then return { clear = true } end
                if not row.details or not row.details.instanceMetadata then error("complete item metadata is unavailable; update the live agent") end
                row.dataTable = row.details.instanceMetadata.itemDataTable
                return row
            end
            local firstValue, secondValue = snapshot(first), snapshot(second)
            local intoFirst = ctx.prepareSlotWrite(first, secondValue)
            local intoSecond = ctx.prepareSlotWrite(second, firstValue)
            ctx.applySlotWrite(intoFirst)
            ctx.applySlotWrite(intoSecond)
            local touched = { [firstInv] = true, [secondInv] = true }
            for inv in pairs(touched) do
                replication.mark(helper, inv, "CurrentInventory")
                inv:OnRep_CurrentInventory()
            end
            return nil
        end, respond)
    end
end
