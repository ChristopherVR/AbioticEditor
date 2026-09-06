-- Live editing area: Leyak Containment Units (round 76).
--
-- Every property/function name below comes straight from the game's own blueprint class layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, "Deployed_LeyakContainment" fragment added
-- this round - dump lives in the PR/session notes) plus CheatConsoleCommands' own working
-- trap/free commands, copied close to verbatim:
--   CommandsManager.lua:942-1081   ("trapleyak"/"freeleyak"/"trapkrasue"/"freekrasue" commands)
--   AFUtils/AFUtils.lua:749-806    (TrapLeyakTypeNpc / FreeLeyakTypeNpc - the actual write path)
--   AFUtils/ObjectsGetter.lua:198-226 (GetAIDirector / GetLeyakDirectorComponent / GetKrasueDirectorComponent)
--   AFUtils/Names.lua:12-17        (LeyakRowName="Leyak", KrasueRowName="Krasue", the food rows)
-- The class dump confirms: ContainsLeyak (FName, "None" when empty, else the creature row),
-- "Stability Level" (FIntProperty - yes, a literal space in the name), MaxStability,
-- TrapLeyak(Amount, RowName), ServerUpdateStabilityLevel(Value, RowName), and "Free Leyak"
-- (again a literal space) as the release function - all inherited DeployableDestroyed/
-- SpawnedAssetID come from the shared AbioticDeployed_ParentBP_C parent, already probed in
-- round 75 and used unmodified by the reference mod's own TrapLeyakTypeNpc/FreeLeyakTypeNpc.
--
-- Only Leyak and Krasue are containable (Core's ContainmentCreatureCatalog.Containable has
-- exactly two entries, matching the blueprint's own two-entry LeyakContainmentData array), so
-- this module only ever traps/frees those two row names.
--
-- Reachable via GetAIDirector() -> AI_Director.LeyakDirectorComponent /.KrasueDirectorComponent,
-- but this module reaches AI_Director through UEHelpers.GetWorld().AuthorityGameMode (the same
-- world->game-mode lookup main.lua's own isHost() already uses and this codebase has already
-- proven works live) rather than the reference mod's separate GetGameModeBase() global, to reuse
-- an access path this project has already verified end to end instead of introducing a second,
-- untested one for the same object.
return function(ctx)
    local UNIT_CLASS = "Deployed_LeyakContainment_C"
    local KRASUE_ROW = "Krasue"
    -- ContainmentCreatureCatalog.Containable's StabilityItem rows (Core/Catalogs/World/
    -- ContainmentCreatureCatalog.cs), also AFUtils.FoodGreyebName ("food_greyeb") / the Krasue's
    -- ice-cream food row used by AFUtils.TrapKrasue.
    local FOOD_BY_ROW = { Leyak = "food_greyeb", Krasue = "food_milk" }

    local function gameMode()
        local ok, world = pcall(function() return ctx.UEHelpers.GetWorld() end)
        if not ok or not world or not world:IsValid() then return nil end
        local ok2, gm = pcall(function() return world.AuthorityGameMode end)
        if ok2 and gm and gm:IsValid() then return gm end
        return nil
    end

    ---@return userdata? # the LeyakDirectorComponent or KrasueDirectorComponent for `row`.
    local function directorComponent(row)
        local gm = gameMode()
        if not gm then return nil end
        local okDirector, director = pcall(function() return gm.AI_Director end)
        if not okDirector or not director or not director:IsValid() then return nil end
        local propName = row == KRASUE_ROW and "KrasueDirectorComponent" or "LeyakDirectorComponent"
        local okComponent, component = pcall(function() return director[propName] end)
        if okComponent and component and component:IsValid() then return component end
        return nil
    end

    ---@return string? # the creature row this unit currently holds, or nil when empty.
    local function unitOccupant(unit)
        local ok, name = pcall(function() return unit.ContainsLeyak:ToString() end)
        if ok and name and name ~= "" and name ~= "None" then return name end
        return nil
    end

    local function unitRows()
        local result = { __forceArray = true }
        for _, unit in ipairs(ctx.findAll(UNIT_CLASS)) do
            if unit:IsValid() then
                local okDestroyed, destroyed = pcall(function() return unit.DeployableDestroyed == true end)
                if not (okDestroyed and destroyed) then
                    local name = ctx.fullName(unit)
                    if name then
                        local x, y, z = ctx.actorLocation(unit)
                        -- "Stability Level" carries a literal space in the blueprint - read only
                        -- (the file editor shows it read-only too); no confirmed direct-write path
                        -- exists, only the feed-to-refill flow inside trapInto below.
                        local okStability, stability = pcall(function() return unit["Stability Level"] end)
                        table.insert(result, {
                            id = name,
                            x = x, y = y, z = z,
                            stability = okStability and stability or nil,
                            creature = unitOccupant(unit),
                        })
                    end
                end
            end
        end
        return result
    end

    ctx.handlers["containment.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { units = unitRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Verbatim FreeLeyakTypeNpc (AFUtils.lua:785-793): the unit's own "Free Leyak" function
    -- (called with no arguments, exactly as that mod calls it: LeyakContainment['Free Leyak']()),
    -- then clear the matching director's ActiveLeyakContainmentID.
    local function freeUnit(unit)
        local occupant = unitOccupant(unit)
        if not occupant then return end
        local director = directorComponent(occupant)
        pcall(function() unit["Free Leyak"]() end)
        if director then pcall(function() director:SetLeyakContainmentID("") end) end
    end

    -- Verbatim TrapLeyakTypeNpc (AFUtils.lua:754-767): feed the unit's stability item to full,
    -- then TrapLeyak(0.0, rowName) (the working mod's own two-argument call - the class dump only
    -- lists one reflected child property on the function, but the shipped mod's call is proven
    -- live, so it is followed exactly rather than trusting an incomplete-looking reflection dump),
    -- then point the matching director's ActiveLeyakContainmentID at this unit's own
    -- SpawnedAssetID (inherited from AbioticDeployed_ParentBP_C, probed in round 75).
    local function trapInto(unit, row)
        local director = directorComponent(row)
        if not director then error("the " .. row .. " director is not loaded (are you in a world?)") end
        local food = FOOD_BY_ROW[row]
        if food then
            pcall(function() unit:ServerUpdateStabilityLevel(unit.MaxStability, FName(food, EFindName.FNAME_Find)) end)
        end
        unit:TrapLeyak(0.0, FName(row, EFindName.FNAME_Find))
        local okAsset, assetId = pcall(function() return unit.SpawnedAssetID:ToString() end)
        if okAsset then pcall(function() director:SetLeyakContainmentID(assetId) end) end
    end

    -- One action per call (assign/release/swap), matching the editor's own
    -- SetContainmentUnitOccupant/ReleaseContainment/SwapContainmentUnits shapes so the live
    -- session can mirror the file session's semantics exactly.
    ctx.handlers["containment.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change containment units") end
            local action = payload.action

            if action == "release" then
                local row = payload.creature
                if not row then error("release needs a creature") end
                for _, unit in ipairs(ctx.findAll(UNIT_CLASS)) do
                    if unit:IsValid() and unitOccupant(unit) == row then
                        freeUnit(unit)
                        break
                    end
                end
            elseif action == "assign" then
                local unit = payload.unitId and ctx.findByFullName(UNIT_CLASS, payload.unitId)
                if not unit then error("containment unit not found (it may have been unloaded or destroyed)") end
                local row = payload.creature
                if not row then error("assign needs a creature") end
                local unitName = ctx.fullName(unit)
                -- Moving a creature automatically takes it out of wherever it already was
                -- (mirrors SetContainmentUnitOccupant's own rule), and evicts whoever the target
                -- unit currently holds.
                for _, other in ipairs(ctx.findAll(UNIT_CLASS)) do
                    if other:IsValid() then
                        local otherName = ctx.fullName(other)
                        local occupant = unitOccupant(other)
                        if occupant == row and otherName ~= unitName then freeUnit(other) end
                        if otherName == unitName and occupant and occupant ~= row then freeUnit(other) end
                    end
                end
                if unitOccupant(unit) ~= row then trapInto(unit, row) end
            elseif action == "swap" then
                local unitA = payload.unitIdA and ctx.findByFullName(UNIT_CLASS, payload.unitIdA)
                local unitB = payload.unitIdB and ctx.findByFullName(UNIT_CLASS, payload.unitIdB)
                if not unitA or not unitB then error("containment unit not found (it may have been unloaded or destroyed)") end
                local rowA, rowB = unitOccupant(unitA), unitOccupant(unitB)
                if rowA then freeUnit(unitA) end
                if rowB then freeUnit(unitB) end
                if rowA then trapInto(unitB, rowA) end
                if rowB then trapInto(unitA, rowB) end
            else
                error("unknown containment action '" .. tostring(action) .. "'")
            end
            return nil
        end, respond)
    end
end
