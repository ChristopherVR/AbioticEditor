using System.Globalization;
using System.Text.RegularExpressions;

using UeSaveGame;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.Compare;

/// <summary>
/// Walks a <see cref="SaveGame"/>'s property tree into a flat, ordered list of
/// <c>path -&gt; value</c> leaves so two saves can be diffed by path. Names carry blueprint
/// hash suffixes (e.g. <c>Hunger_2_A6C5...</c>); those are normalized away so the same
/// logical property lines up across saves and game builds. Containers are walked, not
/// emitted: <see cref="UeSaveGame.StructData.PropertiesStruct"/> recurses, arrays index as
/// <c>[i]</c>, maps key as <c>{key}</c>. Specialized struct types (Vector, Guid, Color,
/// DateTime, gameplay-tag containers, ...) compare by their rendered string.
/// </summary>
public static class SavePropertyFlattener
{
    /// <summary>One flattened value plus where it came from.</summary>
    public readonly record struct Leaf(string Path, string Value, string Type);

    // Blueprint hash suffix: "_<index>_<32-hex-or-longer>" tacked onto save property names.
    private static readonly Regex HashSuffix = new(
        @"_\d+_[0-9A-Fa-f]{8,}$", RegexOptions.Compiled);

    private sealed class Sink
    {
        private readonly Dictionary<string, int> _pathCounts = new(StringComparer.Ordinal);
        public List<Leaf> Leaves { get; } = new();
        public int Max { get; init; }
        public bool Truncated { get; private set; }

        public bool Add(string path, string value, string type)
        {
            if (Leaves.Count >= Max)
            {
                Truncated = true;
                return false;
            }

            // Disambiguate the rare case of two sibling leaves normalizing to the same path
            // (e.g. two properties whose only difference was the stripped hash) so neither
            // shadows the other in the diff.
            if (_pathCounts.TryGetValue(path, out var n))
            {
                _pathCounts[path] = n + 1;
                path = $"{path}#{n + 1}";
            }
            else
            {
                _pathCounts[path] = 1;
            }

            Leaves.Add(new Leaf(path, value, type));
            return true;
        }
    }

    public static IReadOnlyList<Leaf> Flatten(SaveGame save, SaveDiffOptions? options, out bool truncated)
    {
        options ??= SaveDiffOptions.Default;
        var sink = new Sink { Max = options.MaxLeaves };

        if (save.Properties is not null)
        {
            foreach (var tag in save.Properties)
            {
                if (tag.IsNone) continue;
                if (!VisitProperty(string.Empty, tag, sink)) break;
            }
        }

        truncated = sink.Truncated;
        return sink.Leaves;
    }

    private static bool VisitProperty(string parentPath, FPropertyTag tag, Sink sink)
    {
        var name = Normalize(tag.Name?.Value ?? "?");
        var path = parentPath.Length == 0 ? name : $"{parentPath}.{name}";
        return VisitValue(path, tag.Property?.Value, TypeOf(tag), sink);
    }

    private static bool VisitValue(string path, object? value, string type, Sink sink)
    {
        switch (value)
        {
            case null:
                return sink.Add(path, "(none)", type);

            // PropertiesStruct is an IStructData too, so it must be matched first: recurse
            // into its child tags rather than treating it as an opaque leaf.
            case PropertiesStruct ps:
            {
                foreach (var child in ps.Properties)
                {
                    if (child.IsNone) continue;
                    if (!VisitProperty(path, child, sink)) return false;
                }
                return true;
            }

            // Any other struct data (Vector, Guid, Color, DateTime, gameplay tags, ...)
            // has a faithful ToString - compare on that.
            case IStructData sd:
                return sink.Add(path, sd.ToString() ?? "(struct)", type);

            case Array arr:
                return VisitArray(path, arr, sink);

            case IList<KeyValuePair<FProperty, FProperty>> map:
                return VisitMap(path, map, sink);

            default:
                return sink.Add(path, FormatScalar(value), type);
        }
    }

    private static bool VisitArray(string path, Array arr, Sink sink)
    {
        for (int i = 0; i < arr.Length; i++)
        {
            var element = arr.GetValue(i);
            var itemPath = $"{path}[{i}]";
            bool ok = element is FProperty fp
                ? VisitValue(itemPath, fp.Value, fp.GetType().Name, sink)
                : sink.Add(itemPath, FormatScalar(element), string.Empty);
            if (!ok) return false;
        }
        return true;
    }

    private static bool VisitMap(string path, IList<KeyValuePair<FProperty, FProperty>> map, Sink sink)
    {
        var keyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in map)
        {
            var key = Normalize(FormatScalar(pair.Key.Value));
            if (keyCounts.TryGetValue(key, out var n))
            {
                keyCounts[key] = n + 1;
                key = $"{key}#{n + 1}";
            }
            else
            {
                keyCounts[key] = 1;
            }

            var entryPath = $"{path}{{{key}}}";
            if (!VisitValue(entryPath, pair.Value.Value, pair.Value.GetType().Name, sink)) return false;
        }
        return true;
    }

    /// <summary>Strips the blueprint hash suffix so the same logical property aligns across saves.</summary>
    public static string Normalize(string name) => HashSuffix.Replace(name, string.Empty);

    private static string TypeOf(FPropertyTag tag)
        => tag.Type?.Name ?? tag.Property?.GetType().Name ?? string.Empty;

    private static string FormatScalar(object? value) => value switch
    {
        null => "(none)",
        FString fs => fs.Value ?? "(none)",
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "(none)",
    };
}
