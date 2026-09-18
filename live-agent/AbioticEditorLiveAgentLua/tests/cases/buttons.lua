-- World buttons (areas/buttons.lua). Property/function names here are the CONFIRMED ones from the
-- coordinator's CUE4Parse dump (Button_Generic_C's own ChildProperties/ScriptBytecode) - see the
-- module's own header comment for the full mapping and why "pressed once" is read-only. Discovery
-- is a hierarchy sweep (FindAllOf("Button_Generic_C")), not a hardcoded leaf-class list, so every
-- fake button object here needs `__bases = { "Button_Generic_C" }` for the fake FindAllOf's own
-- hierarchy matching (harness.lua's `matches()`) to find it at all - exactly mirroring how the
-- real UE4SS FindAllOf is hierarchy-inclusive.
local PRESSED_ONCE_LEAF = "ButtonHasBeenPressedOnce_110_C4AE20D34162FCD3FA3323907300CB1F"

return function(H)
    H.hostSession()

    local function button(className, activated, disabled, noVignetteReset, pressedOnce)
        return H.world.add(H.object(className, {
            __bases = { "Button_Generic_C" },
            Activated = activated,
            ButtonDisabled = disabled,
            NoVignetteReset = noVignetteReset,
            ButtonSaveData = { [PRESSED_ONCE_LEAF] = pressedOnce },
        }, {
            OnRep_Activated = function() end,
            OnRep_ButtonDisabled = function() end,
            -- Mirrors the real UpdateButtonSaveData(Force) bytecode closely enough to prove the
            -- side effect this module's tests care about: it ALWAYS marks pressedOnce true,
            -- unconditionally, the moment it runs at all (see the module's own header comment).
            UpdateButtonSaveData = function(self)
                rawget(self, "__fields").ButtonSaveData[PRESSED_ONCE_LEAF] = true
            end,
            K2_GetActorLocation = function() return H.vector(5, 5, 5) end,
        }))
    end
    -- reactor: enabled (ButtonDisabled=false), not activated, never pressed yet. A concrete class
    -- this module has always known about (still just data to the sweep, not a special case).
    local reactor = button("Button_DFWarReactor_C", false, false, false, false)
    -- weatherEnd: disabled (ButtonDisabled=true), not activated, already pressed once.
    local weatherEnd = button("Button_WeatherEnd_C", false, true, false, true)

    -- A brand-new button subclass this module has NEVER heard of (no hardcoded name anywhere in
    -- buttons.lua matches "Button_TotallyNewType_C") - the hierarchy sweep must still find and
    -- list it purely because it declares Button_Generic_C as a base, exactly as a real future
    -- button type would. It also only has SOME of the usual properties (no NoVignetteReset, no
    -- ButtonSaveData at all), proving per-field feature detection: present fields still read/write
    -- normally, missing ones report as unavailable instead of erroring the whole button away.
    local futureType = H.world.add(H.object("Button_TotallyNewType_C", {
        __bases = { "Button_Generic_C" },
        Activated = false,
        ButtonDisabled = false,
    }, {
        OnRep_Activated = function() end,
        OnRep_ButtonDisabled = function() end,
        -- No UpdateButtonSaveData either - proves the persistence call itself is pcall-guarded,
        -- not assumed, for a class this module has never seen before.
        K2_GetActorLocation = function() return H.vector(1, 2, 3) end,
    }))

    -- Something that is NOT a button at all (no Button_Generic_C ancestry) - the hierarchy sweep
    -- must exclude it, the same way it would exclude a real CartRecallButton_C/
    -- Button_SpecialImageButton_C (neither of which derives from Button_Generic_C - see the
    -- module's own header comment).
    H.world.add(H.object("NotAButtonActor_C", { __bases = { "SomethingElseEntirely" } }, {}))

    -- A known-hierarchy button where NEITHER Activated/ButtonDisabled/NoVignetteReset NOR
    -- ButtonSaveData resolve at all: still listed (id/position are a plain FindAllOf sweep, never
    -- guessed), just with every state field absent.
    local unresolvable = H.world.add(H.object("Button_ORDER_C", { __bases = { "Button_Generic_C" } }, {
        K2_GetActorLocation = function() return H.vector(9, 9, 9) end,
    }))

    -- Rows come back in FindAllOf's own order (creation order, in this fake), not grouped by name,
    -- so look each one up by class rather than assuming a position.
    local function rowFor(list, classFragment)
        for _, row in ipairs(list.buttons) do
            if row.id:find(classFragment, 1, true) then return row end
        end
        return nil
    end

    local list = H.ok(H.dispatch("buttons.list"), "buttons.list")
    H.eq(#list.buttons, 4, "every Button_Generic_C-derived actor is listed, the non-button excluded")
    local reactorRow = rowFor(list, "Button_DFWarReactor_C")
    local weatherEndRow = rowFor(list, "Button_WeatherEnd_C")
    local futureRow = rowFor(list, "Button_TotallyNewType_C")
    local orderRow = rowFor(list, "Button_ORDER_C")
    H.check(reactorRow ~= nil and weatherEndRow ~= nil and futureRow ~= nil and orderRow ~= nil,
        "all four rows found")
    H.eq(reactorRow.enabled, true, "reactor button enabled (ButtonDisabled=false, inverted)")
    H.eq(reactorRow.activated, false, "reactor button not activated")
    H.eq(reactorRow.pressedOnce, false, "reactor button never pressed yet")
    H.eq(weatherEndRow.enabled, false, "weather-end button disabled (ButtonDisabled=true, inverted)")
    H.eq(weatherEndRow.pressedOnce, true, "weather-end button already pressed once")
    H.eq(futureRow.label, "Button_TotallyNewType_C",
        "an unfamiliar class's real name comes through as the row label, not a generic fallback")
    H.eq(futureRow.enabled, true, "the unfamiliar class's ButtonDisabled still reads (feature-detected, not assumed)")
    H.eq(futureRow.pressedOnce, nil, "the unfamiliar class has no ButtonSaveData at all, so pressedOnce is absent")
    H.eq(orderRow.enabled, nil, "no live property present on the ORDER button, so the field is absent")

    -- buttons.set: flip the reactor button's activated flag on.
    H.ok(H.dispatch("buttons.set", { buttons = { { id = reactorRow.id, activated = true } } }),
        "activate the reactor button")
    H.eq(H.field(reactor, "Activated"), true, "reactor button Activated field written")
    H.eq(H.calls(reactor, "OnRep_Activated"), 1, "matching OnRep_Activated pushed once")
    H.eq(H.calls(reactor, "UpdateButtonSaveData"), 1, "UpdateButtonSaveData(true) called to persist it")
    H.eq(H.field(reactor, "ButtonSaveData")[PRESSED_ONCE_LEAF], true,
        "pressedOnce forced true as a side effect of UpdateButtonSaveData, exactly like the real bytecode")
    H.eq(H.field(weatherEnd, "Activated"), false, "weather-end button left alone")

    -- enabled=true must invert onto ButtonDisabled=false.
    H.ok(H.dispatch("buttons.set", { buttons = { { id = weatherEndRow.id, enabled = true } } }),
        "enable the weather-end button")
    H.eq(H.field(weatherEnd, "ButtonDisabled"), false, "enabled=true wrote ButtonDisabled=false (inverted)")
    H.eq(H.calls(weatherEnd, "OnRep_ButtonDisabled"), 1, "matching OnRep_ButtonDisabled pushed once")

    -- Settable even though buttons.lua has never heard of this exact class name - the whole point
    -- of hierarchy-based discovery. UpdateButtonSaveData is missing on it; the write must still
    -- land without the missing persistence call erroring the request.
    H.ok(H.dispatch("buttons.set", { buttons = { { id = futureRow.id, activated = true } } }),
        "activate the unfamiliar future button type")
    H.eq(H.field(futureType, "Activated"), true, "the unfamiliar class's Activated field still writes")
    H.eq(H.calls(futureType, "OnRep_Activated"), 1, "its own OnRep_Activated still gets called")

    -- Missing button id: player-safe failure, not a Lua error - and any resolvable rows in the
    -- same call still apply first (matching doors.set/portals.set).
    local missingReply = H.dispatch("buttons.set", { buttons = {
        { id = reactorRow.id, noReset = true },
        { id = "no-such-button", activated = true },
    } })
    H.fails(missingReply, "not found", "unknown button id fails cleanly")
    H.eq(H.field(reactor, "NoVignetteReset"), true, "the resolvable row in the same batch still applied")

    -- ROUND-110: "pressed once" is now genuinely settable, on its own, bypassing
    -- UpdateButtonSaveData entirely (that wrapper is never called for a pressedOnce-only request -
    -- calling it would immediately re-force the leaf true). Instead the leaf is written directly
    -- and persisted through the fake GameMode's own UpdateActorToWorldSave. weatherEnd starts
    -- pressedOnce=true (see the fixture above); flip it to false directly.
    local updateCallsBeforeClear = H.calls(weatherEnd, "UpdateButtonSaveData")
    local clearReply = H.dispatch("buttons.set", { buttons = { { id = weatherEndRow.id, pressedOnce = false } } })
    H.ok(clearReply, "pressedOnce can be cleared back to false live")
    H.eq(H.field(weatherEnd, "ButtonSaveData")[PRESSED_ONCE_LEAF], false,
        "the leaf actually holds false now, not forced true")
    H.eq(H.calls(weatherEnd, "UpdateButtonSaveData"), updateCallsBeforeClear,
        "the wrapper that would force it back to true was never called for this pressedOnce-only request")
    local lastPersist = H.field(H.gameMode, "__lastUpdateActorToWorldSave")
    H.check(lastPersist ~= nil and lastPersist.actor == weatherEnd, "GameMode:UpdateActorToWorldSave was called with this exact button")
    H.eq(lastPersist.removeFromSave, false, "RemoveFromSave is false, matching the game's own call")
    H.eq(lastPersist.saveType, 4, "SaveType is the literal 4 copied from UpdateButtonSaveData's own bytecode")

    -- A button with no ButtonSaveData at all (orderRow/unresolvable, see the fixture above): the
    -- write target genuinely does not exist, so this fails honestly by name rather than lying
    -- about success - never a Lua error, never a silent no-op.
    local noStructReply = H.dispatch("buttons.set", { buttons = { { id = orderRow.id, pressedOnce = true } } })
    H.fails(noStructReply, "could not set 'pressed once'", "a button without ButtonSaveData fails cleanly")
    H.eq(H.calls(unresolvable, "UpdateButtonSaveData"), 0,
        "no persistence call was made for a button with no other field touched")

    -- A pressedOnce request alongside a real field: the real field applies AND pressedOnce ends up
    -- exactly as requested - UpdateButtonSaveData's own forced-true side effect (from the "touched"
    -- block) is overridden by the pressedOnce write that runs after it in the same row.
    local mixedReply = H.dispatch("buttons.set",
        { buttons = { { id = reactorRow.id, activated = false, pressedOnce = false } } })
    H.ok(mixedReply, "a mixed request (real field + pressedOnce=false) now succeeds")
    H.eq(H.field(reactor, "Activated"), false, "the real field in the same mixed request still applied")
    H.eq(H.field(reactor, "ButtonSaveData")[PRESSED_ONCE_LEAF], false,
        "pressedOnce ends up false, winning over UpdateButtonSaveData's own forced-true side effect")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(reactor)
    H.fails(H.dispatch("buttons.set", { buttons = { { id = reactorRow.id, activated = true } } }),
        "only the host", "client cannot edit world buttons")
end
