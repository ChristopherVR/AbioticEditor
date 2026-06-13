using UeSaveGame;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Core.Compare;

/// <summary>Where a save file sits relative to the two folders being compared.</summary>
public enum FolderEntryStatus
{
    Identical,
    Differs,
    OnlyLeft,
    OnlyRight,
    Error,
}

/// <summary>One paired (or unpaired) save file in a folder comparison.</summary>
public sealed record FolderFileComparison(
    string RelativePath,
    FolderEntryStatus Status,
    SaveDiff? Diff,
    string? Error)
{
    /// <summary>Number of property-level differences, or 0 when not applicable.</summary>
    public int DifferenceCount => Diff?.Differences.Count ?? 0;
}

/// <summary>The result of comparing every <c>*.sav</c> under two folders.</summary>
public sealed class FolderDiff
{
    public FolderDiff(string leftFolder, string rightFolder, IReadOnlyList<FolderFileComparison> files)
    {
        LeftFolder = leftFolder;
        RightFolder = rightFolder;
        Files = files;
    }

    public string LeftFolder { get; }
    public string RightFolder { get; }
    public IReadOnlyList<FolderFileComparison> Files { get; }

    public int IdenticalCount => Files.Count(f => f.Status == FolderEntryStatus.Identical);
    public int DifferingCount => Files.Count(f => f.Status == FolderEntryStatus.Differs);
    public int OnlyLeftCount => Files.Count(f => f.Status == FolderEntryStatus.OnlyLeft);
    public int OnlyRightCount => Files.Count(f => f.Status == FolderEntryStatus.OnlyRight);
    public int ErrorCount => Files.Count(f => f.Status == FolderEntryStatus.Error);

    public bool AreIdentical => DifferingCount == 0 && OnlyLeftCount == 0 && OnlyRightCount == 0 && ErrorCount == 0;
}

/// <summary>
/// Compares two folders of saves (e.g. a world folder vs one of its backups) by pairing
/// <c>*.sav</c> files on their path relative to each folder root. Each matched pair is run
/// through <see cref="SaveComparer"/>; unmatched files are reported as only-on-one-side.
/// </summary>
public static class SaveFolderComparer
{
    public static FolderDiff Compare(string leftFolder, string rightFolder, SaveDiffOptions? options = null)
    {
        AbioticSaveClasses.EnsureLoaded();

        var leftFiles = Enumerate(leftFolder);
        var rightFiles = Enumerate(rightFolder);

        var allKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(leftFiles.Keys);
        allKeys.UnionWith(rightFiles.Keys);

        var results = new List<FolderFileComparison>(allKeys.Count);
        foreach (var key in allKeys)
        {
            var hasLeft = leftFiles.TryGetValue(key, out var leftPath);
            var hasRight = rightFiles.TryGetValue(key, out var rightPath);

            if (hasLeft && !hasRight)
            {
                results.Add(new FolderFileComparison(key, FolderEntryStatus.OnlyLeft, null, null));
                continue;
            }
            if (!hasLeft && hasRight)
            {
                results.Add(new FolderFileComparison(key, FolderEntryStatus.OnlyRight, null, null));
                continue;
            }

            try
            {
                SaveGame left = SaveComparer.Load(leftPath!);
                SaveGame right = SaveComparer.Load(rightPath!);
                var diff = SaveComparer.Compare(left, right, leftPath, rightPath, options);
                results.Add(new FolderFileComparison(
                    key,
                    diff.AreIdentical ? FolderEntryStatus.Identical : FolderEntryStatus.Differs,
                    diff,
                    null));
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("Compare", $"Failed to compare {key}", ex);
                results.Add(new FolderFileComparison(key, FolderEntryStatus.Error, null, ex.Message));
            }
        }

        return new FolderDiff(leftFolder, rightFolder, results);
    }

    private static Dictionary<string, string> Enumerate(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return map;

        foreach (var path in Directory.EnumerateFiles(folder, "*.sav", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(folder, path);
            map[rel] = path;
        }
        return map;
    }
}
