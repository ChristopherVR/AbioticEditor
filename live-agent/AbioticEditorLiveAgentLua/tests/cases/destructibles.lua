-- Breakable world objects (areas/destructibles.lua). Property/function names here are the
-- CONFIRMED ones from the coordinator's CUE4Parse dump (Abiotic_GenericDestructible_BP_C's own
-- ChildProperties/ScriptBytecode) - see that module's own header comment for the full mapping and
-- why "repair" is refused rather than attempted. Discovery is a hierarchy sweep
-- (FindAllOf("Abiotic_GenericDestructible_BP_C")), not a hardcoded leaf-class list, so every fake
-- destructible object here needs `__bases = { "Abiotic_GenericDestructible_BP_C" }` for the fake
-- FindAllOf's own hierarchy matching (harness.lua's `matches()`) to find it at all - exactly
-- mirroring buttons.lua's own test fixture.
return function(H)
    H.hostSession()

    local function destructible(className, broken)
        return H.world.add(H.object(className, {
            __bases = { "Abiotic_GenericDestructible_BP_C" },
            Broken = broken,
        }, {
            -- Mirrors the real bytecode closely enough to prove the shape this module relies on:
            -- OnRep_Broken does nothing when Broken is false (no branch exists for it in the real
            -- class - see the module's own header), and only flips visible state to "broken" when
            -- Broken is true. The fake tracks that with a simple __visualBroken field so the test
            -- can prove a "repair" request never touches anything.
            OnRep_Broken = function(self)
                local fields = rawget(self, "__fields")
                if fields.Broken then fields.__visualBroken = true end
            end,
            K2_GetActorLocation = function() return H.vector(5, 5, 5) end,
        }))
    end

    -- iceWall: intact (Broken=false). A concrete class this module has always known about (still
    -- just data to the sweep, not a special case).
    local iceWall = destructible("IceWall_BP_C", false)
    -- webbing: already broken. Reflect the already-broken starting state the same way a real
    -- save-loaded actor would (its own OnLoadedFromSave-equivalent path already ran before this
    -- module ever sees it) - written via rawset so it doesn't count as a write this test asserts on.
    local webbing = destructible("Webbing_BP_C", true)
    rawget(webbing, "__fields").__visualBroken = true

    -- A brand-new destructible subclass this module has NEVER heard of (no hardcoded name anywhere
    -- in destructibles.lua matches "Destructible_TotallyNewType_C") - the hierarchy sweep must
    -- still find and list it purely because it declares Abiotic_GenericDestructible_BP_C as a
    -- base, exactly as a real future destructible type would.
    local futureType = H.world.add(H.object("Destructible_TotallyNewType_C", {
        __bases = { "Abiotic_GenericDestructible_BP_C" },
        Broken = false,
    }, {
        OnRep_Broken = function(self)
            local fields = rawget(self, "__fields")
            if fields.Broken then fields.__visualBroken = true end
        end,
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))

    -- Something that is NOT a destructible at all (no Abiotic_GenericDestructible_BP_C ancestry) -
    -- the hierarchy sweep must exclude it.
    H.world.add(H.object("NotADestructibleActor_C", { __bases = { "SomethingElseEntirely" } }, {}))

    -- A known-hierarchy destructible where Broken does not resolve at all: still listed (id/position
    -- are a plain FindAllOf sweep, never guessed), just with the state field absent.
    local unresolvable = H.world.add(H.object("Destructible_PortablePortal_C",
        { __bases = { "Abiotic_GenericDestructible_BP_C" } }, {
            K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
        }))

    local function rowFor(list, classFragment)
        for _, row in ipairs(list.destructibles) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local list = H.ok(H.dispatch("destructibles.list"), "destructibles.list")
    H.eq(#list.destructibles, 4, "every Abiotic_GenericDestructible_BP_C-derived actor is listed, the non-destructible excluded")
    local iceWallRow = rowFor(list, "IceWall_BP_C")
    local webbingRow = rowFor(list, "Webbing_BP_C")
    local futureRow = rowFor(list, "Destructible_TotallyNewType_C")
    local unresolvableRow = rowFor(list, "Destructible_PortablePortal_C")
    H.check(iceWallRow ~= nil and webbingRow ~= nil and futureRow ~= nil and unresolvableRow ~= nil,
        "all four rows found")
    H.eq(iceWallRow.broken, false, "ice wall intact")
    H.eq(webbingRow.broken, true, "webbing already broken")
    H.eq(futureRow.label, "Destructible_TotallyNewType_C",
        "an unfamiliar class's real name comes through as the row label, not a generic fallback")
    H.eq(futureRow.broken, false, "the unfamiliar class's Broken still reads (feature-detected, not assumed)")
    H.eq(unresolvableRow.broken, nil, "no live Broken property present, so the field is absent")

    -- destructibles.set: break the ice wall. The real OnRep_Broken path runs (proven by
    -- __visualBroken flipping), matching the game's own break behavior.
    H.ok(H.dispatch("destructibles.set", { destructibles = { { id = iceWallRow.id, broken = true } } }),
        "break the ice wall")
    H.eq(H.field(iceWall, "Broken"), true, "ice wall Broken field written")
    H.eq(H.calls(iceWall, "OnRep_Broken"), 1, "the real OnRep_Broken was called to do the actual break")
    H.eq(H.field(iceWall, "__visualBroken"), true, "the break actually took visual effect, not just the flag")

    -- Settable even though destructibles.lua has never heard of this exact class name - the whole
    -- point of hierarchy-based discovery.
    H.ok(H.dispatch("destructibles.set", { destructibles = { { id = futureRow.id, broken = true } } }),
        "break the unfamiliar future destructible type")
    H.eq(H.field(futureType, "Broken"), true, "the unfamiliar class's Broken field still writes")
    H.eq(H.calls(futureType, "OnRep_Broken"), 1, "its own OnRep_Broken still gets called")

    -- Repair is refused outright: never written, never a silent no-op, and any resolvable row in
    -- the SAME batch still applies first (matching buttons.set's pressedOnce shape).
    local repairReply = H.dispatch("destructibles.set", { destructibles = {
        { id = futureRow.id, broken = false },
    } })
    H.fails(repairReply, "cannot be repaired live", "a repair request is refused by name")
    H.eq(H.field(futureType, "Broken"), true, "Broken was never written back to false")

    local mixedReply = H.dispatch("destructibles.set", { destructibles = {
        { id = webbingRow.id, broken = true },
        { id = iceWallRow.id, broken = false },
    } })
    H.fails(mixedReply, "cannot be repaired live", "a mixed request still reports the repair refusal")
    H.eq(H.calls(webbing, "OnRep_Broken"), 1, "the real field in the same mixed request still applied")
    H.eq(H.field(iceWall, "Broken"), true, "the refused repair left the ice wall's Broken flag untouched")

    -- Missing destructible id: player-safe failure, not a Lua error - and any resolvable rows in
    -- the same call still apply first (matching doors.set/buttons.set).
    local missingReply = H.dispatch("destructibles.set", { destructibles = {
        { id = "no-such-destructible", broken = true },
    } })
    H.fails(missingReply, "not found", "unknown destructible id fails cleanly")
    H.eq(H.calls(unresolvable, "OnRep_Broken"), 0, "no call was made for a destructible never named in the batch")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(iceWall)
    H.fails(H.dispatch("destructibles.set", { destructibles = { { id = iceWallRow.id, broken = true } } }),
        "only the host", "client cannot break world objects")
end
