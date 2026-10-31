-- Active effects (buffs and debuffs) on a player character: list, add, remove, clear debuffs,
-- and "block new debuffs".
--
-- Grounding:
--   - CharacterBuffComponent.CurrentBuffs is an array of BuffDebuffEntry { BuffRow
--     (BuffDebuffRowHandle), ParentLimb (EBodyLimbs), BuffExpireTime } - read the same way by
--     CheatConsoleCommands' AFUtils.LogCharacterBuffComponent.
--   - Adding: the game's own load path (Abiotic_PlayerState_C "Apply Save Data to Character")
--     re-applies every saved effect with Server_ApplyBuff(BuffRow, true, BuffExpireTime,
--     ParentLimb, None, false), so (row, overrideDuration, seconds, limb, linkedActor, skipDialog)
--     is the game's own argument order. EBodyLimbs: AllBones = 0 (AFUtils Enums.lua).
--   - Removing: BuffObject_CrystalLens removes Debuff_NoCloak with
--     Server_RemoveBuff(handle, true, 0, false); the same call is used here with the entry's limb.
--   - Blocking: CheatConsoleCommands' "No Debuffs" pre-hooks Server_ApplyBuff and, for its own
--     player, shortens every row named Debuff* except Debuff_Underwater to 0.001 s with the
--     dialog skipped. The same hook runs here for each player it is switched on for. Unlike that
--     mod, switching it on also removes the debuffs the player already has.
-- Row handles come from BuffDebuffHandleFunctionLibrary, as general.trait.set builds them.
return function(ctx)
    local UNDERWATER = "Debuff_Underwater"
    local ALL_BONES = 0
    local blocked = {}  -- player full name -> true while "block new debuffs" is on
    local hookRegistered = false

    local function library()
        local lib = StaticFindObject("/Script/AbioticFactor.Default__BuffDebuffHandleFunctionLibrary")
        if not lib or not lib:IsValid() then error("effect support is unavailable in this game version") end
        return lib
    end

    local function buffComponent(player)
        local class = StaticFindObject("/Script/AbioticFactor.CharacterBuffComponent")
        if not class or not class:IsValid() then error("effect support is unavailable in this game version") end
        local component = player and player:GetComponentByClass(class)
        if not component or not component:IsValid() then error("no character buff component found") end
        return component
    end

    local function targetPlayer(payload)
        local player = ctx.resolvePlayer(payload)
        if not player then error("player not found") end
        return player
    end

    local function isDebuff(row)
        return type(row) == "string" and #row > 6 and row:sub(1, 6) == "Debuff" and row ~= UNDERWATER
    end

    local function rowString(entry)
        local ok, name = pcall(function() return entry.BuffRow.RowName:ToString() end)
        if ok and name and name ~= "None" then return name end
        return nil
    end

    local function limbNumber(value)
        return tonumber(tostring(value)) or ALL_BONES
    end

    -- Resolves a row name to the game's handle, refusing names the buff table does not have.
    local function handleFor(row)
        if type(row) ~= "string" or row == "" then error("an effect row is required") end
        local name = FName(row, EFindName.FNAME_Find)
        if name:ToString() == "None" then error("unknown effect: " .. row) end
        local lib = library()
        local known = false
        for _, existing in ipairs(ctx.outNames(function(out) lib:GetAllBuffDebuffRowNames(out) end)) do
            if existing == row then known = true; break end
        end
        if not known then error("unknown effect: " .. row) end
        return lib:MakeBuffDebuffRowHandle(name)
    end

    local function worldSeconds(player)
        local ok, seconds = pcall(function()
            return ctx.UEHelpers.GetGameplayStatics():GetTimeSeconds(player)
        end)
        if ok and type(seconds) == "number" then return seconds end
        return nil
    end

    local function removeEntry(component, row, limb)
        component:Server_RemoveBuff(handleFor(row), true, limb, false)
    end

    local function clearDebuffs(component)
        local removed = 0
        local pending = {}
        for index = 1, #component.CurrentBuffs do
            local entry = component.CurrentBuffs[index]
            local row = rowString(entry)
            if isDebuff(row) then pending[#pending + 1] = { row = row, limb = limbNumber(entry.ParentLimb) } end
        end
        for _, item in ipairs(pending) do
            if pcall(removeEntry, component, item.row, item.limb) then removed = removed + 1 end
        end
        return removed
    end

    local function ensureHook()
        if hookRegistered then return end
        RegisterHook("/Script/AbioticFactor.CharacterBuffComponent:Server_ApplyBuff",
            function(Context, BuffRow, bOverrideDefaultDuration, NewDuration, Limb, LinkedActor, bSkipDialog)
                if next(blocked) == nil then return end
                local ok, owner = pcall(function() return Context:get():GetOwner() end)
                if not ok or not owner or not owner:IsValid() or not blocked[ctx.fullName(owner)] then return end
                local okRow, row = pcall(function() return BuffRow:get().RowName:ToString() end)
                if okRow and isDebuff(row) then
                    NewDuration:set(0.001)
                    bOverrideDefaultDuration:set(true)
                    bSkipDialog:set(true)
                end
            end)
        hookRegistered = true
    end

    ctx.handlers["effects.list"] = function(payload, respond)
        ctx.runOnGameThread(function()
            local player = targetPlayer(payload)
            local component = buffComponent(player)
            local effects = { __forceArray = true }
            for index = 1, #component.CurrentBuffs do
                local entry = component.CurrentBuffs[index]
                local row = rowString(entry)
                if row then
                    local okExpire, expire = pcall(function() return entry.BuffExpireTime end)
                    effects[#effects + 1] = {
                        row = row,
                        limb = limbNumber(entry.ParentLimb),
                        expireTime = okExpire and type(expire) == "number" and expire or -1,
                    }
                end
            end
            return {
                effects = effects,
                now = worldSeconds(player),
                blockDebuffs = blocked[ctx.fullName(player)] == true,
                isHost = ctx.isHost(),
            }
        end, respond)
    end

    ctx.handlers["effects.add"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can add effects") end
            local component = buffComponent(targetPlayer(payload))
            local handle = handleFor(payload.row)
            local seconds = payload.seconds
            local override = type(seconds) == "number" and seconds > 0
            component:Server_ApplyBuff(handle, override, override and seconds or 0, ALL_BONES, nil, true)
            return nil
        end, respond)
    end

    ctx.handlers["effects.remove"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can remove effects") end
            local component = buffComponent(targetPlayer(payload))
            local limb = type(payload.limb) == "number" and payload.limb or ALL_BONES
            removeEntry(component, payload.row, limb)
            return nil
        end, respond)
    end

    ctx.handlers["effects.clear"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can remove effects") end
            return { removed = clearDebuffs(buffComponent(targetPlayer(payload))) }
        end, respond)
    end

    ctx.handlers["effects.block"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can block debuffs") end
            if type(payload.enabled) ~= "boolean" then error("enabled must be true or false") end
            local player = targetPlayer(payload)
            local component = buffComponent(player)
            local key = ctx.fullName(player)
            if payload.enabled then
                ensureHook()
                blocked[key] = true
                clearDebuffs(component)
            else
                blocked[key] = nil
            end
            return { blockDebuffs = blocked[key] == true }
        end, respond)
    end
end
