-- Live tamed pets (areas/pets.lua): Pest-family NPCs matched by their own Guid, with per-limb
-- health (universal AbioticCharacter fields) and DynamicProperties XP - see that file's own
-- header comment for the round-77 research and the round-78 bug fix this exercises. Round 105
-- (bottom of this file) added the generic tamed-marker sweep: Peccary/Lamogi-shaped creatures
-- (no Guid of their own) listed via a fake AbioticFunctionLibrary.IsTamedPet, matching=false,
-- health/dead editable but name/xp refused as warnings.
return function(H)
    H.hostSession()

    -- A stand-in for the live enum key, matching companions.lua's own test fixture shape.
    local function enumKey(suffix)
        return setmetatable({}, { __index = { ToString = function() return "EDynamicProperty::" .. suffix end } })
    end

    local fullHealth = { Head = 100, Torso = 100, LeftArm = 100, RightArm = 100, LeftLeg = 100, RightLeg = 100 }

    -- A pet that has already earned real XP: its DynamicProperties array carries an "XP" entry.
    local leveled = H.object("NPC_Monster_Pest_C", {
        Guid = H.fstring("11111111-1111-1111-1111-111111111111"),
        PetName = H.fstring("Rex"),
        IsDead = false,
        CurrentHealth_Head = 100, CurrentHealth_Torso = 100,
        CurrentHealth_LeftArm = 100, CurrentHealth_RightArm = 100,
        CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
        DynamicProperties = { { Key = enumKey("XP"), Value = 40 } },
    }, {
        OnRep_IsDead = function() end,
        OnRep_PetName = function() end,
        OnRep_CurrentHealth = function() end,
    })
    H.world.add(leveled)

    -- A pet that has never earned XP: no DynamicProperties entry at all - the exact
    -- delta-omission case the round-78 bug fix grounds pets.set's honest-error path in (see
    -- WorldSaveWriter.ApplyDynamicInt's own comment on the identical file-format gap).
    local levelZero = H.object("NPC_Monster_Pest_C", {
        Guid = H.fstring("22222222-2222-2222-2222-222222222222"),
        PetName = H.fstring(""),
        IsDead = false,
        CurrentHealth_Head = 50, CurrentHealth_Torso = 50,
        CurrentHealth_LeftArm = 50, CurrentHealth_RightArm = 50,
        CurrentHealth_LeftLeg = 50, CurrentHealth_RightLeg = 50,
        DynamicProperties = {},
    }, {
        OnRep_IsDead = function() end,
        OnRep_PetName = function() end,
        OnRep_CurrentHealth = function() end,
        -- Same technique the reference CheatConsoleCommands mod's "deleteobject" command uses on
        -- an arbitrary world actor - see pets.lua's own remarks on pets.remove. The fake mirrors
        -- what a real K2_DestroyActor does to FindAllOf's view of the world: the actor stops
        -- being valid.
        K2_DestroyActor = function(self) rawset(self, "__valid", false) end,
    })
    H.world.add(levelZero)

    local list = H.ok(H.dispatch("pets.list"), "pets.list")
    H.eq(#list.pets, 2, "both Pest-family pets listed")
    H.eq(list.available, true, "pets area reports available")
    -- Round 109: this agent build now attempts species change for matched pets - see the
    -- dedicated section near the bottom of this file for the full success/failure coverage.
    H.eq(list.supportsSpeciesChange, true, "live species change is attempted for matched pets")
    H.eq(list.supportsRemoval, true, "live removal supported (round 78: K2_DestroyActor)")

    local function findRow(rows, id) for _, r in ipairs(rows) do if r.id == id then return r end end end
    local row = findRow(list.pets, "11111111-1111-1111-1111-111111111111")
    H.check(row ~= nil, "pet matched by its own Guid")
    H.eq(row.customName, "Rex", "custom name read")
    H.eq(row.xp, 40, "xp read via the enum-tail match")
    H.eq(row.limbHealth.Head, 100, "limb health read")

    -- pets.set: health and name round-trip normally (xp unchanged, so no XP write is attempted),
    -- with no warnings.
    local applied = H.ok(H.dispatch("pets.set", { id = row.id, isDead = false, customName = "Rex II",
        limbHealth = { Head = 40, Torso = 100, LeftArm = 100, RightArm = 100, LeftLeg = 100, RightLeg = 100 }, xp = 40 }),
        "pets.set health+name")
    H.eq(#applied.warnings, 0, "no warnings for a clean health+name edit")
    local updated = findRow(H.ok(H.dispatch("pets.list")).pets, row.id)
    H.eq(updated.customName, "Rex II", "name written")
    H.eq(updated.limbHealth.Head, 40, "limb health written")

    -- pets.set: raising XP on a pet that already has an entry succeeds, no warnings.
    applied = H.ok(H.dispatch("pets.set", { id = row.id, isDead = false, limbHealth = updated.limbHealth, xp = 90 }),
        "pets.set xp raised on a pet with an existing XP entry")
    H.eq(#applied.warnings, 0, "no warnings when the XP entry already exists")
    updated = findRow(H.ok(H.dispatch("pets.list")).pets, row.id)
    H.eq(updated.xp, 90, "xp written")

    -- Round-78 bug fix: a pet with no existing XP entry used to make the WHOLE pets.set call fail
    -- (a hard Lua error aborting the health/name writes already applied above it in the same
    -- call), which read live as "health AND level editing don't seem to work". Raising its level
    -- still can't fabricate the missing entry (no evidenced way to do that live - see this file's
    -- own header comment), but that is now a WARNING inside a successful reply, and every other
    -- field in the same call still lands.
    local zeroRow = findRow(list.pets, "22222222-2222-2222-2222-222222222222")
    H.eq(zeroRow.xp, 0, "level-zero pet has no XP entry yet")
    applied = H.ok(H.dispatch("pets.set", { id = zeroRow.id, isDead = false, customName = "Buddy",
        limbHealth = { Head = 100, Torso = 100, LeftArm = 100, RightArm = 100, LeftLeg = 100, RightLeg = 100 }, xp = 40 }),
        "pets.set on a never-levelled pet still succeeds overall")
    H.eq(#applied.warnings, 1, "one warning: the level couldn't be raised")
    H.check(applied.warnings[1]:find("never earned XP", 1, true) ~= nil,
        "the warning explains why the level couldn't be raised")
    updated = findRow(H.ok(H.dispatch("pets.list")).pets, zeroRow.id)
    H.eq(updated.customName, "Buddy", "the name edit landed even though the level edit could not")
    H.eq(updated.limbHealth.Head, 100, "the health edit landed even though the level edit could not")
    H.eq(updated.xp, 0, "the level itself is unchanged - it was only ever a warning, not a silent success")

    -- ...and resending its current (unchanged) xp alongside an unrelated field edit produces no
    -- warning at all - only an actually-attempted change is ever flagged.
    applied = H.ok(H.dispatch("pets.set", { id = zeroRow.id, isDead = false, customName = "Named Now",
        limbHealth = updated.limbHealth, xp = 0 }),
        "an unrelated edit on a never-levelled pet produces no warning")
    H.eq(#applied.warnings, 0, "no warning when xp is only echoed back unchanged")
    updated = findRow(H.ok(H.dispatch("pets.list")).pets, zeroRow.id)
    H.eq(updated.customName, "Named Now", "the unrelated name edit still landed")

    -- Missing pet: player-safe failure, not a Lua error.
    H.fails(H.dispatch("pets.set", { id = "not-a-real-guid", xp = 1 }), "pet not found", "an unknown pet id fails cleanly")

    -- pets.remove (round 78): destroys the live actor outright - no undo, no save-file fallback.
    H.fails(H.dispatch("pets.remove", { id = "not-a-real-guid" }), "pet not found", "removing an unknown pet id fails cleanly")
    H.ok(H.dispatch("pets.remove", { id = zeroRow.id }), "pets.remove")
    H.eq(H.calls(levelZero, "K2_DestroyActor"), 1, "the pet's actor was destroyed")
    H.eq(#H.ok(H.dispatch("pets.list")).pets, 1, "the removed pet no longer appears")

    -- ===== Round 105: generic tamed-marker sweep (Peccary/Lamogi-shaped classes with no Guid) =====
    -- AbioticFunctionLibrary::IsTamedPet(Actor) is a real static function (see areas/pets.lua's
    -- own header comment) - faked here the same way every other static library this project
    -- already drives is faked (H.world.static). The fake NPCs carry a harness-only `__tamed`
    -- marker field since this stub environment has no real engine to natively tell tamed from
    -- wild - only the CALL SHAPE (`lib:IsTamedPet(npc)`) and the resulting matched=false row shape
    -- are under test here, not the game's own internal tamed-detection logic.
    local library = H.object("AbioticFunctionLibrary", {}, {
        IsTamedPet = function(_, npc) return rawget(npc, "__fields").__tamed == true end,
    })
    H.world.static("/Script/AbioticFactor.Default__AbioticFunctionLibrary", library)

    -- A tamed Peccary: NPC_Base_ParentBP_C hierarchy (via __bases, the same hierarchy-faking
    -- convention harness.lua's own H.hostSession() already uses for PlayerController), with NONE
    -- of Guid/PetName/DynamicProperties - exactly what areas/pets.lua's header comment documents.
    local peccary = H.object("NPC_Monster_Peccary_C", {
        __bases = { "NPC_Base_ParentBP_C" },
        __tamed = true,
        IsDead = false,
        CurrentHealth_Head = 100, CurrentHealth_Torso = 100,
        CurrentHealth_LeftArm = 100, CurrentHealth_RightArm = 100,
        CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
    }, {
        OnRep_IsDead = function() end,
        OnRep_CurrentHealth = function() end,
        K2_DestroyActor = function(self) rawset(self, "__valid", false) end,
    })
    H.world.add(peccary)

    -- A wild (untamed) creature of the same hierarchy - must never appear in pets.list.
    local wildCreature = H.object("NPC_Monster_Peccary_C", {
        __bases = { "NPC_Base_ParentBP_C" },
        __tamed = false,
        IsDead = false,
    }, {})
    H.world.add(wildCreature)

    -- A Pest-family pet ALSO tagged with the generic hierarchy marker - proves the generic sweep
    -- skips anything with its own Guid instead of double-listing an already-matched pet.
    local pestWithBases = H.object("NPC_Monster_Pest_C", {
        __bases = { "NPC_Base_ParentBP_C" },
        Guid = H.fstring("33333333-3333-3333-3333-333333333333"),
        PetName = H.fstring("Sparky"),
        IsDead = false,
        CurrentHealth_Head = 100, CurrentHealth_Torso = 100,
        CurrentHealth_LeftArm = 100, CurrentHealth_RightArm = 100,
        CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
        DynamicProperties = {},
    }, {
        OnRep_IsDead = function() end,
        OnRep_PetName = function() end,
        OnRep_CurrentHealth = function() end,
    })
    H.world.add(pestWithBases)

    local peccaryFullName = peccary:GetFullName()
    local afterSweep = H.ok(H.dispatch("pets.list"), "pets.list after the generic tamed sweep")
    local unmatchedCount = 0
    for _, r in ipairs(afterSweep.pets) do
        if r.matched == false then unmatchedCount = unmatchedCount + 1 end
    end
    H.eq(unmatchedCount, 1,
        "only the tamed Peccary is listed unmatched - the wild one and the Guid-bearing Pest are excluded")

    local peccaryRow = findRow(afterSweep.pets, peccaryFullName)
    H.check(peccaryRow ~= nil, "the tamed Peccary is listed, keyed by its own full path")
    H.eq(peccaryRow.matched, false, "the Peccary row reports matched=false")
    H.eq(peccaryRow.customName, nil, "no customName - the class has no PetName field")
    H.eq(peccaryRow.xp, 0, "no xp - the class has no DynamicProperties field")
    H.eq(peccaryRow.limbHealth.Head, 100, "universal AbioticCharacter health is still read")
    H.check(findRow(afterSweep.pets, wildCreature:GetFullName()) == nil, "the wild (untamed) creature is never listed")

    local pestRow = findRow(afterSweep.pets, "33333333-3333-3333-3333-333333333333")
    H.check(pestRow ~= nil, "the Guid-bearing Pest is still listed once, by its own Guid")
    H.eq(pestRow.matched, true, "a Guid-matched pet reports matched=true")

    -- pets.set on an unmatched row: health/dead apply exactly as for a matched pet; name/xp are
    -- refused with a warning (the round-78 non-fatal shape), not silently dropped or a hard error.
    local peccaryApplied = H.ok(H.dispatch("pets.set", { id = peccaryFullName, isDead = false, customName = "Truffles",
        xp = 40, limbHealth = { Head = 60, Torso = 100, LeftArm = 100, RightArm = 100, LeftLeg = 100, RightLeg = 100 } }),
        "pets.set on an unmatched Peccary still succeeds overall")
    H.eq(#peccaryApplied.warnings, 2, "two warnings: no name field, no xp field")
    H.check((peccaryApplied.warnings[1] .. peccaryApplied.warnings[2]):find("no name field", 1, true) ~= nil,
        "one warning explains the missing name field")
    H.check((peccaryApplied.warnings[1] .. peccaryApplied.warnings[2]):find("no XP", 1, true) ~= nil,
        "one warning explains the missing xp field")
    local peccaryUpdated = findRow(H.ok(H.dispatch("pets.list")).pets, peccaryFullName)
    H.eq(peccaryUpdated.limbHealth.Head, 60, "health still writes for an unmatched pet - it is a universal field")
    H.eq(peccaryUpdated.customName, nil, "the name never changed - there was nothing to write it to")

    -- pets.remove works for an unmatched row too - no stable save id is needed, only the live actor.
    H.ok(H.dispatch("pets.remove", { id = peccaryFullName }), "pets.remove on an unmatched Peccary")
    H.eq(H.calls(peccary, "K2_DestroyActor"), 1, "the Peccary's actor was destroyed")
    H.check(findRow(H.ok(H.dispatch("pets.list")).pets, peccaryFullName) == nil, "the removed Peccary no longer appears")

    -- ===== Round 109: live species change (matched pets only) =====
    -- See areas/pets.lua's own header comment for the full reasoning (SpawnTransform is read
    -- fresh off the old actor via K2_GetActorTransform and passed through unchanged, never
    -- fabricated). The harness's default GameMode.SpawnPet fake (tests/harness.lua) spawns a real
    -- new fake actor carrying the given guid/name/owner/dynamicProperties - a "clean" success;
    -- individual cases below override it per H.gameMode's own documented technique to exercise the
    -- failure paths that must leave the original pet untouched.
    local skinkClassPath = "/Game/Blueprints/Characters/NPCs/NPC_Skink_Basic.NPC_Skink_Basic_C"
    -- NPC_Skink_Basic_C really does extend NPC_Monster_Pest_C (see this file's header comment) -
    -- __resultBases matches that so the spawned fake stays hierarchy-findable by PET_FAMILY_CLASS,
    -- the same way the real class hierarchy keeps it findable by FindAllOf("NPC_Monster_Pest_C").
    H.world.static(skinkClassPath, H.object("Class",
        { __resultClass = "NPC_Skink_Basic_C", __resultBases = { "NPC_Monster_Pest_C" } }, {}))

    local function speciesPet(guid, name)
        return H.world.add(H.object("NPC_Monster_Pest_C", {
            Guid = H.fstring(guid), PetName = H.fstring(name), FollowingOwner = nil, IsDead = false,
            CurrentHealth_Head = 100, CurrentHealth_Torso = 100, CurrentHealth_LeftArm = 100,
            CurrentHealth_RightArm = 100, CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
            DynamicProperties = { { Key = enumKey("XP"), Value = 40 } },
        }, {
            OnRep_IsDead = function() end, OnRep_PetName = function() end, OnRep_CurrentHealth = function() end,
            K2_GetActorTransform = function() return { __transform = true } end,
            K2_DestroyActor = function(self) rawset(self, "__valid", false) end,
        }))
    end

    -- ---- success: a real species change, old actor destroyed, new one takes over the same id ----
    local changeling = speciesPet("77777777-7777-7777-7777-777777777777", "Changeling")
    local changed = H.ok(H.dispatch("pets.set", {
        id = "77777777-7777-7777-7777-777777777777", isDead = false, customName = "Changeling",
        limbHealth = fullHealth, xp = 40, npcClass = skinkClassPath,
    }), "pets.set with a species change")
    H.eq(#changed.warnings, 0, "a clean species change reports no warnings")
    H.eq(H.calls(changeling, "K2_DestroyActor"), 1, "the old actor was destroyed after a confirmed swap")
    H.eq(H.calls(H.gameMode, "SpawnPet"), 1, "SpawnPet was called exactly once")
    local afterChange = findRow(H.ok(H.dispatch("pets.list")).pets, "77777777-7777-7777-7777-777777777777")
    H.check(afterChange ~= nil, "the pet is still listed at the SAME id after a species change (Guid preserved)")
    H.eq(afterChange.npcClass, "NPC_Skink_Basic_C", "the listed creature type reflects the new species")
    H.eq(afterChange.xp, 40, "XP carried over via the pet's own DynamicProperties array")

    -- ---- resending the CURRENT species is a no-op: never calls SpawnPet at all ----
    local unchangedPet = speciesPet("88888888-8888-8888-8888-888888888888", "Steady")
    local unchangedList = H.ok(H.dispatch("pets.list")).pets
    local unchangedRow = findRow(unchangedList, "88888888-8888-8888-8888-888888888888")
    H.ok(H.dispatch("pets.set", {
        id = "88888888-8888-8888-8888-888888888888", isDead = false, customName = "Steady",
        limbHealth = fullHealth, xp = 40, npcClass = unchangedRow.npcClass,
    }), "pets.set resending the pet's own current species")
    H.eq(H.calls(unchangedPet, "K2_DestroyActor"), 0, "resending the same species never destroys the actor")

    -- ---- unknown/unresolvable species: old pet untouched, named warning ----
    local unknownTarget = speciesPet("99999999-9999-9999-9999-999999999999", "Steadfast")
    local unknownApplied = H.ok(H.dispatch("pets.set", {
        id = "99999999-9999-9999-9999-999999999999", isDead = false, customName = "Steadfast",
        limbHealth = fullHealth, xp = 40, npcClass = "/Game/Blueprints/Characters/NPCs/NPC_DoesNotExist.NPC_DoesNotExist_C",
    }), "pets.set with an unresolvable species still succeeds overall")
    H.eq(#unknownApplied.warnings, 1, "one warning: the species could not be resolved")
    H.check(unknownApplied.warnings[1]:find("unknown or not-yet-loaded creature type", 1, true) ~= nil,
        "the warning names the real failure reason")
    H.eq(H.calls(unknownTarget, "K2_DestroyActor"), 0, "the old pet was never touched")
    H.check(findRow(H.ok(H.dispatch("pets.list")).pets, "99999999-9999-9999-9999-999999999999") ~= nil,
        "the original pet is still listed, unchanged")

    -- ---- SpawnPet returns an invalid actor: old pet untouched ----
    local invalidResultPet = speciesPet("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "Steadier")
    -- rawset (not a fields-table {__valid=false}, which lands in __fields, not the object's own
    -- top-level validity flag ObjectMeta.IsValid actually reads) - same technique K2_DestroyActor
    -- fakes throughout this file already use.
    rawget(H.gameMode, "__methods").SpawnPet = function()
        local invalid = H.object("Invalid", {}, {})
        rawset(invalid, "__valid", false)
        return invalid
    end
    local invalidApplied = H.ok(H.dispatch("pets.set", {
        id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", isDead = false, customName = "Steadier",
        limbHealth = fullHealth, xp = 40, npcClass = skinkClassPath,
    }), "pets.set when SpawnPet returns an invalid actor still succeeds overall")
    H.eq(#invalidApplied.warnings, 1, "one warning: the game would not spawn the new creature type")
    H.check(invalidApplied.warnings[1]:find("would not spawn", 1, true) ~= nil, "the warning names the real failure reason")
    H.eq(H.calls(invalidResultPet, "K2_DestroyActor"), 0, "the old pet was never touched")

    -- ---- SpawnPet "succeeds" but the new actor reports a DIFFERENT identity: old pet untouched ----
    local mismatchPet = speciesPet("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "Steadiest")
    rawget(H.gameMode, "__methods").SpawnPet = function(_, _, _, _, name, owner, dynamicProperties)
        return H.world.add(H.object("NPC_Skink_Basic_C",
            { Guid = H.fstring("00000000-0000-0000-0000-000000000000"), PetName = name,
              FollowingOwner = owner, DynamicProperties = dynamicProperties or {}, IsDead = false }, {}))
    end
    local mismatchApplied = H.ok(H.dispatch("pets.set", {
        id = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", isDead = false, customName = "Steadiest",
        limbHealth = fullHealth, xp = 40, npcClass = skinkClassPath,
    }), "pets.set when the spawned actor comes back with a different identity still succeeds overall")
    H.eq(#mismatchApplied.warnings, 1, "one warning: the new creature didn't come back with the same identity")
    H.check(mismatchApplied.warnings[1]:find("didn't come back with the same identity", 1, true) ~= nil,
        "the warning names the real failure reason")
    H.eq(H.calls(mismatchPet, "K2_DestroyActor"), 0, "the old pet was kept - a spawn with the wrong identity is never trusted")
    H.check(findRow(H.ok(H.dispatch("pets.list")).pets, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb") ~= nil,
        "the original pet is still listed under its own guid")

    -- ---- unmatched (Peccary/Lamogi-shaped) pet: species change refused with a named warning ----
    local secondPeccary = H.object("NPC_Monster_Peccary_C", {
        __bases = { "NPC_Base_ParentBP_C" }, __tamed = true, IsDead = false,
        CurrentHealth_Head = 100, CurrentHealth_Torso = 100, CurrentHealth_LeftArm = 100,
        CurrentHealth_RightArm = 100, CurrentHealth_LeftLeg = 100, CurrentHealth_RightLeg = 100,
    }, { OnRep_IsDead = function() end, OnRep_CurrentHealth = function() end,
         K2_DestroyActor = function(self) rawset(self, "__valid", false) end })
    H.world.add(secondPeccary)
    local secondPeccaryFullName = secondPeccary:GetFullName()
    local peccarySpeciesApplied = H.ok(H.dispatch("pets.set", {
        id = secondPeccaryFullName, isDead = false, limbHealth = fullHealth, npcClass = skinkClassPath,
    }), "pets.set species change on an unmatched Peccary still succeeds overall")
    H.eq(#peccarySpeciesApplied.warnings, 1, "one warning: unmatched pets can't have their species changed live")
    H.check(peccarySpeciesApplied.warnings[1]:find("can't be matched to a save record", 1, true) ~= nil,
        "the warning explains why the species couldn't be changed")
    H.eq(H.calls(secondPeccary, "K2_DestroyActor"), 0, "the unmatched pet was never touched")
end
