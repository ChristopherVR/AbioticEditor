-- Live editing area: NPC spawners (NPCSpawnMap, round 102).
--
-- Confirmed against the coordinator's own CUE4Parse class+bytecode dump (tests/AbioticEditor.
-- Probes/LiveGapProbe.cs; see docs/PROGRESS.md's Round-102 entry for the exact citations). Every
-- property/function name below is copied verbatim from that dump, not guessed.
--
-- CLASS DISCOVERY, corrected from the original task assumption: the overwhelming majority of
-- placed spawners (every zombie/pest/gatekeeper/order/pillager/darklens/security-bot/peccary/
-- winter-sprite/single-grunt/... family, confirmed one by one from each class's own `super=` in
-- the dump) chain up to Abiotic_NPCSpawn_ParentBP_C, so FindAllOf on that one root already covers
-- them - the same hierarchy-inclusive FindAllOf idiom buttons.lua/elevators.lua/pets.lua already
-- rely on. BUT NPCSpawn_Entity_C and NPCSpawn_Narrative_C are NOT subclasses of that parent -
-- the dump shows both declare `super=Actor` directly, a genuinely separate root each (confirmed:
-- NPCSpawn_Trader_Chef_C/NPCSpawn_Trader_Marion_C chain from NPCSpawn_Narrative_C;
-- NPCSpawn_VOTV_UFO_C/NPCSpawn_VOTV_Wisp_C chain from NPCSpawn_Entity_C). Both are added as
-- ADDITIONAL_ROOT_CLASSES (data, not logic) so their instances are still found; neither exposes
-- any of the cooldown/count system below (confirmed from their own, much smaller property/
-- function lists in the dump - no HasSpawnedOnce, no IsOnCooldown, no SetSpawnOnCooldown, no
-- TrySpawnNPC/TrySpawnNPCNew), so every one of their rows lists with controllable=false and every
-- state field absent, via the same per-instance pcall feature detection every other field here
-- uses - never erroring or being dropped from the list.
--
-- COOLDOWN/COUNT STATE lives partly on the spawner actor itself and partly on a native world
-- subsystem, AIDirectorSubsystem (/Script/AbioticFactor, obtained via
-- SubsystemBlueprintLibrary::GetWorldSubsystem the same way the spawner's own bytecode gets it -
-- confirmed from Abiotic_NPCSpawn_ParentBP_C's ExecuteUbergraph/CheckSpawnProximity* functions,
-- which call GetCurrentCooldownRemainingFromSpawner/GetHasBeenEncounteredOnceForSpawner/
-- GetSpawnedAIFromSpawner on it, each passed `self` as the spawner argument). This module fetches
-- it fresh with FindFirstOf("AIDirectorSubsystem") on every request - the same "not cached, IsValid
-- re-checked" idiom main.lua's own worldFlagSubsystem() uses for UWorldFlagSubsystem - rather than
-- holding a stale reference across calls.
--
-- FIELD MAPPING (Abiotic_NPCSpawn_ParentBP_C family only):
--   onCooldown               <- spawner:IsOnCooldown() - a real, actor-level BlueprintCallable
--                                function; its own bytecode is exactly
--                                (GetCurrentCooldownRemainingFromSpawner > 0) OR
--                                (GetCooldownDaysRemainingFromSpawner > 0).
--   cooldownRemainingSeconds <- AIDirectorSubsystem:GetCurrentCooldownRemainingFromSpawner(spawner)
--   cooldownDaysRemaining    <- AIDirectorSubsystem:GetCooldownDaysRemainingFromSpawner(spawner)
--   hasBeenEncounteredOnce   <- AIDirectorSubsystem:GetHasBeenEncounteredOnceForSpawner(spawner)
--   hasSpawnedOnce           <- spawner.HasSpawnedOnce (a direct, unsuffixed instance property -
--                                not replicated, no OnRep_ exists for it in the dump, but this
--                                process runs as the server/host so its own in-memory value is
--                                authoritative regardless)
--   spawnCount                <- spawner:GetCurrentSpawnedCount(false) - a real, actor-level
--                                BlueprintCallable function (CheckDead: bool in, Count: int out);
--                                false = count every currently-spawned NPC from this spawner, not
--                                only the live ones.
-- The offline save leaf `MinutesPassedCooldownStarted_` has NO confirmed live counterpart anywhere
-- in the dump (the closest actor-level properties, LastCooldownSaveTimestamp/CooldownSaveInterval,
-- are a different, unconfirmed concept) - this module reports nothing for it rather than guessing.
--
-- ACTIONS (both write-only "do it now" toggles, not persistent state - see npcspawns.set):
--   resetCooldown - spawner:SetSpawnOnCooldown(0.0, 0) - a real, actor-level BlueprintCallable
--                    function (TimeRemaining: double, InCurrentDay: int). Its own bytecode: when
--                    InCurrentDay is 0 it looks up the CURRENT in-game day itself (via
--                    `AI Director.DayNightManager.CurrentDay`, both feature-detected with IsValid
--                    checks in the real bytecode) rather than requiring the caller to supply one,
--                    so passing (0, 0) sets TimeRemaining=0 and CooldownDay=today in one call -
--                    exactly "allow this spawner to fire again right now". It then calls
--                    AIDirectorSubsystem:SetCooldownForSpawner(spawner, TimeRemaining, CooldownDay,
--                    spawner.OnlySpawnOnce) itself, so this module never has to call that subsystem
--                    function directly.
--   forceSpawn     - spawner:TrySpawnNPCNew(IsNight=false, ForceSuccessByTrigger=true,
--                    CheckOnlyNoSpawn=false), falling back to the older TrySpawnNPC with the same
--                    three arguments if TrySpawnNPCNew is not present on a given game build (both
--                    are real, actor-level BlueprintCallable functions with the identical
--                    signature - TrySpawnNPCNew is newer, and the dump does not confirm which one
--                    live gameplay actually calls on a tick, so this tries the newer one first).
--                    ForceSuccessByTrigger is confirmed from the bytecode to gate multiple
--                    individual JumpIfNot checks (each "skip this failing check when
--                    ForceSuccessByTrigger is true") - the same parameter a Trigger_* volume would
--                    pass to force a spawn outside the normal distance/cooldown/line-of-sight
--                    gating. Genuinely unverified against the running game: whether the resulting
--                    NPC actually appears is not confirmed by any live capture, only by this
--                    bytecode reading - a call that does not error is reported as "requested", not
--                    a confirmed spawn (see forceSpawn below).
return function(ctx)
    local SPAWNER_ROOT_CLASS = "Abiotic_NPCSpawn_ParentBP_C"
    -- Data, not logic - see the header comment for why these two are listed separately rather than
    -- folded into the root sweep above.
    local ADDITIONAL_ROOT_CLASSES = { "NPCSpawn_Entity_C", "NPCSpawn_Narrative_C" }
    local ROOT_CLASSES = { SPAWNER_ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local function allSpawners()
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
            end
        end
        return result
    end

    local function findSpawner(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- Re-fetched every call rather than cached, matching main.lua's own worldFlagSubsystem() idiom
    -- for the same reason: a subsystem reference can go stale across a long-lived connection.
    local function aiDirectorSubsystem()
        local ok, subsystem = pcall(function() return FindFirstOf("AIDirectorSubsystem") end)
        if ok and subsystem and subsystem:IsValid() then return subsystem end
        return nil
    end

    -- Same explicit-if idiom buttons.lua documents: `... and value or nil` silently turns a real
    -- `false`/`0` reading into `nil` when value itself is falsy, so every optional read below goes
    -- through one of these two helpers instead.
    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end
    local function numOrNil(ok, value)
        if ok and type(value) == "number" then return value end
        return nil
    end

    local function spawnerRow(spawner, subsystem)
        local name = ctx.fullName(spawner)
        if not name then return nil end
        local x, y, z = ctx.actorLocation(spawner)
        local okCooldown, onCooldown = pcall(function() return spawner:IsOnCooldown() end)
        local okSpawnedOnce, hasSpawnedOnce = pcall(function() return spawner.HasSpawnedOnce end)
        local okCount, spawnCount = pcall(function() return spawner:GetCurrentSpawnedCount(false) end)
        local cooldownRemainingSeconds, cooldownDaysRemaining, hasBeenEncounteredOnce = nil, nil, nil
        if subsystem then
            local okRemaining, remaining = pcall(function()
                return subsystem:GetCurrentCooldownRemainingFromSpawner(spawner)
            end)
            cooldownRemainingSeconds = numOrNil(okRemaining, remaining)
            local okDays, days = pcall(function()
                return subsystem:GetCooldownDaysRemainingFromSpawner(spawner)
            end)
            cooldownDaysRemaining = numOrNil(okDays, days)
            local okEncountered, encountered = pcall(function()
                return subsystem:GetHasBeenEncounteredOnceForSpawner(spawner)
            end)
            hasBeenEncounteredOnce = boolOrNil(okEncountered, encountered)
        end
        -- "Controllable" (whether resetCooldown/forceSpawn have anything to act on) tracks whether
        -- this instance exposes the actor-level cooldown functions at all - true for the
        -- Abiotic_NPCSpawn_ParentBP_C family, false for the two additional-root families, and false
        -- for any future class reached through either that turns out not to have them either.
        return {
            id = name,
            label = ctx.classLabel(name),
            controllable = okCooldown == true,
            onCooldown = boolOrNil(okCooldown, onCooldown),
            cooldownRemainingSeconds = cooldownRemainingSeconds,
            cooldownDaysRemaining = cooldownDaysRemaining,
            hasSpawnedOnce = boolOrNil(okSpawnedOnce, hasSpawnedOnce),
            hasBeenEncounteredOnce = hasBeenEncounteredOnce,
            spawnCount = numOrNil(okCount, spawnCount),
            x = x, y = y, z = z,
        }
    end

    local function spawnerRows()
        local result = { __forceArray = true }
        local subsystem = aiDirectorSubsystem()
        for _, spawner in ipairs(allSpawners()) do
            if spawner:IsValid() then
                local row = spawnerRow(spawner, subsystem)
                if row then table.insert(result, row) end
            end
        end
        return result
    end

    ctx.handlers["npcspawns.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { spawners = spawnerRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Best-effort: TrySpawnNPCNew first (see header comment), TrySpawnNPC as a fallback for an
    -- older game build. Returns true only when one of the two calls did not error - NOT a
    -- confirmed "an NPC actually appeared" readback (no live capture confirms that yet).
    local function tryForceSpawn(spawner)
        local ok = pcall(function() spawner:TrySpawnNPCNew(false, true, false) end)
        if ok then return true end
        local okFallback = pcall(function() spawner:TrySpawnNPC(false, true, false) end)
        return okFallback
    end

    -- Matches doors.set/buttons.set/elevators.set: every resolvable row applies first; only once
    -- every row has run does an unresolved id or a failed action turn the whole reply into an
    -- error, naming the first one.
    ctx.handlers["npcspawns.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change NPC spawners") end
            local rows = payload.spawners or {}
            local missingId, failedId, failedReason = nil, nil, nil
            for i = 1, #rows do
                local row = rows[i]
                local spawner = row.id and findSpawner(row.id)
                if spawner then
                    if row.resetCooldown then
                        local ok = pcall(function() spawner:SetSpawnOnCooldown(0.0, 0) end)
                        if not ok then
                            failedId = failedId or row.id
                            failedReason = failedReason or "this spawner type has no known live cooldown control"
                        end
                    end
                    if row.forceSpawn then
                        if not tryForceSpawn(spawner) then
                            failedId = failedId or row.id
                            failedReason = failedReason or "this spawner type has no known live spawn control"
                        end
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("NPC spawner not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if failedId then error(tostring(failedReason) .. ": " .. tostring(failedId)) end
            return nil
        end, respond)
    end
end
