-- Saving the running world on request (world.save / world.saving).
--
-- Moving a base out of a running world needs the pieces' exact saved records, and those only
-- reach the disk when the game saves. The game's own player controller saves the world by calling
-- the native async action USaveCurrentWorld::SaveCurrentWorld(WorldContextObject = itself) and
-- then Activate() on what it returns, after checking AbioticGameInstance.bIsSaving (read from the
-- cooked bytecode of Abiotic_PlayerController's ubergraph, BlueprintBytecodeProbe). This does the
-- same. The save runs on the game's save thread; world.saving reports bIsSaving so the editor can
-- wait for it to finish, and ActiveWorldSaveName tells it which world folder was written.
return function(ctx)
    local function gameInstance()
        local ok, instance = pcall(function() return ctx.UEHelpers.GetGameInstance() end)
        if ok and instance and instance:IsValid() then return instance end
        return nil
    end

    local function worldName(instance)
        local ok, name = pcall(function() return instance.ActiveWorldSaveName:ToString() end)
        if ok and type(name) == "string" and name ~= "" then return name end
        return nil
    end

    local function isSaving(instance)
        local ok, saving = pcall(function() return instance.bIsSaving == true end)
        return ok and saving or false
    end

    ctx.handlers["world.save"] = function(_, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can save the world") end
            local instance = gameInstance()
            if not instance then error("the game is not ready to save yet") end
            if isSaving(instance) then return { started = false, saving = true, worldName = worldName(instance) } end
            local controller = ctx.UEHelpers.GetPlayerController()
            if not controller or not controller:IsValid() then error("no player to save the world with") end
            local saver = StaticFindObject("/Script/AbioticFactor.Default__SaveCurrentWorld")
            if not saver or not saver:IsValid() then error("this game build has no world save action") end
            local action = saver:SaveCurrentWorld(controller)
            if not action or not action:IsValid() then error("the game did not start a world save") end
            action:Activate()
            return { started = true, saving = true, worldName = worldName(instance) }
        end, respond)
    end

    ctx.handlers["world.saving"] = function(_, respond)
        ctx.runOnGameThread(function()
            local instance = gameInstance()
            if not instance then error("the game is not ready yet") end
            return { saving = isSaving(instance), worldName = worldName(instance) }
        end, respond)
    end
end
