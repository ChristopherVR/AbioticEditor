return function(H)
    local pawn = H.hostSession()
    local class = H.object("Class")
    H.world.static("/Script/AbioticFactor.CharacterBuffComponent", class)
    local applied, removed = {}, {}
    local function entry(row, limb, expire)
        return { BuffRow = { RowName = H.fname(row) }, ParentLimb = limb, BuffExpireTime = expire }
    end
    local buffs = H.object("CharacterBuffComponent", {
        CurrentBuffs = {
            entry("Debuff_BrokenLeg", 7, -1),
            entry("Buff_Caffeinated", 0, 512.5),
            entry("Debuff_Underwater", 0, 3),
            entry("Debuff_Bleeding", 4, 90),
        },
    }, {
        Server_ApplyBuff = function(_, handle, override, seconds, limb, linked, skipDialog)
            applied[#applied + 1] = { row = handle.RowName:ToString(), override = override, seconds = seconds,
                limb = limb, linked = linked, skipDialog = skipDialog }
        end,
        Server_RemoveBuff = function(_, handle, allStacks, limb, flag)
            removed[#removed + 1] = { row = handle.RowName:ToString(), allStacks = allStacks, limb = limb, flag = flag }
        end,
        GetOwner = function() return pawn end,
    })
    rawget(pawn, "__methods").GetComponentByClass = function(_, requested)
        H.eq(requested, class, "effects use the native character buff component")
        return buffs
    end
    H.world.static("/Script/AbioticFactor.Default__BuffDebuffHandleFunctionLibrary", H.object("BuffLibrary", {}, {
        MakeBuffDebuffRowHandle = function(_, name) return { RowName = name } end,
        GetAllBuffDebuffRowNames = function(_, out)
            for i, row in ipairs({ "Debuff_BrokenLeg", "Buff_Caffeinated", "Debuff_Underwater", "Debuff_Bleeding", "Debuff_Stinky" }) do
                out[i] = H.outParam(H.fname(row))
            end
        end,
    }))

    -- list
    local listed = H.ok(H.dispatch("effects.list"), "effects.list")
    H.eq(#listed.effects, 4, "every current effect listed")
    H.eq(listed.effects[1].row, "Debuff_BrokenLeg", "row name read from the handle")
    H.eq(listed.effects[1].limb, 7, "limb read as its enum number")
    H.eq(listed.effects[2].expireTime, 512.5, "expire time passed through")
    H.eq(listed.blockDebuffs, false, "blocking starts off")
    H.eq(listed.isHost, true, "host flag reported")

    -- add: the game's own argument order (row, override, seconds, limb, linked actor, skip dialog)
    H.ok(H.dispatch("effects.add", { row = "Debuff_Stinky", seconds = 60 }), "timed add")
    H.eq(applied[1].row, "Debuff_Stinky", "added row")
    H.eq(applied[1].override, true, "a duration overrides the default")
    H.eq(applied[1].seconds, 60, "duration in seconds")
    H.eq(applied[1].limb, 0, "added on all limbs")
    H.eq(applied[1].skipDialog, true, "no character voice line for an editor add")
    H.ok(H.dispatch("effects.add", { row = "Buff_Caffeinated" }), "default-duration add")
    H.eq(applied[2].override, false, "no duration keeps the game's default")
    H.fails(H.dispatch("effects.add", { row = "Buff_NotReal" }), "unknown effect", "unknown rows are refused")
    H.eq(#applied, 2, "a refused add never reaches the game")

    -- remove one, with its own limb, matching the Crystal Lens call shape
    H.ok(H.dispatch("effects.remove", { row = "Debuff_BrokenLeg", limb = 7 }), "remove")
    H.eq(removed[1].row, "Debuff_BrokenLeg", "removed row")
    H.eq(removed[1].allStacks, true, "remove passes true like the game does")
    H.eq(removed[1].limb, 7, "remove targets the effect's limb")
    H.eq(removed[1].flag, false, "remove passes false like the game does")

    -- clear debuffs only, never the underwater state
    local cleared = H.ok(H.dispatch("effects.clear"), "clear debuffs")
    H.eq(cleared.removed, 2, "broken leg and bleeding cleared")
    H.eq(removed[2].row, "Debuff_BrokenLeg", "first debuff cleared")
    H.eq(removed[3].row, "Debuff_Bleeding", "second debuff cleared")
    H.eq(#removed, 3, "buffs and the underwater state stay")

    -- block: registers the hook once, clears existing debuffs, then shortens new ones
    local hook
    local realRegister = _G.RegisterHook
    local registrations = 0
    _G.RegisterHook = function(path, callback)
        H.eq(path, "/Script/AbioticFactor.CharacterBuffComponent:Server_ApplyBuff", "hooks the game's apply call")
        registrations = registrations + 1
        hook = callback
    end
    H.eq(H.ok(H.dispatch("effects.block", { enabled = true }), "block on").blockDebuffs, true, "block reported on")
    H.eq(#removed, 5, "turning blocking on clears current debuffs too")
    H.ok(H.dispatch("effects.block", { enabled = true }), "block on again")
    H.eq(registrations, 1, "the hook is registered only once")
    H.eq(H.ok(H.dispatch("effects.list")).blockDebuffs, true, "list reports blocking")

    local function param(value)
        local p = { value = value }
        function p:get() return self.value end
        function p:set(v) self.value = v end
        return p
    end
    local function fire(row, owner)
        local duration, override, skip = param(30), param(false), param(false)
        local component = { get = function() return { GetOwner = function() return owner end } end }
        hook(component, param({ RowName = H.fname(row) }), override, duration, param(0), param(nil), skip)
        return duration:get(), override:get(), skip:get()
    end
    local duration, override, skip = fire("Debuff_Bleeding", pawn)
    H.eq(duration, 0.001, "a new debuff is cut to a blink")
    H.eq(override, true, "and its default duration is overridden")
    H.eq(skip, true, "and its voice line skipped")
    H.eq(fire("Buff_Caffeinated", pawn), 30, "buffs are left alone")
    H.eq(fire("Debuff_Underwater", pawn), 30, "the underwater state is left alone")
    H.eq(fire("Debuff_Bleeding", H.object("Abiotic_PlayerCharacter_C")), 30, "other players are left alone")

    H.eq(H.ok(H.dispatch("effects.block", { enabled = false }), "block off").blockDebuffs, false, "block reported off")
    H.eq(fire("Debuff_Bleeding", pawn), 30, "blocking off lets debuffs through again")
    H.fails(H.dispatch("effects.block", {}), "true or false", "enabled is required")
    _G.RegisterHook = realRegister

    -- guests cannot change effects
    H.clientSession()
    H.fails(H.dispatch("effects.add", { row = "Buff_Caffeinated" }), "only the host", "client add rejected")
    H.fails(H.dispatch("effects.remove", { row = "Debuff_Bleeding" }), "only the host", "client remove rejected")
    H.fails(H.dispatch("effects.block", { enabled = true }), "only the host", "client block rejected")
end
