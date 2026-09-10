-- A game sitting at its main menu (no save loaded yet) can already have a valid
-- MyPlayerCharacter on the local controller - a menu/default pawn, not a real in-world character.
-- vitals.get must fail the same way skills.get already does instead of handing back
-- plausible-looking default numbers, or the connect dialog's readiness check (which only used to
-- require vitals) and the live-connect page's stricter adoption (which also needs skills) end up
-- disagreeing about whether the game is actually ready. See ModeSelectDialog.razor's
-- TryConnectAsync and main.lua's hasLoadedWorldState for the two sides of this fix.
return function(H)
    H.world.reset()
    local menuPawn = H.object("Abiotic_PlayerCharacter_C", {
        CurrentHunger = 0, CurrentThirst = 0, CurrentSanity = 0, CurrentFatigue = 0, CurrentContinence = 0,
        CurrentMoney = 0, CurrentHealth_Head = 0, CurrentHealth_Torso = 0, CurrentHealth_LeftArm = 0,
        CurrentHealth_RightArm = 0, CurrentHealth_LeftLeg = 0, CurrentHealth_RightLeg = 0,
        -- No CharacterProgressionComponent: the real signal that no save/world is loaded yet -
        -- the same one skills.get already relies on via getProgressionComponent.
    })
    H.playerStates = {}
    H.playerController = H.object("Abiotic_PlayerController_C", { MyPlayerCharacter = menuPawn })
    H.world.add(menuPawn)

    H.fails(H.dispatch("vitals.get"), "no world loaded",
        "vitals.get at the main menu errors instead of returning defaults")
    H.fails(H.dispatch("skills.get"), "no CharacterProgressionComponent",
        "skills.get still fails the same way it already did")
end
