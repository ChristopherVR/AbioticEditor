-- Bases and deployables. Bench state reads replicated GameplayTags directly.
-- Never call Has Upgrade/AddUpgrade with Lua-built row handles: both fabricated and
-- real-enumerated-handle copies crashed the native bridge during Cascade verification.
-- Round 111 re-check: canEditUpgrades below is not wider than needed - it already requires both
-- the class-level SupportsUpgrades flag AND bench_tags.available (replication support plus both
-- of this exact instance's own tag containers being readable), so it cannot report an unsafe
-- bench as editable; see bench_tags.lua's own header for the new evidence that its tag-write path
-- matches AddUpgrade's own internal implementation. supportsBenchUpgrades and
-- supportsBenchUpgradeRemoval are intentionally reported as the same value below: since removal
-- uses the identical tag-write path as install (no separate native call, unlike the old
-- AddUpgrade-only round 77 shape), there is no longer any capability removal needs that install
-- does not already have.
--
-- Round 121: a live report on the BASES tab said two things: benches renamed in-game showed no
-- name here, and the list itself carried deployables the player did not recognise as belonging to
-- the world save they were looking at.
--
-- NAME - fixed here. This module used to read/write AlternativeObjectName (an FTextProperty on
-- AbioticDeployed_ParentBP_C, "Edit | BlueprintVisible | DisableEditOnInstance" - no Net flag at
-- all, confirmed against the class dump). main.lua's own containers.rename already investigated
-- and rejected that exact field for the identical reason (see its header comment there): a write
-- with no Net flag is only ever seen by whichever machine made it, so even a successful write here
-- would not show up for anyone else, and would not survive being replaced by whatever the game's
-- own systems last wrote to the real field. The real field is PlayerMadeString - a replicated
-- (Net | RepNotify) StrProperty declared on AbioticDeployed_Furniture_ParentBP_C (confirmed
-- against the class dump; crafting benches and containers both derive from it), read with the
-- game's own NewPlayerMadeString()/OnRep_PlayerMadeString() pair exactly like containers.rename
-- already does, and the one that matches the save file's own leaf for this data
-- (CustomTextDisplay_ - see WorldSaveWriter.ApplyDeployableCustomText/ApplyContainerCustomName and
-- WorldSaveReader's matching read). AlternativeObjectName is kept below as a READ-ONLY fallback
-- for a deployable class with no PlayerMadeString at all (anything not Furniture-derived - lights,
-- turrets, and similar non-nameable placeables); bases.set no longer writes it for a class that
-- does have PlayerMadeString, and a bench renamed under the old code may still read back its old
-- AlternativeObjectName value once, until it is renamed again through the fixed path.
--
-- SCOPE - not changed here, on the evidence available. deployableRows() below sweeps
-- AbioticDeployed_ParentBP_C with FindAllOf exactly the way every other region-scoped area does
-- (doors.list/containers.list in main.lua, destructibles.lua, triggers.lua - none of them filter
-- by the actor's map/level path either), and LiveConnect.razor's ResetRegionScopedWorldSessions is
-- the one and only scoping mechanism this whole mod has: it drops the cached BASES session (along
-- with every other region-scoped one) the moment world.info's levelToken changes, so the next tab
-- visit re-sweeps whatever region is loaded now. Adding a per-actor map-path filter here with no
-- working precedent anywhere else in this mod, and no way to test it against the running game this
-- round, risks hiding real, loaded bases rather than fixing anything. What actually changed for
-- this report: WorldBasesTab now shows each deployable's own sub-level (parsed from its actor path
-- client-side, the same DoorIdParser the DOORS tab already uses on WorldDoor.Id - see
-- WorldDeployable.SubLevel) and, when a player position is known, a per-row distance with a
-- "Nearest first" sort - see WorldBasesTab.razor. If the Facility region's several sub-levels
-- really do all stream in at once regardless of where the player stands (plausible - it is the one
-- ~16 MB region, per CLAUDE.md), that is the same thing every other region-scoped tab already
-- shows for it, and the sub-level/distance additions are exactly the mitigation for "which of
-- these is actually near me" the report asked for.
return function(ctx)
    local benchTags = require("bench_tags")
    local replication = require("replication")

    -- textValue: shared with main.lua's own helper of the same name (not exposed through ctx,
    -- so duplicated here rather than threading one more field through it for one small function) -
    -- a StrProperty sometimes hands back a plain Lua string, sometimes FString-like userdata
    -- needing :ToString(), and this does not assume either shape.
    local function textValue(value)
        if value == nil then return nil end
        if type(value) == "string" then return value end
        local ok, str = pcall(function() return value:ToString() end)
        return ok and str or nil
    end

    -- See the header comment: PlayerMadeString is the real, networked name field (matches
    -- containers.rename's own read); AlternativeObjectName is a read-only fallback for a
    -- deployable class that has no PlayerMadeString at all.
    local function deployableCustomName(obj)
        local ok, value = pcall(function() return obj.PlayerMadeString end)
        local text = ok and textValue(value)
        if text and text ~= "" then return text end
        local okAlt, alt = pcall(function() return obj.AlternativeObjectName:ToString() end)
        if okAlt and alt and alt ~= "" then return alt end
        return nil
    end

    -- EPaintColor::None (see AbioticEditor.Core.WorldSaves.DeployablePaintCatalog.NoneValue) -
    -- the CDO default, meaning "unpainted". Paintability itself is decided client-side from the
    -- deployable's class name, the same DeployablePaintCatalog the file editor uses, so this file
    -- only ever reports/writes the raw colour value.
    local PAINT_NONE = 12
    -- The 11 known upgrade rows (DT_BenchUpgrades), matching
    -- AbioticEditor.Core.WorldSaves.BenchUpgradeCatalog.All row-for-row so live and file report
    -- the same catalog. Kept here rather than fetched live since there is no enumeration
    -- function for this table (see header comment).
    local BENCH_UPGRADE_ROWS = {
        "ItemTransporter", "TougherBench", "BenchWarmer", "Dioxohealer", "PortalSuppression",
        "MatterSynthesizer", "MetabolicField", "BenchTurret", "Cheffigy",
        "ItemTransporter_ChefStation", "ItemTransporter_UpgradeBench",
    }

    local function benchSupportsUpgrades(obj)
        local ok, supports = pcall(function() return obj.SupportsUpgrades == true end)
        return ok and supports
    end

    -- Read the actual replicated tags without invoking Has Upgrade. Even real native
    -- handles passed through a Lua table caused a fatal native error in Cascade.
    local function benchInstalledUpgrades(obj)
        local result = { __forceArray = true }
        if not benchSupportsUpgrades(obj) then return result end
        local ok, tags = pcall(function() return obj.UpgradeTagContainer.GameplayTags end)
        if not ok or not tags then return result end
        for i = 1, #tags do
            local tag = tags[i].TagName:ToString()
            local row = tag:match("^BenchUpgrade%.(.+)$")
            if row then table.insert(result, row) end
        end
        return result
    end

    -- PaintedColor is a plain top-level EPaintColor property on AbioticDeployed_ParentBP_C (no
    -- hash suffix - confirmed via the probe in
    -- tests/AbioticEditor.Probes/DeployablePaintProbeTests.cs), read here purely for display; the
    -- save's own paint field lives elsewhere (ChangableData_.DynamicProperties_), so this value is
    -- NOT expected to match a freshly-loaded save until the game itself round-trips it.
    -- The saved side of a paint colour: {Key = EDynamicProperty::PaintColor, Value = colour} in
    -- the deployable's ChangeableData dynamic-property array (same struct and field names as an
    -- inventory item's ChangeableData - see item_metadata.lua and DeployablePaintCatalog).
    local SAVED_DYNAMIC = "DynamicProperties_50_5C138DB145048726E8C0FEAC7C9600F7"
    local savedPaint = {}
    local function paintKey()
        local enum = StaticFindObject("/Script/AbioticFactor.EDynamicProperty")
        if not enum or not enum:IsValid() then return nil end
        local key
        enum:ForEachName(function(name, value)
            local text = type(name) == "string" and name or name:ToString()
            if text == "EDynamicProperty::PaintColor" then key = value end
        end)
        return key
    end
    -- Returns true when the saved entry was updated, false when this object has no saved
    -- dynamic-property array to update (an older build, or a class that never saves one).
    function savedPaint.write(obj, colour)
        local okData, data = pcall(function() return obj.ChangeableData end)
        if not okData or not data then return false end
        local okArray, array = pcall(function() return data[SAVED_DYNAMIC] end)
        if not okArray or not array then return false end
        local key = paintKey()
        if key == nil then error("dynamic property enum is unavailable") end
        local entries, replaced = {}, false
        for i = 1, #array do
            local entry = array[i]
            local entryKey = tonumber(entry.Key) or tonumber(tostring(entry.Key))
            if entryKey == key then
                if not replaced then entries[#entries + 1] = { Key = key, Value = colour }; replaced = true end
            else
                entries[#entries + 1] = { Key = entry.Key, Value = entry.Value }
            end
        end
        if not replaced then entries[#entries + 1] = { Key = key, Value = colour } end
        data[SAVED_DYNAMIC] = entries
        return true
    end

    local function deployablePaintColor(obj)
        local ok, value = pcall(function() return obj.PaintedColor end)
        if not ok or value == nil then return nil end
        local numeric = tonumber(value)
        if numeric == nil then
            local okStr, str = pcall(function() return tostring(value) end)
            numeric = okStr and tonumber(str) or nil
        end
        if numeric == nil or numeric == PAINT_NONE then return nil end
        return numeric
    end

    local function deployableRows()
        -- Round 118: every other single-sweep area (corpses.lua, destructibles.lua, etc.) already
        -- dedupes its findAll() result by full name even with one root class, defensively, because
        -- FindAllOf itself is not guaranteed one entry per actor. This sweep was the one that
        -- didn't - a live crash report showed the exact same crafting bench id twice in one
        -- bases.list reply, which the app-side dictionary build then threw on. Kept here too so a
        -- genuinely duplicated find never reaches the wire at all.
        local result, seen = { __forceArray = true }, {}
        for _, obj in ipairs(ctx.findAll("AbioticDeployed_ParentBP_C")) do
            if obj:IsValid() then
                local name = ctx.fullName(obj)
                if name and not seen[name] then
                    seen[name] = true
                    local x, y, z = ctx.actorLocation(obj)
                    local inv = ctx.containerInventory(obj)
                    local hasInventory = inv ~= nil and inv.CurrentInventory ~= nil
                    local stored = 0
                    if hasInventory then
                        for i = 1, #inv.CurrentInventory do
                            local rowName = ctx.slotRowName(inv.CurrentInventory[i])
                            if rowName ~= "" and rowName ~= "None" and rowName ~= "Empty" then
                                stored = stored + 1
                            end
                        end
                    end
                    table.insert(result, {
                        id = name,
                        className = ctx.classLabel(name),
                        x = x, y = y, z = z,
                        customName = deployableCustomName(obj),
                        hasInventory = hasInventory,
                        storedItemCount = stored,
                        supportsUpgrades = benchSupportsUpgrades(obj),
                        canEditUpgrades = benchSupportsUpgrades(obj) and benchTags.available(obj),
                        installedUpgrades = benchInstalledUpgrades(obj),
                        paintColor = deployablePaintColor(obj),
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["bases.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            local rows, available = deployableRows(), false
            for _, row in ipairs(rows) do if row.canEditUpgrades then available = true break end end
            return { deployables = rows, isHost = ctx.isHost(), supportsBenchUpgrades = available,
                supportsBenchUpgradeRemoval = available }
        end, respond)
    end

    -- Host-only, matching every other shared-world-object write (containers.set, doors.set): a
    -- deployable belongs to the world, not to whichever client happens to be editing it.
    ctx.handlers["bases.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change deployables") end
            local obj = payload.id and ctx.findByFullName("AbioticDeployed_ParentBP_C", payload.id)
            if not obj then error("deployable not found (it may have been unloaded or destroyed)") end
            if payload.customName ~= nil then
                -- containers.rename already refuses to rename a container whose inventory is
                -- shared (a Void Chest, whose GetContainerInventory() redirects every placed
                -- instance to one pool owned by the world's GameState - see that function's own
                -- remarks in main.lua) because a live report showed renaming ONE bleeding into
                -- every one of them. This BASES screen reaches the exact same actors through its
                -- own, wider AbioticDeployed_ParentBP_C sweep (a Void Chest is a deployable too)
                -- and had its own, completely separate customName write here with no such check -
                -- a live report confirmed renaming from here still bled across every Void Chest
                -- even after containers.rename's own block landed, because this path was never
                -- protected. Reuses containerInventory's own shared-detection (the same function
                -- deployableRows already calls for storedItemCount, just above) rather than
                -- re-implementing it here.
                -- Round 90: the shared-inventory refusal added the round before was removed here
                -- too - see containers.rename's own remarks in main.lua (a live test proved the
                -- name write is per-actor; only the contents are shared).
                --
                -- Round 121: writes PlayerMadeString first - the same real, networked field
                -- containers.rename already uses (see this file's own header comment for why
                -- AlternativeObjectName was wrong) - with the identical mark-dirty +
                -- NewPlayerMadeString refresh. A class with no PlayerMadeString at all (not
                -- Furniture-derived) falls back to the old AlternativeObjectName write so it still
                -- shows something on the host's own screen, same as before this round for those
                -- classes; that fallback is never attempted for a class that does have
                -- PlayerMadeString, so a bench/container/furniture rename never regresses to the
                -- non-networked field again.
                local text = payload.customName
                local ok = pcall(function() obj.PlayerMadeString = text end)
                if not ok then ok = pcall(function() obj.PlayerMadeString = FString(text) end) end
                if ok then
                    local markOk = pcall(function()
                        local helper = replication.requireHelper()
                        replication.mark(helper, obj, "PlayerMadeString")
                    end)
                    -- Mirrors OnRep_PlayerMadeString -> NewPlayerMadeString (see containers.rename's
                    -- own remarks in main.lua): the host never gets its own RepNotify, so this call
                    -- is what makes the host's own view catch up immediately too.
                    pcall(function() obj:NewPlayerMadeString() end)
                    -- See main.lua's voidChestSiblings: a Void Chest's name is applied to every
                    -- Void Chest, from this screen the same as from the CONTAINERS one.
                    for _, sibling in ipairs(ctx.voidChestSiblings(obj)) do
                        local okSibling = pcall(function() sibling.PlayerMadeString = text end)
                        if not okSibling then pcall(function() sibling.PlayerMadeString = FString(text) end) end
                        pcall(function()
                            local helper = replication.requireHelper()
                            replication.mark(helper, sibling, "PlayerMadeString")
                        end)
                        pcall(function() sibling:NewPlayerMadeString() end)
                    end
                    if not markOk then
                        error("the name was set but could not be confirmed as sent to other connected players on this game build")
                    end
                else
                    -- No precedent anywhere in the reference mod for writing an FText property
                    -- from Lua. FText(...) is UE4SS's own documented constructor but nothing here
                    -- has exercised it before. Try it, then fall back to a plain string assignment
                    -- (some UE4SS builds coerce a string into an FText field), and only then report
                    -- failure instead of silently doing nothing.
                    local okAlt = pcall(function() obj.AlternativeObjectName = FText(text) end)
                    if not okAlt then okAlt = pcall(function() obj.AlternativeObjectName = text end) end
                    if not okAlt then error("could not set this object's custom name on this game build") end
                    for _, sibling in ipairs(ctx.voidChestSiblings(obj)) do
                        local okSibling = pcall(function() sibling.AlternativeObjectName = FText(text) end)
                        if not okSibling then pcall(function() sibling.AlternativeObjectName = text end) end
                    end
                end
            end
            if payload.upgradeRow ~= nil then
                if not benchSupportsUpgrades(obj) then error("this deployable does not support upgrades") end
                local found = false
                for _, row in ipairs(BENCH_UPGRADE_ROWS) do
                    if row == payload.upgradeRow then found = true break end
                end
                if not found then error("unknown bench upgrade row") end
                if not benchTags.available(obj) then error("editing a bench upgrade isn't supported by this runtime") end
                benchTags.set(obj, payload.upgradeRow, payload.upgradeInstalled ~= false)
            end
            if payload.paintColor ~= nil then
                -- Plain replicated enum property with its own OnRep (see the probe cited above),
                -- so this follows the same set-then-notify shape as every other direct-property
                -- write in this file - never SetPaintColor itself, which is a Blueprint function
                -- and untested from Lua. The save keeps paint in the deployable's own
                -- ChangeableData dynamic-property array (the field the file editor writes), so
                -- that entry is updated too, the same both-sides approach bench_tags.lua takes for
                -- upgrade tags; without it a live repaint could be lost on the next world save.
                -- Awaiting in-game verification.
                local ok, err = pcall(function()
                    local helper = replication.requireHelper()
                    obj.PaintedColor = payload.paintColor
                    pcall(function() obj:OnRep_PaintedColor() end)
                    replication.mark(helper, obj, "PaintedColor")
                    if savedPaint.write(obj, payload.paintColor) then
                        replication.mark(helper, obj, "ChangeableData")
                        pcall(function() obj:SaveDeployable() end)
                    end
                end)
                if not ok then error("could not set this object's paint colour on this game build: " .. tostring(err)) end
            end
            return nil
        end, respond)
    end
end
