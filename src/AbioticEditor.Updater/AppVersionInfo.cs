using System.Reflection;

namespace AbioticEditor.Updater;

/// <summary>Reads the running build's version the same way the host's <c>version</c> command does.</summary>
public static class AppVersionInfo
{
    /// <summary>
    /// The informational version of <paramref name="assembly"/> (falls back to the assembly
    /// version, then "0.0.0"). Build metadata after "+" is stripped so it matches a release tag.
    /// </summary>
    public static string For(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var raw = informational
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var plus = raw.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? raw[..plus] : raw;
    }

    /// <summary>The version of the assembly that called this method.</summary>
    public static string ForCallingAssembly() => For(Assembly.GetCallingAssembly());
}
