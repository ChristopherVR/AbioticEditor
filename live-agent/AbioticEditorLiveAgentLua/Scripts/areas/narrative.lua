-- ===== Narrative NPCs (NarrativeNPC_ParentBP_C) - round 77 =====
-- The offline session has Npcs/SetNpc: a narrative-NPC map (dead state, narrative state) with no
-- dedicated live tab so far - LiveNpcsTab only ever covered ordinary wildlife/monster NPCs
-- (main.lua's npcs.list/npcs.set, shipped and working). Grounded in the game's own class layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragments "NarrativeNPC_ParentBP",
-- "NarrativeNPC_Human_ParentBP"):
--   IsCorpse : FBoolProperty        -- no hash suffix, direct class member
--   NarrativeState : FByteProperty  -- no hash suffix, direct class member (raw enum byte)
--   func SetNewNarrativeState(NarrativeState: byte)  -- a real ONE-PARAMETER setter, confirmed in
--     the dump (not guessed) - preferred over a raw field write since it is the game's own entry
--     point (it also updates LastPlayedNarrativeState/broadcasts OnNarrativeStateChanged
--     internally, none of which a bare field write would trigger).
-- No confirmed setter for IsCorpse (no "OnRep_IsCorpse"/"SetCorpse" anywhere in this class's
-- function list) - written directly, same "no confirmed OnRep, write the field, done" pattern
-- bases.lua's rename and vehicles.lua's PendingDestroy already use. Wrapped in pcall; genuinely
-- unproven whether this alone updates the NPC's ragdoll/visual state live without a game restart.
--
-- NarrativeState is a raw byte here (the enum's own integer value), not the file's own string
-- representation - this module does not attempt to decode/re-encode ENarrativeNPCState's names,
-- since no probe dump anywhere carries that enum's value list. The wire value is passed straight
-- through as an integer both ways; a caller wanting the file's string names has to keep its own
-- mapping (or just toggle "dead"/"alive" via IsCorpse and leave NarrativeState alone).
return function(ctx)
    local NARRATIVE_CLASS = "NarrativeNPC_ParentBP_C" -- hierarchy-inclusive: also finds NarrativeNPC_Human_ParentBP_C and every named trader/story NPC under it.

    local function narrativeRows()
        local result = { __forceArray = true }
        for _, npc in ipairs(ctx.findAll(NARRATIVE_CLASS)) do
            if npc:IsValid() then
                local name = ctx.fullName(npc)
                if name then
                    local x, y, z = ctx.actorLocation(npc)
                    local okCorpse, isCorpse = pcall(function() return npc.IsCorpse == true end)
                    local okState, state = pcall(function() return npc.NarrativeState end)
                    table.insert(result, {
                        id = name,
                        label = ctx.classLabel(name),
                        isCorpse = okCorpse and isCorpse or false,
                        narrativeState = (okState and state) or 0,
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["narrativenpcs.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { npcs = narrativeRows(), isHost = ctx.isHost() }
        end, respond)
    end

    ctx.handlers["narrativenpcs.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can edit narrative NPCs") end
            local rows = payload.npcs or {}
            for i = 1, #rows do
                local row = rows[i]
                local npc = row.id and ctx.findByFullName(NARRATIVE_CLASS, row.id)
                if npc then
                    if row.isCorpse ~= nil then
                        pcall(function() npc.IsCorpse = row.isCorpse end)
                    end
                    if row.narrativeState ~= nil then
                        local ok = pcall(function() npc:SetNewNarrativeState(row.narrativeState) end)
                        if not ok then pcall(function() npc.NarrativeState = row.narrativeState end) end
                    end
                end
            end
            return nil
        end, respond)
    end
end
