-- ===== World bases / deployables (AbioticDeployed_ParentBP_C) =====
-- Round 76. Every player-placed object (benches, furniture, defenses, containers) derives from
-- this one blueprint class - confirmed from the game's own pak layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragment "AbioticDeployed_ParentBP"):
--   AlternativeObjectName : FTextProperty   -- the player-given custom name (file: CustomTextDisplay_)
--   CurrentDurability / MaxDurability : FDoubleProperty, with OnRep_CurrentDurability/OnRep_MaxDurability
--   DestroyDeployable(), GetItemNameText() functions
-- FindAllOf("AbioticDeployed_ParentBP_C") is hierarchy-inclusive (confirmed live already by
-- containers.list, which finds every Deployed_Container_* subclass through the narrower
-- Deployed_Container_ParentBP_C the same way), so this one scan covers every deployable a base
-- is built from - no per-subclass enumeration needed.
--
-- Bench upgrades (BenchUpgradeCatalog on the file side) are NOT exposed here: the live bench
-- class does carry an UpgradeTagContainer property (confirmed from the same probe dump, on
-- AbioticDeployed_CraftingBench_ParentBP_C), but no installed mod anywhere touches it, there is
-- no confirmed way to build a valid BenchUpgradeRowHandle from a bare row name (the weather/flag
-- areas could always ENUMERATE real handles from a matching function library; no such library
-- was found for bench upgrades), and the TArray-of-struct add/remove API for a live
-- GameplayTagContainer has no working precedent to copy either. Guessing three unconfirmed
-- things at once for a write that can leave a bench in a wrong state is exactly the mistake this
-- project already got burned by once (GetMyPlayerController) - so bases.lua reports
-- supportsBenchUpgrades = false instead, and the shared tab hides that section live.
return function(ctx)
    local function deployableRows()
        local result = { __forceArray = true }
        for _, obj in ipairs(ctx.findAll("AbioticDeployed_ParentBP_C")) do
            if obj:IsValid() then
                local name = ctx.fullName(obj)
                if name then
                    local x, y, z = ctx.actorLocation(obj)
                    local okName, customName = pcall(function() return obj.AlternativeObjectName:ToString() end)
                    local inv = ctx.containerInventory(obj)
                    local hasInventory = inv ~= nil and inv.CurrentInventory ~= nil
                    local stored = 0
                    if hasInventory then
                        for i = 1, #inv.CurrentInventory do
                            if not ctx.slotRow(inv.CurrentInventory[i], i - 1).isEmpty then
                                stored = stored + 1
                            end
                        end
                    end
                    table.insert(result, {
                        id = name,
                        className = ctx.classLabel(name),
                        x = x, y = y, z = z,
                        customName = (okName and customName ~= "" and customName ~= nil) and customName or nil,
                        hasInventory = hasInventory,
                        storedItemCount = stored,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["bases.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { deployables = deployableRows(), isHost = ctx.isHost(), supportsBenchUpgrades = false }
        end, respond)
    end

    -- Host-only, matching every other shared-world-object write (containers.set, doors.set): a
    -- deployable belongs to the world, not to whichever client happens to be editing it.
    ctx.handlers["bases.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can rename deployables") end
            local obj = payload.id and ctx.findByFullName("AbioticDeployed_ParentBP_C", payload.id)
            if not obj then error("deployable not found (it may have been unloaded or destroyed)") end
            if payload.customName ~= nil then
                local text = payload.customName
                -- No precedent anywhere in the reference mod for writing an FText property from
                -- Lua. FText(...) is UE4SS's own documented constructor but nothing here has
                -- exercised it before. Try it, then fall back to a plain string assignment (some
                -- UE4SS builds coerce a string into an FText field), and only then report failure
                -- instead of silently doing nothing.
                local ok = pcall(function() obj.AlternativeObjectName = FText(text) end)
                if not ok then ok = pcall(function() obj.AlternativeObjectName = text end) end
                if not ok then error("could not set this object's custom name on this game build") end
            end
            return nil
        end, respond)
    end
end
