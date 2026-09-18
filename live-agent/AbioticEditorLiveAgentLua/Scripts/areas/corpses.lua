-- Live editing area: NPC corpses (CorpseMap, round 100).
--
-- Confirmed against the coordinator's own CUE4Parse class dump of CharacterCorpse_ParentBP_C and
-- CharacterCorpse_Human_BP.json - every property name below is copied verbatim from that dump, not
-- guessed. See docs/reference/live-editing-protocol.md for the round this landed and the fuller
-- citation.
--
-- CLASS DISCOVERY: every fixture-confirmed corpse class (CharacterCorpse_Human_BP_C,
-- CharacterCorpse_MonsterGeneric_C, CharacterCorpse_OrderGrunt_C, CharacterCorpse_OrderSniper_C -
-- plus others the dump shows, e.g. CharacterCorpse_OrderBreacher_C, CharacterCorpse_OrderCaptain_C,
-- CharacterCorpse_LabRat_C, CharacterCorpse_Human_GATESecurity_C) declares
-- `super=CharacterCorpse_ParentBP_C` either directly (MonsterGeneric) or through
-- CharacterCorpse_Human_BP_C (every named human-shaped variant) - either way FindAllOf on the one
-- root class already returns every subclass instance (the same hierarchy-inclusive idiom
-- buttons.lua/elevators.lua document), so no per-class name is hardcoded anywhere in this module
-- and a future corpse type needs no code change here.
--
-- FIELD MAPPING (confirmed from CharacterCorpse_ParentBP_C's own ChildProperties - matches the
-- file-editor twin, CorpseMapFeature.cs, one field name apart):
--   offline "gibbed" <- live `actor.IsGibbed` (direct; replicated BoolProperty, RepNotify
--                        OnRep_IsGibbed)
--   offline "looted" <- live `actor.HasBeenLooted` (direct; replicated BoolProperty, PLAIN - no
--                        RepNotify exists for it, confirmed from its own PropertyFlags in the dump)
-- Both stay read-only here too (CorpseMapFeature.cs's own remarks: "There is no in-game reason to
-- flip IsGibbed/HasBeenLooted by hand"), reported best-effort via pcall the same way every other
-- fixed-actor feature in this mod does, so a corpse type this module has never seen still lists
-- with whatever it has rather than erroring or being dropped.
--
-- REMOVAL: checked every function CharacterCorpse_ParentBP_C declares (SaveCorpse, DropLoot,
-- RefreshGibbedState, OnRep_IsGibbed/OnRep_CurrentGibCuts, GetTypeOfInteractableCorpse,
-- MergeAndClearSkeletals, GetAttackerLootChance, ...) and none of them cleanly despawns/removes an
-- already-placed corpse actor - there is no "DespawnCorpse" or equivalent. This mirrors round 78's
-- pets.remove finding exactly (no blueprint function "releases" a tamed pet either), so this module
-- uses the same standard `K2_DestroyActor()` the reference CheatConsoleCommands mod's own
-- "deleteobject" command already uses on an arbitrary world actor (CommandsManager.lua) - a
-- standard AActor function, not a guess specific to corpses. No undo once this runs, matching the
-- file editor's own remove description ("clear the clutter, and any loot still on it").
return function(ctx)
    -- The one class every fixture-confirmed corpse chains up to (see header) - a hierarchy sweep
    -- on this alone already covers every subclass, present and future.
    local CORPSE_ROOT_CLASS = "CharacterCorpse_ParentBP_C"

    -- Fallback layer for a corpse-shaped class that does NOT chain up to
    -- CharacterCorpse_ParentBP_C - DATA, not logic, exactly like buttons.lua's own
    -- ADDITIONAL_ROOT_CLASSES. Currently empty: every fixture-derived class this round confirmed
    -- (see the header comment) already chains up to the one root.
    local ADDITIONAL_ROOT_CLASSES = {}

    local ROOT_CLASSES = { CORPSE_ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local function allCorpses()
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
            end
        end
        return result
    end

    local function findCorpse(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- Same explicit-if helper buttons.lua/destructibles.lua document (never the `and/or` idiom).
    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    local function corpseRows()
        local result = { __forceArray = true }
        for _, corpse in ipairs(allCorpses()) do
            if corpse:IsValid() then
                local name = ctx.fullName(corpse)
                if name then
                    local x, y, z = ctx.actorLocation(corpse)
                    local okGibbed, gibbed = pcall(function() return corpse.IsGibbed end)
                    local okLooted, looted = pcall(function() return corpse.HasBeenLooted end)
                    table.insert(result, {
                        id = name,
                        -- The actor's real runtime class, off GetFullName() - true for any class,
                        -- known or not, matching the owner rule that an unfamiliar subclass is
                        -- still visible by its real name rather than folded into a generic label.
                        label = ctx.classLabel(name),
                        gibbed = boolOrNil(okGibbed, gibbed),
                        looted = boolOrNil(okLooted, looted),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["corpses.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { corpses = corpseRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Real live removal - see the header's REMOVAL section. No undo once this runs, matching
    -- pets.remove exactly (same K2_DestroyActor technique, same lack of an evidenced "clean"
    -- despawn path).
    ctx.handlers["corpses.remove"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can remove corpses") end
            local corpse = payload.id and findCorpse(payload.id)
            if not corpse then error("corpse not found (it may already be gone)") end
            local ok = pcall(function() corpse:K2_DestroyActor() end)
            if not ok then error("couldn't remove this corpse from the world") end
            return nil
        end, respond)
    end
end
