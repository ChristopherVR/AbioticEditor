-- Round 91: reviving a killed creature (main.lua's reviveNpc, reached through npcs.set with
-- isDead = false) has to undo everything an NPC's death does in this game, not just flip the
-- flag back - see the "Reviving a killed creature" header in main.lua for the blueprint-level
-- evidence. These cases stub the pieces that header names and assert each one is exercised.
return function(H)
    H.hostSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers",
        H.object("Replication", {}, { MarkPropertyDirty = function() end }))

    local function mesh(location)
        return H.object("SkeletalMeshComponent", {
            RelativeLocation = location or H.vector(-5, 0, -110),
            RelativeRotation = H.rotator(0, -90, 0),
            bNoSkeletonUpdate = true,
        }, {
            SetSimulatePhysics = function() end,
            SetAllBodiesSimulatePhysics = function() end,
            SetPhysicsAsset = function() end,
            SetCollisionProfileName = function() end,
            SetEnableGravity = function() end,
            K2_SetRelativeLocationAndRotation = function(self, location, rotation)
                self.RelativeLocation = location
                self.RelativeRotation = rotation
            end,
        })
    end

    local function robot(fields, methods)
        local base = {
            __bases = { "NPC_Base_ParentBP_C" },
            IsDead = false, IsDisabled = false, Invincible = false, Faction = 1,
            IsGibbed = false, BodyFadingOut = false, bCanBeDamaged = true, CombatState = true,
            DefaultNPCBodyFadeOutTime = 500,
            CurrentHealth_Head = 80, CurrentHealth_Torso = 120, CurrentHealth_LeftArm = 40,
            CurrentHealth_RightArm = 40, CurrentHealth_LeftLeg = 60, CurrentHealth_RightLeg = 60,
            Mesh = mesh(),
        }
        for k, v in pairs(fields or {}) do base[k] = v end
        local npc
        local calls = {
            OnRep_IsDead = function() end,
            OnRep_CurrentHealth = function() end,
            InitalizeHealthValues = function(self)
                -- The game's own spawn-time initialisation: every limb back to its data-row max.
                self.CurrentHealth_Head = 100; self.CurrentHealth_Torso = 100
                self.CurrentHealth_LeftArm = 50; self.CurrentHealth_RightArm = 50
                self.CurrentHealth_LeftLeg = 75; self.CurrentHealth_RightLeg = 75
            end,
            Server_TryFadeOutCorpse = function(self)
                -- RetriggerableDelay reads the duration at call time; record what it saw.
                rawget(self, "__fields").__fadeArmedWith = self.DefaultNPCBodyFadeOutTime
            end,
            SpawnDefaultController = function(self)
                self.Controller = H.object("AI_Controller_NPC_Robot_Defense_C", { IsPendingDespawn = false })
            end,
        }
        for k, v in pairs(methods or {}) do calls[k] = v end
        npc = H.world.add(H.object("NPC_Robot_Defense_C", base, calls))
        return npc
    end

    -- Kill through the editor, then revive: the exact pre-death limb health comes back, the
    -- ragdoll is stopped and the mesh returned to its rest pose, the corpse-fade delay is
    -- re-armed with an effectively infinite duration and then the real setting restored, and a
    -- fresh controller is spawned because the old one despawned itself.
    do
        local npc = robot()
        local id = H.ok(H.dispatch("npcs.list")).npcs[1].id
        H.ok(H.dispatch("npcs.set", { npcs = { { id = id, isDead = true } } }), "kill")
        H.eq(H.field(npc, "IsDead"), true, "killed")
        -- What death does to the body in the running game, simulated: ragdolled mesh moved off
        -- the capsule, limbs at 0, the AI controller gone, damage disabled once fading starts.
        local m = H.field(npc, "Mesh")
        m.RelativeLocation = H.vector(140, -30, -12)
        npc.CurrentHealth_Head = 0; npc.CurrentHealth_Torso = 0
        npc.CombatState = true

        H.ok(H.dispatch("npcs.set", { npcs = { { id = id, isDead = false } } }), "revive")
        H.eq(H.field(npc, "IsDead"), false, "alive again")
        H.eq(H.calls(npc, "OnRep_IsDead"), 2, "OnRep_IsDead ran for the kill and the revive")
        H.eq(H.calls(m, "SetSimulatePhysics"), 1, "ragdoll physics stopped")
        H.eq(H.calls(m, "SetAllBodiesSimulatePhysics"), 1, "every body stopped")
        H.eq(H.calls(m, "SetCollisionProfileName"), 1, "mesh collision profile restored")
        H.eq(H.field(m, "bNoSkeletonUpdate"), false, "skeleton updates again")
        H.eq(H.field(m, "RelativeLocation").Z, -110, "mesh back on its capsule (pre-death snapshot)")
        H.eq(H.calls(npc, "InitalizeHealthValues"), 1, "health re-initialised")
        H.eq(H.field(npc, "CurrentHealth_Head"), 80, "pre-death head health restored over the re-init")
        H.eq(H.field(npc, "CurrentHealth_Torso"), 120, "pre-death torso health restored")
        H.eq(H.calls(npc, "OnRep_CurrentHealth"), 1, "health pushed out")
        H.eq(H.field(npc, "__fadeArmedWith"), 1e9, "corpse fade re-armed with a never duration")
        H.eq(H.field(npc, "DefaultNPCBodyFadeOutTime"), 500, "the real fade setting is put back")
        H.eq(H.field(npc, "bCanBeDamaged"), true, "damage enabled")
        H.eq(H.field(npc, "CombatState"), false, "combat state cleared")
        H.eq(H.calls(npc, "SpawnDefaultController"), 1, "a new controller spawned")
        H.check(H.field(npc, "Controller") ~= nil, "the creature has a brain again")
    end

    -- A creature that died on its own (no snapshot): health comes from the game's own
    -- initialisation and the rest pose is borrowed from a living creature of the same class.
    do
        H.hostSession()
        H.world.static("/Script/Engine.Default__NetPushModelHelpers",
            H.object("Replication", {}, { MarkPropertyDirty = function() end }))
        local living = robot({ Mesh = mesh(H.vector(-5, 0, -110)) })
        local dead = robot({ IsDead = true, CurrentHealth_Head = 0, CurrentHealth_Torso = 0,
            Mesh = mesh(H.vector(200, 0, 5)) })
        local list = H.ok(H.dispatch("npcs.list")).npcs
        local deadId
        for _, row in ipairs(list) do if row.isDead then deadId = row.id end end
        H.check(deadId ~= nil, "the dead robot is listed")
        H.ok(H.dispatch("npcs.set", { npcs = { { id = deadId, isDead = false } } }), "revive without snapshot")
        H.eq(H.field(dead, "CurrentHealth_Head"), 100, "health from the game's own initialisation")
        H.eq(H.field(H.field(dead, "Mesh"), "RelativeLocation").Z, -110, "rest pose borrowed from the living robot")
        H.eq(H.calls(living, "SpawnDefaultController"), 0, "the living robot is untouched")
    end

    -- A controller that never noticed the death is kept and its pending despawn cancelled.
    do
        H.hostSession()
        H.world.static("/Script/Engine.Default__NetPushModelHelpers",
            H.object("Replication", {}, { MarkPropertyDirty = function() end }))
        local controller = H.object("AI_Controller_NPC_Robot_Defense_C", { IsPendingDespawn = true })
        local npc = robot({ IsDead = true, Controller = controller })
        local id = H.ok(H.dispatch("npcs.list")).npcs[1].id
        H.ok(H.dispatch("npcs.set", { npcs = { { id = id, isDead = false } } }), "revive with a live controller")
        H.eq(H.field(controller, "IsPendingDespawn"), false, "pending despawn cancelled")
        H.eq(H.calls(npc, "SpawnDefaultController"), 0, "no second controller spawned")
    end

    -- Honest refusals: a body that is already fading (destroyed within seconds) or blown apart.
    do
        H.hostSession()
        H.world.static("/Script/Engine.Default__NetPushModelHelpers",
            H.object("Replication", {}, { MarkPropertyDirty = function() end }))
        local fading = robot({ IsDead = true, BodyFadingOut = true })
        local id = H.ok(H.dispatch("npcs.list")).npcs[1].id
        H.fails(H.dispatch("npcs.set", { npcs = { { id = id, isDead = false } } }), "fading away", "fading body refused")
        H.eq(H.field(fading, "IsDead"), true, "left dead")

        H.hostSession()
        H.world.static("/Script/Engine.Default__NetPushModelHelpers",
            H.object("Replication", {}, { MarkPropertyDirty = function() end }))
        robot({ IsDead = true, IsGibbed = true })
        id = H.ok(H.dispatch("npcs.list")).npcs[1].id
        H.fails(H.dispatch("npcs.set", { npcs = { { id = id, isDead = false } } }), "blown apart", "gibbed body refused")

        -- And when the engine hands back no controller, the revive says so instead of
        -- pretending the creature is fine.
        H.hostSession()
        H.world.static("/Script/Engine.Default__NetPushModelHelpers",
            H.object("Replication", {}, { MarkPropertyDirty = function() end }))
        robot({ IsDead = true }, { SpawnDefaultController = function() end })
        id = H.ok(H.dispatch("npcs.list")).npcs[1].id
        H.fails(H.dispatch("npcs.set", { npcs = { { id = id, isDead = false } } }), "new brain", "missing controller reported")
    end
end
