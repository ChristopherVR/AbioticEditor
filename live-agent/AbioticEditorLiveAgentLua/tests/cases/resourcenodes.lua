-- Harvestable resource nodes (areas/resourcenodes.lua). Property/function names here are the
-- CONFIRMED ones from the coordinator's CUE4Parse dump (ResourceNode_ParentBP_C's own
-- ChildProperties/ScriptBytecode) - see the module's own header comment for the full mapping.
-- Discovery is a hierarchy sweep (FindAllOf("ResourceNode_ParentBP_C")), not a hardcoded leaf-class
-- list, so every fake node here needs `__bases = { "ResourceNode_ParentBP_C" }` (or a chain through
-- it) for the fake FindAllOf's own hierarchy matching (harness.lua's `matches()`) to find it at
-- all - exactly mirroring how the real UE4SS FindAllOf is hierarchy-inclusive.
return function(H)
    H.hostSession()

    local function node(className, isDepleted, dayWasDepleted, bases)
        return H.world.add(H.object(className, {
            __bases = bases or { "ResourceNode_ParentBP_C" },
            IsDepleted = isDepleted,
            DayWasDepleted = dayWasDepleted,
        }, {
            Force_DepleteNode = function(self) rawget(self, "__fields").IsDepleted = true end,
            RespawnResourceNode = function(self) rawget(self, "__fields").IsDepleted = false end,
            K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
        }))
    end

    -- Available, never harvested, day 0 - the common case.
    local crate = node("ResourceNode_WoodCrate_Manufacturing_C", false, 0)
    -- Already depleted on day 7 - a Resource_MicroNode subclass, reached via the SAME
    -- FindAllOf("ResourceNode_ParentBP_C") sweep since Resource_MicroNode_ParentBP_C itself chains
    -- up to ResourceNode_ParentBP_C (never hardcoded as a second root in the module). The fake
    -- harness's own `matches()` only checks direct __bases membership (no transitive resolution,
    -- unlike UE4SS's real hierarchy-inclusive FindAllOf), so both ancestor names are listed here -
    -- that is a fixture-fidelity detail of this harness, not something resourcenodes.lua itself
    -- needs to know about.
    local micro = node("Resource_MicroNode_DuctTape_C", true, 7,
        { "Resource_MicroNode_ParentBP_C", "ResourceNode_ParentBP_C" })
    -- A window-type node, matched by the offline naming heuristic's own "GlassPane" special case -
    -- this module doesn't care about that distinction (it's a WorldFeaturesTab-only display rule),
    -- but it is a useful classFilter target below.
    local glass = node("ResourceNode_GlassPane_C", false, 0)

    -- A brand-new subclass this module has NEVER heard of by name (no class name anywhere in
    -- resourcenodes.lua matches "ResourceNode_TotallyNewType_C") - the hierarchy sweep must still
    -- find, list AND set it purely because it declares ResourceNode_ParentBP_C as a base, with the
    -- same real Force_DepleteNode/RespawnResourceNode functions any concrete node class has -
    -- proving the whole point of hierarchy-based discovery: nothing here needed a code change for
    -- a name this module has never heard of.
    local futureType = node("ResourceNode_TotallyNewType_C", false, 0)

    -- A resource-node-hierarchy actor with no readable IsDepleted at all (an unusual/partial
    -- instance) - still listed (id/position are a plain FindAllOf sweep, never guessed), just with
    -- both state fields absent.
    local unresolvable = H.world.add(H.object("ResourceNode_Unresolvable_C",
        { __bases = { "ResourceNode_ParentBP_C" } },
        { K2_GetActorLocation = function() return H.vector(7, 7, 7) end }))

    -- A node whose Force_DepleteNode exists but silently does nothing - proves the readback
    -- honesty check (a call with no confirmed effect is an error, not a false success), covering
    -- both "wrong function name" bugs and the documented ContinualRespawnFlag quirk.
    local stubborn = H.world.add(H.object("ResourceNode_Stubborn_C", {
        __bases = { "ResourceNode_ParentBP_C" },
        IsDepleted = false,
        DayWasDepleted = 0,
    }, {
        Force_DepleteNode = function() end, -- no-op: does not flip IsDepleted
        K2_GetActorLocation = function() return H.vector(8, 8, 8) end,
    }))

    -- Something that is NOT a resource node at all (no ResourceNode_ParentBP_C ancestry) - the
    -- hierarchy sweep must exclude it.
    H.world.add(H.object("NotAResourceNodeActor_C", { __bases = { "SomethingElseEntirely" } }, {}))

    local function rowFor(list, classFragment)
        for _, row in ipairs(list.nodes) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local list = H.ok(H.dispatch("resourcenodes.list"), "resourcenodes.list")
    H.eq(#list.nodes, 6, "every ResourceNode_ParentBP_C-derived actor is listed, the non-node excluded")
    local crateRow = rowFor(list, "ResourceNode_WoodCrate_Manufacturing_C")
    local microRow = rowFor(list, "Resource_MicroNode_DuctTape_C")
    local futureRow = rowFor(list, "ResourceNode_TotallyNewType_C")
    local unresolvableRow = rowFor(list, "ResourceNode_Unresolvable_C")
    local stubbornRow = rowFor(list, "ResourceNode_Stubborn_C")
    H.check(crateRow ~= nil and microRow ~= nil and futureRow ~= nil and unresolvableRow ~= nil and stubbornRow ~= nil,
        "all five distinguishable rows found")
    H.eq(crateRow.harvested, false, "crate not yet harvested")
    H.eq(crateRow.dayPickedUp, 0, "crate has no pickup day yet")
    H.eq(microRow.harvested, true, "micro-node subclass reached via the same single sweep, already depleted")
    H.eq(microRow.dayPickedUp, 7, "micro-node's saved pickup day reads back")
    H.eq(futureRow.label, "ResourceNode_TotallyNewType_C",
        "an unfamiliar class's real name comes through as the row label, not a generic fallback")
    H.eq(futureRow.harvested, false, "the unfamiliar class's IsDepleted still reads (feature-detected, not assumed)")
    H.eq(unresolvableRow.harvested, nil, "no live IsDepleted on this instance, so the field is absent")
    H.eq(unresolvableRow.dayPickedUp, nil, "no live DayWasDepleted on this instance either")

    -- classFilter narrows the sweep to one harvestable type.
    local filtered = H.ok(H.dispatch("resourcenodes.list", { classFilter = "GlassPane" }), "filtered list")
    H.eq(#filtered.nodes, 1, "classFilter matches only the glass-pane node")
    H.check(filtered.nodes[1].id:find("GlassPane", 1, true) ~= nil, "filtered row is the glass pane")

    -- resourcenodes.set: deplete the crate via Force_DepleteNode (never a bare IsDepleted write).
    H.ok(H.dispatch("resourcenodes.set", { nodes = { { id = crateRow.id, harvested = true } } }),
        "deplete the crate")
    H.eq(H.field(crate, "IsDepleted"), true, "crate's own Force_DepleteNode ran and flipped IsDepleted")
    H.eq(H.calls(crate, "Force_DepleteNode"), 1, "Force_DepleteNode called exactly once")
    H.eq(H.calls(crate, "RespawnResourceNode"), 0, "RespawnResourceNode never called for a deplete request")

    -- Respawn the micro-node via RespawnResourceNode.
    H.ok(H.dispatch("resourcenodes.set", { nodes = { { id = microRow.id, harvested = false } } }),
        "respawn the micro-node")
    H.eq(H.field(micro, "IsDepleted"), false, "micro-node's own RespawnResourceNode ran and cleared IsDepleted")
    H.eq(H.calls(micro, "RespawnResourceNode"), 1, "RespawnResourceNode called exactly once")

    -- Already in the requested state: no function call at all (matching elevators.lua's
    -- "already parked there" skip).
    H.ok(H.dispatch("resourcenodes.set", { nodes = { { id = microRow.id, harvested = false } } }),
        "asking for the already-current state")
    H.eq(H.calls(micro, "RespawnResourceNode"), 1, "still only ever called once - no redundant call")

    -- dayPickedUp is a direct, non-replicated write.
    H.ok(H.dispatch("resourcenodes.set", { nodes = { { id = crateRow.id, dayPickedUp = 12 } } }),
        "set the crate's pickup day")
    H.eq(H.field(crate, "DayWasDepleted"), 12, "DayWasDepleted written directly")

    -- Settable even though this module has never heard of this exact class name - the whole point
    -- of hierarchy-based discovery.
    H.ok(H.dispatch("resourcenodes.set", { nodes = { { id = futureRow.id, harvested = true } } }),
        "deplete the unfamiliar future node type")
    H.eq(H.field(futureType, "IsDepleted"), true, "the unfamiliar class's IsDepleted field still writes")

    -- A class with no readable IsDepleted at all: an honest, named failure, not a silent no-op.
    local unresolvableReply = H.dispatch("resourcenodes.set", { nodes = { { id = unresolvableRow.id, harvested = true } } })
    H.fails(unresolvableReply, "not controllable", "a node with no live state fails by name")

    -- A node whose Force_DepleteNode exists but has no real effect: the readback honesty check
    -- catches it instead of reporting a false success.
    local stubbornReply = H.dispatch("resourcenodes.set", { nodes = { { id = stubbornRow.id, harvested = true } } })
    H.fails(stubbornReply, "could not confirm", "a call with no confirmed effect is reported as an error")
    H.eq(H.field(stubborn, "IsDepleted"), false, "IsDepleted genuinely never changed")

    -- Missing node id: player-safe failure, not a Lua error - and any resolvable rows in the same
    -- call still apply first (matching doors.set/portals.set/buttons.set).
    local missingReply = H.dispatch("resourcenodes.set", { nodes = {
        { id = crateRow.id, dayPickedUp = 20 },
        { id = "no-such-node", harvested = true },
    } })
    H.fails(missingReply, "not found", "unknown node id fails cleanly")
    H.eq(H.field(crate, "DayWasDepleted"), 20, "the resolvable row in the same batch still applied")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(crate)
    H.fails(H.dispatch("resourcenodes.set", { nodes = { { id = crateRow.id, harvested = false } } }),
        "only the host", "client cannot edit resource nodes")
end
