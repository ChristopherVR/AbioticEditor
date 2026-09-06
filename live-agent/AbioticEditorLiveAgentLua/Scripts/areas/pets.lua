-- ===== Tamed pets (PetNPC in the save; live actors are ordinary NPC_Base_ParentBP_C) =====
-- Round 76 research finding: NO general live path exists yet, and this module says so honestly
-- instead of guessing one.
--
-- A tamed pet is not a separate blueprint - it is the SAME NPC_Base_ParentBP_C actor npcs.list
-- already finds, just with a "tamed" runtime state. But the fields the save's WorldPet record
-- needs (Guid, CustomName_, NPCClass_, CurrentHealthMap_, DynamicProperties_/XP) are exposed
-- WILDLY inconsistently between creature families in the game's own class layout
-- (tests/AbioticEditor.Probes/LiveClassPropsProbe.cs, fragments "NPC_Monster_Pest.",
-- "NPC_Monster_Peccary.", "NPC_Peccary_Sow", "NPC_Skink_Basic", "NPC_Monster_WinterSprite"):
--   - NPC_Monster_Pest_C (and NPC_Skink_Basic_C, which inherits from it) directly exposes
--     PetName (FTextProperty), Guid (FStrProperty), FollowingOwner (FObjectProperty),
--     DynamicProperties (FArrayProperty), SanitizedName (FStrProperty) - no hash suffix.
--   - NPC_Monster_Peccary_C / NPC_Peccary_Sow_C expose NONE of those as their own properties.
--   - NPC_Monster_WinterSprite_C (the Lamogi family) exposes only a bare WasTamed bool.
-- So there is no single property this module could read across every tameable species to match
-- a live actor back to this world save's PetNPC GUID - Pest/Skink pets could theoretically be
-- matched by their own Guid property, but Peccary and Lamogi pets have no evidenced id at all,
-- and building a species-by-species matcher for three DIFFERENT partial mechanisms (one of them
-- with no id whatsoever) is exactly the kind of guess this project got burned by once already
-- (GetMyPlayerController) - worse here, since a wrong match could edit or delete the wrong pet.
--
-- Health is also a dead end for a write: the only function this probe found touching
-- CurrentHealthMap_-shaped data is GetCurrentHealthMap(), called ONLY by the game's own
-- UpdatePetToWorldSave (i.e. it is used to WRITE the world save FROM the live pet, not the other
-- way around) - no setter/apply-health-map counterpart was found anywhere. Species mutation
-- (upgrade/downgrade) would mean despawning and respawning the actor via the GameMode's
-- SpawnPet(Class, SpawnTransform, Guid, Name, Owner, DynamicProperties, Tamed) function - real,
-- but never exercised by any mod, and a wrong parameter here (especially SpawnTransform, an
-- FTransform this probe never saw constructed anywhere) risks duplicating or losing a pet.
--
-- So: pets.list responds honestly with available = false and no pets, list.set is not
-- registered at all, and the shared WorldPetsTab shows the reason instead of an empty list.
return function(ctx)
    ctx.handlers["pets.list"] = function(_, respond)
        -- Nothing here touches a live UObject except isHost() itself, but that call reaches into
        -- UEHelpers.GetWorld() and is only ever exercised elsewhere inside runOnGameThread - kept
        -- consistent with every other area rather than assuming it is safe off-thread.
        ctx.runOnGameThread(function()
            return {
                pets = { __forceArray = true },
                isHost = ctx.isHost(),
                available = false,
                reason = "Live pet editing isn't available yet - the game exposes tame/name/health "
                    .. "data inconsistently between creature families, with no safe way to match a "
                    .. "game pet back to this file's records. Edit pets in the save file instead.",
            }
        end, respond)
    end
end
