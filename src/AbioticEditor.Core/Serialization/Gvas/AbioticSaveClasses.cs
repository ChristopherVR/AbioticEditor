namespace AbioticEditor.Core.SaveClasses;

/// <summary>
/// Touching this type ensures the AbioticEditor.Core assembly is loaded into the AppDomain
/// before <see cref="UeSaveGame.SaveGame.LoadFrom"/> scans for [SaveClassPath]-annotated types.
/// </summary>
public static class AbioticSaveClasses
{
    public static void EnsureLoaded()
    {
        // No-op. Calling this forces the JIT to load this assembly.
    }
}
