using System.CommandLine;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>pet</c> - inspect and edit tamed companions in a world save's <c>PetNPC</c> map:
/// rename, heal / down / revive / kill, set level (XP), upgrade or downgrade the creature
/// type (the game's "mutation"), and delete from the world. Every write keeps a <c>.bak</c>.
/// Pet editing lives entirely in AbioticEditor.Core, so this writes byte-identical output to
/// the desktop app.
/// </summary>
internal static class PetCommands
{
    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("pet",
            "Inspect and edit tamed pets in a world save (rename, heal/down/revive, level, upgrade, delete).");
        cmd.Subcommands.Add(BuildList(quiet));
        cmd.Subcommands.Add(BuildRename(quiet));
        cmd.Subcommands.Add(BuildLifeAction("revive", "Revive a dead/downed pet (clears dead, restores ~25% health).", quiet));
        cmd.Subcommands.Add(BuildLifeAction("heal", "Heal a pet to full (its strongest limb value).", quiet));
        cmd.Subcommands.Add(BuildLifeAction("down", "Down a pet (set all limb health to 0).", quiet));
        cmd.Subcommands.Add(BuildLifeAction("kill", "Mark a pet as dead.", quiet));
        cmd.Subcommands.Add(BuildDelete(quiet));
        cmd.Subcommands.Add(BuildLevel(quiet));
        cmd.Subcommands.Add(BuildUpgrade(quiet));
        cmd.Subcommands.Add(BuildVariants(quiet));
        cmd.Subcommands.Add(BuildHotbar(quiet));
        cmd.Subcommands.Add(BuildSend(quiet));
        cmd.Subcommands.Add(BuildGrab(quiet));
        return cmd;
    }

    // ---------- pet hotbar <PlayerSave> ----------

    private static Command BuildHotbar(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("playersave") { Description = "Path to a Player_*.sav file." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };

        var cmd = new Command("hotbar", "List the pets a player is carrying (hotbar / Companion slot / backpack).");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parse => Cli.Run(() => Hotbar(parse.GetValue(saveArg), parse.GetValue(jsonOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Hotbar(string? file, bool json, bool quiet)
    {
        var path = Cli.RequireFile(file, "player save");
        var pets = AbioticEditor.Core.PlayerSaves.PlayerSaveReader.ReadFromFile(path).CarriedPets;

        if (json)
        {
            Cli.WriteJson(pets.Select((p, i) => new
            {
                index = i, slot = p.Slot.ToString(), p.Index, p.ItemRow, p.Variant, name = p.Name,
                p.Health, p.MaxHealth, level = p.Level, p.Xp, companion = p.IsCompanionSlot,
            }));
            return Cli.Ok;
        }
        if (pets.Count == 0) { Cli.Info(quiet, "This player is carrying no pets."); return Cli.Ok; }

        Console.WriteLine($"Carried pets in {Path.GetFileName(path)} ({pets.Count}):");
        var idx = 0;
        foreach (var p in pets)
        {
            var where = p.IsCompanionSlot ? "Companion" : $"{p.Slot}[{p.Index}]";
            Console.WriteLine($"  #{idx,-2} {p.DisplayName,-18} {p.Variant,-16} {where,-12} Lv{p.Level,-2} hp {p.Health:0}/{p.MaxHealth:0}");
            idx++;
        }
        Console.WriteLine();
        Console.WriteLine("Move to a world: pet grab <playersave> <#index> --to <worldsave> [--x .. --y .. --z ..]");
        return Cli.Ok;
    }

    // ---------- pet send <WorldSave> <pet> --to <PlayerSave> ----------

    private static Command BuildSend(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("worldsave") { Description = "Path to the WorldSave_*.sav holding the pet." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID." };
        var toOpt = new Option<string>("--to") { Description = "Target Player_*.sav.", Required = true };
        var companionOpt = new Option<bool>("--companion") { Description = "Place in the Companion slot (default)." };
        var hotbarOpt = new Option<bool>("--hotbar") { Description = "Place in the first free hotbar slot instead." };

        var cmd = new Command("send", "Move a world pet into a player's inventory (Companion or hotbar).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(companionOpt);
        cmd.Options.Add(hotbarOpt);
        cmd.SetAction(parse => Cli.Run(() => Send(
            parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(toOpt),
            parse.GetValue(hotbarOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Send(string? worldFile, string? petRef, string? playerFile, bool hotbar, bool quiet)
    {
        var worldPath = Cli.RequireFile(worldFile, "world save");
        var playerPath = Cli.RequireFile(playerFile, "player save");

        var world = WorldSaveReader.ReadFromFile(worldPath);
        var player = AbioticEditor.Core.PlayerSaves.PlayerSaveReader.ReadFromFile(playerPath);
        var pet = ResolvePet(world, petRef);

        var kind = hotbar ? AbioticEditor.Core.PlayerSaves.PetSlotKind.Hotbar : AbioticEditor.Core.PlayerSaves.PetSlotKind.Equipment;
        var result = PetTransfer.WorldToPlayer(world, pet.Id, player, kind);
        if (!result.Ok) throw new CliUserErrorException(result.Message);

        WorldSaveWriter.WriteToFile(world, worldPath);
        AbioticEditor.Core.PlayerSaves.PlayerSaveWriter.WriteToFile(player, playerPath);
        Cli.Info(quiet, $"{result.Message} Wrote {Path.GetFileName(worldPath)} and {Path.GetFileName(playerPath)} (each kept a .bak).");
        return Cli.Ok;
    }

    // ---------- pet grab <PlayerSave> <#index> --to <WorldSave> ----------

    private static Command BuildGrab(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("playersave") { Description = "Path to the Player_*.sav holding the carried pet." };
        var petArg = new Argument<string>("pet") { Description = "Carried-pet #index (see 'pet hotbar')." };
        var toOpt = new Option<string>("--to") { Description = "Target WorldSave_*.sav.", Required = true };
        var xOpt = new Option<double>("--x") { Description = "World X (default 0)." };
        var yOpt = new Option<double>("--y") { Description = "World Y (default 0)." };
        var zOpt = new Option<double>("--z") { Description = "World Z (default 0)." };

        var cmd = new Command("grab", "Move a carried pet out of a player's inventory into the world.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(xOpt);
        cmd.Options.Add(yOpt);
        cmd.Options.Add(zOpt);
        cmd.SetAction(parse => Cli.Run(() => Grab(
            parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(toOpt),
            parse.GetValue(xOpt), parse.GetValue(yOpt), parse.GetValue(zOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Grab(string? playerFile, string? petRef, string? worldFile, double x, double y, double z, bool quiet)
    {
        var playerPath = Cli.RequireFile(playerFile, "player save");
        var worldPath = Cli.RequireFile(worldFile, "world save");

        var player = AbioticEditor.Core.PlayerSaves.PlayerSaveReader.ReadFromFile(playerPath);
        var world = WorldSaveReader.ReadFromFile(worldPath);

        var pets = player.CarriedPets;
        if (pets.Count == 0) throw new CliUserErrorException("this player is carrying no pets.");
        if (string.IsNullOrWhiteSpace(petRef) || !petRef.StartsWith('#') || !int.TryParse(petRef[1..], out var i) || i < 0 || i >= pets.Count)
        {
            throw new CliUserErrorException($"use a carried-pet #index 0..{pets.Count - 1} (see 'pet hotbar').");
        }
        var carried = pets[i];

        var result = PetTransfer.PlayerToWorld(player, carried.Slot, carried.Index, world, x, y, z);
        if (!result.Ok) throw new CliUserErrorException(result.Message);

        AbioticEditor.Core.PlayerSaves.PlayerSaveWriter.WriteToFile(player, playerPath);
        WorldSaveWriter.WriteToFile(world, worldPath);
        Cli.Info(quiet, $"{result.Message} Wrote {Path.GetFileName(playerPath)} and {Path.GetFileName(worldPath)} (each kept a .bak).");
        return Cli.Ok;
    }

    // ---------- pet list <save> ----------

    private static Command BuildList(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };

        var cmd = new Command("list", "List the pets in a world save.");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parse => Cli.Run(() => List(parse.GetValue(saveArg), parse.GetValue(jsonOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int List(string? file, bool json, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var pets = WorldSaveReader.ReadFromFile(path).Pets;

        if (json)
        {
            Cli.WriteJson(pets.Select((p, i) => new
            {
                index = i,
                p.Id,
                name = p.CustomName,
                p.DisplayName,
                family = PetCatalog.Categorize(p.NpcClass).ToString(),
                friendly = PetCatalog.FriendlyName(p.NpcClass),
                shortClass = p.ShortClass,
                status = PetHealth.Status(p).ToString(),
                level = p.Level,
                xp = p.Xp,
                totalHealth = p.TotalHealth,
            }));
            return Cli.Ok;
        }

        if (pets.Count == 0)
        {
            Cli.Info(quiet, "No pets in this save.");
            return Cli.Ok;
        }

        Console.WriteLine($"Pets in {Path.GetFileName(path)} ({pets.Count}):");
        var idx = 0;
        foreach (var p in pets)
        {
            Console.WriteLine($"  #{idx,-2} {p.DisplayName,-18} {PetCatalog.FriendlyName(p.NpcClass),-18} "
                + $"{PetHealth.Status(p),-8} Lv{p.Level,-2} (xp {p.Xp})  {p.Id}");
            idx++;
        }
        Console.WriteLine();
        Console.WriteLine("Edit with: pet rename|heal|down|revive|kill|level|upgrade|delete <save> <#index|name|guid> ...");
        return Cli.Ok;
    }

    // ---------- pet rename <save> <pet> <name> ----------

    private static Command BuildRename(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID (see 'pet list')." };
        var nameArg = new Argument<string>("name") { Description = "New pet name (empty to clear)." };

        var cmd = new Command("rename", "Rename a pet.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.Arguments.Add(nameArg);
        cmd.SetAction(parse => Cli.Run(() => Edit(
            parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(quiet),
            pet => pet with { CustomName = string.IsNullOrEmpty(parse.GetValue(nameArg)) ? null : parse.GetValue(nameArg) },
            p => $"renamed to '{p.CustomName ?? "(unnamed)"}'")));
        return cmd;
    }

    // ---------- pet revive|heal|down|kill <save> <pet> ----------

    private static Command BuildLifeAction(string verb, string help, Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID (see 'pet list')." };

        var cmd = new Command(verb, help);
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.SetAction(parse => Cli.Run(() => Edit(
            parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(quiet),
            pet => verb switch
            {
                "revive" => pet with { IsDead = false, LimbHealth = PetHealth.RevivedLimbs(pet) },
                "heal" => pet with { LimbHealth = PetHealth.HealedLimbs(pet) },
                "down" => pet with { LimbHealth = PetHealth.DownedLimbs(pet) },
                "kill" => pet with { IsDead = true },
                _ => pet,
            },
            p => $"{verb}d ({PetHealth.Status(p)}, total health {p.TotalHealth:0})")));
        return cmd;
    }

    // ---------- pet level <save> <pet> <0-20> ----------

    private static Command BuildLevel(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID." };
        var levelArg = new Argument<int>("level") { Description = $"Target level (0-{PetCatalog.MaxLevel})." };

        var cmd = new Command("level", "Set a pet's level (writes the matching XP).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.Arguments.Add(levelArg);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var level = parse.GetValue(levelArg);
            if (level < 0 || level > PetCatalog.MaxLevel)
            {
                throw new CliUserErrorException($"level must be 0..{PetCatalog.MaxLevel}.");
            }
            return Edit(
                parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(quiet),
                pet => pet with { Xp = PetCatalog.XpForLevel(level) },
                p => $"set to level {p.Level} (xp {p.Xp})");
        }));
        return cmd;
    }

    // ---------- pet upgrade <save> <pet> <variant> ----------

    private static Command BuildUpgrade(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID." };
        var variantArg = new Argument<string>("variant") { Description = "Target variant: friendly name, short class, or full class path (see 'pet variants')." };

        var cmd = new Command("upgrade", "Change a pet's creature type (upgrade/downgrade/mutate).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.Arguments.Add(variantArg);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var classPath = ResolveVariantClass(parse.GetValue(variantArg));
            return Edit(
                parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(quiet),
                pet => pet with { NpcClass = classPath },
                p => $"changed type to {PetCatalog.FriendlyName(p.NpcClass)} ({p.ShortClass})");
        }));
        return cmd;
    }

    // ---------- pet delete <save> <pet> ----------

    private static Command BuildDelete(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var petArg = new Argument<string>("pet") { Description = "Pet #index, name, or GUID." };

        var cmd = new Command("delete", "Delete a pet from the world.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(petArg);
        cmd.SetAction(parse => Cli.Run(() => Delete(parse.GetValue(saveArg), parse.GetValue(petArg), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Delete(string? file, string? petRef, bool quiet)
    {
        var path = Cli.RequireFile(file, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        var pet = ResolvePet(data, petRef);

        if (!WorldSaveWriter.RemovePet(data, pet.Id))
        {
            throw new CliUserErrorException($"pet '{pet.Id}' could not be removed.");
        }
        WorldSaveWriter.WriteToFile(data, path);
        Cli.Info(quiet, $"Deleted pet '{pet.DisplayName}' ({pet.Id}). "
            + $"Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
        return Cli.Ok;
    }

    // ---------- pet variants ----------

    private static Command BuildVariants(Option<bool> quiet)
    {
        var categoryOpt = new Option<string?>("--category", "-c") { Description = "Filter by family: Pest, Peccary, Skink, Other." };
        var jsonOpt = new Option<bool>("--json") { Description = "Emit JSON." };

        var cmd = new Command("variants", "List the pet families and variant classes available for 'pet upgrade'.");
        cmd.Options.Add(categoryOpt);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parse => Cli.Run(() => Variants(parse.GetValue(categoryOpt), parse.GetValue(jsonOpt), parse.GetValue(quiet))));
        return cmd;
    }

    private static int Variants(string? category, bool json, bool quiet)
    {
        using var provider = TryCreateProvider();
        var variants = PetCatalog.BuildVariants(provider).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<PetCategory>(category, ignoreCase: true, out var cat))
            {
                throw new CliUserErrorException($"unknown family '{category}'. Known: Pest, Peccary, Skink, Other.");
            }
            variants = variants.Where(v => v.Category == cat);
        }
        var list = variants.ToList();

        if (json)
        {
            Cli.WriteJson(list.Select(v => new
            {
                v.FriendlyName, v.ShortClass, v.ClassPath, family = v.Category.ToString(), v.IsSummon, v.IsEditable,
            }));
            return Cli.Ok;
        }

        Cli.Info(quiet, provider is null
            ? "(game install not found - showing the built-in curated list)"
            : "(merged from the installed game's blueprints)");
        foreach (var grp in list.GroupBy(v => v.Category))
        {
            Console.WriteLine($"{grp.Key}:");
            foreach (var v in grp)
            {
                var note = v.IsSummon ? "  (summon - not editable)" : "";
                Console.WriteLine($"  {v.FriendlyName,-18} {v.ShortClass}{note}");
            }
        }
        return Cli.Ok;
    }

    // ---------- shared edit pipeline ----------

    private static int Edit(
        string? file, string? petRef, bool quiet,
        Func<WorldPet, WorldPet> mutate, Func<WorldPet, string> describe)
    {
        var path = Cli.RequireFile(file, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        var pet = ResolvePet(data, petRef);

        var updated = mutate(pet);
        WorldSaveWriter.ApplyPets(data, new[] { updated });
        WorldSaveWriter.WriteToFile(data, path);
        Cli.Info(quiet, $"Pet '{updated.DisplayName}' {describe(updated)}. "
            + $"Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
        return Cli.Ok;
    }

    private static WorldPet ResolvePet(WorldSaveData data, string? petRef)
    {
        var pets = data.Pets;
        if (pets.Count == 0)
        {
            throw new CliUserErrorException("this save has no pets.");
        }
        if (string.IsNullOrWhiteSpace(petRef))
        {
            throw new CliUserErrorException("missing pet reference (use a #index, name, or GUID from 'pet list').");
        }

        if (petRef.StartsWith('#') && int.TryParse(petRef[1..], out var index))
        {
            if (index < 0 || index >= pets.Count)
            {
                throw new CliUserErrorException($"pet index {index} out of range (0..{pets.Count - 1}).");
            }
            return pets[index];
        }

        var exact = pets.FirstOrDefault(p => string.Equals(p.Id, petRef, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var matches = pets.Where(p =>
            (p.CustomName is not null && p.CustomName.Contains(petRef, StringComparison.OrdinalIgnoreCase))
            || p.Id.Contains(petRef, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CliUserErrorException($"no pet matches '{petRef}'. Use 'pet list' to see #index, name, GUID."),
            _ => throw new CliUserErrorException($"'{petRef}' matches {matches.Count} pets; be more specific or use #index."),
        };
    }

    private static string ResolveVariantClass(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new CliUserErrorException("missing variant.");
        }
        // A full class path is accepted as-is.
        if (input.Contains('/'))
        {
            return input;
        }

        using var provider = TryCreateProvider();
        var variants = PetCatalog.BuildVariants(provider);
        var match = variants.FirstOrDefault(v =>
            string.Equals(v.ShortClass, input, StringComparison.OrdinalIgnoreCase)
            || string.Equals(v.FriendlyName, input, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new CliUserErrorException($"unknown variant '{input}'. See 'pet variants' for valid names.");
        }
        if (match.IsSummon)
        {
            throw new CliUserErrorException($"'{match.FriendlyName}' is an armor-set summon and cannot be assigned as a pet type.");
        }
        return match.ClassPath;
    }

    private static GameAssetProvider? TryCreateProvider()
    {
        try
        {
            return GameAssetProvider.CreateForLocalInstall();
        }
        catch
        {
            return null;
        }
    }
}
