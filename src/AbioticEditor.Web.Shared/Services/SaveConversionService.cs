using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Web.Services;

public enum SaveConversionDirection { ToGamePass, ToSteam }

public enum SaveConversionSourceValidation { Valid, MissingSteamWorldSave, MissingGamePassContainer }

/// <summary>Desktop conversion workflow shared by the settings UI and its parity tests.</summary>
public static class SaveConversionService
{
    public static SaveConversionSourceValidation ValidateSource(SaveConversionDirection direction, string sourceFolder)
    {
        if (!Directory.Exists(sourceFolder))
            return direction == SaveConversionDirection.ToGamePass
                ? SaveConversionSourceValidation.MissingSteamWorldSave
                : SaveConversionSourceValidation.MissingGamePassContainer;

        return direction switch
        {
            SaveConversionDirection.ToGamePass when Directory.EnumerateFiles(sourceFolder, "WorldSave_*.sav").Any()
                => SaveConversionSourceValidation.Valid,
            SaveConversionDirection.ToSteam when GamePassSaveSet.IsGamePassFolder(sourceFolder)
                => SaveConversionSourceValidation.Valid,
            SaveConversionDirection.ToGamePass => SaveConversionSourceValidation.MissingSteamWorldSave,
            _ => SaveConversionSourceValidation.MissingGamePassContainer,
        };
    }

    public static string DestinationFor(SaveConversionDirection direction, string sourceFolder)
    {
        var source = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var baseName = source + (direction == SaveConversionDirection.ToGamePass ? "-GamePass" : "-Steam");

        // Never hand back a folder that already holds something. Converting twice used to aim at
        // the same path both times, so the second run wrote a world on top of the first one's.
        if (!Exists(baseName)) return baseName;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseName}-{n}";
            if (!Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"Could not find an unused folder name next to '{source}'.");
    }

    private static bool Exists(string folder)
        => Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();

    /// <summary>The worlds in a Game Pass save folder, so the caller can offer a choice when there
    /// is more than one. Empty for a Steam source.</summary>
    public static IReadOnlyList<string> WorldContainers(SaveConversionDirection direction, string sourceFolder)
        => direction == SaveConversionDirection.ToSteam && GamePassSaveSet.IsGamePassFolder(sourceFolder)
            ? GamePassConverter.ListWorldContainers(sourceFolder)
            : Array.Empty<string>();

    public static string Convert(
        SaveConversionDirection direction, string sourceFolder, string? playerAccountId, string? containerName = null)
    {
        var validation = ValidateSource(direction, sourceFolder);
        if (validation != SaveConversionSourceValidation.Valid)
            throw new InvalidOperationException(validation.ToString());

        var destination = DestinationFor(direction, sourceFolder);
        return direction == SaveConversionDirection.ToGamePass
            ? GamePassConverter.SteamWorldToGamePass(sourceFolder, destination, worldName: null, newPlayerId: playerAccountId)
            : GamePassConverter.GamePassToSteamWorld(sourceFolder, containerName, destination, newPlayerId: playerAccountId);
    }
}
