-- Live tamed pets (areas/pets.lua): Pest-family NPCs matched by their own Guid, with per-limb
-- health (universal AbioticCharacter fields) and DynamicProperties XP - see that file's own
-- header comment for the round-77 research and the round-78 bug fix this exercises.
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
    H.eq(list.supportsSpeciesChange, false, "no live species change")
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
end
