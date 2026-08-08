using System.CommandLine;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>dump-icons</c> - a maintainer command that mounts a real game install and writes every
/// item icon out as a PNG. The result is bundled in the editor's <c>assets/icons/</c> folder so
/// the browser build can show real item pictures.
/// </summary>
/// <remarks>
/// The desktop app extracts these from the installed game on demand and needs no dump. The
/// browser cannot: mounting the paks in a tab is not possible (the IoStore container alone is
/// several gigabytes against a much smaller WebAssembly heap), so the pictures have to be
/// extracted ahead of time and shipped. Icons are language-independent artwork - only the names
/// beside them are translated, and those live in the registry - so ONE dump covers every
/// language.
/// </remarks>
public static class DumpIconsCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var outOpt = new Option<string?>("--output", "-o")
        {
            Description = "Folder to write the icon PNGs into (default: ./icons).",
        };
        var gameDirOpt = new Option<string?>("--game-dir")
        {
            Description = "Game install folder to read from (default: auto-detect via Steam / ABIOTIC_GAME_DIR).",
        };

        var cmd = new Command("dump-icons",
            "Extract every item icon as a PNG for the browser build (maintainer tool; needs the game installed).");
        cmd.Options.Add(outOpt);
        cmd.Options.Add(gameDirOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Dump(
            parseResult.GetValue(outOpt),
            parseResult.GetValue(gameDirOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Dump(string? output, string? gameDir, bool quiet)
    {
        if (!string.IsNullOrWhiteSpace(gameDir))
        {
            AfInstallLocator.OverrideInstallRoot = Cli.RequireDirectory(gameDir, "game folder");
        }

        var outDir = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? "icons" : output);
        Directory.CreateDirectory(outDir);

        // Base game only, for the same reason the registry dump excludes mods: what ships with
        // the editor has to be reproducible and free of whatever the maintainer has installed.
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is null)
        {
            throw new CliUserErrorException(
                "no Abiotic Factor install found. Pass --game-dir <folder> or set ABIOTIC_GAME_DIR.");
        }
        if (!provider.HasMappings)
        {
            throw new CliUserErrorException(
                "the game was found but Mappings.usmap is missing, so its icons can't be read. "
                + "Keep Mappings.usmap next to the editor or import one.");
        }

        var catalog = ItemCatalog.LoadFrom(provider);
        var written = 0;
        var skipped = 0;
        var failed = new List<string>();

        foreach (var entry in catalog.Entries.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(entry.IconAssetPath)) { skipped++; continue; }

            try
            {
                var raw = provider.ExtractTextureByGameRef(entry.IconAssetPath);
                // Colorize applies the same per-item tinting the app shows at run time, so the
                // shipped picture matches what the desktop draws rather than a grey base texture.
                var cached = raw is null ? null : IconColorizer.Colorize(raw, entry);
                if (cached is null || !File.Exists(cached)) { failed.Add(entry.Id); continue; }

                // Lower-cased on purpose. The same item is spelled differently in different
                // places (a save says "Bandage", the data-table row is "bandage"), and the web
                // server that serves these does care about the difference even though Windows
                // does not. One agreed spelling on both sides is what stops those items 404ing.
                File.Copy(cached, Path.Combine(outDir, entry.Id.ToLowerInvariant() + ".png"), overwrite: true);
                written++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                failed.Add(entry.Id);
            }
        }

        Cli.Info(quiet, $"Wrote {written} icon(s) -> {outDir}");
        if (skipped > 0) Cli.Info(quiet, $"  {skipped} item(s) have no icon in the game data.");
        if (failed.Count > 0)
        {
            // Named, not just counted: a texture format this decoder cannot read is worth
            // knowing about rather than silently shipping a gap.
            Cli.Info(quiet, $"  {failed.Count} icon(s) could not be decoded: {string.Join(", ", failed.Take(10))}"
                + (failed.Count > 10 ? ", ..." : string.Empty));
        }
        Cli.Info(quiet, "Copy the folder to assets/icons/ and commit to bundle it with the browser build.");
        return 0;
    }
}
