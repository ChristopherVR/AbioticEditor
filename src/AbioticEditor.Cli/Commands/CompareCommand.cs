using System.CommandLine;

using AbioticEditor.Core.Compare;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>compare &lt;a&gt; &lt;b&gt;</c> - diffs two saves (or two folders of saves) property by
/// property and prints every difference. File vs file, or folder vs folder (e.g. a world
/// vs one of its backups), pairing files by their path relative to each folder root.
/// </summary>
internal static class CompareCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var leftArg = new Argument<string>("a")
        {
            Description = "First save file, or a folder of saves.",
        };
        var rightArg = new Argument<string>("b")
        {
            Description = "Second save file, or a folder of saves (must match the kind of A).",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit the full diff as JSON instead of the human-readable report.",
        };
        var limitOpt = new Option<int>("--limit")
        {
            Description = "Max differences to print per file in the text report (0 = no limit).",
            DefaultValueFactory = _ => 200,
        };
        var fullOpt = new Option<bool>("--full")
        {
            Description = "For folder comparisons, list every property difference (default: per-file summary only).",
        };
        var allOpt = new Option<bool>("--all")
        {
            Description = "Include non-gameplay differences (identity, clock, instance ids, position). "
                + "By default only meaningful gameplay changes are listed.",
        };

        var cmd = new Command("compare", "Compare two saves (or two save folders) and report all differences.");
        cmd.Arguments.Add(leftArg);
        cmd.Arguments.Add(rightArg);
        cmd.Options.Add(jsonOpt);
        cmd.Options.Add(limitOpt);
        cmd.Options.Add(fullOpt);
        cmd.Options.Add(allOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Execute(
            parseResult.GetValue(leftArg),
            parseResult.GetValue(rightArg),
            parseResult.GetValue(jsonOpt),
            parseResult.GetValue(limitOpt),
            parseResult.GetValue(fullOpt),
            parseResult.GetValue(allOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Execute(string? a, string? b, bool json, int limit, bool full, bool all, bool quiet)
    {
        var leftIsDir = a is not null && Directory.Exists(a);
        var rightIsDir = b is not null && Directory.Exists(b);

        if (leftIsDir || rightIsDir)
        {
            if (!(leftIsDir && rightIsDir))
            {
                throw new CliUserErrorException("compare both files or both folders, not a mix.");
            }
            return CompareFolders(Path.GetFullPath(a!), Path.GetFullPath(b!), json, limit, full, all, quiet);
        }

        var leftPath = Cli.RequireFile(a, "first save (a)");
        var rightPath = Cli.RequireFile(b, "second save (b)");
        return CompareSingle(leftPath, rightPath, json, limit, all, quiet);
    }

    private static int CompareSingle(string leftPath, string rightPath, bool json, int limit, bool all, bool quiet)
    {
        var diff = SaveComparer.CompareFiles(leftPath, rightPath);

        if (json)
        {
            Cli.WriteJson(ToJsonPayload(diff));
            return Cli.Ok;
        }

        Cli.Info(quiet, $"A: {leftPath}");
        Cli.Info(quiet, $"B: {rightPath}");
        if (!string.Equals(diff.LeftSaveClass, diff.RightSaveClass, StringComparison.Ordinal))
        {
            Cli.Warn($"save classes differ: A='{diff.LeftSaveClass}' B='{diff.RightSaveClass}'");
        }
        Cli.Info(quiet, $"Result: {diff.MeaningfulSummary}");
        if (diff.Truncated)
        {
            Cli.Warn("the saves were too large to flatten completely; some differences may be missing.");
        }
        Cli.Info(quiet, string.Empty);
        PrintDifferences(diff, limit, all);
        // Differences are not an error; identical vs differing is read from stdout/JSON.
        return Cli.Ok;
    }

    private static int CompareFolders(string leftDir, string rightDir, bool json, int limit, bool full, bool all, bool quiet)
    {
        var folderDiff = SaveFolderComparer.Compare(leftDir, rightDir);

        if (json)
        {
            Cli.WriteJson(new
            {
                leftFolder = folderDiff.LeftFolder,
                rightFolder = folderDiff.RightFolder,
                identical = folderDiff.IdenticalCount,
                differing = folderDiff.DifferingCount,
                onlyLeft = folderDiff.OnlyLeftCount,
                onlyRight = folderDiff.OnlyRightCount,
                errors = folderDiff.ErrorCount,
                files = folderDiff.Files.Select(f => new
                {
                    path = f.RelativePath,
                    status = f.Status.ToString(),
                    differences = f.DifferenceCount,
                    meaningful = f.Diff?.MeaningfulCount ?? 0,
                    error = f.Error,
                    diff = full && f.Diff is not null ? ToJsonPayload(f.Diff) : null,
                }),
            });
            return Cli.Ok;
        }

        Cli.Info(quiet, $"A: {leftDir}");
        Cli.Info(quiet, $"B: {rightDir}");
        Cli.Info(quiet,
            $"Result: {folderDiff.DifferingCount} differing, {folderDiff.IdenticalCount} identical, "
            + $"{folderDiff.OnlyLeftCount} only in A, {folderDiff.OnlyRightCount} only in B"
            + (folderDiff.ErrorCount > 0 ? $", {folderDiff.ErrorCount} error(s)" : string.Empty));
        Cli.Info(quiet, string.Empty);

        foreach (var f in folderDiff.Files)
        {
            var tag = f.Status switch
            {
                FolderEntryStatus.Identical => "  ==",
                FolderEntryStatus.Differs => "  ~~",
                FolderEntryStatus.OnlyLeft => "  A only",
                FolderEntryStatus.OnlyRight => "  B only",
                FolderEntryStatus.Error => "  !! ",
                _ => "  ? ",
            };
            var detail = f.Status switch
            {
                FolderEntryStatus.Differs when f.Diff is not null =>
                    $" ({f.Diff.MeaningfulCount} gameplay, {f.DifferenceCount} total)",
                FolderEntryStatus.Differs => $" ({f.DifferenceCount} difference(s))",
                FolderEntryStatus.Error => $" ({f.Error})",
                _ => string.Empty,
            };
            Console.WriteLine($"{tag}  {f.RelativePath}{detail}");

            if (full && f.Status == FolderEntryStatus.Differs && f.Diff is not null)
            {
                PrintDifferences(f.Diff, limit, all, indent: "        ");
                Console.WriteLine();
            }
        }

        return Cli.Ok;
    }

    private static void PrintDifferences(SaveDiff diff, int limit, bool all, string indent = "  ")
    {
        var rows = (all ? diff.Differences : diff.Differences.Where(d => !d.IsNoise)).ToList();

        var shown = 0;
        foreach (var d in rows)
        {
            if (limit > 0 && shown >= limit)
            {
                Console.WriteLine($"{indent}... {rows.Count - shown} more (raise --limit to see all)");
                break;
            }
            shown++;

            // Tag non-gameplay lines so it's clear why they're "noise" when --all is on.
            var tag = d.IsNoise ? $" [{d.Category.ToString().ToLowerInvariant()}]" : string.Empty;
            switch (d.Kind)
            {
                case SaveDiffKind.Changed:
                    Console.WriteLine($"{indent}~ {d.Path}: {d.Left} -> {d.Right}{tag}");
                    break;
                case SaveDiffKind.Added:
                    Console.WriteLine($"{indent}+ {d.Path}: {d.Right}{tag}");
                    break;
                case SaveDiffKind.Removed:
                    Console.WriteLine($"{indent}- {d.Path}: {d.Left}{tag}");
                    break;
            }
        }

        if (!all && diff.NoiseCount > 0)
        {
            Console.WriteLine(
                $"{indent}({diff.NoiseCount} identity/clock/instance/position difference(s) hidden; use --all to show)");
        }
    }

    private static object ToJsonPayload(SaveDiff diff) => new
    {
        leftLabel = diff.LeftLabel,
        rightLabel = diff.RightLabel,
        leftSaveClass = diff.LeftSaveClass,
        rightSaveClass = diff.RightSaveClass,
        identical = diff.AreIdentical,
        meaningfullyIdentical = diff.AreMeaningfullyIdentical,
        truncated = diff.Truncated,
        changed = diff.ChangedCount,
        added = diff.AddedCount,
        removed = diff.RemovedCount,
        meaningful = diff.MeaningfulCount,
        noise = diff.NoiseCount,
        differences = diff.Differences.Select(d => new
        {
            kind = d.Kind.ToString(),
            category = d.Category.ToString(),
            path = d.Path,
            left = d.Left,
            right = d.Right,
            type = d.Type,
        }),
    };
}
