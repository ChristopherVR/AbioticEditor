-- Live journal/codex editing (round 76): EMAIL/NOTES/FISH mark-known, plus a read-only view of
-- COMPENDIUM. Grounded in the game's own class layout, NOT a working mod: LiveClassPropsProbe's
-- dump of Content/Blueprints/Characters/Abiotic_CharacterProgressionComponent.uasset carries:
--   prop EmailsRead : FArrayProperty          -- matches the file format's own EmailsRead_ tag
--   prop JournalEntries : FArrayProperty       -- matches JournalEntries_
--   prop FishCaughtArray : FArrayProperty      -- matches the file format's FishCaught concept
--   prop Local_AllCompendiumEntries : FSetProperty  -- every compendium row known to this client
--   func Server_AddEmailToReadList(EmailRow: FName)
--   func Server_AddNoteToJournal(JournalRow: FName)
--   func Request_UnlockNewFish(FishRowName: FName)
-- None of these three write functions is called by any installed reference mod, but they are
-- named/shaped exactly like Request_UnlockCompendiumSection - a function on this SAME component
-- class confirmed real by CheatConsoleCommands/scripts/Features.lua:900. See
-- docs/reference/live-editing-protocol.md "codex.get / codex.set" for the wire shape.
--
-- COMPENDIUM (Entities/Locations/IS/People/Theories) is read-only: the only unlock function for
-- it, Request_UnlockCompendiumSection(CompendiumRow, UnlockType), takes an UnlockType ENUM whose
-- values this project could not ground - the one call site that exists (Features.lua:894-900)
-- only ever forwards a value read live off a widget property, never a literal, and the pak dump
-- carries no enum value names. Guessing an enum value risks unlocking the wrong section or
-- writing garbage into that property, so this area reads Local_AllCompendiumEntries (every
-- compendium row this client currently knows) but never calls the unlock function.
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
    -- EmailsRead/JournalEntries/FishCaughtArray (all TArray<FName> per the pak dump), and,
    -- separately, to Local_AllCompendiumEntries, which the dump says is a TSet<FName>, not a
    -- TArray. Whether UE4SS's Lua binding exposes a TSet with the same #/[i] indexing as a
    -- TArray is NOT confirmed anywhere in this project - untested against the real game. The
    -- pcall means a TSet that does not support this just comes back as an empty compendium list
    -- (a visible "0 known" rather than a crash), which is an acceptable failure mode precisely
    -- because COMPENDIUM is already read-only here.
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

    ctx.handlers["codex.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            return {
                emails = readNameArray(function() return component.EmailsRead end),
                journals = readNameArray(function() return component.JournalEntries end),
                fish = readNameArray(function() return component.FishCaughtArray end),
                compendium = readNameArray(function() return component.Local_AllCompendiumEntries end),
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

    ctx.handlers["codex.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local component = getProgressionComponent(payload)
            if not component then error("no CharacterProgressionComponent found") end
            unlockEach(component, payload.emails or {}, function(c, name) c:Server_AddEmailToReadList(name) end)
            unlockEach(component, payload.journals or {}, function(c, name) c:Server_AddNoteToJournal(name) end)
            unlockEach(component, payload.fish or {}, function(c, name) c:Request_UnlockNewFish(name) end)
            -- payload.compendium is deliberately not accepted - see the file header comment.
            return nil
        end, respond)
    end
end
