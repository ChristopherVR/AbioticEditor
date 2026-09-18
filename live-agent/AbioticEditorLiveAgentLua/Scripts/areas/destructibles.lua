-- Live editing area: breakable world objects (DestructibleMap, round 100).
--
-- Confirmed against the coordinator's own CUE4Parse class dump and Abiotic_GenericDestructible_BP_C's
-- own blueprint bytecode (ScriptBytecode JSON) - every property/function name below is copied
-- verbatim from that dump, not guessed. See docs/reference/live-editing-protocol.md for the round
-- this landed and the fuller citation.
--
-- CLASS DISCOVERY: every fixture-confirmed destructible class (Destructible_CeilingTile_C,
-- Webbing_BP_C, IceWall_BP_C, Destructible_Fracture_MageEye_C, Destructible_InvisibleWall_C,
-- XRayField_BP_C, Destructible_CafeteriaDoor_C, Destructible_PortablePortal_C,
-- Destructible_SyncrotronHole_C, Destructible_SecurityContainerDoor_C,
-- Destructible_BookCartStack_C, Destructible_ContainmentShield_C - plus others the dump shows,
-- e.g. Destructible_LargeEnemy_C, DestructibleTank_C, NightWebbing_BP_C) declares
-- `super=Abiotic_GenericDestructible_BP_C` directly, or (Webbing_Marshmallow_BP_C/
-- Webbing_Marshmallow_Small_BP_C, both super=Webbing_BP_C) chains up to it through one more step -
-- either way FindAllOf on the one root class already returns every subclass instance (the same
-- hierarchy-inclusive idiom buttons.lua/elevators.lua document), so no per-class name is hardcoded
-- anywhere in this module and a future destructible type needs no code change here.
--
-- FIELD MAPPING (confirmed from Abiotic_GenericDestructible_BP_C's own ChildProperties/
-- ScriptBytecode):
--   offline "broken" <- live `actor.Broken` (direct; replicated BoolProperty, RepNotify
--                        OnRep_Broken - PropertyFlags "Edit | BlueprintVisible | Net |
--                        DisableEditOnInstance | RepNotify")
-- Breaking is a real, reversible-in-the-sense-of-repeatable live write: the class's own ubergraph
-- (the code a world-flag-triggered break and TryApplyDamage-reaching-zero both jump into) does
-- exactly `Broken = true; OnRep_Broken(); SetStateBroken(NoFX)` - so this module mirrors that shape
-- for `broken = true`: write `Broken = true`, then call the real `OnRep_Broken()` (which itself
-- calls `SetStateBroken(NoFX)` with `NoFX` computed from `AbioticFunctionLibrary.HasActorRecentlyLoaded`
-- - a live-triggered break plays its FX/SFX the same as a real player-caused break would, since the
-- actor did not "just load").
--
-- REPAIR IS NOT POSSIBLE LIVE (confirmed, not assumed): `OnRep_Broken`'s own bytecode starts with
-- `if not Broken then return end` - there is NO branch that runs when Broken is false. Nothing else
-- in the class (checked every function in the dump: SetStateBroken, TryApplyDamage, Server_InitialBreakEvent,
-- WorldFlagBreakCheck, HealthUpdated, UserConstructionScript) ever re-enables the intact mesh's
-- collision/visibility or disables the destroyed mesh's once `SetStateBroken` has run - `SetStateBroken`
-- itself takes only a `NoFX` bool, never a "which state" argument, so it is a one-way break, not a
-- toggle. Writing `Broken = false` back onto an already-broken actor would desync the save's own flag
-- from what the player still sees (a permanently broken mesh/collision), so this module refuses a
-- `broken = false` request outright with a named reason rather than performing a silent no-op or a
-- lying "success" - see the header of DestructibleMapFeature.cs (the file-editor twin) for the same
-- field, and buttons.lua's own `pressedOnce` refusal for the identical "refuse before writing
-- anything" shape this reuses.
return function(ctx)
    -- The one class every fixture-confirmed destructible chains up to (see header) - a hierarchy
    -- sweep on this alone already covers every subclass, present and future.
    local DESTRUCTIBLE_ROOT_CLASS = "Abiotic_GenericDestructible_BP_C"

    -- Fallback layer for a destructible-shaped class that does NOT chain up to
    -- Abiotic_GenericDestructible_BP_C - DATA, not logic, exactly like buttons.lua's own
    -- ADDITIONAL_ROOT_CLASSES. Currently empty: every fixture-derived class this round confirmed
    -- (see the header comment) already chains up to the one root.
    local ADDITIONAL_ROOT_CLASSES = {}

    local ROOT_CLASSES = { DESTRUCTIBLE_ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    local function allDestructibles()
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
            end
        end
        return result
    end

    local function findDestructible(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- Same explicit-if helper buttons.lua documents (never the `and/or` idiom - that trap
    -- silently turns a real "false" reading into "unresolved" the moment the value itself is
    -- false).
    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    local function destructibleRows()
        local result = { __forceArray = true }
        for _, destructible in ipairs(allDestructibles()) do
            if destructible:IsValid() then
                local name = ctx.fullName(destructible)
                if name then
                    local x, y, z = ctx.actorLocation(destructible)
                    local okBroken, broken = pcall(function() return destructible.Broken end)
                    table.insert(result, {
                        id = name,
                        -- The actor's real runtime class, off GetFullName() - true for any class,
                        -- known or not, matching the owner rule that an unfamiliar subclass is
                        -- still visible by its real name rather than folded into a generic label.
                        label = ctx.classLabel(name),
                        broken = boolOrNil(okBroken, broken),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["destructibles.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { destructibles = destructibleRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Matching doors.set/buttons.set: every resolvable row in the batch is applied first; only
    -- once every row has run does an unresolved id (or a rejected "repair" request) turn the whole
    -- reply into an error, rather than a silent partial no-op.
    ctx.handlers["destructibles.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change breakable objects") end
            local rows = payload.destructibles or {}
            local missingId, repairRequested = nil, false
            for i = 1, #rows do
                local row = rows[i]
                local destructible = row.id and findDestructible(row.id)
                if destructible then
                    if row.broken == true then
                        -- pcall-guarded, not assumed: a class this module has never heard of (an
                        -- ADDITIONAL_ROOT_CLASSES entry, or a future subclass missing this
                        -- property) simply has this write silently skipped rather than erroring
                        -- the whole batch - matching buttons.lua's own "list/set whatever could be
                        -- read" principle.
                        pcall(function() destructible.Broken = true end)
                        -- The real game function - see the header. It internally calls
                        -- SetStateBroken(NoFX) itself, so this is the one call needed to fully
                        -- reproduce a real break (collision, mesh swap, and FX/SFX when the actor
                        -- has not "just loaded").
                        pcall(function() destructible:OnRep_Broken() end)
                    elseif row.broken == false then
                        -- Never written - see the header's "REPAIR IS NOT POSSIBLE LIVE" section.
                        -- Collected first so any OTHER resolvable row in the same batch still
                        -- applies before the whole reply becomes an error.
                        repairRequested = true
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("destructible not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if repairRequested then
                error("this object cannot be repaired live - the game's own OnRep_Broken does nothing "
                    .. "once Broken is set back to false, and no other live function restores the intact "
                    .. "mesh/collision once it has broken (edit the save file directly for that)")
            end
            return nil
        end, respond)
    end
end
