-- Leyak Containment Units (areas/containment.lua). Fixtures are built from the game's own class
-- dump (Deployed_LeyakContainment_C, LeyakDirectorComponent_C / KrasueDirectorComponent_C) - see
-- docs/PROGRESS.md round 77 for the property/function names this pins down.
return function(H)
    H.hostSession()

    -- "EDynamicProperty::XP"-shaped enum key stand-in, matching companions.lua's own comment
    -- about how the real enum stringifies - not needed here, but the same style of small helper
    -- is used below for the director lookup.
    local function director(id)
        return H.object(id .. "DirectorComponent", { ActiveLeyakContainmentID = "" }, {
            SetLeyakContainmentID = function(self, newId) self.ActiveLeyakContainmentID = newId end,
        })
    end
    local leyakDirector = director("Leyak")
    local krasueDirector = director("Krasue")
    local aiDirector = H.object("AI_Director_C", { LeyakDirectorComponent = leyakDirector, KrasueDirectorComponent = krasueDirector }, {})
    H.gameMode.AI_Director = aiDirector

    -- Field/function names straight off the class dump: ContainsLeyak (FName, "None" when
    -- empty), "Stability Level" (a literal space, FIntProperty, read-only here), MaxStability,
    -- SpawnedAssetID (FString, inherited from AbioticDeployed_ParentBP_C), TrapLeyak(RowName) -
    -- ONE reflected input, ServerUpdateStabilityLevel(Value, RowName), "Free Leyak" (also a
    -- literal space) and OnRep_ContainsLeyak.
    local function unit(occupant, assetId)
        return H.world.add(H.object("Deployed_LeyakContainment_C", {
            ContainsLeyak = H.fname(occupant or "None"),
            ["Stability Level"] = 40,
            MaxStability = 100,
            DeployableDestroyed = false,
            SpawnedAssetID = H.fstring(assetId),
        }, {
            TrapLeyak = function(self, rowName) self.ContainsLeyak = rowName end,
            ["Free Leyak"] = function(self) self.ContainsLeyak = H.fname("None") end,
            ServerUpdateStabilityLevel = function(self, value, rowName) self["Stability Level"] = value end,
            OnRep_ContainsLeyak = function() end,
            K2_GetActorLocation = function() return H.vector(10, 20, 30) end,
        }))
    end
    local unitA = unit(nil, "UnitA")
    local unitB = unit("Krasue", "UnitB")

    -- containment.list: an empty unit and an occupied one, both readable.
    local list = H.ok(H.dispatch("containment.list"), "containment.list")
    H.eq(#list.units, 2, "two units")
    H.eq(list.units[1].creature, nil, "unit A starts empty")
    H.eq(list.units[1].stability, 40, "stability level read (space in the name)")
    H.eq(list.units[2].creature, "Krasue", "unit B starts holding a Krasue")
    local idA, idB = list.units[1].id, list.units[2].id

    -- assign: trap a Leyak into the empty unit.
    H.ok(H.dispatch("containment.set", { action = "assign", unitId = idA, creature = "Leyak" }), "assign Leyak")
    H.eq(H.calls(unitA, "TrapLeyak"), 1, "TrapLeyak called once")
    H.eq(H.field(unitA, "ContainsLeyak"):ToString(), "Leyak", "unit A now holds a Leyak")
    H.eq(H.field(leyakDirector, "ActiveLeyakContainmentID"), "UnitA", "Leyak director points at unit A")

    -- release: free the Krasue from unit B.
    H.ok(H.dispatch("containment.set", { action = "release", creature = "Krasue" }), "release Krasue")
    H.eq(H.calls(unitB, "Free Leyak"), 1, "Free Leyak called once (with its receiver, not stranded)")
    H.eq(H.field(unitB, "ContainsLeyak"):ToString(), "None", "unit B emptied")
    H.eq(H.field(krasueDirector, "ActiveLeyakContainmentID"), "", "Krasue director cleared")

    -- swap: put a Krasue back in B, then swap A (Leyak) and B (Krasue).
    H.ok(H.dispatch("containment.set", { action = "assign", unitId = idB, creature = "Krasue" }), "reassign Krasue")
    H.ok(H.dispatch("containment.set", { action = "swap", unitIdA = idA, unitIdB = idB }), "swap units")
    H.eq(H.field(unitA, "ContainsLeyak"):ToString(), "Krasue", "unit A now holds the Krasue")
    H.eq(H.field(unitB, "ContainsLeyak"):ToString(), "Leyak", "unit B now holds the Leyak")

    -- Unknown unit id: player-safe failure, not a Lua error.
    H.fails(H.dispatch("containment.set", { action = "assign", unitId = "no-such-unit", creature = "Leyak" }),
        "not found", "unknown containment unit id fails cleanly")
    H.fails(H.dispatch("containment.set", { action = "swap", unitIdA = "nope", unitIdB = "also-nope" }),
        "not found", "unknown swap ids fail cleanly")

    -- Non-host refusal.
    H.clientSession()
    H.world.add(unitA)
    H.fails(H.dispatch("containment.set", { action = "release", creature = "Krasue" }), "only the host", "client cannot edit containment")
end
