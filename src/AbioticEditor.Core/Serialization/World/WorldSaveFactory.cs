using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Options for creating a new world save folder from a blank template.</summary>
public sealed record CreateWorldOptions
{
    /// <summary>The folder name for the new world (becomes the directory name).</summary>
    public required string WorldName { get; init; }

    /// <summary>Parent directory under which <see cref="WorldName"/> will be created.</summary>
    public required string ParentDirectory { get; init; }

    /// <summary>Owner ids (SteamID64 or non-Steam account token) for players who should have an
    /// initial blank player save.</summary>
    public required IReadOnlyList<string> PlayerIds { get; init; }

    /// <summary>
    /// Sandbox difficulty preset: 1 = Casual, 2 = Normal, 3 = Survival, 4 = Nightmare.
    /// Controls GameDifficulty and related multipliers written to SandboxSettings.ini.
    /// </summary>
    public int GameDifficulty { get; init; } = 2;
}

/// <summary>
/// Builds a new world save folder from a blank <c>WorldSave_MetaData.sav</c> template.
/// The same template + reset pattern that <see cref="PlayerSaveFactory"/> uses for
/// player saves is applied here for world saves.
/// </summary>
public static class WorldSaveFactory
{
    static WorldSaveFactory()
    {
        AbioticSaveClasses.EnsureLoaded();
    }

    /// <summary>
    /// Creates a complete new world folder under <see cref="CreateWorldOptions.ParentDirectory"/>:
    /// <list type="bullet">
    ///   <item><c>WorldSave_MetaData.sav</c> - reset to a brand-new-game state.</item>
    ///   <item><c>PlayerData/Player_*.sav</c> - one blank player per <see cref="CreateWorldOptions.PlayerIds"/>.</item>
    ///   <item><c>SandboxSettings.ini</c> - difficulty-tuned sandbox settings.</item>
    /// </list>
    /// Returns the absolute path of the created world directory.
    /// </summary>
    /// <exception cref="IOException">
    /// Thrown when the target folder already exists and is not empty, or when any file write fails.
    /// </exception>
    public static string CreateWorldFolder(
        CreateWorldOptions options,
        byte[] metadataTemplate,
        byte[] playerTemplate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metadataTemplate);
        ArgumentNullException.ThrowIfNull(playerTemplate);

        var worldDir = Path.Combine(options.ParentDirectory, options.WorldName);
        if (Directory.Exists(worldDir) && Directory.EnumerateFileSystemEntries(worldDir).Any())
        {
            throw new IOException(
                $"A folder named '{options.WorldName}' already exists at that location and is not empty.");
        }

        Directory.CreateDirectory(worldDir);
        Diagnostics.EditorLog.Info("WorldFactory", $"Creating world '{options.WorldName}' at {worldDir}");

        WriteBlankMetadata(metadataTemplate, Path.Combine(worldDir, "WorldSave_MetaData.sav"));

        if (options.PlayerIds.Count > 0)
        {
            var playerDir = Path.Combine(worldDir, "PlayerData");
            Directory.CreateDirectory(playerDir);
            foreach (var id in options.PlayerIds)
            {
                PlayerSaveFactory.CreateFromTemplate(playerTemplate, playerDir, id);
                Diagnostics.EditorLog.Info("WorldFactory", $"  + Player_{id}.sav");
            }
        }

        WriteSandboxSettings(Path.Combine(worldDir, "SandboxSettings.ini"), options.GameDifficulty);

