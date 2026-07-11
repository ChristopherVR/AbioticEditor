using System.Text;

namespace AbioticEditor.Core.Compare;

/// <summary>
/// Renders a <see cref="SaveDiff"/> or <see cref="FolderDiff"/> as a Markdown report the user
/// can copy to the clipboard or save to disk. Lives in Core so the CLI and App share one
/// format. The App passes its domain-aware semantic sections in as a pre-rendered block.
/// </summary>
public static class SaveDiffReport
{
    /// <summary>
    /// Builds the Markdown report for a single file comparison. <paramref name="semanticBlock"/>
    /// is optional pre-rendered Markdown (the App's human-readable summary) inserted under the
    /// summary, ahead of the raw property diff.
    /// </summary>
    public static string ForFile(SaveDiff diff, string? semanticBlock = null, bool includeNoise = false)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var a = diff.LeftLabel ?? "A";
        var b = diff.RightLabel ?? "B";
        var sb = new StringBuilder();

        sb.Append("# Save comparison: ").Append(a).Append(" vs ").Append(b).Append("\n\n");

        sb.Append("## Summary\n\n");
        if (diff.AreIdentical)
        {
            sb.Append("The two saves are identical.\n\n");
        }
        else if (diff.AreMeaningfullyIdentical)
        {
            sb.Append("No gameplay differences (")
              .Append(diff.NoiseCount)
              .Append(" identity / clock / instance / position difference(s) only).\n\n");
        }
        else
        {
            sb.Append(diff.MeaningfulSummary).Append(".\n\n");
        }

        if (diff.LeftSaveClass != diff.RightSaveClass)
        {
            sb.Append("- Save classes differ: A = `").Append(diff.LeftSaveClass)
              .Append("`, B = `").Append(diff.RightSaveClass).Append("`\n");
        }
        if (diff.Truncated)
        {
            sb.Append("- Saves too large to flatten completely; some differences may be missing.\n");
        }
        if (diff.LeftSaveClass != diff.RightSaveClass || diff.Truncated)
        {
            sb.Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(semanticBlock))
        {
            sb.Append(semanticBlock!.TrimEnd()).Append("\n\n");
        }

        AppendRawDiff(sb, diff, includeNoise);
        return sb.ToString();
    }

    /// <summary>Builds the Markdown report for a folder comparison.</summary>
    public static string ForFolder(FolderDiff folderDiff)
    {
        ArgumentNullException.ThrowIfNull(folderDiff);

        var sb = new StringBuilder();
        sb.Append("# Folder comparison\n\n");
        sb.Append("- A: `").Append(folderDiff.LeftFolder).Append("`\n");
        sb.Append("- B: `").Append(folderDiff.RightFolder).Append("`\n\n");

        sb.Append("## Summary\n\n");
        if (folderDiff.AreIdentical)
        {
            sb.Append("The two folders are identical.\n\n");
        }
        else
        {
            sb.Append("- ").Append(folderDiff.DifferingCount).Append(" differing\n");
            sb.Append("- ").Append(folderDiff.IdenticalCount).Append(" identical\n");
            sb.Append("- ").Append(folderDiff.OnlyLeftCount).Append(" only in A\n");
            sb.Append("- ").Append(folderDiff.OnlyRightCount).Append(" only in B\n");
            if (folderDiff.ErrorCount > 0)
            {
                sb.Append("- ").Append(folderDiff.ErrorCount).Append(" error(s)\n");
            }
            sb.Append('\n');
        }

        foreach (var f in folderDiff.Files)
        {
            var tag = f.Status switch
            {
                FolderEntryStatus.Identical => "identical",
                FolderEntryStatus.Differs => "differs",
                FolderEntryStatus.OnlyLeft => "only in A",
                FolderEntryStatus.OnlyRight => "only in B",
                _ => "error",
            };
            sb.Append("### ").Append(f.RelativePath).Append("  (").Append(tag).Append(")\n\n");

            if (f.Status == FolderEntryStatus.Error)
            {
                sb.Append("Error: ").Append(f.Error).Append("\n\n");
                continue;
            }
            if (f.Status == FolderEntryStatus.Differs && f.Diff is not null)
            {
                AppendRawDiff(sb, f.Diff, includeNoise: false);
            }
        }

        return sb.ToString();
    }

    private static void AppendRawDiff(StringBuilder sb, SaveDiff diff, bool includeNoise)
    {
        if (diff.AreIdentical) return;

        var leaves = includeNoise ? diff.Differences : diff.Differences.Where(d => !d.IsNoise);

        var changed = new List<SaveLeafDiff>();
        var added = new List<SaveLeafDiff>();
        var removed = new List<SaveLeafDiff>();
        foreach (var d in leaves)
        {
            switch (d.Kind)
            {
                case SaveDiffKind.Changed: changed.Add(d); break;
                case SaveDiffKind.Added: added.Add(d); break;
                case SaveDiffKind.Removed: removed.Add(d); break;
            }
        }

        sb.Append("## Property differences\n\n");
        sb.Append('_').Append(diff.MeaningfulCount).Append(" gameplay, ")
          .Append(diff.NoiseCount).Append(" identity / clock / instance / position");
        if (!includeNoise && diff.NoiseCount > 0)
        {
            sb.Append(" (not listed)");
        }
        sb.Append("._\n\n");

        AppendList(sb, "Changed", changed, d => $"`{d.Path}`: {d.Left} -> {d.Right}");
        AppendList(sb, "Added", added, d => $"`{d.Path}`: {d.Right}");
        AppendList(sb, "Removed", removed, d => $"`{d.Path}`: {d.Left}");
    }

    private static void AppendList(StringBuilder sb, string heading, List<SaveLeafDiff> items, Func<SaveLeafDiff, string> line)
    {
        if (items.Count == 0) return;
        sb.Append("### ").Append(heading).Append(" (").Append(items.Count).Append(")\n\n");
        foreach (var d in items)
        {
            sb.Append("- ").Append(line(d)).Append('\n');
        }
        sb.Append('\n');
    }
}
