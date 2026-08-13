using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Services;

public enum SaveConversionDirection { ToGamePass, ToSteam }

public enum SaveConversionSourceValidation { Valid, MissingSteamWorldSave, MissingGamePassContainer }

/// <summary>How doubtful a typed account looks. Never a refusal: see
/// <see cref="SaveConversionService.WarnAboutAccountId"/>.</summary>
public enum SaveConversionIdWarning
{
    /// <summary>Nothing to say about it.</summary>
    None,

    /// <summary>The conversion will refuse this one: it cannot name a save file.</summary>
    UnusableInFileName,

    /// <summary>Allowed, but it is not shaped like a Steam account and Steam is the destination.</summary>
    NotShapedLikeASteamId,
}

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

    /// <summary>
    /// What looks doubtful about an account the player typed, so the caller can say so beside the
    /// box. This deliberately never refuses anything: the editor cannot know every id every
    /// platform issues, and the ordinary reason to type one at all is to hand a world to an
    /// account this machine has never seen. A blank account is not doubtful either, it means
    /// "leave the existing ones alone".
    /// </summary>
    public static SaveConversionIdWarning WarnAboutAccountId(
        SaveConversionDirection direction, string? playerAccountId)
    {
        var id = playerAccountId?.Trim();
        if (string.IsNullOrEmpty(id)) return SaveConversionIdWarning.None;

        // This one is not a guess. A save is named after its account, so an id that cannot be
        // part of a file name is turned away by the conversion itself further down.
        if (!PlayerIdentifier.IsSafeFileToken(id)) return SaveConversionIdWarning.UnusableInFileName;

        // Only Steam has a shape worth checking. Xbox ids are opaque, so there is nothing
        // honest to say about one going the other way.
        return direction == SaveConversionDirection.ToSteam && !PlayerIdentifier.IsSteamId(id)
            ? SaveConversionIdWarning.NotShapedLikeASteamId
            : SaveConversionIdWarning.None;
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

    /// <summary>
    /// The characters in the world about to be converted, so the caller can ask which one is the
    /// player's before re-homing. A shared world holds the player's friends' characters too, and
    /// only the player knows which of the ids is theirs. Empty when the source cannot be read: this
    /// only drives an optional question, and failing to ask must not fail the conversion.
    /// </summary>
    public static IReadOnlyList<string> Characters(
        SaveConversionDirection direction, string sourceFolder, string? containerName = null)
    {
        try
        {
            return direction == SaveConversionDirection.ToGamePass
                ? GamePassConverter.ListSteamWorldPlayers(sourceFolder)
                : GamePassConverter.ListContainerPlayers(sourceFolder, containerName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or InvalidOperationException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// True when running this conversion as it stands would hand the player a world whose
    /// characters are not theirs on the other platform, so the game starts them fresh. Giving an
    /// account to re-home to is what turns this off, which is why a blank one is the trigger.
    /// </summary>
    public static bool WouldStrandCharacters(
        SaveConversionDirection direction, IReadOnlyList<string>? characters, string? playerAccountId)
        => string.IsNullOrWhiteSpace(playerAccountId)
            && GamePassConverter.WouldStrandCharacters(DestinationPlatform(direction), characters);

    /// <summary>The platform a direction ends on, which is the one whose naming rules apply.</summary>
    public static SavePlatform DestinationPlatform(SaveConversionDirection direction)
        => direction == SaveConversionDirection.ToSteam ? SavePlatform.Steam : SavePlatform.GamePass;

    public static string Convert(
        SaveConversionDirection direction, string sourceFolder, string? playerAccountId,
        string? containerName = null, string? sourcePlayerId = null)
    {
        var validation = ValidateSource(direction, sourceFolder);
        if (validation != SaveConversionSourceValidation.Valid)
            throw new InvalidOperationException(validation.ToString());

        var destination = DestinationFor(direction, sourceFolder);
        return direction == SaveConversionDirection.ToGamePass
            ? GamePassConverter.SteamWorldToGamePass(
                sourceFolder, destination, worldName: null, newPlayerId: playerAccountId,
                sourcePlayerId: sourcePlayerId)
            : GamePassConverter.GamePassToSteamWorld(
                sourceFolder, containerName, destination, newPlayerId: playerAccountId,
                sourcePlayerId: sourcePlayerId);
    }
}
