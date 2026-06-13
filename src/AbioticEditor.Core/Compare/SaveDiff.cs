namespace AbioticEditor.Core.Compare;

/// <summary>How a single leaf differs between the two saves being compared.</summary>
public enum SaveDiffKind
{
    /// <summary>Present in both saves but the value changed (left -> right).</summary>
    Changed,

    /// <summary>Present only in the right (second / "new") save.</summary>
    Added,

    /// <summary>Present only in the left (first / "old") save.</summary>
    Removed,
}

/// <summary>
/// One property-level difference between two saves. <see cref="Path"/> is the normalized
/// dotted path to the value (hash suffixes stripped, array indices as <c>[i]</c>, map keys
/// as <c>{key}</c>). <see cref="Left"/>/<see cref="Right"/> are the rendered values, null
/// when the leaf is absent on that side. <see cref="Category"/> separates real gameplay
/// changes from the differences any two distinct saves carry (identity, clock, instance
/// handles, position).
/// </summary>
public sealed record SaveLeafDiff(
    SaveDiffKind Kind, string Path, string? Left, string? Right, string Type, SaveDiffCategory Category)
{
    /// <summary>True when this is not a meaningful gameplay change (identity/time/instance/position).</summary>
    public bool IsNoise => Category.IsNoise();
}

/// <summary>
/// The result of comparing two save files: every property-level difference plus a couple of
/// header facts. <see cref="AreIdentical"/> is true when no differences were found.
/// </summary>
public sealed class SaveDiff
{
    public SaveDiff(
        string? leftLabel,
        string? rightLabel,
        string? leftSaveClass,
        string? rightSaveClass,
        IReadOnlyList<SaveLeafDiff> differences,
        bool truncated)
    {
        LeftLabel = leftLabel;
        RightLabel = rightLabel;
        LeftSaveClass = leftSaveClass;
        RightSaveClass = rightSaveClass;
        Differences = differences;
        Truncated = truncated;
    }

    /// <summary>Friendly label for the left/first save (usually a file name or path).</summary>
    public string? LeftLabel { get; }

    /// <summary>Friendly label for the right/second save.</summary>
    public string? RightLabel { get; }

    public string? LeftSaveClass { get; }

    public string? RightSaveClass { get; }

    public IReadOnlyList<SaveLeafDiff> Differences { get; }

    /// <summary>True when the flattener hit its leaf cap and the diff may be incomplete.</summary>
    public bool Truncated { get; }

    public bool AreIdentical => Differences.Count == 0;

    public int ChangedCount => Differences.Count(d => d.Kind == SaveDiffKind.Changed);

    public int AddedCount => Differences.Count(d => d.Kind == SaveDiffKind.Added);

    public int RemovedCount => Differences.Count(d => d.Kind == SaveDiffKind.Removed);

    /// <summary>Meaningful (gameplay) differences only.</summary>
    public int MeaningfulCount => Differences.Count(d => !d.IsNoise);

    /// <summary>Differences that any two distinct saves would have (identity/clock/instance/position).</summary>
    public int NoiseCount => Differences.Count(d => d.IsNoise);

    /// <summary>True when the two saves differ only in identity/time/instance/position - no gameplay change.</summary>
    public bool AreMeaningfullyIdentical => MeaningfulCount == 0;

    public int CountOf(SaveDiffCategory category) => Differences.Count(d => d.Category == category);

    /// <summary>One-line headline, e.g. <c>"12 changed, 3 added, 1 removed"</c> or <c>"identical"</c>.</summary>
    public string Summary => AreIdentical
        ? "identical"
        : $"{ChangedCount} changed, {AddedCount} added, {RemovedCount} removed";

    /// <summary>
    /// Headline that leads with the meaningful changes and folds the rest into a count, e.g.
    /// <c>"5 gameplay differences (+ 312 identity/clock/instance/position)"</c>.
    /// </summary>
    public string MeaningfulSummary
    {
        get
        {
            if (AreIdentical) return "identical";
            if (AreMeaningfullyIdentical)
            {
                return $"no gameplay differences ({NoiseCount} identity/clock/instance/position difference(s) only)";
            }
            var core = $"{MeaningfulCount} gameplay difference(s)";
            return NoiseCount == 0 ? core : $"{core} (+ {NoiseCount} identity/clock/instance/position)";
        }
    }
}

/// <summary>Tunables for a comparison run.</summary>
public sealed class SaveDiffOptions
{
    /// <summary>
    /// Maximum number of leaf values to flatten from each save before giving up. The big
    /// Facility world save flattens into a lot of leaves; this guards against pathological
    /// memory use. Default is generous enough for real saves.
    /// </summary>
    public int MaxLeaves { get; init; } = 4_000_000;

    public static SaveDiffOptions Default { get; } = new();
}
