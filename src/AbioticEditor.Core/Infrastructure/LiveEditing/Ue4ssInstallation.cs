namespace AbioticEditor.Core.LiveEditing;

/// <summary>Finds an existing standard UE4SS installation without changing or downloading files.</summary>
public static class Ue4ssInstallation
{
    public static string? FindModsDirectory(string win64)
    {
        foreach (var root in new[] { Path.Combine(win64, "ue4ss"), win64 })
        {
            var mods = Path.Combine(root, "Mods");
            if (File.Exists(Path.Combine(root, "UE4SS.dll")))
                return File.Exists(Path.Combine(mods, "shared", "UEHelpers", "UEHelpers.lua")) ? mods : null;
        }
        return null;
    }
}
