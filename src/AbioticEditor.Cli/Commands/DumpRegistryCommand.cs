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

        var cmd = new Command("dump-registry",
            "Extract the game's data tables into a bundled registry JSON (maintainer tool; needs the game installed).");
        cmd.Options.Add(outOpt);
        cmd.Options.Add(gameDirOpt);
        cmd.Options.Add(gameVersionOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Dump(
            parseResult.GetValue(outOpt),
            parseResult.GetValue(gameDirOpt),
            parseResult.GetValue(gameVersionOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Dump(string? output, string? gameDir, string? gameVersion, bool quiet)
    {
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var outPath = string.IsNullOrWhiteSpace(output)
            ? Path.GetFullPath(GameDataRegistry.RegistryFileName)
            : Path.GetFullPath(output);

        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null)
        {
            throw new CliUserErrorException(
                "no Abiotic Factor install found. Pass --game-dir <folder> or set ABIOTIC_GAME_DIR.");
        }
        if (!provider.HasMappings)
        {
            throw new CliUserErrorException(
                "the game was found but Mappings.usmap is missing, so its data tables can't be read. "
                + "Keep Mappings.usmap next to the editor or import one.");
        }

        var registry = GameDataRegistry.BuildFromInstall(provider, gameVersion);
        registry.Save(outPath);

        Cli.Info(quiet, $"Wrote registry -> {outPath}");
        Cli.Info(quiet, $"  schema v{registry.SchemaVersion}"
            + (registry.GameVersion is { } v ? $", game {v}" : "")
            + $", {registry.Items?.Count ?? 0} item(s).");
        Cli.Info(quiet, "Copy it to assets/registry/ and commit to bundle it with the editor.");
        return Cli.Ok;
    }
}
