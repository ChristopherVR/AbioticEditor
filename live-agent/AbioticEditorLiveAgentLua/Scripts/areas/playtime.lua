-- GameMode.ApplyWorldSaveData|DayNightCycle loads metadata.MinutesPassed into
-- AbioticGameState.SavedElapsedMinutes. SetTimeOfDayOnWorldSave persists GetElapsedMinutes().
-- Adjust the saved offset, keeping the native session timer running normally.
return function(ctx)
    local replication = require("replication")
    local function state()
        local obj = FindFirstOf("Abiotic_Survival_GameState_C")
        if obj and obj:IsValid() then return obj end
    end
    ctx.worldGetWithoutPlaytime = ctx.worldGetWithoutPlaytime or ctx.handlers["world.get"]
    ctx.handlers["world.get"] = function(payload, respond)
        ctx.worldGetWithoutPlaytime(payload, function(result, err)
            if result and not err then
                local ok, minutes = pcall(function() local obj=state(); return obj and obj:GetElapsedMinutes() end)
                if ok and type(minutes) == "number" then
                    result.minutesPassed = minutes
                    result.canSetMinutesPassed = ctx.isHost() and replication.available()
                end
            end
            respond(result, err)
        end)
    end
    ctx.handlers["world.setPlaytime"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can change world playtime") end
            local value = payload.minutesPassed
            if type(value) ~= "number" or value ~= value or value % 1 ~= 0 or value < 0 or value > 2147483647 then
                error("playtime must be a nonnegative whole number of minutes")
            end
            local obj = state()
            if not obj then error("the world game state is unavailable") end
            local saved = obj.SavedElapsedMinutes
            local adjustment = saved + value - obj:GetElapsedMinutes()
            if adjustment < -2147483648 or adjustment > 2147483647 then error("playtime offset is outside the supported range") end
            local helper = replication.requireHelper()
            obj.SavedElapsedMinutes = adjustment
            replication.mark(helper, obj, "SavedElapsedMinutes")
            obj:FlushNetDormancy()
            if obj:GetElapsedMinutes() ~= value then error("the game did not retain the requested playtime") end
            return nil
        end, respond)
    end
end
