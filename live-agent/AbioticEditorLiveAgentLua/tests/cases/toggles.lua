-- Survival toggles, plus the radiation/temperature fields and OnRep refresh added to vitals.
return function(H)
    local pawn = H.hostSession()
    local fields = rawget(pawn, "__fields")
    local methods = rawget(pawn, "__methods")
    fields.Invincible = false
    fields.InfiniteStamina = false
    fields.CurrentStamina = 20; fields.MaxStamina = 100
    fields.HasHunger = true; fields.MaxHunger = 100
    fields.HasThirst = true; fields.MaxThirst = 100
    fields.HasFatigue = true
    fields.HasContinence = true; fields.MaxContinence = 100
    fields.CanReceiveRadiation = true; fields.CurrentRadiation = 12
    fields.HasBodyTemperature = true; fields.CurrentBodyTemperature = 31; fields.PreferredBodyTemperature = 37
    fields.DrownTime = 30
    fields.NPC_Targetable = true
    fields.TakeFallDamage = true
    fields.HasMasterKey = false
    local healed = 0
    methods.Server_HealRandomLimb = function() healed = healed + 1 end
    for _, name in ipairs({ "OnRep_CurrentStamina", "OnRep_CurrentHunger", "OnRep_CurrentThirst", "OnRep_CurrentFatigue",
        "OnRep_CurrentContinence", "OnRep_CurrentRadiation", "OnRep_CurrentBodyTemperature", "OnRep_CurrentSanity" }) do
        methods[name] = function() end
    end

    -- vitals: radiation and temperature are reported, with the game's comfortable temperature
    local vitals = H.ok(H.dispatch("vitals.get"), "vitals.get with conditions")
    H.eq(vitals.radiation, 12, "radiation read")
    H.eq(vitals.bodyTemperature, 31, "body temperature read")
    H.eq(vitals.normalBodyTemperature, 37, "comfortable temperature read")
    H.ok(H.dispatch("vitals.set", { radiation = 0, bodyTemperature = 37, hunger = 90 }), "vitals.set conditions")
    H.eq(H.field(pawn, "CurrentRadiation"), 0, "radiation written")
    H.eq(H.field(pawn, "CurrentBodyTemperature"), 37, "temperature written")
    H.eq(H.field(pawn, "CurrentHunger"), 90, "hunger still written")

    -- all twelve toggles are reported, all off
    local got = H.ok(H.dispatch("toggles.get"), "toggles.get").toggles
    for _, name in ipairs({ "invincible", "infiniteStamina", "noHunger", "noThirst", "noFatigue", "noContinence",
        "noRadiation", "perfectTemperature", "infiniteOxygen", "untargetable", "noFallDamage", "masterKey" }) do
        H.eq(got[name], false, name .. " starts off")
    end

    -- switching on applies the same follow-up the reference mod does
    local all = { invincible = true, infiniteStamina = true, noHunger = true, noThirst = true, noFatigue = true,
        noContinence = true, noRadiation = true, perfectTemperature = true, infiniteOxygen = true,
        untargetable = true, noFallDamage = true, masterKey = true }
    local after = H.ok(H.dispatch("toggles.set", { toggles = all }), "every toggle on").toggles
    for name in pairs(all) do H.eq(after[name], true, name .. " now on") end
    H.eq(H.field(pawn, "Invincible"), true, "invincible flag")
    H.eq(H.field(pawn, "CurrentHealth_Head"), 70.0, "invincible heals the head to full")
    H.eq(healed, 6, "invincible heals every limb through the game")
    H.eq(H.field(pawn, "CurrentStamina"), 100, "stamina topped up")
    H.eq(H.field(pawn, "HasHunger"), false, "hunger switched off")
    H.eq(H.field(pawn, "CurrentHunger"), 100, "hunger filled")
    H.eq(H.field(pawn, "CurrentFatigue"), 0.0, "fatigue cleared (it counts up)")
    H.eq(H.field(pawn, "CurrentContinence"), 100, "continence filled")
    H.eq(H.field(pawn, "CanReceiveRadiation"), false, "radiation blocked")
    H.eq(H.field(pawn, "HasBodyTemperature"), false, "temperature frozen")
    H.eq(H.field(pawn, "CurrentBodyTemperature"), 37, "temperature set to comfortable")
    H.eq(H.field(pawn, "DrownTime"), 999999.0, "oxygen made infinite")
    H.eq(H.field(pawn, "NPC_Targetable"), false, "enemies ignore the player")
    H.eq(H.field(pawn, "TakeFallDamage"), false, "no fall damage")
    H.eq(H.field(pawn, "HasMasterKey"), true, "master key")

    -- switching off flips the property back and restores the remembered drown time
    local off = H.ok(H.dispatch("toggles.set", { toggles = { infiniteOxygen = false, noHunger = false } }), "two off").toggles
    H.eq(off.infiniteOxygen, false, "oxygen off")
    H.eq(H.field(pawn, "DrownTime"), 30, "original drown time restored")
    H.eq(H.field(pawn, "HasHunger"), true, "hunger back on")
    H.eq(H.field(pawn, "CurrentHunger"), 100, "hunger keeps its value")
    H.eq(off.masterKey, true, "untouched toggles stay on")

    H.fails(H.dispatch("toggles.set", { toggles = { flying = true } }), "unknown toggle", "unknown toggles are refused")
    H.fails(H.dispatch("toggles.set", { toggles = { masterKey = "yes" } }), "true or false", "values must be booleans")
    H.fails(H.dispatch("toggles.set", {}), "toggles are required", "an empty request is refused")

    -- a game version without one of the properties simply does not offer it
    fields.HasMasterKey = nil
    H.eq(H.ok(H.dispatch("toggles.get")).toggles.masterKey, nil, "missing property hides the toggle")

    H.clientSession()
    H.fails(H.dispatch("toggles.set", { toggles = { noHunger = true } }), "only the host", "client toggles rejected")
end
