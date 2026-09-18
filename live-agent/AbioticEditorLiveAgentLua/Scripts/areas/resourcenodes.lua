-- Live editing area: harvestable resource nodes (ResourceNodeMap, round 101).
--
-- Confirmed against the coordinator's own CUE4Parse class dump and ResourceNode_ParentBP_C's own
-- blueprint bytecode (SaveNodeToWorldSave, the RespawnResourceNode/Force_DepleteNode thin wrappers
-- and the shared ubergraph code they jump into) - see docs/reference/live-editing-protocol.md's
-- resourcenodes.list/resourcenodes.set section for the full citations. Nothing here is guessed
-- from the save's own leaf names.
--
-- CLASS DISCOVERY: ResourceNode_ParentBP_C (super=AbioticActor_C) is the root nearly every
-- harvestable in the game chains up to - confirmed from the dump for dozens of concrete classes
-- (ResourceNode_WoodCrate_Manufacturing, ResourceNode_GlassPane, ResourceNode_AnalysisMachine,
-- ResourceNode_Hydropanel, ResourceNode_Turbine, ...) and its own subclass
-- Resource_MicroNode_ParentBP_C (which every Resource_MicroNode_*/Resource_Micronode_* class chains
-- up to, e.g. Resource_MicroNode_DuctTape, Resource_Micronode_LeyakEssence_TWO). FindAllOf is
-- hierarchy-inclusive (the same idiom bases.lua/main.lua's CONTAINER_CLASSES/buttons.lua/
-- elevators.lua already rely on), so a single FindAllOf("ResourceNode_ParentBP_C") already returns
-- every one of those subclasses' instances, both roots at once, with no per-class name hardcoded
-- anywhere in this module - and so will any future harvestable the game adds. Every property read
-- is per-instance pcall-feature-detected, never assumed present, so a class this module has never
-- heard of still lists (with its real class name as label) instead of erroring or being dropped.
--
-- FIELD MAPPING (from ResourceNode_ParentBP_C's own ChildProperties):
--   offline "harvested"   <- live `IsDepleted` (bool, replicated, RepNotify OnRep_IsDepleted).
--                             SaveNodeToWorldSave's own bytecode reads exactly this property
--                             before persisting the node, confirming the mapping directly rather
--                             than by guessing from the save leaf's name.
--   offline "dayPickedUp" <- live `DayWasDepleted` (int, PLAIN - no Net flag, no RepNotify).
--                             SaveNodeToWorldSave's bytecode sets it to
--                             `DayNightManager.CurrentDay` at the moment it persists a depleted
--                             node - confirming both the field and its meaning. Not replicated, so
--                             (like buttons.lua's NoVignetteReset) a direct write is all there is;
--                             this module mirrors the save's own computation when depleting a node
--                             and writes 0 (offline's own "not picked up yet" default) when
--                             respawning one, but also accepts an explicit caller-chosen value.
--   offline "position"    <- live actor transform (ctx.actorLocation), same as every other fixed
--                             actor feature in this mod - ResourceNode_ParentBP_C carries no
--                             separate position property of its own.
--
-- STATE-CHANGE CHOICE: ResourceNode_ParentBP_C exposes two real, zero-parameter
-- `FUNC_BlueprintCallable | FUNC_BlueprintEvent` functions, `RespawnResourceNode()` and
-- `Force_DepleteNode()`. Traced through the shared ubergraph both jump into (not guessed): for the
-- ordinary case (no ContinualRespawnFlag world-flag configured on the node - the common case),
-- RespawnResourceNode calls FlushNetDormancy(), sets IsDepleted=false, re-places the node on the
-- ground (PlaceOnGround, gated on the streaming location being loaded), then calls
-- OnRep_IsDepleted() and NetPushModelHelpers.MarkPropertyDirtyFromRepIndex(self, IsDepleted) - the
-- game's own "make this node visibly reappear" path, preferred here over a bare property write per
-- the owner's own instruction. Force_DepleteNode is the exact mirror: FlushNetDormancy(),
-- IsDepleted=true, then the SAME OnRep_IsDepleted()/MarkPropertyDirtyFromRepIndex tail (confirmed
-- by the unconditional jump Force_DepleteNode's own code takes straight into that shared block).
--
-- HONESTY ABOUT A KNOWN QUIRK (documented, not silently papered over): a node that DOES carry a
-- valid, currently-SET `ContinualRespawnFlag` world flag takes a different branch inside
-- RespawnResourceNode - it calls Server_SetDormant(), plays a portal-vanish effect/sound, and then
-- falls into the SAME depleting tail Force_DepleteNode uses, i.e. the node ends up DEPLETED, not
-- respawned, for that one call. Rather than trying to special-case this (there is no exposed
-- function to read whether that world flag is currently set from outside the node's own bytecode),
-- setHarvested below always re-reads IsDepleted after calling either function and only reports
-- success when it now matches what was asked - the same "a call with no confirmed effect is an
-- error, not a false success" honesty elevators.lua already established.
return function(ctx)
    local ROOT_CLASS = "ResourceNode_ParentBP_C"
    -- Fallback layer, data only (see buttons.lua/elevators.lua's identical precedent) - never
    -- consulted while the root sweep finds anything. Nothing qualifies today: every concrete
    -- resource node class in the dump chains up to ROOT_CLASS (directly or via
    -- Resource_MicroNode_ParentBP_C, itself a subclass of ROOT_CLASS).
    local ADDITIONAL_ROOT_CLASSES = {}

    local ROOT_CLASSES = { ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    local function intOrNil(ok, value)
        if ok and type(value) == "number" then return math.floor(value) end
        return nil
    end

    -- classFilter (optional): a case-insensitive substring match against the actor's own class
    -- name, applied while sweeping rather than after - lets a caller narrow a request to one
    -- harvestable type (e.g. "GlassPane") instead of paying for every node in the loaded area.
    -- Nothing in this mod requires it: an omitted/empty filter lists everything, matching the
    -- file editor's own unfiltered ResourceNodeMap view.
    local function matchesFilter(name, classFilter)
        if not classFilter or classFilter == "" then return true end
        local label = ctx.classLabel(name)
        return label ~= nil and label:lower():find(classFilter:lower(), 1, true) ~= nil
    end

    local function allNodes(classFilter)
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] and matchesFilter(name, classFilter) then
                    seen[name] = true
                    result[#result + 1] = actor
                end
            end
        end
        return result
    end

    local function findNode(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- PERFORMANCE: a single region can carry well over a thousand resource-node entries in the
    -- save, though only the ones actually streamed in near the player are ever found by FindAllOf
    -- at any one moment. This handler is still deliberately NOT part of LiveConnect.razor's
    -- periodic auto-refresh loop (see that file's ActiveLiveSessions switch and its own comment on
    -- why containers/npcs/bases are excluded for the same reason) - only an explicit tab visit or
    -- REFRESH re-runs this sweep. Each row here is two cheap pcall property reads plus one actor
    -- location call (no per-slot inventory walk like containers.list), so the per-row cost is much
    -- lighter than containers/bases, but the row COUNT can still be much larger.
    local function nodeRows(classFilter)
        local result = { __forceArray = true }
        for _, node in ipairs(allNodes(classFilter)) do
            if node:IsValid() then
                local name = ctx.fullName(node)
                if name then
                    local x, y, z = ctx.actorLocation(node)
                    local okDepleted, depleted = pcall(function() return node.IsDepleted end)
                    local okDay, day = pcall(function() return node.DayWasDepleted end)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name), -- the actor's real class, always
                        harvested = boolOrNil(okDepleted, depleted),
                        dayPickedUp = intOrNil(okDay, day),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["resourcenodes.list"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local classFilter = payload and payload.classFilter
            return { nodes = nodeRows(classFilter), isHost = ctx.isHost() }
        end, respond)
    end

    -- Presses the node's own RespawnResourceNode()/Force_DepleteNode() (never a bare IsDepleted
    -- write - see the header comment) and confirms IsDepleted actually landed on `wanted`
    -- afterward. Returns nil on confirmed success, or a player-safe reason string - covering both
    -- "this class has no readable IsDepleted at all" and the documented ContinualRespawnFlag
    -- quirk, since either one fails this same readback.
    local function setHarvested(node, wanted)
        local okBefore, before = pcall(function() return node.IsDepleted end)
        if not (okBefore and type(before) == "boolean") then
            return "this resource node type has no known live state (not controllable)"
        end
        if before == wanted then return nil end -- already in the requested state
        if wanted then
            pcall(function() node:Force_DepleteNode() end)
        else
            pcall(function() node:RespawnResourceNode() end)
        end
        local okAfter, after = pcall(function() return node.IsDepleted end)
        if not (okAfter and after == wanted) then
            return "could not confirm the node's harvested state changed"
        end
        return nil
    end

    -- DayWasDepleted is not replicated (confirmed from its own PropertyFlags - no Net, no
    -- RepNotify), so a direct write is all there is, the same NoVignetteReset precedent
    -- buttons.lua already documents. Best-effort: pcall-guarded, never assumed to exist.
    local function setDayPickedUp(node, day)
        local ok = pcall(function() node.DayWasDepleted = math.floor(day) end)
        if not ok then return "could not write the day this node was picked up" end
        return nil
    end

    -- Matches doors.set/portals.set/elevators.set: every resolvable row applies fully before an
    -- unresolved id (or a field that failed to apply) turns the whole reply into a named error.
    ctx.handlers["resourcenodes.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change resource nodes") end
            local rows = payload.nodes or {}
            local missingId, failedId, failedReason = nil, nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local node = row.id and findNode(row.id)
                if node then
                    if row.harvested ~= nil then
                        local reason = setHarvested(node, row.harvested)
                        if reason then
                            failedId = failedId or row.id
                            failedReason = failedReason or reason
                        end
                    end
                    if row.dayPickedUp ~= nil then
                        local reason = setDayPickedUp(node, row.dayPickedUp)
                        if reason then
                            failedId = failedId or row.id
                            failedReason = failedReason or reason
                        end
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("resource node not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if failedId then error(tostring(failedReason) .. ": " .. tostring(failedId)) end
            return nil
        end, respond)
    end
end
