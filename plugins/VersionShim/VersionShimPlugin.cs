using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

namespace AbioticEditor.Samples.VersionShim;

/// <summary>Entry point: registers the save upgrader this plugin provides.</summary>
public sealed class VersionShimPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("Version Shim plugin configured.");
        registry.AddSaveUpgrader(new FixSaveVersionUpgrader());
    }
}

/// <summary>
/// Recovers a save whose GVAS <c>SaveGameVersion</c> field holds a value the editor's
/// UeSaveGame doesn't accept - the typical failure right after a game update bumps the format
/// - by rewriting that field to the modern supported value (3,
/// <c>PackageFileSummaryVersionChange</c>). It only touches the 4-byte version field, leaving
/// the rest of the file intact, which works when a patch bumped the version number without
/// changing the layout the editor already understands.
///
/// <para>
/// This is intentionally a narrow, honest example of the
/// <see cref="ISaveUpgrader"/> contract; a real game-format change would do a deeper
/// transform here. It demonstrates the whole flow: claim by header probe, return corrected
/// bytes, let the host load and (optionally) persist them.
/// </para>
/// </summary>
public sealed class FixSaveVersionUpgrader : ISaveUpgrader
{
    // The save-game format versions UeSaveGame supports (AddedCustomVersions / PackageFileSummary).
    private const int ModernSupportedVersion = 3;
    private const int SaveGameVersionOffset = 4; // bytes 0-3 are the "GVAS" magic.

    public string Id => "fix-save-version";

    public string DisplayName => "Fix Save Version";

    public string Description =>
        "Rewrites a save's SaveGameVersion field to the modern supported value when a game "
        + "update bumped the number without changing the layout the editor understands.";

    public bool CanUpgrade(SaveUpgradeProbe probe)
    {
        // Only claim the precise "unsupported version" failure: the header was readable (the
        // version field parsed, so magic was present) but the value is outside what loads.
        if (probe.SaveGameVersion is 2 or 3)
        {
            return false; // already a supported version - some other reason it failed.
        }
        if (probe.SaveGameVersion == 0)
        {
            return false; // magic wasn't even readable; this isn't our case.
        }
        var error = probe.LoadError ?? string.Empty;
        return error.Contains("version", StringComparison.OrdinalIgnoreCase)
            && error.Contains("support", StringComparison.OrdinalIgnoreCase);
    }

    public Task<SaveUpgradeResult> UpgradeAsync(ISaveUpgradeContext context, CancellationToken cancellationToken = default)
    {
        var bytes = (byte[])context.OriginalBytes.Clone();
        if (bytes.Length < SaveGameVersionOffset + 4)
        {
            return Task.FromResult(SaveUpgradeResult.NotHandled("file too small to carry a version field."));
        }

        var original = BitConverter.ToInt32(bytes, SaveGameVersionOffset);
        BitConverter.GetBytes(ModernSupportedVersion).CopyTo(bytes, SaveGameVersionOffset);
        context.Log.Info($"rewrote SaveGameVersion {original} -> {ModernSupportedVersion}.");

        return Task.FromResult(SaveUpgradeResult.Ok(
            bytes, $"rewrote unsupported save version {original} to {ModernSupportedVersion}."));
    }
}
