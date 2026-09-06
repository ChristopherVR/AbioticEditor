-- Live journal/codex editing (round 76-77): EMAIL/NOTES/FISH mark-known, plus COMPENDIUM
-- unlocking. Grounded in the game's own class layout, NOT a working mod:
-- LiveClassPropsProbe's dump of Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent.uasset
-- carries:
--   prop EmailsRead : FArrayProperty          -- matches the file format's own EmailsRead_ tag
--   prop JournalEntries : FArrayProperty       -- matches JournalEntries_
--   prop FishCaughtArray : FArrayProperty      -- matches the file format's FishCaught concept
--   prop Compendium_ExplorationSections : FArrayProperty  -- per-category unlocked-row lists
--   prop Compendium_EmailSections : FArrayProperty
--   prop Compendium_NarrativeNPCSections : FArrayProperty
--   prop Local_AllCompendiumEntries : FSetProperty  -- client-side derived union of the above
--   func Server_AddEmailToReadList(EmailRow: FName)
--   func Server_AddNoteToJournal(JournalRow: FName)
--   func Request_UnlockNewFish(FishRowName: FName)
--   func Request_UnlockCompendiumSection(CompendiumRow: FName, SectionType: <enum, see below>)
-- None of the first three write functions is called by any installed reference mod, but they are
-- named/shaped exactly like Request_UnlockCompendiumSection - a function on this SAME component
-- class confirmed real by CheatConsoleCommands/scripts/Features.lua:900. See
-- docs/reference/live-editing-protocol.md "codex.get / codex.set" for the wire shape and evidence.
--
-- COMPENDIUM enum GROUNDED (round 77): Request_UnlockCompendiumSection(CompendiumRow, UnlockType)
-- takes an UnlockType enum whose values were previously un-grounded (Features.lua:894-900 only
-- ever forwards a value read live off a widget property, never a literal). Extending
-- LiveClassPropsProbe's usmap dump (LiveNativeClassPropsProbe, native enum table - this enum is a
-- native C++ enum, never its own Blueprint asset export, so it never shows up as a plain UEnum
-- package export) found it directly:
--   USMAP ENUM ECompendiumUnlockType (5 values):
--     [0] Exploration
--     [1] Email
--     [2] NarrativeNPC
--     [3] KilLRequirement   -- auto-unlocked by kill tracking, not this RPC; not exposed here
--     [4] ECompendiumUnlockType_MAX  -- sentinel, not a real value
-- This lines up exactly with the file format's own CompendiumRow.Sections[].UnlockRequirement
-- values ("ECompendiumUnlockType::Exploration"/"::Email"/"::NarrativeNPC" - see
-- Core/Catalogs/Codex/CodexCatalog.cs's BuildCompendium), so `sectionType` on the wire uses the
-- same three plain names, translated to the RPC's integer here.
--
-- COMPENDIUM READ (round 77): the previous round read the TSet `Local_AllCompendiumEntries`, whose
-- Lua-array readability this project could not confirm (a TSet has no established #/[i] indexing
-- precedent anywhere). This round found a much better-grounded read: `Compendium_ExplorationSections`,
-- `Compendium_EmailSections` and `Compendium_NarrativeNPCSections` are all FArrayProperty (plain
-- TArray<FName>, per-category unlocked compendium rows) - the exact same shape/technique as
-- EmailsRead/JournalEntries/FishCaughtArray below, which is a real, working pattern (the reference
-- mod's own "traits" console command indexes a different TArray property the same way). These three
-- arrays are used instead of Local_AllCompendiumEntries (a client-side cache the game itself derives
-- from them, redundant to read directly and carrying the TSet uncertainty).
--
-- WORLD-LEVEL vs PER-PLAYER: the game ALSO carries GlobalCompendiumEmail/Narrative/Exploration on
-- Abiotic_Survival_GameState_C (world-wide, like GlobalRecipesUnlocked - see areas/worldunlocks.lua),
-- but Request_UnlockCompendiumSection lives on the PER-PLAYER CharacterProgressionComponent and
-- writes the per-player Compendium_*Sections arrays, matching the file format's own per-player
-- CompendiumRead_ tag (Core/Catalogs/Codex, PlayerSaveWriter.ApplyCompendium) - so this module reads
-- and writes the per-player arrays, not the world-level ones.
return function(ctx)
    ---@return userdata? progressionComponent
    local function getProgressionComponent(payload)
        local targetPlayer = ctx.resolvePlayer(payload)
        if not targetPlayer then return nil end
        local component = targetPlayer.CharacterProgressionComponent
        if not component or not component:IsValid() then return nil end
        return component
    end

    -- Same indexed iteration + :ToString() the reference mod's "traits" console command uses on
    -- progressionComponen.Traits (a TArray<FName>) - applied here to that same shape for
    -- EmailsRead/JournalEntries/FishCaughtArray and the three Compendium_*Sections arrays (all
    -- TArray<FName> per the pak dump).
    local function readNameArray(getArray)
        local result = { __forceArray = true }
        local ok, arr = pcall(getArray)
        if not ok or not arr then return result end
        for i = 1, #arr do
            local okName, name = pcall(function() return arr[i]:ToString() end)
            if okName and name and name ~= "" then table.insert(result, name) end
        end
        return result
    end

    -- Merges the per-category arrays into one deduplicated list for the wire's flat "compendium"
    -- field (codex.get's reader does not need to know which category a row came from - the
    -- desktop app's own game-data vocabulary already knows that from DT_Compendium).
    local function readCompendiumKnown(component)
        local seen = {}
        local result = { __forceArray = true }
        for _, getArray in ipairs({
            function() return component.Compendium_ExplorationSections end,
            function() return component.Compendium_EmailSections end,
            function() return component.Compendium_NarrativeNPCSections end,
        }) do
            for _, name in ipairs(readNameArray(getArray)) do
                if not seen[name] then
                    seen[name] = true
                    table.insert(result, name)
                end
            end
        end
        return result
    end

    ctx.handlers["codex.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            return {
                emails = readNameArray(function() return component.EmailsRead end),
                journals = readNameArray(function() return component.JournalEntries end),
                fish = readNameArray(function() return component.FishCaughtArray end),
                compendium = readCompendiumKnown(component),
            }
        end, respond)
    end

    local function unlockEach(component, ids, call)
        for i = 1, #ids do
            if ids[i] and ids[i] ~= "" then
                pcall(function() call(component, FName(ids[i], EFindName.FNAME_Find)) end)
            end
        end
    end

    -- ECompendiumUnlockType's three RPC-reachable values (see the file header comment). Only
    -- these three: KilLRequirement sections unlock themselves from kill tracking, never through
    -- this function, and the MAX entry is a sentinel, not a real section.
    local CompendiumSectionType = { Exploration = 0, Email = 1, NarrativeNPC = 2 }

    local function unlockCompendiumSections(component, entries)
        for i = 1, #entries do
            local entry = entries[i]
            if entry and entry.row and entry.row ~= "" then
                local sectionType = entry.sectionType
                if type(sectionType) == "string" then sectionType = CompendiumSectionType[sectionType] end
                if type(sectionType) == "number" and sectionType >= 0 and sectionType <= 2 then
                    pcall(function()
                        component:Request_UnlockCompendiumSection(FName(entry.row, EFindName.FNAME_Find), sectionType)
                    end)
                end
            end
        end
    end

    ctx.handlers["codex.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            unlockEach(component, payload.emails or {}, function(c, name) c:Server_AddEmailToReadList(name) end)
            unlockEach(component, payload.journals or {}, function(c, name) c:Server_AddNoteToJournal(name) end)
            unlockEach(component, payload.fish or {}, function(c, name) c:Request_UnlockNewFish(name) end)
            unlockCompendiumSections(component, payload.compendium or {})
            return nil
        end, respond)
    end
end
