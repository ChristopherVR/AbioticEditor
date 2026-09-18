-- NPC corpses (areas/corpses.lua). Property names here are the CONFIRMED ones from the
-- coordinator's CUE4Parse dump (CharacterCorpse_ParentBP_C's own ChildProperties) - see that
-- module's own header comment for the full mapping and why removal uses K2_DestroyActor. Discovery
-- is a hierarchy sweep (FindAllOf("CharacterCorpse_ParentBP_C")), not a hardcoded leaf-class list,
-- so every fake corpse object here needs `__bases = { "CharacterCorpse_ParentBP_C" }` for the fake
-- FindAllOf's own hierarchy matching (harness.lua's `matches()`) to find it at all - exactly
-- mirroring buttons.lua's/pets.lua's own test fixtures.
return function(H)
    H.hostSession()

    local function corpse(className, gibbed, looted, extraMethods)
        local methods = {
            K2_GetActorLocation = function() return H.vector(7, 7, 7) end,
            -- Same technique pets.lua's own K2_DestroyActor fake uses: mirrors what a real
            -- K2_DestroyActor does to FindAllOf's view of the world - the actor stops being valid.
            -- extraMethods overrides this below for the one test that needs a destroy call to fail.
            K2_DestroyActor = function(self) rawset(self, "__valid", false) end,
        }
        if extraMethods then for k, v in pairs(extraMethods) do methods[k] = v end end
        return H.world.add(H.object(className, {
            __bases = { "CharacterCorpse_ParentBP_C" },
            IsGibbed = gibbed,
            HasBeenLooted = looted,
        }, methods))
    end

    -- orderGrunt: not gibbed, not looted yet. A concrete class this module has always known about
    -- (still just data to the sweep, not a special case).
    local orderGrunt = corpse("CharacterCorpse_OrderGrunt_C", false, false)
    -- human: gibbed and already looted.
    local human = corpse("CharacterCorpse_Human_BP_C", true, true)

    -- A brand-new corpse subclass this module has NEVER heard of (no hardcoded name anywhere in
    -- corpses.lua matches "CharacterCorpse_TotallyNewType_C") - the hierarchy sweep must still find
    -- and list it purely because it declares CharacterCorpse_ParentBP_C as a base, exactly as a
    -- real future corpse type would. It also only has SOME of the usual properties (no
    -- HasBeenLooted at all), proving per-field feature detection: the present field still reads
    -- normally, the missing one reports as unavailable instead of erroring the whole corpse away.
    local futureType = H.world.add(H.object("CharacterCorpse_TotallyNewType_C", {
        __bases = { "CharacterCorpse_ParentBP_C" },
        IsGibbed = false,
    }, {
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))

    -- Something that is NOT a corpse at all (no CharacterCorpse_ParentBP_C ancestry) - the
    -- hierarchy sweep must exclude it.
    H.world.add(H.object("NotACorpseActor_C", { __bases = { "SomethingElseEntirely" } }, {}))

    local function rowFor(list, classFragment)
        for _, row in ipairs(list.corpses) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local list = H.ok(H.dispatch("corpses.list"), "corpses.list")
    H.eq(#list.corpses, 3, "every CharacterCorpse_ParentBP_C-derived actor is listed, the non-corpse excluded")
    local orderGruntRow = rowFor(list, "CharacterCorpse_OrderGrunt_C")
    local humanRow = rowFor(list, "CharacterCorpse_Human_BP_C")
    local futureRow = rowFor(list, "CharacterCorpse_TotallyNewType_C")
    H.check(orderGruntRow ~= nil and humanRow ~= nil and futureRow ~= nil, "all three rows found")
    H.eq(orderGruntRow.gibbed, false, "order grunt corpse not gibbed")
    H.eq(orderGruntRow.looted, false, "order grunt corpse not looted")
    H.eq(humanRow.gibbed, true, "human corpse gibbed")
    H.eq(humanRow.looted, true, "human corpse already looted")
    H.eq(futureRow.label, "CharacterCorpse_TotallyNewType_C",
        "an unfamiliar class's real name comes through as the row label, not a generic fallback")
    H.eq(futureRow.gibbed, false, "the unfamiliar class's IsGibbed still reads (feature-detected, not assumed)")
    H.eq(futureRow.looted, nil, "the unfamiliar class has no HasBeenLooted at all, so the field is absent")

    -- corpses.remove: unknown id fails cleanly, not a Lua error.
    H.fails(H.dispatch("corpses.remove", { id = "no-such-corpse" }), "not found", "removing an unknown corpse id fails cleanly")

    -- Real removal: destroys the live actor outright, the same K2_DestroyActor technique
    -- pets.remove uses - no undo, no "gib" shortcut (checked and rejected, see the module's own
    -- header comment).
    H.ok(H.dispatch("corpses.remove", { id = orderGruntRow.id }), "corpses.remove")
    H.eq(H.calls(orderGrunt, "K2_DestroyActor"), 1, "the corpse's actor was destroyed")
    H.eq(#H.ok(H.dispatch("corpses.list")).corpses, 2, "the removed corpse no longer appears")

    -- A corpse whose K2_DestroyActor call itself fails (a pcall genuinely erroring): an honest,
    -- named failure rather than a false success.
    local stubborn = corpse("CharacterCorpse_MonsterGeneric_C", false, false, {
        K2_DestroyActor = function() error("cannot destroy") end,
    })
    local stubbornList = H.ok(H.dispatch("corpses.list"), "corpses.list after adding the stubborn one")
    local stubbornRow = rowFor(stubbornList, "CharacterCorpse_MonsterGeneric_C")
    H.fails(H.dispatch("corpses.remove", { id = stubbornRow.id }), "couldn't remove",
        "a destroy call that itself errors is an honest failure, not a false success")

    -- Non-host refusal.
    H.clientSession()
    local clientCorpse = corpse("CharacterCorpse_OrderGrunt_C", false, false)
    local clientList = H.ok(H.dispatch("corpses.list"), "corpses.list as client")
    local clientRow = rowFor(clientList, "CharacterCorpse_OrderGrunt_C")
    H.fails(H.dispatch("corpses.remove", { id = clientRow.id }), "only the host", "client cannot remove corpses")
end
