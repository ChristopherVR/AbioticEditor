-- Live editing area: world buttons (ButtonMap, round 80; property/function names confirmed round
-- 95; class discovery switched from a hardcoded leaf list to a hierarchy sweep round 96).
--
-- Confirmed against the coordinator's own CUE4Parse dump (tests/AbioticEditor.Probes/
-- ElevatorButtonProbe.cs, and the "/Button_" fragments LiveClassPropsProbe.cs now includes), plus
-- the full blueprint bytecode (ScriptBytecode JSON) of Button_Generic_C's UpdateButtonSaveData,
-- CanButtonSave and the two OnRep_ functions. Every property/function name below is copied
-- verbatim from that dump, not guessed - see docs/reference/live-editing-protocol.md and
-- docs/PROGRESS.md for the round this landed.
--
-- CLASS DISCOVERY: every placed button class the dump shows (Button_DFWarReactor_C,
-- Button_Keypad_C and ITS subclasses Button_Keypad_VOTV_C/Button_Keypad_VOTV_Terminal_C,
-- Button_LightSwitch_C and its subclass Button_VOTV_Lightswitch_C, Button_ORDER_C,
-- Button_Torii_Lantern_C, Button_Torii_Lantern_Hanging_C, Button_Tram_C and its subclass
-- Button_TramRecall_C, Button_ValveWheel_C, Button_VehicleRecall_C, Button_WeatherEnd_C) chains up
-- to Button_Generic_C, confirmed from each class's own `super=` in the dump. FindAllOf is
-- hierarchy-inclusive (confirmed already by bases.lua/main.lua's CONTAINER_CLASSES/
-- loadedContainers, and documented again in pets.lua's own header comment), so a single
-- FindAllOf("Button_Generic_C") already returns every one of those subclasses' instances AND
-- automatically picks up any button type the game adds later - no hardcoded leaf-class list, no
-- code change needed when a new one ships. Two names that looked like buttons are NOT part of
-- this system and would never match that sweep anyway:
--   Button_SpecialImageButton_C - a UMG WIDGET (WidgetBlueprintGeneratedClass, super=AbioticWidget),
--     not a placed level actor, so it can never appear in a FindAllOf sweep of the world at all.
--   CartRecallButton_C - derives from VehicleRecallStation_C, not Button_Generic_C; its own dump
--     has no Activated/ButtonDisabled/ButtonSaveData at all, so it is not part of ButtonMap/
--     SaveData_ButtonStruct and does not belong under "buttons" at all.
--
-- FIELD MAPPING: Button_Generic_C's UpdateButtonSaveData(Force) is gated on `Force OR
-- CanButtonSave()` (CanButtonSave checks the per-actor ShouldSave/ToggleSwitch design flags, so
-- passing Force=true - the same "ForceWorldSave" idea portals.lua's SavePortalState(true) already
-- uses - bypasses that gate entirely) and, once it runs, does exactly this:
--   ButtonSaveData.ButtonHasBeenPressedOnce_ = true      -- UNCONDITIONALLY, every single call
--   ButtonSaveData.ButtonIsEnabled_ = NOT ButtonDisabled  -- inverted copy of a live top-level bool
--   ButtonSaveData.ButtonActivated_ = Activated           -- direct copy of a live top-level bool
--   ButtonSaveData.NoReset_ = NoVignetteReset             -- direct copy of a live top-level bool
--   GameMode:UpdateActorToWorldSave(Self, false, 4)       -- the actual "persist this now" call
-- That grounds the offline<->live mapping exactly:
--   offline "enabled"     <- live `NOT button.ButtonDisabled` (inverted; replicated, RepNotify
--                             OnRep_ButtonDisabled)
--   offline "activated"   <- live `button.Activated` (direct; replicated, RepNotify OnRep_Activated)
--   offline "noReset"     <- live `button.NoVignetteReset` (direct; plain, NOT replicated - no
--                             OnRep exists for it, confirmed from its own PropertyFlags in the dump)
--   offline "pressedOnce" <- live `button.ButtonSaveData.ButtonHasBeenPressedOnce_110_...`
--                             (read-only: the bytecode never sets this to anything but `true`, and
--                             only as a side effect of UpdateButtonSaveData running at all - there
--                             is no live path that sets it independently, so buttons.set refuses
--                             this field outright rather than silently no-op or lie about it)
-- Every one of these reads/writes is pcall-guarded per instance (see boolOrNil below and the
-- inline pcall around each write in buttons.set), not assumed present: a class this module has
-- never heard of (whether reached through the
-- Button_Generic_C sweep or an ADDITIONAL_ROOT_CLASSES entry) still lists with whatever it
-- actually has and reports the rest as unavailable, rather than erroring or being dropped - and
-- its OWN real class name comes through as `label` (ctx.classLabel, off GetFullName()) regardless,
-- so an unfamiliar type is visible to the player and in logs even when none of its fields resolve.
--
-- STATE-CHANGE CHOICE (deliberate): Button_Generic_C also has TriggerButtonWithoutUser(), but its
-- bytecode is one jump straight into the shared interaction ubergraph
-- (ExecuteUbergraph_Button_Generic[6719]) - the same entry point a player's own interaction uses,
-- which fires linked-button chains, cooldown checks and whatever else that graph does, not a clean
-- "set this exact value". A state editor wants the latter, so this module writes the property
-- directly, calls the matching OnRep_ (a server never gets its own OnRep for something it just
-- wrote locally - the same reasoning transmog.lua/portals.lua already document), then calls
-- UpdateButtonSaveData(true) - exactly the shape the game's own save path already uses.
return function(ctx)
    -- The one class nearly every placed button derives from (see header) - a hierarchy sweep on
    -- this alone already covers every subclass, present and future.
    local BUTTON_ROOT_CLASS = "Button_Generic_C"

    -- Fallback layer for a button-shaped class that does NOT chain up to Button_Generic_C - DATA,
    -- not logic, so a future one only needs a line added here, never a code change. Currently
    -- empty: of the two look-alikes checked this round, Button_SpecialImageButton_C is a UI widget
    -- (never a world actor at all) and CartRecallButton_C has none of this feature's properties -
    -- neither belongs here even as a second root. Entries here must be ROOTS a real button chains
    -- up to (or is itself), never a leaf that already chains up to BUTTON_ROOT_CLASS above - adding
    -- a leaf here would just make it show up twice in the same sweep.
    local ADDITIONAL_ROOT_CLASSES = {}

    local ROOT_CLASSES = { BUTTON_ROOT_CLASS }
    for _, extra in ipairs(ADDITIONAL_ROOT_CLASSES) do table.insert(ROOT_CLASSES, extra) end

    -- The struct-nested leaf's exact hash-suffixed name, confirmed against the real class dump
    -- (SaveData_ButtonStruct.uasset's own ChildProperties) - hardcoded rather than scanned for,
    -- matching main.lua's own SKILL_XP_FIELD precedent: a struct-instance field name is only ever
    -- hardcoded against a real confirmed dump, never guessed.
    local PRESSED_ONCE_LEAF = "ButtonHasBeenPressedOnce_110_C4AE20D34162FCD3FA3323907300CB1F"

    local function allButtons()
        local result, seen = {}, {}
        for _, class in ipairs(ROOT_CLASSES) do
            for _, actor in ipairs(ctx.findAll(class)) do
                local name = ctx.fullName(actor)
                if name and not seen[name] then seen[name] = true; result[#result + 1] = actor end
            end
        end
        return result
    end

    local function findButton(id)
        for _, class in ipairs(ROOT_CLASSES) do
            local actor = ctx.findByFullName(class, id)
            if actor then return actor end
        end
        return nil
    end

    -- `(ok and cond) and value or nil` is the classic Lua ternary trap when value itself is
    -- `false` - `... and false` collapses to `false`, then `false or nil` collapses again to
    -- `nil`, silently turning a real "false" reading into "unresolved". Every boolean read in
    -- this file goes through this explicit-if helper instead (never the `and/or` idiom).
    local function boolOrNil(ok, value)
        if ok and type(value) == "boolean" then return value end
        return nil
    end

    -- Same trap, inverted: nil must stay nil (unresolved), not become "true" because `not nil`
    -- is truthy. Explicit if, no ternary.
    local function invertedOrNil(value)
        if value == nil then return nil end
        return not value
    end

    -- Read-only: ButtonSaveData is a struct VALUE embedded on the actor (not a separate UObject),
    -- so `button.ButtonSaveData` hands back a struct-instance proxy the same shape
    -- CommandsManager.lua's skillStruct is - indexed by the leaf's exact hash-suffixed name, same
    -- as SKILL_XP_FIELD elsewhere in this mod. A class without a ButtonSaveData struct at all
    -- (feature-detected, not assumed) just reads back nil here, same as any other missing field.
    local function readPressedOnce(button)
        local okData, data = pcall(function() return button.ButtonSaveData end)
        if not okData or data == nil then return nil end
        local ok, value = pcall(function() return data[PRESSED_ONCE_LEAF] end)
        return boolOrNil(ok, value)
    end

    local function buttonRows()
        local result = { __forceArray = true }
        for _, button in ipairs(allButtons()) do
            if button:IsValid() then
                local name = ctx.fullName(button)
                if name then
                    local x, y, z = ctx.actorLocation(button)
                    local okDisabled, disabled = pcall(function() return button.ButtonDisabled end)
                    local okActivated, activated = pcall(function() return button.Activated end)
                    local okNoReset, noReset = pcall(function() return button.NoVignetteReset end)
                    table.insert(result, {
                        id = name,
                        -- The actor's real runtime class, off GetFullName() - true for any class,
                        -- known or not, so an unfamiliar button type is still visible by name here
                        -- and not silently folded into a generic label.
                        label = ctx.classLabel(name),
                        enabled = invertedOrNil(boolOrNil(okDisabled, disabled)),
                        activated = boolOrNil(okActivated, activated),
                        noReset = boolOrNil(okNoReset, noReset),
                        pressedOnce = readPressedOnce(button),
                        x = x, y = y, z = z,
                    })
                end
            end
        end
        return result
    end

    ctx.handlers["buttons.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            return { buttons = buttonRows(), isHost = ctx.isHost() }
        end, respond)
    end

    -- Matching doors.set/portals.set: every resolvable row in the batch is applied first; only
    -- once every row has run does an unresolved id (or a rejected pressedOnce request) turn the
    -- whole reply into an error, rather than a silent partial no-op.
    ctx.handlers["buttons.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change world buttons") end
            local rows = payload.buttons or {}
            local missingId, pressedOnceRequested = nil, false
            for i = 1, #rows do
                local row = rows[i]
                local button = row.id and findButton(row.id)
                if button then
                    if row.pressedOnce ~= nil then pressedOnceRequested = true end
                    local touched = false
                    if row.enabled ~= nil then
                        -- pcall-guarded, not assumed: a class reached only through
                        -- ADDITIONAL_ROOT_CLASSES (or a future Button_Generic_C subclass that
                        -- happens to drop this property) simply has this write silently skipped,
                        -- not erroring the whole batch - matching the "list/set whatever could be
                        -- read" principle the header describes.
                        pcall(function() button.ButtonDisabled = not row.enabled end)
                        pcall(function() button:OnRep_ButtonDisabled() end)
                        touched = true
                    end
                    if row.activated ~= nil then
                        pcall(function() button.Activated = row.activated end)
                        pcall(function() button:OnRep_Activated() end)
                        touched = true
                    end
                    if row.noReset ~= nil then
                        -- NoVignetteReset is not replicated (no OnRep_ exists for it - confirmed
                        -- from its own PropertyFlags in the dump), so a direct write is all there
                        -- is; UpdateButtonSaveData below still persists it.
                        pcall(function() button.NoVignetteReset = row.noReset end)
                        touched = true
                    end
                    if touched then
                        -- The one real persistence call - see the file header. It ALSO
                        -- unconditionally marks pressedOnce true as a side effect; there is no way
                        -- to avoid that and still make the other field(s) stick. pcall-guarded: a
                        -- class without this function still gets its property write above (a
                        -- visible live change), just without persistence confirmed.
                        pcall(function() button:UpdateButtonSaveData(true) end)
                    end
                else
                    missingId = missingId or row.id
                end
            end
            if missingId then error("button not found (it may have been unloaded or destroyed): " .. tostring(missingId)) end
            if pressedOnceRequested then
                error("'pressed once' cannot be set independently live - the game always marks it true "
                    .. "the moment any other field on the same button is saved, and there is no live path "
                    .. "to clear it back to false (edit the save file directly for that)")
            end
            return nil
        end, respond)
    end
end
