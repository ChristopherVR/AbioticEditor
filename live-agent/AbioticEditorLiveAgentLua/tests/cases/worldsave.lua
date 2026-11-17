-- Saving the running world on request (areas/worldsave.lua): the same native async action the
-- game's own player controller uses, refused for a client, and the progress flag the editor waits on.
return function(H)
    H.hostSession()
    local activated = 0
    local action = H.object("SaveCurrentWorld", {}, { Activate = function() activated = activated + 1 end })
    local usedController
    local saver = H.object("SaveCurrentWorld", {}, { SaveCurrentWorld = function(_, controller) usedController = controller return action end })
    H.world.static("/Script/AbioticFactor.Default__SaveCurrentWorld", saver)
    H.gameInstance = H.object("AbioticGameInstance", { bIsSaving = false, ActiveWorldSaveName = H.fstring("Cascade") })
    H.playerController = H.object("PlayerController", {})

    local started = H.ok(H.dispatch("world.save"), "world.save")
    H.eq(started.started, true, "a save is started")
    H.eq(started.worldName, "Cascade", "the running world's folder name is reported")
    H.eq(activated, 1, "the save action is activated")
    H.check(usedController == H.playerController, "the player controller is the world context, as in the game's own call")

    H.gameInstance.bIsSaving = true
    local busy = H.ok(H.dispatch("world.save"), "world.save while saving")
    H.eq(busy.started, false, "no second save while one runs")
    H.eq(activated, 1, "nothing new activated")
    H.eq(H.ok(H.dispatch("world.saving")).saving, true, "progress reports saving")
    H.gameInstance.bIsSaving = false
    H.eq(H.ok(H.dispatch("world.saving")).saving, false, "progress reports done")

    H.world.static("/Script/AbioticFactor.Default__SaveCurrentWorld", nil)
    H.fails(H.dispatch("world.save"), "no world save action", "a build without the action says so")

    H.clientSession()
    H.fails(H.dispatch("world.save"), "only the host", "a client cannot save the world")
end
