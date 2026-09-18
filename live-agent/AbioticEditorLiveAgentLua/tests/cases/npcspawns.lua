-- NPC spawners (areas/npcspawns.lua, round 102). Field/function names off the real class+bytecode
-- probe (tests/AbioticEditor.Probes/LiveGapProbe.cs): IsOnCooldown(), HasSpawnedOnce,
-- GetCurrentSpawnedCount(CheckDead), SetSpawnOnCooldown(TimeRemaining, InCurrentDay),
-- TrySpawnNPCNew(IsNight, ForceSuccessByTrigger, CheckOnlyNoSpawn) on the spawner actor itself, and
-- GetCurrentCooldownRemainingFromSpawner/GetCooldownDaysRemainingFromSpawner/
-- GetHasBeenEncounteredOnceForSpawner on the native AIDirectorSubsystem (found via
-- FindFirstOf("AIDirectorSubsystem"), the same idiom main.lua's own worldFlagSubsystem() uses).
return function(H)
    H.hostSession()

    -- The subsystem is a per-spawner lookup table keyed by object identity, mirroring how
    -- AIDirectorSubsystem really tracks state per spawner actor rather than the actor holding it.
    local subsystemState = {}
    local subsystem = H.world.add(H.object("AIDirectorSubsystem", {}, {
        GetCurrentCooldownRemainingFromSpawner = function(_, spawner)
            return (subsystemState[spawner] or {}).remaining or 0
        end,
        GetCooldownDaysRemainingFromSpawner = function(_, spawner)
            return (subsystemState[spawner] or {}).days or 0
        end,
        GetHasBeenEncounteredOnceForSpawner = function(_, spawner)
            return (subsystemState[spawner] or {}).encountered or false
        end,
    }))

    -- Every fake this helper builds is a member of the Abiotic_NPCSpawn_ParentBP_C hierarchy by
    -- default (the vast majority of real spawner classes are) - extraFields can override __bases
    -- for a test that needs a different ancestry.
    local function spawner(class, opts, extraFields, extraMethods)
        opts = opts or {}
        local fields = { HasSpawnedOnce = opts.hasSpawnedOnce or false, __bases = { "Abiotic_NPCSpawn_ParentBP_C" } }
        if extraFields then for k, v in pairs(extraFields) do fields[k] = v end end
        local spawnCount = opts.spawnCount or 0
        local onCooldown = opts.onCooldown or false
        local methods = {
            IsOnCooldown = function() return onCooldown end,
            GetCurrentSpawnedCount = function(_, _checkDead) return spawnCount end,
            SetSpawnOnCooldown = function(self, timeRemaining, inCurrentDay)
                onCooldown = false
                rawget(self, "__fields").__lastSetCooldown = { timeRemaining = timeRemaining, inCurrentDay = inCurrentDay }
            end,
            TrySpawnNPCNew = function() return true, nil end,
            K2_GetActorLocation = function() return H.vector(7, 7, 7) end,
        }
        if extraMethods then for k, v in pairs(extraMethods) do methods[k] = v end end
        local obj = H.world.add(H.object(class, fields, methods))
        subsystemState[obj] = {
            remaining = opts.remaining or 0,
            days = opts.days or 0,
            encountered = opts.encountered or false,
        }
        return obj
    end

    -- A normal, fully-controllable spawner: on cooldown, already spawned once, already encountered.
    local grunt = spawner("NPCSpawn_SingleGrunt_C", {
        hasSpawnedOnce = true, spawnCount = 3, onCooldown = true, remaining = 120.5, days = 1, encountered = true,
    })

    -- NPCSpawn_Narrative_C: a genuinely separate root (super=Actor directly, confirmed in the
    -- dump), never on cooldown/count system at all - lists but everything reports unavailable.
    local trader = H.world.add(H.object("NPCSpawn_Trader_Marion_C", { __bases = { "NPCSpawn_Narrative_C" } }, {
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
    }))

    local list = H.ok(H.dispatch("npcspawns.list"), "npcspawns.list")
    H.eq(#list.spawners, 2, "both the parent-family spawner and the narrative-family spawner are listed")

    local function rowFor(rows, fragment)
        for _, row in ipairs(rows) do
            if row.id:find(fragment, 1, true) then return row end
        end
        return nil
    end
    local gruntRow = rowFor(list.spawners, "NPCSpawn_SingleGrunt_C")
    local traderRow = rowFor(list.spawners, "NPCSpawn_Trader_Marion_C")
    H.check(gruntRow ~= nil and traderRow ~= nil, "both rows found")

    H.eq(gruntRow.controllable, true, "parent-family spawner is controllable")
    H.eq(gruntRow.onCooldown, true, "grunt spawner is on cooldown")
    H.eq(gruntRow.cooldownRemainingSeconds, 120.5, "cooldown remaining read from the subsystem")
    H.eq(gruntRow.cooldownDaysRemaining, 1, "cooldown days remaining read from the subsystem")
    H.eq(gruntRow.hasSpawnedOnce, true, "hasSpawnedOnce read directly off the actor")
    H.eq(gruntRow.hasBeenEncounteredOnce, true, "encountered-once read from the subsystem")
    H.eq(gruntRow.spawnCount, 3, "spawn count read via GetCurrentSpawnedCount(false)")

    H.eq(traderRow.controllable, false, "NPCSpawn_Narrative_C family has no cooldown/count system - not controllable")
    H.eq(traderRow.onCooldown, nil, "onCooldown absent for the narrative-family spawner, not guessed false")
    H.eq(traderRow.spawnCount, nil, "spawnCount absent for the narrative-family spawner")

    -- npcspawns.set: reset the grunt's cooldown - a real actor-level function call, not a
    -- subsystem call, per the module's own header comment.
    H.ok(H.dispatch("npcspawns.set", { spawners = { { id = gruntRow.id, resetCooldown = true } } }),
        "reset the grunt spawner's cooldown")
    H.eq(H.calls(grunt, "SetSpawnOnCooldown"), 1, "SetSpawnOnCooldown called once")
    H.eq(H.field(grunt, "__lastSetCooldown").timeRemaining, 0.0, "reset passes TimeRemaining=0")
    H.eq(H.field(grunt, "__lastSetCooldown").inCurrentDay, 0, "reset passes InCurrentDay=0 (the function looks up 'today' itself)")
    local afterReset = H.ok(H.dispatch("npcspawns.list"), "npcspawns.list after reset")
    H.eq(rowFor(afterReset.spawners, "NPCSpawn_SingleGrunt_C").onCooldown, false, "grunt no longer on cooldown after the reset call")

    -- forceSpawn: TrySpawnNPCNew is tried first.
    H.ok(H.dispatch("npcspawns.set", { spawners = { { id = gruntRow.id, forceSpawn = true } } }),
        "force-spawn via the grunt spawner")
    H.eq(H.calls(grunt, "TrySpawnNPCNew"), 1, "TrySpawnNPCNew called")

    -- forceSpawn falls back to TrySpawnNPC when TrySpawnNPCNew is not present on this build.
    local oldBuildSpawner = spawner("NPCSpawn_Zombie_Scientist_C", {}, nil, {
        TrySpawnNPC = function() return true, nil end,
    })
    -- The helper's default methods table always includes TrySpawnNPCNew; remove it after
    -- construction so this fake genuinely has no such method, proving the fallback path.
    local oldBuildMethods = rawget(oldBuildSpawner, "__methods")
    oldBuildMethods.TrySpawnNPCNew = nil
    local oldBuildList = H.ok(H.dispatch("npcspawns.list"), "npcspawns.list including the old-build spawner")
    local oldBuildRow = rowFor(oldBuildList.spawners, "NPCSpawn_Zombie_Scientist_C")
    H.ok(H.dispatch("npcspawns.set", { spawners = { { id = oldBuildRow.id, forceSpawn = true } } }),
        "force-spawn falls back to TrySpawnNPC")
    H.eq(H.calls(oldBuildSpawner, "TrySpawnNPC"), 1, "TrySpawnNPC used as the fallback")

    -- A request against the narrative-family spawner (no SetSpawnOnCooldown/TrySpawnNPC* at all)
    -- fails with a named reason instead of silently doing nothing.
    local narrativeReply = H.dispatch("npcspawns.set", { spawners = { { id = traderRow.id, resetCooldown = true } } })
    H.fails(narrativeReply, "no known live cooldown control", "narrative-family spawner refuses resetCooldown by name")

    -- A brand-new spawner subclass this module has never heard of, reached only through the
    -- hierarchy sweep on the parent root - still listed and controllable, proving no hardcoded
    -- leaf-class list anywhere in npcspawns.lua.
    local futureType = spawner("NPCSpawn_FutureDLC_C", { onCooldown = false })
    local futureList = H.ok(H.dispatch("npcspawns.list"), "npcspawns.list including an unknown future spawner class")
    local futureRow = rowFor(futureList.spawners, "NPCSpawn_FutureDLC_C")
    H.eq(futureRow.label, "NPCSpawn_FutureDLC_C", "the unknown subclass's real class name is reported as its label")
    H.eq(futureRow.controllable, true, "the unknown subclass is still controllable (it has the normal capabilities)")

    -- Missing spawner id: player-safe failure, not a Lua error - any resolvable row in the same
    -- call still applies first.
    local missingReply = H.dispatch("npcspawns.set", { spawners = {
        { id = gruntRow.id, resetCooldown = true },
        { id = "no-such-spawner", forceSpawn = true },
    } })
    H.fails(missingReply, "not found", "unknown spawner id fails cleanly")
    H.eq(H.calls(grunt, "SetSpawnOnCooldown"), 2, "the resolvable row in the same batch still applied")

    -- Non-host refusal.
    H.clientSession()
    local clientGrunt = spawner("NPCSpawn_SingleGrunt_C", {})
    local clientList = H.ok(H.dispatch("npcspawns.list"), "npcspawns.list as client")
    H.fails(H.dispatch("npcspawns.set", { spawners = { { id = clientList.spawners[1].id, resetCooldown = true } } }),
        "only the host", "client cannot edit NPC spawners")
end
