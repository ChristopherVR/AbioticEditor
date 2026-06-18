using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// Loads a plugin's translation file into a key -> value dictionary. Two formats, chosen by
/// extension:
/// <list type="bullet">
/// <item><c>.json</c> - a flat object: <c>{ "Common_Save": "Speichern", ... }</c>.</item>
/// <item><c>.resx</c> - the standard string table: each <c>&lt;data name="Key"&gt;&lt;value&gt;Text&lt;/value&gt;&lt;/data&gt;</c>.</item>
/// </list>
/// Never throws: a missing/malformed/unsupported file logs a warning and yields null, so one
/// bad pack cannot break plugin loading.
/// </summary>
internal static class LocalizationResourceLoader
{
    public static IReadOnlyDictionary<string, string>? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                EditorLog.Warn("Plugins", $"Localization file not found: {path}");
                return null;
            }

            var ext = Path.GetExtension(path);
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                return LoadJson(path);
            }
            if (string.Equals(ext, ".resx", StringComparison.OrdinalIgnoreCase))
            {
                return LoadResx(path);
            }

            EditorLog.Warn("Plugins", $"Unsupported localization file (need .json or .resx): {path}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or XmlException or UnauthorizedAccessException)
        {
            EditorLog.Warn("Plugins", $"Could not read localization file {path}: {ex.Message}");
            return null;
        }
    }

    private static Dictionary<string, string> LoadJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Only string values are strings to localize; ignore comment-ish objects/arrays.
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    result[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }
        return result;
    }

    private static Dictionary<string, string> LoadResx(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = XDocument.Load(path).Root;
        if (root is null)
        {
            return result;
        }

        // Standard resx: <data name="Key" ...><value>Text</value></data>. The schema/resheader
        // elements are ignored - only <data> entries with a name carry strings.
        foreach (var data in root.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }
            result[name] = data.Element("value")?.Value ?? string.Empty;
        }
        return result;
    }
}
