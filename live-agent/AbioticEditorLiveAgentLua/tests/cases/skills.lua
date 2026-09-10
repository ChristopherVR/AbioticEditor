-- Player skills (areas handled directly in main.lua, not a separate areas/ module - see its own
-- "CharacterSkills" section). CharacterSkills_Keys/_Values is a key/value map keyed by the live
-- CharacterSkills enum, a completely different numbering scheme than this repo's own file-index
-- order (see FileIndexToLiveSkillId in main.lua) - this test's main job is proving that mapping
-- table actually lines an id up with the right file index end to end, something no case file
-- exercised before now (skills.get/skills.set had zero coverage here).
--
-- NOTE: this stub environment models every CharacterSkills_Keys entry as a plain Lua number.
-- Real UE4SS may hand back an enum-wrapper value instead of a bare number for an enum-KEYED
-- TMap (there is no other enum-keyed TMap read anywhere else in this mod to compare against) -
-- skillKeyToNumber() in main.lua guards against that, but this stub cannot prove or disprove
-- which shape a real running game actually returns; only a live game can settle that.
return function(H)
    local pawn = H.hostSession()
    local progression = H.field(pawn, "CharacterProgressionComponent")
    local keys = H.field(progression, "CharacterSkills_Keys")
    local values = H.field(progression, "CharacterSkills_Values")

    -- Only three of the fifteen skills have ever been touched in-game (a fresh-ish character) -
    -- every other file index must read back 0, not error, and the three present must land on the
    -- correct FILE index, not the live enum id's own number.
    local SKILL_XP_FIELD = "CurrentSkillXP_20_8F7934CD4A4542F036AE5C9649362556"
    local function addSkill(liveId, xp)
        table.insert(keys, liveId)
        table.insert(values, { [SKILL_XP_FIELD] = xp })
    end
    addSkill(1, 51102.9)  -- live id 1 = Sprinting = file index 0
    addSkill(15, 91655)   -- live id 15 = Strength = file index 1 (maxed)
    addSkill(7, 200)      -- live id 7 = Fishing = file index 14

    local got = H.ok(H.dispatch("skills.get"), "skills.get")
    H.eq(#got, 15, "one row per file-index skill, always")

    local byIndex = {}
    for _, row in ipairs(got) do byIndex[row.index] = row end

    H.eq(byIndex[0].xp, 51102.9, "Sprinting (file index 0, live id 1) read its own XP")
    H.eq(byIndex[1].xp, 91655, "Strength (file index 1, live id 15) read its own XP, not Sprinting's")
    H.eq(byIndex[14].xp, 200, "Fishing (file index 14, live id 7) read its own XP")

    -- Every untouched skill reads 0, not an error and not a stale/duplicated value from one of
    -- the three above - this is exactly the failure mode that reads as "no skill ever unlocks".
    for fileIndex = 0, 14 do
        if fileIndex ~= 0 and fileIndex ~= 1 and fileIndex ~= 14 then
            H.eq(byIndex[fileIndex].xp, 0, "untouched skill " .. fileIndex .. " reads 0 XP, not an error")
        end
    end

    -- Real bug this guards against: "clicking Max for Agriculture says unlocked Fishing lvl 20".
    -- The write itself (skills.set -> Server_RemoveAllXPFromSkill/Server_AddXPToSkill against
    -- FileIndexToLiveSkillId) was always correct - confirmed against the real game's own
    -- ECharacterSkills/E_CharacterSkills enum data, both of which number every skill identically
    -- (0=NoSkill, 1=Sprinting, ..., 7=Fishing, ..., 11=Agriculture, ..., 18=MAX). The actual bug was
    -- one level up: LivePlayerSkillsSession.SaveAsync used to resend EVERY skill on every save, and
    -- the remove-then-add write briefly zeroes a skill before restoring it - so every untouched
    -- skill got re-applied too, re-triggering the game's own level-up popup for it. Fishing sits
    -- last in file order, so its redundant popup was always the one left on screen. Sending only
    -- the edited skill (what the fixed SaveAsync now does) means this handler must never touch
    -- Fishing - or Sprinting/Strength - when only Agriculture (file index 13, live id 11) is sent.
    local setReply = H.ok(
        H.dispatch("skills.set", { skills = { { index = 13, xp = 91655, xpMultiplier = 1 } } }),
        "skills.set (Agriculture only)")

    local afterEdit = H.ok(H.dispatch("skills.get"), "skills.get after editing only Agriculture")
    local afterByIndex = {}
    for _, row in ipairs(afterEdit) do afterByIndex[row.index] = row end

    H.eq(afterByIndex[13].xp, 91655, "Agriculture (file index 13, live id 11) got the new XP")
    H.eq(afterByIndex[14].xp, 200, "Fishing was left exactly as it was - an Agriculture edit never touches it")
    H.eq(afterByIndex[0].xp, 51102.9, "Sprinting was left exactly as it was too")
    H.eq(afterByIndex[1].xp, 91655, "Strength was left exactly as it was too")

    H.eq(H.calls(progression, "Server_AddXPToSkill"), 1,
        "only the edited skill's XP was written to the live game, not all fifteen")
    H.eq(H.calls(progression, "Server_RemoveAllXPFromSkill"), 1,
        "only the edited skill had its XP cleared before the write")
end
