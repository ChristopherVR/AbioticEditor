using AbioticEditor.Core.Compare;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;
using Xunit;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// The app's SAVE button re-applies EVERY editable section (stats, limbs, skills, recipes,
/// traits, inventory, transmog, codex ...) from the staged view models, not just what the user
/// changed - so a lossy read-model default anywhere would silently rewrite a real value on every
/// save. This opens a real player save through the same <see cref="PlayerSaveSession"/> the UI
/// uses, saves with NO edits, and asserts the result is semantically identical to the original.
/// </summary>
public sealed class PlayerSaveSessionRoundTripTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> PlayerFixtures()
    {
        var root = FixturesRoot();
        if (root is null) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "Player_*.sav", SearchOption.AllDirectories)
                     .Where(p => !p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return [path];
        }
    }

    /// <summary>Every leaf grouped by its array-index-stripped path, values sorted, so arrays
    /// compare as multisets and scalars as single-element lists.</summary>
    private static Dictionary<string, List<string>> Leaves(UeSaveGame.SaveGame save)
    {
        var leaves = SavePropertyFlattener.Flatten(save, null, out _);
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var leaf in leaves)
        {
            var key = System.Text.RegularExpressions.Regex.Replace(leaf.Path, @"\[\d+\]", "[]");
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(leaf.Value ?? "(null)");
        }
        foreach (var list in groups.Values) list.Sort(StringComparer.Ordinal);
        return groups;
    }

    private static UeSaveGame.SaveGame Load(string path)
    {
        using var stream = File.OpenRead(path);
        return UeSaveGame.SaveGame.LoadFrom(stream);
    }

    private static string? FixturesRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Theory]
    [MemberData(nameof(PlayerFixtures))]
    public async Task Saving_without_edits_changes_nothing(string fixturePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-roundtrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var copy = Path.Combine(dir, Path.GetFileName(fixturePath));
        File.Copy(fixturePath, copy);
        try
        {
            var session = new PlayerSaveSession(PlayerSaveReader.ReadFromFile(copy), copy);
            await session.SaveAsync();

            // Compare per-array as multisets: the session deliberately re-sorts several string
            // arrays (recipes, items picked up, codex lists) on save, which the game does not care
            // about, so an index-by-index diff would drown any real change in reorder noise.
            var original = Load(fixturePath);
            var resaved = Load(copy);
            var left = Leaves(original);
            var right = Leaves(resaved);
            var problems = new List<string>();
            foreach (var key in left.Keys.Union(right.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                var l = left.GetValueOrDefault(key) ?? [];
                var r = right.GetValueOrDefault(key) ?? [];
                if (l.SequenceEqual(r)) continue;
                var removed = l.Except(r).ToList();
                var added = r.Except(l).ToList();
                problems.Add($"{key}: {l.Count} -> {r.Count} value(s)"
                    + (removed.Count > 0 ? $"; dropped [{string.Join(", ", removed.Take(8))}]" : string.Empty)
                    + (added.Count > 0 ? $"; added [{string.Join(", ", added.Take(8))}]" : string.Empty)
                    + (removed.Count == 0 && added.Count == 0 ? " (same values, different multiplicity)" : string.Empty));
            }
            foreach (var problem in problems) output.WriteLine(problem);
            Assert.Empty(problems);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
