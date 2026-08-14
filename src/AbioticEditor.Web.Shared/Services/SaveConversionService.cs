using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.Steam;

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

    /// <summary>
    /// Where a conversion writes its output. <paramref name="containerName"/>, when known, names
    /// the specific world being converted (used only for the Game Pass -&gt; Steam direction, to
    /// name the destination folder); pass null to let it be worked out from the source.
    /// </summary>
    public static string DestinationFor(
        SaveConversionDirection direction, string sourceFolder, string? containerName = null)
    {
        if (direction == SaveConversionDirection.ToGamePass)
        {
            // A Steam world folder already lives somewhere normal and writable, so the Game
            // Pass copy goes right beside it - easy to find, and obviously paired with its
            // source.
            var source = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return UniqueFolder(source + "-GamePass");
        }

        // A Game Pass source lives inside the Xbox app's own virtualized package folder
        // (...\Packages\<publisher>\SystemAppData\wgs\...). Writing "beside it" - the rule
        // above, and this direction's rule until this was reported - buried the converted
        // Steam world inside that Xbox package folder instead of anywhere a player, or the
        // game, would think to look. A Steam world belongs in the same place a new one would
        // be created.
        var worldName = WorldNameFor(sourceFolder, containerName);
        return UniqueFolder(Path.Combine(DefaultSteamSaveRoot(), worldName));
    }

    /// <summary>The name a converted Steam world's destination folder should have: the world's
    /// own name (its container, minus the "-WC" suffix that means nothing to a player) when it
    /// can be determined, or the source folder's own name otherwise.</summary>
    private static string WorldNameFor(string sourceFolder, string? containerName)
    {
        var container = containerName;
        if (container is null)
        {
            try
            {
                var containers = GamePassConverter.ListWorldContainers(sourceFolder);
                container = containers.Count > 0 ? containers[0] : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or InvalidDataException or InvalidOperationException)
            {
                container = null;
            }
        }
        if (container is not null)
        {
            return container.EndsWith("-WC", StringComparison.OrdinalIgnoreCase) ? container[..^3] : container;
        }
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder)));
    }

    /// <summary>The folder a newly converted (or newly created) Steam world belongs under: the
    /// first known Steam account's Worlds folder, or the platform's base SaveGames folder when no
    /// account is known yet. Mirrors where the "create a new world" flow puts a fresh Steam
    /// world, so a converted one ends up findable the same way.</summary>
    private static string DefaultSteamSaveRoot()
    {
        var accounts = SteamPersonaIndex.LoadMachineAccounts();
        if (accounts.Count > 0)
        {
            var candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AbioticFactor", "Saved", "SaveGames", accounts.Keys.First(), "Worlds");
            if (Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticFactor", "Saved", "SaveGames");
    }

    /// <summary>Never hands back a folder that already holds something. Converting twice used to
    /// aim at the same path both times, so the second run wrote a world on top of the first
    /// one's.</summary>
    private static string UniqueFolder(string baseName)
    {
        if (!Exists(baseName)) return baseName;
        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseName}-{n}";
            if (!Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"Could not find an unused folder name near '{baseName}'.");
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

    /// <summary>
    /// Converts <paramref name="sourceFolder"/> and returns the destination path. <paramref
    /// name="rehomes"/>, when given and non-empty, re-homes every character it names to its own
    /// destination account in one pass - a co-op world's players each keep their own character
    /// instead of the conversion being limited to claiming a single one. When it is null or empty,
    /// <paramref name="playerAccountId"/>/<paramref name="sourcePlayerId"/> behave as before (one
    /// character, or the world's only one).
    /// </summary>
    public static string Convert(
        SaveConversionDirection direction, string sourceFolder, string? playerAccountId,
        string? containerName = null, string? sourcePlayerId = null,
        IReadOnlyDictionary<string, string>? rehomes = null)
    {
        var validation = ValidateSource(direction, sourceFolder);
        if (validation != SaveConversionSourceValidation.Valid)
            throw new InvalidOperationException(validation.ToString());

        var destination = DestinationFor(direction, sourceFolder, containerName);
        return direction == SaveConversionDirection.ToGamePass
            ? GamePassConverter.SteamWorldToGamePass(
                sourceFolder, destination, worldName: null, newPlayerId: playerAccountId,
                sourcePlayerId: sourcePlayerId, rehomes: rehomes)
            : GamePassConverter.GamePassToSteamWorld(
                sourceFolder, containerName, destination, newPlayerId: playerAccountId,
                sourcePlayerId: sourcePlayerId, rehomes: rehomes);
    }
}
