-- Live editing area: trader availability (round 76).
--
-- No installed mod touches a trader UObject at all - CheatConsoleCommands has zero trader-
-- related commands (checked: nothing under "trader" anywhere in its scripts folder), and the
-- barter mechanics (TraderComponent, W_TraderScreen, DT_NPC_Traders/DT_NPC_TraderItems) are pure
-- UI/data-table driven with no simple property this module could safely flip. What IS real and
-- already proven in this exact mod is that trader availability/stock gating is a set of
-- QUEST/STORY WORLD FLAGS (WorldTradersTab's own HasWorldFlag checks, matching
-- Core/Catalogs/Codex/TraderCatalog.cs's RequiredFlags/RequiredFlag) - the same
-- UWorldFlagSubsystem this mod already drives for flags.list/flags.set (round 75, see
-- main.lua's worldFlagSubsystem()/worldFlagLibrary()/currentWorldFlags() helpers on ctx). So
-- this module does not add any new live UObject write path: it reuses the flags subsystem
-- verbatim, scoped to "does the trader roster's gating read as unlocked" instead of guessing at
-- a Trader-specific API that does not exist in any working mod.
return function(ctx)
    ctx.handlers["traders.list"] = function(_, respond)
        ctx.runOnGameThread(function()
            local set = ctx.currentWorldFlags()
            local flags = { __forceArray = true }
            for name, _ in pairs(set) do table.insert(flags, name) end
            return { setFlags = flags, isHost = ctx.isHost() }
        end, respond)
    end

    -- Sets one or more world flags to unlock a trader (or one of its stock items), exactly like
    -- flags.set - kept as its own command so the wire protocol names the intent (a trader unlock,
    -- not a bare flag edit) even though the underlying write is identical.
    ctx.handlers["traders.unlock"] = function(payload, respond)
        ctx.runOnGameThread(function()
            if not ctx.isHost() then error("only the host can unlock traders") end
            local subsystem = ctx.worldFlagSubsystem()
            local lib = ctx.worldFlagLibrary()
            if not subsystem or not lib then error("the quest flag system is not loaded (are you in a world?)") end
            local out = {}
            lib:GetAllWorldFlagRowHandles(out)
            local handles = {}
            for i = 1, #out do
                local okHandle, handle = pcall(function() return out[i]:get() end)
                if okHandle and handle then
                    local okName, name = pcall(function() return handle.RowName:ToString() end)
                    if okName and name then handles[name] = handle end
                end
            end
            local instigator = ctx.getMyPlayer()
            local flags = payload.flags or {}
            for i = 1, #flags do
                local flagName = flags[i]
                local handle = handles[flagName]
                if not handle then error("unknown quest flag " .. tostring(flagName)) end
                local okCall = pcall(function()
                    subsystem:SetWorldFlag({ RowName = handle.RowName, DataTablePath = handle.DataTablePath }, true, instigator)
                end)
                if not okCall then subsystem:SetWorldFlag(handle, true, instigator) end
            end
            return nil
        end, respond)
    end
end
