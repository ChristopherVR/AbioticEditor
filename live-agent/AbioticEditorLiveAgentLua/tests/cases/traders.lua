-- Trader availability (areas/traders.lua) - no dedicated trader UObject, reuses the same world
-- flag subsystem flags.list/flags.set already drives (see the file header comment for why).
return function(H)
    H.hostSession()

    local subsystem = H.world.add(H.object("WorldFlagSubsystem", {}, {
        GetWorldFlags = function(_, out) out[1] = H.outParam(H.fname("Trader_Carson_Unlocked")) return true end,
        SetWorldFlag = function() end,
    }))
    H.world.static("/Script/AbioticFactor.Default__WorldFlagHandleFunctionLibrary", H.object("WorldFlagHandleFunctionLibrary", {}, {
        GetAllWorldFlagRowNames = function(_, out)
            out[1] = H.outParam(H.fname("Trader_Carson_Unlocked"))
            out[2] = H.outParam(H.fname("Trader_Greyson_StockA"))
        end,
        GetAllWorldFlagRowHandles = function(_, out)
            out[1] = H.outParam({ RowName = H.fname("Trader_Carson_Unlocked"), DataTablePath = "DT_WorldFlags" })
            out[2] = H.outParam({ RowName = H.fname("Trader_Greyson_StockA"), DataTablePath = "DT_WorldFlags" })
        end,
    }))

    -- traders.list: reports whichever flags currently read as set.
    local list = H.ok(H.dispatch("traders.list"), "traders.list")
    H.eq(#list.setFlags, 1, "one trader flag currently set")
    H.eq(list.setFlags[1], "Trader_Carson_Unlocked", "the set flag name round-trips")
    H.eq(list.isHost, true, "host authority reported")

    -- traders.unlock: sets a second trader's gating flag through the shared subsystem.
    H.ok(H.dispatch("traders.unlock", { flags = { "Trader_Greyson_StockA" } }), "unlock a trader")
    H.eq(H.calls(subsystem, "SetWorldFlag"), 1, "SetWorldFlag called through the shared subsystem")

    -- Unknown flag name: player-safe failure, not a Lua error (matches flags.set's own wording).
    H.fails(H.dispatch("traders.unlock", { flags = { "Trader_DoesNotExist" } }), "unknown quest flag", "unknown trader flag fails cleanly")

    -- Non-host refusal.
    H.clientSession()
    H.fails(H.dispatch("traders.unlock", { flags = { "Trader_Carson_Unlocked" } }), "only the host", "client cannot unlock traders")
end
