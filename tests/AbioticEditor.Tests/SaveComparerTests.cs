using System.IO;

using AbioticEditor.Core.Compare;

namespace AbioticEditor.Tests;

public class SaveComparerTests
{
    /// <summary>Server root holding the live Worlds/ and the Backups/ snapshots.</summary>
    private static string? ServerRoot =>
        Fixtures.ServerWorldsDir is { } w ? Path.GetFullPath(Path.Combine(w, "..", "..")) : null;

    private static string? BackupDir(int n) =>
        ServerRoot is { } root ? Path.Combine(root, "Backups", "Cascade", n.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;

    [Fact]
    public void Normalize_StripsBlueprintHashSuffix()
    {
        Assert.Equal("Hunger", SavePropertyFlattener.Normalize("Hunger_2_A6C5CC6E41993323B119FA9E0B3894CA"));
        Assert.Equal("CurrentMoney", SavePropertyFlattener.Normalize("CurrentMoney_85_7425E5BF43364C11279E4C8C26F5A7CA"));
        // No suffix -> unchanged.
        Assert.Equal("SaveIdentifier", SavePropertyFlattener.Normalize("SaveIdentifier"));
    }

    [Fact]
    public void CompareFiles_SameFileTwice_IsIdentical()
    {
        var metadata = Fixtures.CascadeDir is { } dir ? Path.Combine(dir, "WorldSave_MetaData.sav") : null;
        if (metadata is null || !File.Exists(metadata)) return; // fixture missing -> skip

        var diff = SaveComparer.CompareFiles(metadata, metadata);
        Assert.True(diff.AreIdentical, $"a file compared with itself should be identical, got: {diff.Summary}");
        Assert.Empty(diff.Differences);
        Assert.False(diff.Truncated);
    }

    [Fact]
    public void CompareFiles_DifferentBackupSnapshots_ReportsDifferences()
    {
        var a = BackupDir(1);
        var b = BackupDir(5);
        if (a is null || !Directory.Exists(a) || b is null || !Directory.Exists(b)) return;

        var left = Path.Combine(a, "WorldSave_Facility.sav");
        var right = Path.Combine(b, "WorldSave_Facility.sav");
        if (!File.Exists(left) || !File.Exists(right)) return;

        var diff = SaveComparer.CompareFiles(left, right);

        Assert.False(diff.AreIdentical);
        Assert.NotEmpty(diff.Differences);
        // Same save class on both sides.
        Assert.Equal(diff.LeftSaveClass, diff.RightSaveClass);
        // Every reported diff carries a path and the correct side populated.
        foreach (var d in diff.Differences)
        {
            Assert.False(string.IsNullOrEmpty(d.Path));
            switch (d.Kind)
            {
                case SaveDiffKind.Added: Assert.Null(d.Left); Assert.NotNull(d.Right); break;
                case SaveDiffKind.Removed: Assert.NotNull(d.Left); Assert.Null(d.Right); break;
                case SaveDiffKind.Changed: Assert.NotEqual(d.Left, d.Right); break;
            }
        }
    }

    [Fact]
    public void CompareFiles_DifferentPlayers_DiffersOnIdentity()
    {
        var dir = Fixtures.CascadeDir is { } c ? Path.Combine(c, "PlayerData") : null;
        if (dir is null || !Directory.Exists(dir)) return;

        var players = Directory.GetFiles(dir, "Player_*.sav");
        if (players.Length < 2) return;

        var diff = SaveComparer.CompareFiles(players[0], players[1]);
        Assert.False(diff.AreIdentical);
        // Two different accounts must differ on the stored SaveIdentifier (steamid64).
        Assert.Contains(diff.Differences, d => d.Path.Contains("SaveIdentifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Classify_FoldsIdentityInstanceAndPositionOutOfMeaningful()
    {
        var dir = Fixtures.CascadeDir is { } c ? Path.Combine(c, "PlayerData") : null;
        if (dir is null || !Directory.Exists(dir)) return;

        var players = Directory.GetFiles(dir, "Player_*.sav");
        if (players.Length < 2) return;

        var diff = SaveComparer.CompareFiles(players[0], players[1]);

        // The SteamID is an identity difference, not a gameplay change.
        Assert.All(
            diff.Differences.Where(d => d.Path.EndsWith("SaveIdentifier", StringComparison.Ordinal)),
            d => Assert.Equal(SaveDiffCategory.Identity, d.Category));

        // Every per-instance AssetID handle is classified as noise (InstanceId), never gameplay.
        Assert.All(
            diff.Differences.Where(d => d.Path.EndsWith("AssetID", StringComparison.Ordinal)),
            d => Assert.Equal(SaveDiffCategory.InstanceId, d.Category));

        // Player world position is classified as Position, not a gameplay change.
        Assert.All(
            diff.Differences.Where(d => d.Path.Contains("Location", StringComparison.Ordinal)),
            d => Assert.Equal(SaveDiffCategory.Position, d.Category));

        // Folding noise away leaves strictly fewer differences, and the classifier never
        // double-counts: meaningful + noise = total.
        Assert.Equal(diff.Differences.Count, diff.MeaningfulCount + diff.NoiseCount);
        Assert.True(diff.NoiseCount > 0, "expected identity/instance/position noise between two players");
    }

    [Fact]
    public void CompareFolder_PairsByRelativePath_AndFlagsDifferences()
    {
        var a = BackupDir(1);
        var b = BackupDir(5);
        if (a is null || !Directory.Exists(a) || b is null || !Directory.Exists(b)) return;

        var folderDiff = SaveFolderComparer.Compare(a, b);

        Assert.NotEmpty(folderDiff.Files);
        Assert.Equal(0, folderDiff.ErrorCount);
        // The two snapshots are of the same world, so most files pair up and at least one
        // paired save differs.
        Assert.True(folderDiff.DifferingCount > 0, "expected at least one differing save between snapshots");
        // Backup 5 reached the post-boss island region (WorldSave_V_ISLAND) that backup 1
        // never had: the comparer must flag it as only-on-the-right.
        Assert.Contains(folderDiff.Files, f =>
            f.Status == FolderEntryStatus.OnlyRight &&
            f.RelativePath.Contains("V_ISLAND", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CompareFolder_IdenticalSnapshot_ReportsAllIdentical()
    {
        var a = BackupDir(1);
        if (a is null || !Directory.Exists(a)) return;

        var folderDiff = SaveFolderComparer.Compare(a, a);
        Assert.True(folderDiff.AreIdentical, "a folder compared with itself should be fully identical");
        Assert.All(folderDiff.Files, f => Assert.Equal(FolderEntryStatus.Identical, f.Status));
    }
}
