using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Plugins;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Reads and writes <c>plugin.json</c> manifests. Reading happens before any plugin
/// assembly is loaded, so this is the only code that inspects an untrusted plugin folder
/// up front - it must be defensive and never throw for a malformed file (it returns null
/// and logs instead). Writing is used only to persist the enabled/disabled toggle.
/// </summary>
public static class PluginManifestIo
{
    public const string FileName = "plugin.json";

    // Pre-release / build-metadata separators stripped before the numeric version parse.
    private static readonly char[] SemverSuffixChars = { '-', '+' };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Reads and validates a manifest. Returns null (and logs a warning) when the file is
    /// missing, unparseable, or fails validation - the caller treats that plugin as absent
    /// rather than letting one bad folder break discovery.
    /// </summary>
    public static PluginManifest? TryRead(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, Options);
            if (manifest is null)
            {
                EditorLog.Warn("Plugins", $"Manifest deserialized to null: {manifestPath}");
                return null;
            }

            var error = Validate(manifest);
            if (error is not null)
            {
                EditorLog.Warn("Plugins", $"Invalid manifest {manifestPath}: {error}");
                return null;
            }

            return manifest;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            EditorLog.Warn("Plugins", $"Could not read manifest {manifestPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns a human-readable problem with the manifest, or null if it is usable. Kept
    /// strict on the few fields the loader relies on (id + entry assembly) and lenient on
    /// the descriptive ones.
    /// </summary>
    public static string? Validate(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            return "missing 'id'.";
        }

        var isJavaScript = string.Equals(manifest.Runtime, PluginRuntimes.JavaScript, StringComparison.OrdinalIgnoreCase);
        if (!isJavaScript
            && !string.Equals(manifest.Runtime, PluginRuntimes.DotNet, StringComparison.OrdinalIgnoreCase))
        {
            return $"unknown 'runtime': '{manifest.Runtime}' (expected '{PluginRuntimes.DotNet}' or '{PluginRuntimes.JavaScript}').";
        }

        // The entry file (assembly or script) must be a bare file name in the plugin's own
        // folder - no path traversal - so a manifest can't point the loader at an arbitrary
        // file elsewhere on disk.
        var entry = isJavaScript ? manifest.EntryScript : manifest.EntryAssembly;
        var field = isJavaScript ? "entryScript" : "entryAssembly";
        if (string.IsNullOrWhiteSpace(entry))
        {
            return $"missing '{field}'.";
        }
        if (entry.Contains('/') || entry.Contains('\\') || entry.Contains(".."))
        {
            return $"'{field}' must be a file name in the plugin folder, not a path.";
        }
        var expectedExt = isJavaScript ? ".js" : ".dll";
        if (!entry.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{field}' must be a {expectedExt} file name.";
        }
        if (!Version.TryParse(NormalizeVersion(manifest.Version), out _))
        {
            return $"'version' is not a valid version: '{manifest.Version}'.";
        }
        if (!Version.TryParse(NormalizeVersion(manifest.MinHostVersion), out _))
        {
            return $"'minHostVersion' is not a valid version: '{manifest.MinHostVersion}'.";
        }
        return null;
    }

    /// <summary>
    /// Persists a manifest back to disk (used to save the enabled toggle). Best-effort:
    /// failures are logged, not thrown, so a read-only install just keeps the in-memory
    /// state.
    /// </summary>
    public static bool TryWrite(string manifestPath, PluginManifest manifest)
    {
        try
        {
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Options));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Warn("Plugins", $"Could not write manifest {manifestPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Coerces common version spellings (a bare <c>"1"</c> or <c>"1.2"</c>) into something
    /// <see cref="Version.TryParse"/> accepts, so authors can write friendly strings.
    /// </summary>
    public static string NormalizeVersion(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "0.0.0" : raw.Trim();
        // Strip a leading 'v' and any semver pre-release/build suffix for the numeric parse.
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }
        var cut = value.IndexOfAny(SemverSuffixChars);
        if (cut >= 0)
        {
            value = value[..cut];
        }
        return value.Count(c => c == '.') switch
        {
            0 => $"{value}.0",
            _ => value,
        };
    }
}