        Diagnostics.EditorLog.Info("WorldFactory",
            $"World '{options.WorldName}' created ({options.PlayerIds.Count} player(s), difficulty {options.GameDifficulty}).");
        return worldDir;
    }

    /// <summary>The embedded blank-region template's manifest resource name (see the .csproj).</summary>
    private const string BlankRegionResource = "blank-region-template.sav";

    /// <summary>
    /// Crafts a minimal, valid <c>WorldSave_&lt;region&gt;.sav</c> for a region a save has not
    /// visited yet, so story / quest-flag edits that reference that region have a real world save
    /// to point at. The file is a near-empty region save (the game regenerates the region's actors
    /// on first visit); only its <c>SaveIdentifier</c> is stamped with <paramref name="region"/>.
    /// </summary>
    /// <param name="worldDir">The world folder to write into (where the other WorldSave_*.sav live).</param>
    /// <param name="region">
    /// The region token, e.g. <c>V_DistantShore</c> or <c>Facility_Office1</c>. A leading
    /// <c>WorldSave_</c> and a trailing <c>.sav</c> are tolerated and stripped.
    /// </param>
    /// <param name="template">
    /// An optional region-save template (raw <c>.sav</c> bytes). When null the embedded
    /// blank-region template is used, so no fixture or installed game is required.
    /// </param>
    /// <returns>The absolute path of the created <c>WorldSave_&lt;region&gt;.sav</c>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="region"/> is empty or unsafe.</exception>
    /// <exception cref="IOException">When the target file already exists.</exception>
    public static string CreateMinimalRegion(string worldDir, string region, byte[]? template = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldDir);
        var token = NormalizeRegionToken(region);

        var destPath = Path.Combine(worldDir, $"WorldSave_{token}.sav");
        if (File.Exists(destPath))
        {
            throw new IOException($"A region save already exists at {destPath}.");
        }

        var bytes = template ?? LoadBlankRegionTemplate();
        using var ms = new MemoryStream(bytes, writable: false);
        var data = WorldSaveReader.ReadFromStream(ms);

        SetSaveIdentifier(data.Raw, token);

        Directory.CreateDirectory(worldDir);
        using var outMs = new MemoryStream();
        data.Raw.WriteTo(outMs);
        File.WriteAllBytes(destPath, outMs.ToArray());

        Diagnostics.EditorLog.Info("WorldFactory", $"Created minimal region save WorldSave_{token}.sav at {worldDir}");
        return destPath;
    }

    /// <summary>
    /// Strips an optional <c>WorldSave_</c> prefix / <c>.sav</c> suffix and rejects a token that
    /// would escape the world folder or produce an invalid file name.
    /// </summary>
    internal static string NormalizeRegionToken(string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        var token = region.Trim();
        if (token.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
        {
            token = token[..^4];
        }
        if (token.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase))
        {
            token = token["WorldSave_".Length..];
        }
        token = token.Trim();
        if (token.Length == 0)
        {
            throw new ArgumentException("Region token is empty after normalization.", nameof(region));
        }
        if (token.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || token.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{region}' is not a valid region token.", nameof(region));
        }
        return token;
    }

    private static byte[] LoadBlankRegionTemplate()
    {
        var asm = typeof(WorldSaveFactory).Assembly;
        using var stream = asm.GetManifestResourceStream(BlankRegionResource)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{BlankRegionResource}' is missing from {asm.GetName().Name}.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Sets the top-level <c>SaveIdentifier</c> StrProperty to <paramref name="value"/>, appending
    /// the tag if a template somehow lacks it (every real region save carries it). Mirrors
    /// <see cref="PlayerSaves.PlayerSaveIdentity"/>'s identifier write.
    /// </summary>
    private static void SetSaveIdentifier(SaveGame save, string value)
    {
        var props = save.Properties
            ?? throw new InvalidOperationException("The region template has no properties.");

        var tag = props.FindByPrefix("SaveIdentifier");
        if (tag?.Property is not null)
        {
            tag.Property.Value = new FString(value);
            return;
        }

        var name = new FString("SaveIdentifier");
        var type = new FPropertyTypeName(name: new FString(nameof(StrProperty)));
        var property = FProperty.Create(name, type);
        property.Value = new FString(value);
        props.Add(new FPropertyTag(name, type, EPropertyTagFlags.None) { Property = property });
    }

    private static void WriteBlankMetadata(byte[] template, string destPath)
    {
        using var ms = new MemoryStream(template, writable: false);
        var data = WorldSaveReader.ReadFromStream(ms);

        WorldSaveWriter.ApplyMinutesPassed(data, 0);
        WorldSaveWriter.ApplyStoryProgression(data, string.Empty);
        // WorldFlags and GlobalRecipes are absent in the template (delta-serialized away),
        // so ApplyFlags/ApplyGlobalRecipes would no-op - no need to call them here.

        using var outMs = new MemoryStream();
        data.Raw.WriteTo(outMs);
        File.WriteAllBytes(destPath, outMs.ToArray());
    }

    private static void WriteSandboxSettings(string path, int difficulty)
    {
        // Tuned per-difficulty defaults; values match what the game client writes for each preset.
        var (xp, damage, stack, spawn, sink) = difficulty switch
        {
            1 => (1.5f, 0.0f, 2.0f, 0.5f, 2.0f),  // Casual
            3 => (1.0f, 1.0f, 1.0f, 1.5f, 1.0f),  // Survival
            4 => (0.75f, 1.5f, 1.0f, 2.0f, 1.0f), // Nightmare
            _ => (1.0f, 0.5f, 1.0f, 1.0f, 1.0f),  // Normal (2, the default)
        };

        var content =
            $"[SandboxSettings]\r\n" +
            $"GameDifficulty={difficulty}\r\n" +
            $"PlayerXPGainMultiplier={xp:F2}\r\n" +
            $"DamageToAlliesMultiplier={damage:F2}\r\n" +
            $"ItemStackSizeMultiplier={stack:F2}\r\n" +
            $"EnemySpawnRate={spawn:F2}\r\n" +
            $"SinkRefillRate={sink:F2}\r\n";

        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }
}
