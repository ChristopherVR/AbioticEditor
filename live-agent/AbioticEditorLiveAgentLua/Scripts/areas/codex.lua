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
--     [3] KilLRequirement   -- see round 106 below: also reachable through this RPC
--     [4] ECompendiumUnlockType_MAX  -- sentinel, not a real value
-- This lines up exactly with the file format's own CompendiumRow.Sections[].UnlockRequirement
-- values ("ECompendiumUnlockType::Exploration"/"::Email"/"::NarrativeNPC" - see
-- Core/Catalogs/Codex/CodexCatalog.cs's BuildCompendium), so `sectionType` on the wire uses the
-- same three plain names, translated to the RPC's integer here.
--
-- KILL-TRACKED SECTIONS GROUNDED (round 106): the round-77 comment above assumed UnlockType=3
-- (KilLRequirement) was never reachable through Request_UnlockCompendiumSection, since no
-- installed mod calls it that way. The full bytecode of the private function it forwards to,
-- "Server Try Unlock Compendium Section" (pass2\Abiotic_CharacterProgressionComponent.json,
-- statement dump around its EX_SwitchValue on Temp_byte_Variable), disassembles to an
-- EX_SwitchValue keyed on UnlockType with FOUR cases, not three - 0/1/2 select
-- Compendium_ExplorationSections/EmailSections/NarrativeNPCSections exactly as documented, and
-- case 3 selects `Compendium_KillSections` (an FArrayProperty with
-- PropertyFlags "Edit | BlueprintVisible | Net | DisableEditOnInstance | RepNotify" and
-- RepNotifyFunc OnRep_Compendium_KillSections - the same shape as the other three section
-- arrays), then the switch's selected array reference is passed to KismetArrayLibrary.Array_Add
-- with CompendiumRow. The only gate before that add is a duplicate check
-- (HasCompendiumSectionUnlocked(CompendiumRow, UnlockType) short-circuits if already unlocked) -
-- there is no check of Compendium_KillCount/AllowedCompendiumKills in this path, so the RPC adds
-- the row to Compendium_KillSections unconditionally, the same as the other three types. This is a
-- real, grounded unlock path for a kill-tracked compendium row, reachable through the exact same
-- RPC and calling convention already used for the other three types - not a guess from the leaf
-- name. `Compendium_KillCount` (the actual per-row kill tally, a separate FArrayProperty
-- incremented by `Server_AddCompendiumKill`) is NOT touched by this RPC and stays out of scope
-- here; this only marks the compendium SECTION unlocked, the same thing offline editing's
-- CompendiumRead_ tag records.
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
    local replication = require("replication")
    local names = require("name_arrays")
    local clearFields = {
        emails = { { "EmailsRead" } },
        journals = { { "JournalEntries", "OnRep_JournalEntries" } },
        fish = { { "FishCaughtArray", "OnRep_FishCaughtArray" } },
        compendium = {
            { "Compendium_ExplorationSections", "OnRep_Compendium_ExplorationSections" },
            { "Compendium_EmailSections", "OnRep_Compendium_EmailSections" },
            { "Compendium_NarrativeNPCSections", "OnRep_Compendium_NarrativeNPCSections" },
            -- Round 106: the kill-tracked section array, same shape as the other three - see the
            -- file header comment for the bytecode evidence.
            { "Compendium_KillSections", "OnRep_Compendium_KillSections" },
        },
    }
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
            -- Round 106: the kill-tracked section array - see the file header comment.
            function() return component.Compendium_KillSections end,
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
                canUnsetKnown = ctx.isHost() and replication.available(),
                -- Round 106: whether this agent's unlockCompendiumSections() maps the
                -- kill-requirement section type at all (older agents omit the field entirely,
                -- which the app treats as false and keeps kill-only rows read-only, same as
                -- every other agent-version capability flag in this protocol).
                canUnlockKillSections = true,
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

    -- ECompendiumUnlockType's four RPC-reachable values (see the file header comment).
    -- KillRequirement (round 106) is grounded the same way as the other three: the server
    -- function's own bytecode adds the row to Compendium_KillSections for this case, gated only
    -- by the same already-unlocked check the other three share. The MAX entry is a sentinel, not
    -- a real section, and is never exposed here.
    local CompendiumSectionType = { Exploration = 0, Email = 1, NarrativeNPC = 2, KillRequirement = 3 }

    local function unlockCompendiumSections(component, entries)
        for i = 1, #entries do
            local entry = entries[i]
            if entry and entry.row and entry.row ~= "" then
                local sectionType = entry.sectionType
                if type(sectionType) == "string" then sectionType = CompendiumSectionType[sectionType] end
                if type(sectionType) == "number" and sectionType >= 0 and sectionType <= 3 then
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
            if payload.clear then
                if not ctx.isHost() then error("clearing codex entries requires host authority") end
                local fields = clearFields[payload.clear.section]
                if not fields then error("unknown codex section") end
                local helper = replication.requireHelper()
                local prepared = {}
                for _, field in ipairs(fields) do
                    prepared[#prepared + 1] = { field = field, values = names.prepare(component[field[1]], {}, payload.clear.ids) }
                end
                for _, entry in ipairs(prepared) do
                    component[entry.field[1]] = entry.values
                    replication.mark(helper, component, entry.field[1])
                end
                for _, entry in ipairs(prepared) do if entry.field[2] then component[entry.field[2]](component) end end
            end
            unlockEach(component, payload.emails or {}, function(c, name) c:Server_AddEmailToReadList(name) end)
            unlockEach(component, payload.journals or {}, function(c, name) c:Server_AddNoteToJournal(name) end)
            unlockEach(component, payload.fish or {}, function(c, name) c:Request_UnlockNewFish(name) end)
            unlockCompendiumSections(component, payload.compendium or {})
            return nil
        end, respond)
    end
end
