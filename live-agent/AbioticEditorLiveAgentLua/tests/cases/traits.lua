return function(H)
    local pawn = H.hostSession()
    local component = pawn.CharacterProgressionComponent
    component.Traits = { H.fname("Trait_Chef"), H.fname("Trait_Sundisk"), H.fname("Trait_Sundisk") }
    local dirty = 0
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("NetPushModelHelpers", {}, {
        MarkPropertyDirty = function(_, object, name)
            H.eq(object, component, "trait dirty notification uses progression")
            H.eq(name:ToString(), "Traits", "trait replication property")
            dirty = dirty + 1
        end,
    }))
    local class = H.object("Class")
    H.world.static("/Script/AbioticFactor.CharacterBuffComponent", class)
    local calls = {}
    local buffs = H.object("CharacterBuffComponent", {}, {
        Server_AddTraitBuff = function(_, handle) calls[#calls + 1] = "add:" .. handle.row end,
        Server_RemoveTraitBuff = function(_, handle) calls[#calls + 1] = "remove:" .. handle.row end,
    })
    rawget(pawn, "__methods").GetComponentByClass = function(_, requested)
        H.eq(requested, class, "native character buff component")
        return buffs
    end
    H.world.static("/Script/AbioticFactor.Default__BuffDebuffHandleFunctionLibrary", H.object("BuffLibrary", {}, {
        MakeBuffDebuffRowHandle = function(_, name) return {row = name:ToString(), RowName = name,
            DataTablePath = H.fstring("/Script/Engine.DataTable'/Game/Buffs.Buffs'")} end,
    }))
    H.world.static("/Game/Buffs.Buffs", H.object("DataTable"))
    H.world.static("/Script/Engine.Default__DataTableFunctionLibrary", H.object("TableLibrary", {}, {
        DoesDataTableRowExist = function(_, _, name) return name:ToString() == "Buff_Trait_Strong" end,
    }))
    H.eq(H.ok(H.dispatch("general.get")).canEditTraits, true, "host trait capability")
    local edit = {id = "Trait_Strong", enabled = true, buffRowName = "Buff_Trait_Strong"}
    H.ok(H.dispatch("general.trait.set", edit), "add trait")
    H.eq(calls[1], "add:Buff_Trait_Strong", "add uses actual buff row handle")
    H.eq(#H.ok(H.dispatch("general.get")).traits, 4, "existing traits and duplicates retained")
    H.ok(H.dispatch("general.trait.set", edit), "repeated trait add")
    H.eq(#calls, 1, "repeated add does not duplicate buff")
    edit.id = "trait_strong"
    edit.enabled = false
    H.ok(H.dispatch("general.trait.set", edit), "remove trait")
    H.eq(calls[2], "remove:Buff_Trait_Strong", "remove persistent buff")
    H.eq(#H.ok(H.dispatch("general.get")).traits, 3, "only requested trait removed")
    H.eq(dirty, 2, "only changed traits replicate")
    H.fails(H.dispatch("general.trait.set", {id = "Trait_Strong", enabled = true}), "installed trait buff row", "reject missing catalog mapping")
    H.fails(H.dispatch("general.trait.set", {id = "Trait_Strong", enabled = true, buffRowName = "NotABuff"}), "unknown trait buff row", "reject existing FName outside buff table")
    H.eq(#calls, 2, "invalid buff row never calls game mutation")
    H.clientSession()
    H.eq(H.ok(H.dispatch("general.get")).canEditTraits, false, "client trait capability disabled")
    H.fails(H.dispatch("general.trait.set", edit), "host authority", "client trait editing rejected")
end
