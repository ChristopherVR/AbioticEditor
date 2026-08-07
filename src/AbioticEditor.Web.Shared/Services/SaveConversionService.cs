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
        return source + (direction == SaveConversionDirection.ToGamePass ? "-GamePass" : "-Steam");
    }

    public static string Convert(SaveConversionDirection direction, string sourceFolder, string? playerAccountId)
    {
        var validation = ValidateSource(direction, sourceFolder);
        if (validation != SaveConversionSourceValidation.Valid)
            throw new InvalidOperationException(validation.ToString());

        var destination = DestinationFor(direction, sourceFolder);
        return direction == SaveConversionDirection.ToGamePass
            ? GamePassConverter.SteamWorldToGamePass(sourceFolder, destination, worldName: null, newPlayerId: playerAccountId)
            : GamePassConverter.GamePassToSteamWorld(sourceFolder, containerName: null, destination, newPlayerId: playerAccountId);
    }
}
