using System.CommandLine;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>dump-registry</c> - a maintainer command that mounts a real game install and writes a
/// <see cref="GameDataRegistry"/> JSON. The result is bundled in the editor's <c>assets/registry/</c>
/// so the catalogs work with no game installed. Re-run it per game patch and commit the new file.
/// </summary>
internal static class DumpRegistryCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var outOpt = new Option<string?>("--output", "-o")
        {
            Description = $"Output path for the registry JSON (default: ./{GameDataRegistry.RegistryFileName}).",
        };
        var gameDirOpt = new Option<string?>("--game-dir")
        {
            Description = "Game install folder to read from (default: auto-detect via Steam / ABIOTIC_GAME_DIR).",
        };
        var gameVersionOpt = new Option<string?>("--game-version")
        {
            Description = "Game build string to stamp into the registry (informational, e.g. 1.0.3).",
        };

        var cultureOpt = new Option<string?>("--culture")
        {
            Description = "Game culture to read display text in (e.g. ru). Default: the game's own default.",
        };
        var allCulturesOpt = new Option<bool>("--all-cultures")
        {
            Description = "Write one registry per language the game ships, into the --output folder.",
        };
        var learningOnlyOpt = new Option<bool>("--recipe-learning-only")
        {
            Description = "Refresh only recipe-learning metadata in compatible existing registry files, preserving every other field.",
        };

        var cmd = new Command("dump-registry",
            "Extract the game's data tables into a bundled registry JSON (maintainer tool; needs the game installed).");
        cmd.Options.Add(outOpt);
        cmd.Options.Add(gameDirOpt);
        cmd.Options.Add(gameVersionOpt);
        cmd.Options.Add(cultureOpt);
        cmd.Options.Add(allCulturesOpt);
        cmd.Options.Add(learningOnlyOpt);
        cmd.SetAction(parseResult => Cli.Run(() => parseResult.GetValue(allCulturesOpt)
            ? DumpEveryCulture(
                parseResult.GetValue(outOpt),
                parseResult.GetValue(gameDirOpt),
                parseResult.GetValue(gameVersionOpt),
                parseResult.GetValue(quiet),
                parseResult.GetValue(learningOnlyOpt))
            : Dump(
                parseResult.GetValue(outOpt),
                parseResult.GetValue(gameDirOpt),
                parseResult.GetValue(gameVersionOpt),
                parseResult.GetValue(cultureOpt),
                parseResult.GetValue(quiet),
                parseResult.GetValue(learningOnlyOpt))));
        return cmd;
    }

    /// <summary>
    /// Writes one registry per language the installed game ships, so a player with no game
    /// installed still reads item and story text in their own language.
    /// </summary>
    /// <remarks>
    /// Each culture needs its own mount: the game's translated strings are applied to the loaded
    /// tables at mount time, so one provider cannot produce two languages.
    /// </remarks>
    private static int DumpEveryCulture(string? output, string? gameDir, string? gameVersion, bool quiet, bool learningOnly)
    {
        ValidateLearningOptions(learningOnly, gameVersion);
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? "registry" : output);
        if (learningOnly) Cli.RequireDirectory(directory, "existing registry folder");
        else Directory.CreateDirectory(directory);

        IReadOnlyList<string> cultures;
        using (var probe = RequireProvider())
        {
            cultures = probe.DiscoverAvailableCultures();
        }
        Cli.Info(quiet, $"The game ships text for: {string.Join(", ", cultures)}");

        if (learningOnly)
        {
            var targets = cultures.Prepend<string?>(null).Select(culture =>
                (Culture: culture, Path: Cli.RequireFile(Path.Combine(directory, GameDataRegistry.FileNameFor(culture)), "existing registry"))).ToArray();
            var updates = new List<(string Path, string Json)>();
            foreach (var target in targets)
            {
                using var provider = RequireProvider(target.Culture);
                updates.Add((target.Path, PrepareLearningUpdate(target.Path, target.Culture, provider)));
            }
            // Every target is checked before the first existing registry is changed.
            foreach (var update in updates) File.WriteAllText(update.Path, update.Json);
            Cli.Info(quiet, $"Updated recipe-learning metadata in {updates.Count} existing registry file(s) -> {directory}");
            return Cli.Ok;
        }

        // The default (no culture asked for) is written as plain registry.json, which is what
        // every host falls back to when the player's own language did not ship.
        var written = 0;
        foreach (var culture in cultures.Prepend<string?>(null))
        {
            using var provider = RequireProvider(culture);
            var registry = GameDataRegistry.BuildFromInstall(provider, gameVersion, culture);
            var path = Path.Combine(directory, GameDataRegistry.FileNameFor(culture));
            registry.Save(path);
            written++;
            Cli.Info(quiet, $"  {Path.GetFileName(path)} - {registry.Items?.Count ?? 0} item(s)");
        }

        Cli.Info(quiet, $"Wrote {written} registry file(s) -> {directory}");
        Cli.Info(quiet, "Copy them to assets/registry/ and commit to bundle them with the editor.");
        return Cli.Ok;
    }

    private static GameAssetProvider RequireProvider(string? culture = null)
    {
        // Base game only, for the same reason the single-culture dump excludes mods.
        var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false, culture: culture)
            ?? throw new CliUserErrorException(
                "no Abiotic Factor install found. Pass --game-dir <folder> or set ABIOTIC_GAME_DIR.");
        if (!provider.HasMappings)
        {
            provider.Dispose();
            throw new CliUserErrorException(
                "the game was found but Mappings.usmap is missing, so its data tables can't be read. "
                + "Keep Mappings.usmap next to the editor or import one.");
        }
        return provider;
    }

    private static int Dump(string? output, string? gameDir, string? gameVersion, string? culture, bool quiet, bool learningOnly)
    {
        ValidateLearningOptions(learningOnly, gameVersion);
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var outPath = string.IsNullOrWhiteSpace(output)
            ? Path.GetFullPath(GameDataRegistry.FileNameFor(culture))
            : Path.GetFullPath(output);
        if (learningOnly) Cli.RequireFile(outPath, "existing registry");

        // Base game only: the bundled registry must stay clean and reproducible, so it never
        // picks up content from any mods installed on the maintainer's machine.
        using var provider = RequireProvider(culture);

        if (learningOnly)
        {
            var json = PrepareLearningUpdate(outPath, culture, provider);
            File.WriteAllText(outPath, json);
            Cli.Info(quiet, $"Updated recipe-learning metadata -> {outPath}");
            return Cli.Ok;
        }

        var registry = GameDataRegistry.BuildFromInstall(provider, gameVersion, culture);
        registry.Save(outPath);

        Cli.Info(quiet, $"Wrote registry -> {outPath}");
        Cli.Info(quiet, $"  schema v{registry.SchemaVersion}"
            + (registry.GameVersion is { } v ? $", game {v}" : "")
            + (registry.Culture is { } c ? $", culture {c}" : "")
            + $", {registry.Items?.Count ?? 0} item(s).");
        Cli.Info(quiet, "Copy it to assets/registry/ and commit to bundle it with the editor.");
        return Cli.Ok;
    }

    private static void ValidateLearningOptions(bool learningOnly, string? gameVersion)
    {
        if (learningOnly && !string.IsNullOrWhiteSpace(gameVersion))
            throw new CliUserErrorException("--game-version cannot be combined with --recipe-learning-only; existing registry stamps are preserved.");
    }

    private static string PrepareLearningUpdate(string path, string? culture, GameAssetProvider provider)
    {
        var recipes = RecipeCatalog.LoadInfosFrom(provider);
        var learning = GameDataRegistry.BuildRecipeLearning(provider)
            ?? throw new CliUserErrorException("The installed game's recipe-learning data could not be read; existing registries were not changed.");
        try { return RecipeLearningRegistryUpdater.Merge(File.ReadAllText(path), recipes, learning, culture); }
        catch (InvalidDataException ex) { throw new CliUserErrorException($"Cannot update '{path}': {ex.Message}", ex); }
    }
}
