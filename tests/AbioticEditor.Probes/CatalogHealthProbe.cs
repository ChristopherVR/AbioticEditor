using System.Collections;
using System.IO;
using System.Reflection;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe after a game update: loads every game-data catalog from the installed game and
/// reports, per catalog, how many entries it produced and which fields are empty for (almost)
/// every entry - the signature of a source field the game renamed or reshaped. Output-only; runs
/// only when CATALOG_HEALTH_OUT names an output file.
/// </summary>
public class CatalogHealthProbe
{
    [Fact]
    public void Dump_CatalogHealth()
    {
        var outPath = Environment.GetEnvironmentVariable("CATALOG_HEALTH_OUT");
        if (string.IsNullOrWhiteSpace(outPath)) return;
        using var provider = GameAssetProvider.CreateForLocalInstall(includeMods: false);
        Assert.NotNull(provider);
        using var writer = new StreamWriter(outPath, false);

        var core = typeof(GameAssetProvider).Assembly;
        var loaders = core.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetParameters() is [{ } p] && p.ParameterType == typeof(GameAssetProvider) && m.ReturnType != typeof(void)
                        && !m.Name.Contains("Extract", StringComparison.Ordinal) && !m.ContainsGenericParameters)
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var loader in loaders)
        {
            var label = $"{loader.DeclaringType!.Name}.{loader.Name}";
            object? result;
            try { result = loader.Invoke(null, [provider]); }
            catch (Exception ex) { writer.WriteLine($"## {label}: THREW {(ex.InnerException ?? ex).GetType().Name}: {(ex.InnerException ?? ex).Message}"); continue; }
            Report(writer, label, result);
        }
    }

    private static void Report(StreamWriter writer, string label, object? result)
    {
        if (result is null) { writer.WriteLine($"## {label}: null"); return; }
        var items = Items(result).ToList();
        if (items.Count == 0 && result is not IEnumerable)
        {
            // A catalog object: report each collection-valued member separately.
            var members = result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string)).ToList();
            if (members.Count == 0)
            {
                var counts = result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.PropertyType == typeof(int))
                    .Select(p => $"{p.Name}={p.GetValue(result)}");
                writer.WriteLine($"## {label}: {result.GetType().Name} {string.Join(", ", counts)}");
                return;
            }
            foreach (var m in members)
            {
                object? value;
                try { value = m.GetValue(result); } catch { continue; }
                Report(writer, $"{label}.{m.Name}", value);
            }
            return;
        }
        writer.WriteLine($"## {label}: {items.Count} entries");
        if (items.Count == 0) return;
        var first = items.First(i => i is not null)!;
        var type = first.GetType();
        if (type.IsPrimitive || type == typeof(string)) { writer.WriteLine($"   sample: {string.Join(", ", items.Take(5))}"); return; }
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            var empty = 0;
            foreach (var item in items)
            {
                object? v;
                try { v = prop.GetValue(item); } catch { v = null; }
                if (IsEmpty(v)) empty++;
            }
            if (empty * 100 >= items.Count * 95)
                writer.WriteLine($"   EMPTY {empty}/{items.Count}: {prop.Name} ({prop.PropertyType.Name})");
        }
    }

    private static IEnumerable<object?> Items(object result) => result switch
    {
        string => [],
        IDictionary d => d.Values.Cast<object?>(),
        IEnumerable e => e.Cast<object?>(),
        _ => [],
    };

    private static bool IsEmpty(object? v) => v switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s) || s == "None",
        bool => false,
        int i => i == 0,
        double d => d == 0,
        float f => f == 0,
        ICollection c => c.Count == 0,
        IEnumerable e => !e.Cast<object>().Any(),
        _ => false,
    };
}
