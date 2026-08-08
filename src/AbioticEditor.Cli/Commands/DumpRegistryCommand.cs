using System.CommandLine;
using AbioticEditor.Core.Assets;

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

        var cmd = new Command("dump-registry",
            "Extract the game's data tables into a bundled registry JSON (maintainer tool; needs the game installed).");
        cmd.Options.Add(outOpt);
        cmd.Options.Add(gameDirOpt);
        cmd.Options.Add(gameVersionOpt);
        cmd.Options.Add(cultureOpt);
        cmd.Options.Add(allCulturesOpt);
        cmd.SetAction(parseResult => Cli.Run(() => parseResult.GetValue(allCulturesOpt)
            ? DumpEveryCulture(
                parseResult.GetValue(outOpt),
                parseResult.GetValue(gameDirOpt),
                parseResult.GetValue(gameVersionOpt),
                parseResult.GetValue(quiet))
            : Dump(
                parseResult.GetValue(outOpt),
                parseResult.GetValue(gameDirOpt),
                parseResult.GetValue(gameVersionOpt),
                parseResult.GetValue(cultureOpt),
                parseResult.GetValue(quiet))));
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
    private static int DumpEveryCulture(string? output, string? gameDir, string? gameVersion, bool quiet)
    {
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? "registry" : output);
        Directory.CreateDirectory(directory);

        IReadOnlyList<string> cultures;
        using (var probe = RequireProvider())
        {
            cultures = probe.DiscoverAvailableCultures();
        }
        Cli.Info(quiet, $"The game ships text for: {string.Join(", ", cultures)}");

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

    private static int Dump(string? output, string? gameDir, string? gameVersion, string? culture, bool quiet)
    {
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var outPath = string.IsNullOrWhiteSpace(output)
            ? Path.GetFullPath(GameDataRegistry.FileNameFor(culture))
            : Path.GetFullPath(output);

        // Base game only: the bundled registry must stay clean and reproducible, so it never
        // picks up content from any mods installed on the maintainer's machine.
        using var provider = RequireProvider(culture);

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
}
