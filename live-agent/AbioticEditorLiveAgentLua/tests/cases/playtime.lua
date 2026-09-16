return function(H)
    H.hostSession()
    H.world.static("/Script/Engine.Default__NetPushModelHelpers", H.object("Replication", {}, {MarkPropertyDirty=function() end}))
    H.world.add(H.object("DayNightManager_C", {CurrentDay=2,CurrentTimeInSeconds=120,IsNight=false,DayNightManuallyPaused=false,CurrentWeatherEvent=H.fname("None")}))
    local elapsed = 7
    local state = H.gameState
    state.SavedElapsedMinutes = 100
    rawget(state,"__methods").GetElapsedMinutes=function(self) return self.SavedElapsedMinutes + elapsed end
    rawget(state,"__methods").FlushNetDormancy=function() end
    local before = H.ok(H.dispatch("world.get"))
    H.eq(before.minutesPassed,107,"playtime includes live session timer")
    H.eq(before.canSetMinutesPassed,true,"host gets playtime capability")
    H.ok(H.dispatch("world.setPlaytime",{minutesPassed=10}),"playtime reduced")
    H.eq(state.SavedElapsedMinutes,3,"offset adjusted without resetting session timer")
    H.ok(H.dispatch("world.setPlaytime",{minutesPassed=0}),"playtime cleared even after session time passes")
    H.eq(state:GetElapsedMinutes(),0,"zero playtime reads back")
    elapsed=elapsed+1
    H.eq(state:GetElapsedMinutes(),1,"playtime timer continues after edit")
    H.fails(H.dispatch("world.setPlaytime",{minutesPassed=-1}),"nonnegative","negative playtime rejected")
    H.fails(H.dispatch("world.setPlaytime",{minutesPassed=1.5}),"nonnegative","fractional playtime rejected")
    H.clientSession()
    H.fails(H.dispatch("world.setPlaytime",{minutesPassed=1}),"only the host","client cannot edit world playtime")
end
