using UeSaveGame;

using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Core.Compare;

/// <summary>
/// Compares two save files property-by-property and reports every difference. Works for any
/// Abiotic Factor save (player, world, metadata) because it diffs the generic property tree
/// rather than a per-type model. Use <see cref="SaveFolderComparer"/> to compare whole folders.
/// </summary>
public static class SaveComparer
{
    /// <summary>Compares two already-loaded saves.</summary>
    public static SaveDiff Compare(
        SaveGame left,
        SaveGame right,
        string? leftLabel = null,
        string? rightLabel = null,
        SaveDiffOptions? options = null)
    {
        var leftLeaves = SavePropertyFlattener.Flatten(left, options, out var leftTrunc);
        var rightLeaves = SavePropertyFlattener.Flatten(right, options, out var rightTrunc);

        // Index the right side by path. Paths are made unique by the flattener, so a plain
        // dictionary is enough.
        var rightByPath = new Dictionary<string, SavePropertyFlattener.Leaf>(rightLeaves.Count, StringComparer.Ordinal);
        foreach (var leaf in rightLeaves)
        {
            rightByPath[leaf.Path] = leaf;
        }

        var diffs = new List<SaveLeafDiff>();
        var matchedRight = new HashSet<string>(StringComparer.Ordinal);

        // Walk the left side in order: changed + removed leaves keep the left's natural order.
        foreach (var l in leftLeaves)
        {
            if (rightByPath.TryGetValue(l.Path, out var r))
            {
                matchedRight.Add(l.Path);
                if (!string.Equals(l.Value, r.Value, StringComparison.Ordinal))
                {
                    var category = SaveDiffClassifier.Classify(l.Path, l.Type, l.Value, r.Value);
                    diffs.Add(new SaveLeafDiff(SaveDiffKind.Changed, l.Path, l.Value, r.Value, l.Type, category));
                }
            }
            else
            {
                var category = SaveDiffClassifier.Classify(l.Path, l.Type, l.Value, null);
                diffs.Add(new SaveLeafDiff(SaveDiffKind.Removed, l.Path, l.Value, null, l.Type, category));
            }
        }

        // Anything in the right that the left never matched was added.
        foreach (var r in rightLeaves)
        {
            if (!matchedRight.Contains(r.Path))
            {
                var category = SaveDiffClassifier.Classify(r.Path, r.Type, null, r.Value);
                diffs.Add(new SaveLeafDiff(SaveDiffKind.Added, r.Path, null, r.Value, r.Type, category));
            }
        }

        return new SaveDiff(
            leftLabel,
            rightLabel,
            left.SaveClass?.Value,
            right.SaveClass?.Value,
            diffs,
            leftTrunc || rightTrunc);
    }

    /// <summary>Loads both files and compares them. Labels default to the file names.</summary>
    public static SaveDiff CompareFiles(string leftPath, string rightPath, SaveDiffOptions? options = null)
    {
        AbioticSaveClasses.EnsureLoaded();
        var left = Load(leftPath);
        var right = Load(rightPath);
        return Compare(left, right, Path.GetFileName(leftPath), Path.GetFileName(rightPath), options);
    }

    internal static SaveGame Load(string path)
    {
        using var fs = File.OpenRead(path);
        return SaveGame.LoadFrom(fs);
    }
}
