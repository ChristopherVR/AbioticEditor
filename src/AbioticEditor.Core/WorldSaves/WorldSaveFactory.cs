using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>Options for creating a new world save folder from a blank template.</summary>
public sealed record CreateWorldOptions
{
    /// <summary>The folder name for the new world (becomes the directory name).</summary>
    public required string WorldName { get; init; }

    /// <summary>Parent directory under which <see cref="WorldName"/> will be created.</summary>
    public required string ParentDirectory { get; init; }

    /// <summary>SteamID64 values for players who should have an initial blank player save.</summary>
    public required IReadOnlyList<ulong> PlayerSteamIds { get; init; }

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
    ///   <item><c>PlayerData/Player_*.sav</c> - one blank player per <see cref="CreateWorldOptions.PlayerSteamIds"/>.</item>
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

        if (options.PlayerSteamIds.Count > 0)
        {
            var playerDir = Path.Combine(worldDir, "PlayerData");
            Directory.CreateDirectory(playerDir);
            foreach (var id in options.PlayerSteamIds)
            {
                PlayerSaveFactory.CreateFromTemplate(playerTemplate, playerDir, id);
                Diagnostics.EditorLog.Info("WorldFactory", $"  + Player_{id}.sav");
            }
        }

        WriteSandboxSettings(Path.Combine(worldDir, "SandboxSettings.ini"), options.GameDifficulty);

        Diagnostics.EditorLog.Info("WorldFactory",
            $"World '{options.WorldName}' created ({options.PlayerSteamIds.Count} player(s), difficulty {options.GameDifficulty}).");
        return worldDir;
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
