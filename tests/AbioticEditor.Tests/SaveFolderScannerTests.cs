using AbioticEditor.Core.Saves;

namespace AbioticEditor.Tests;

public sealed class SaveFolderScannerTests
{
    [Fact]
    public void Scan_ExcludesSavesUnderBackupsFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "abiotic-scan-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Backups", "2024-01-01"));
        try
        {
            // Dummy (unparseable) .sav files: Scan still lists live ones (with a LoadError),
            // but must drop anything under a Backups directory entirely.
            File.WriteAllBytes(Path.Combine(root, "Player_live.sav"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(root, "Backups", "2024-01-01", "Player_old.sav"), new byte[] { 1, 2, 3, 4 });

            var results = SaveFolderScanner.Scan(root);

            Assert.Single(results);
            Assert.EndsWith("Player_live.sav", results[0].FullPath);
            Assert.DoesNotContain(results, r => r.FullPath.Contains("Backups", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
