using System.CommandLine;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>dump-art</c> - a maintainer command that mounts a real game install and writes out every
/// picture the editor shows that is NOT an item icon: skill icons, trader portraits, chapter
/// cards, sector maps, creature and pet portraits, appearance previews and the logo. The result
/// is bundled in the editor's <c>assets/art/</c> folder so the browser build can draw them.
/// </summary>
/// <remarks>
/// Companion to <c>dump-icons</c>, which covers item pictures (keyed by item id). Everything here
/// is keyed by the game's own asset path instead, so a manifest ships alongside the files: a
/// screen consults it to decide whether to draw a picture or its fallback symbol, rather than
/// requesting one that is not there.
/// </remarks>
public static class DumpArtCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var outOpt = new Option<string?>("--output", "-o")
        {
            Description = "Folder to write the PNGs and manifest into (default: ./art).",
        };
        var gameDirOpt = new Option<string?>("--game-dir")
        {
            Description = "Game install folder to read from (default: auto-detect via Steam / ABIOTIC_GAME_DIR).",
        };

        var cmd = new Command("dump-art",
            "Extract the editor's non-item pictures as PNGs for the browser build (maintainer tool; needs the game installed).");
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

        var outDir = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? "art" : output);
        Directory.CreateDirectory(outDir);

        // Base game only, matching the registry and icon dumps: what ships with the editor has to
        // be reproducible and free of whatever the maintainer happens to have installed.
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        if (provider is null)
        {
            throw new CliUserErrorException(
                "no Abiotic Factor install found. Pass --game-dir <folder> or set ABIOTIC_GAME_DIR.");
        }
        if (!provider.HasMappings)
        {
            throw new CliUserErrorException(
                "the game was found but Mappings.usmap is missing, so its pictures can't be read. "
                + "Keep Mappings.usmap next to the editor or import one.");
        }

        var refs = CollectRefs(provider);
        var written = new List<string>();
        var failed = new List<string>();

        foreach (var gameRef in refs)
        {
            try
            {
                var cached = provider.ExtractTextureByGameRef(gameRef);
                if (cached is null || !File.Exists(cached)) { failed.Add(gameRef); continue; }

                File.Copy(cached, Path.Combine(outDir, BundledArt.FileNameFor(gameRef)), overwrite: true);
                written.Add(gameRef);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                failed.Add(gameRef);
            }
        }

        // Only what actually came out is listed, so a screen never asks for a picture the dump
        // could not decode.
        new BundledArt { Refs = written }.Save(Path.Combine(outDir, BundledArt.ManifestFileName));

        Cli.Info(quiet, $"Wrote {written.Count} picture(s) -> {outDir}");
        if (failed.Count > 0)
        {
            Cli.Info(quiet, $"  {failed.Count} could not be decoded: {string.Join(", ", failed.Take(10))}"
                + (failed.Count > 10 ? ", ..." : string.Empty));
        }
        Cli.Info(quiet, "Copy the folder to assets/art/ and commit to bundle it with the browser build.");
        return 0;
    }

    /// <summary>
    /// Every asset path the editor's screens can ask for a picture of. Each source is read
    /// defensively: a table this game build renamed costs its own pictures, not the whole dump.
    /// </summary>
    private static List<string> CollectRefs(GameAssetProvider provider)
    {
        var refs = new SortedSet<string>(StringComparer.Ordinal);

        void Add(string? gameRef)
        {
            if (!string.IsNullOrWhiteSpace(gameRef)) refs.Add(gameRef);
        }

        void From(Action collect)
        {
            try { collect(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or KeyNotFoundException or NotSupportedException)
            {
                // Skipped on purpose; the manifest simply will not list this group.
            }
        }

        // The shell logo, which the desktop app draws from the install on every screen.
        Add("AbioticFactor/Content/Textures/GUI/Inventory/T_ABF_Logo_1024");
        Add("AbioticFactor/Content/Textures/GUI/Logos/ABF-Full-Color-1024w");

        From(() => { foreach (var skill in SkillCatalog.LoadFrom(provider)) Add(skill.IconAssetPath); });
        From(() => { foreach (var trader in TraderCatalog.LoadFrom(provider)) Add(trader.ImageAssetPath); });
        From(() =>
        {
            foreach (var table in CustomizationCatalog.LoadFrom(provider).Values)
            {
                foreach (var option in table) Add(option.IconAssetPath);
            }
        });
        From(() => { foreach (var map in SectorMapCatalog.LoadFrom(provider)) Add(map.TexturePath); });
        From(() => { foreach (var chapter in StoryProgressionCatalog.Chapters) Add(chapter.CardArt); });
        From(() =>
        {
            foreach (var creature in ContainmentCreatureCatalog.Containable)
            {
                foreach (var texture in ContainmentCreatureCatalog.TextureRefs(creature.Row)) Add(texture);
            }
        });
        From(() =>
        {
            // Both the curated list and whatever the game's own pet tables add, so a pet the
            // curated list has not caught up with still gets its portrait shipped.
            PetCatalog.ApplyGameData(PetGameData.TryLoadFrom(provider));
            foreach (var pet in PetCatalog.BuildVariants(provider))
            {
                foreach (var texture in PetCatalog.CompendiumTextureRefs(pet.ShortClass)) Add(texture);
            }
        });

        return refs.ToList();
    }
}
