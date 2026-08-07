using System.Reflection;

namespace AbioticEditor.Web;

/// <summary>
/// The running host's version string, for the header readout.
///
/// <para>This deliberately does not use <c>AbioticEditor.Updater.AppVersionInfo</c>: the Nexus
/// Mods build is published with <c>-p:NexusMods=true</c>, which drops the updater assembly
/// entirely, and showing a version number is not update logic. Keeping the two apart means the
/// header keeps working in both builds. The formatting matches AppVersionInfo so the two
/// channels display identically.</para>
/// </summary>
internal static class HostVersion
{
    /// <summary>The assembly's informational version, without any build metadata suffix.</summary>
    public static string Current { get; } = Read(typeof(HostVersion).Assembly);

    private static string Read(Assembly assembly)
    {
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // "2.1.0+abc1234" -> "2.1.0"; the commit hash is noise in the window header.
        var plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? raw[..plus] : raw;
    }
}
