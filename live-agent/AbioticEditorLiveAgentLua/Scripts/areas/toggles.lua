-- Player survival toggles: switches on the player character that the game reads every tick.
-- None of them is written to a save, so each lasts until the world is left (the game rebuilds
-- the character from its defaults on load).
--
-- Every property and the follow-up write are verbatim from CheatConsoleCommands' Features.lua:
--   invincible         Invincible = true, then a full heal (InfiniteHealth)
--   infiniteStamina    InfiniteStamina = true, CurrentStamina = MaxStamina, OnRep_CurrentStamina
--   noHunger/noThirst  HasHunger/HasThirst = false, Current* = Max*, OnRep_Current*
--   noFatigue          HasFatigue = false, CurrentFatigue = 0 (fatigue counts up), OnRep
--   noContinence       HasContinence = false, CurrentContinence = MaxContinence, OnRep
--   noRadiation        CanReceiveRadiation = false, CurrentRadiation = 0, OnRep_CurrentRadiation
--   perfectTemperature HasBodyTemperature = false, CurrentBodyTemperature = PreferredBodyTemperature
--   infiniteOxygen     DrownTime = 999999 (the previous value is kept to restore it)
--   untargetable       NPC_Targetable = false (enemies ignore the player)
--   noFallDamage       TakeFallDamage = false
--   masterKey          HasMasterKey = true (every keyed door opens)
-- Switching one off only flips the property back, as the mod does; the stat itself resumes
-- from wherever it is.
return function(ctx)
    local INFINITE_DROWN_TIME = 999999.0
    local savedDrownTime = {}  -- player full name -> DrownTime before infinite oxygen

    local function call(player, name)
        pcall(function() player[name](player) end)
    end

    local function healAll(player)
        player.CurrentHealth_Head = 70.0
        player.CurrentHealth_Torso = 100.0
        player.CurrentHealth_LeftArm = 100.0
        player.CurrentHealth_RightArm = 100.0
        player.CurrentHealth_LeftLeg = 100.0
        player.CurrentHealth_RightLeg = 100.0
        local outSuccess = { Success = false }
        for _ = 1, 6 do pcall(function() player:Server_HealRandomLimb(100.0, outSuccess) end) end
        call(player, "OnRep_CurrentHealth")
    end

    -- A toggle reads as on when its boolean property equals `onValue`; nil when this game
    -- version has no such boolean property (the toggle is then not offered).
    local function flag(field, onValue)
        return function(p)
            local value = p[field]
            if type(value) ~= "boolean" then return nil end
            return value == onValue
        end
    end

    -- name -> { read = function(player) -> bool?, write = function(player, on) }
    local toggles = {
        invincible = {
            read = flag("Invincible", true),
            write = function(p, on) p.Invincible = on; if on then healAll(p) end end,
        },
        infiniteStamina = {
            read = flag("InfiniteStamina", true),
            write = function(p, on)
                p.InfiniteStamina = on
                if on then p.CurrentStamina = p.MaxStamina; call(p, "OnRep_CurrentStamina") end
            end,
        },
        noHunger = {
            read = flag("HasHunger", false),
            write = function(p, on)
                p.HasHunger = not on
                if on then p.CurrentHunger = p.MaxHunger; call(p, "OnRep_CurrentHunger") end
            end,
        },
        noThirst = {
            read = flag("HasThirst", false),
            write = function(p, on)
                p.HasThirst = not on
                if on then p.CurrentThirst = p.MaxThirst; call(p, "OnRep_CurrentThirst") end
            end,
        },
        noFatigue = {
            read = flag("HasFatigue", false),
            write = function(p, on)
                p.HasFatigue = not on
                if on then p.CurrentFatigue = 0.0; call(p, "OnRep_CurrentFatigue") end
            end,
        },
        noContinence = {
            read = flag("HasContinence", false),
            write = function(p, on)
                p.HasContinence = not on
                if on then p.CurrentContinence = p.MaxContinence; call(p, "OnRep_CurrentContinence") end
            end,
        },
        noRadiation = {
            read = flag("CanReceiveRadiation", false),
            write = function(p, on)
                p.CanReceiveRadiation = not on
                if on then p.CurrentRadiation = 0.0; call(p, "OnRep_CurrentRadiation") end
            end,
        },
        perfectTemperature = {
            read = flag("HasBodyTemperature", false),
            write = function(p, on)
                p.HasBodyTemperature = not on
                if on then
                    p.CurrentBodyTemperature = p.PreferredBodyTemperature
                    call(p, "OnRep_CurrentBodyTemperature")
                end
            end,
        },
        infiniteOxygen = {
            read = function(p)
                local value = p.DrownTime
                if type(value) ~= "number" then return nil end
                return value == INFINITE_DROWN_TIME
            end,
            write = function(p, on)
                local key = ctx.fullName(p) or ""
                if on then
                    if p.DrownTime ~= INFINITE_DROWN_TIME then savedDrownTime[key] = p.DrownTime end
                    p.DrownTime = INFINITE_DROWN_TIME
                elseif savedDrownTime[key] then
                    p.DrownTime = savedDrownTime[key]
                    savedDrownTime[key] = nil
                end
            end,
        },
        untargetable = {
            read = flag("NPC_Targetable", false),
            write = function(p, on) p.NPC_Targetable = not on end,
        },
        noFallDamage = {
            read = flag("TakeFallDamage", false),
            write = function(p, on) p.TakeFallDamage = not on end,
        },
        masterKey = {
            read = flag("HasMasterKey", true),
            write = function(p, on) p.HasMasterKey = on end,
        },
    }

    local function targetPlayer(payload)
        local player = ctx.resolvePlayer(payload)
        if not player then error("player not found") end
        return player
    end

    -- Only toggles whose property this game version actually has are reported.
    local function readAll(player)
        local result = {}
        for name, toggle in pairs(toggles) do
            local ok, value = pcall(toggle.read, player)
            if ok and type(value) == "boolean" then result[name] = value end
        end
        return result
    end

    ctx.handlers["toggles.get"] = function(payload, respond)
        ctx.runOnGameThread(function()
            return { toggles = readAll(targetPlayer(payload)), isHost = ctx.isHost() }
        end, respond)
    end

    ctx.handlers["toggles.set"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change survival toggles") end
            if type(payload.toggles) ~= "table" then error("toggles are required") end
            local player = targetPlayer(payload)
            for name, on in pairs(payload.toggles) do
                local toggle = toggles[name]
                if not toggle then error("unknown toggle: " .. tostring(name)) end
                if type(on) ~= "boolean" then error(name .. " must be true or false") end
            end
            for name, on in pairs(payload.toggles) do
                local ok, err = pcall(toggles[name].write, player, on)
                if not ok then error("could not change " .. name .. ": " .. tostring(err)) end
            end
            return { toggles = readAll(player) }
        end, respond)
    end
end
