-- Resolve and snapshot both sides on one game-thread callback. No client-side read/write
-- sequence, so a failed validation cannot duplicate an item in the other inventory.
return function(ctx)
    ctx.handlers["inventory.transfer"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can transfer items between inventories") end
            local replication = require("replication")
            local helper = replication.requireHelper()
            -- shared: true when this endpoint's inventory is a container's shared/GameState-owned
            -- one (see ctx.containerInventory's own remarks, e.g. a Void Chest) rather than an
            -- ordinary per-actor one or a player's own inventory (always false for a player -
            -- ctx.inventoryComponent has no such redirect to worry about).
            local function resolve(endpoint)
                if type(endpoint) ~= "table" or type(endpoint.slotIndex) ~= "number"
                    or endpoint.slotIndex < 0 or endpoint.slotIndex % 1 ~= 0 then error("invalid transfer slot") end
                local inv, shared = nil, false
                if endpoint.containerId then
                    local actor = ctx.findContainer(endpoint.containerId)
                    if actor then inv, shared = ctx.containerInventory(actor) end
                else
                    local player = ctx.resolvePlayer({ playerId = endpoint.playerId })
                    if player then inv = ctx.inventoryComponent(player, endpoint.kind) end
                end
                local slot = inv and inv.CurrentInventory and inv.CurrentInventory[endpoint.slotIndex + 1]
                if not slot then error("transfer slot is unavailable; refresh and retry") end
                return inv, slot, shared
            end
            local firstInv, first, firstShared = resolve(payload.first)
            local secondInv, second, secondShared = resolve(payload.second)
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
            -- Round 85 skipped the manual OnRep_CurrentInventory() call below for a shared
            -- inventory (a Void Chest) to fix a reported freeze. Round 87: a live report showed a
            -- drag-and-drop move into a Void Chest then never actually appearing anywhere, for
            -- anyone - marking a property dirty only pushes it to OTHER clients via replication,
            -- it does nothing for the HOST's own already-authoritative state, so skipping this
            -- call meant the write's own author never saw it land. Restored unconditionally - see
            -- containers.set's matching remarks for the full reasoning and what to investigate if
            -- the freeze itself is reported again (most likely OnRep_CurrentInventory()'s own
            -- InventoryUpdated delegate broadcast doing more work when every placed Void Chest's
            -- UI is bound to the same shared component, not this call itself).
            local touched = { [firstInv] = true, [secondInv] = true }
            for inv in pairs(touched) do
                replication.mark(helper, inv, "CurrentInventory")
                inv:OnRep_CurrentInventory()
            end
            return nil
        end, respond)
    end
end
