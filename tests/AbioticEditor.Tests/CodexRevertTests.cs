using System.IO;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Tests;

public class CodexRevertTests
{
    [Fact]
    public void IsReachable_GatesJournalAndEmailRowsByTheirEmbeddedRegion()
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MF_ManufacturingOpen" };

        // A Manufacturing journal row is reachable once MF has opened.
        Assert.True(CodexRevert.IsReachable("MF_CraneGuy", reached));
        // A Reactors journal row is NOT reachable yet - MF_ManufacturingOpen doesn't imply
        // Reactors_FirstContact is set.
        Assert.False(CodexRevert.IsReachable("Reactors_AbeJanet", reached));
        // An email with the region past the "Email_" marker is gated the same way.
        Assert.False(CodexRevert.IsReachable("Email_Reactors_Waterbot", reached));
        // Rows with no recognised region (side content, generic emails) are never gated.
        Assert.True(CodexRevert.IsReachable("Email_Random_IsWrestlingReal", reached));
    }

    [Fact]
    public void ClearForwardRows_DropsOnlyWhatIsNoLongerReachable()
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Office_NewGameStarted" };
        var rows = new[] { "Office_ReachedLobby", "Reactors_AbeJanet", "Email_Random_IsWrestlingReal" };

        var kept = CodexRevert.ClearForwardRows(rows, reached);

        Assert.Contains("Office_ReachedLobby", kept);
        Assert.Contains("Email_Random_IsWrestlingReal", kept);
        Assert.DoesNotContain("Reactors_AbeJanet", kept);
    }

    [Fact]
    public void ClearForwardPlayerUnlocks_OnARealSave_ClearsReactorAndResidenceRowsOnly()
    {
        // tests/fixtures/DedicatedServerSaves/Worlds/Cascade has real player saves with actual
        // Reactors/Residence journal and email rows read during play. Simulate a rewind to
        // "MF" (before the Reactors even open) and confirm those late-game rows disappear
        // while early Office/MF rows survive.
        if (Fixtures.ServerWorldsDir is null) return;
        var metaSrc = Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_MetaData.sav");
        var playerDataSrc = Path.Combine(Fixtures.ServerWorldsDir, "PlayerData");
        if (!File.Exists(metaSrc) || !Directory.Exists(playerDataSrc)) return;
        var playerFiles = Directory.GetFiles(playerDataSrc, "Player_*.sav");
        if (playerFiles.Length == 0) return;

        var dir = Directory.CreateTempSubdirectory("codex-revert");
        try
        {
            File.Copy(metaSrc, Path.Combine(dir.FullName, "WorldSave_MetaData.sav"));
            File.Copy(
                Path.Combine(Fixtures.ServerWorldsDir, "WorldSave_Facility.sav"),
                Path.Combine(dir.FullName, "WorldSave_Facility.sav"));
            var playerDataDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "PlayerData"));
            foreach (var f in playerFiles)
            {
                File.Copy(f, Path.Combine(playerDataDir.FullName, Path.GetFileName(f)));
            }

            var metaCopy = Path.Combine(dir.FullName, "WorldSave_MetaData.sav");
            var facilityCopy = Path.Combine(dir.FullName, "WorldSave_Facility.sav");

            // Find a player who actually has late-game rows to clear, before reverting.
            var candidate = Directory.GetFiles(playerDataDir.FullName, "Player_*.sav")
                .Select(PlayerSaveReader.ReadFromFile)
                .FirstOrDefault(p => p.Journals.Contains("Reactors_AbeJanet", StringComparer.OrdinalIgnoreCase));
            Assert.NotNull(candidate);

            StoryFlagSync.SyncFacilityFlags(metaCopy, "MF");
            StoryFlagSync.ClearForwardFlags(metaCopy, "MF");
            var reachedFlags = new HashSet<string>(
                WorldSaveReader.ReadFromFile(facilityCopy).Flags, StringComparer.OrdinalIgnoreCase);

            var (playersChanged, rowsRemoved, _) = CodexRevert.ClearForwardPlayerUnlocks(metaCopy, reachedFlags);
            Assert.True(playersChanged > 0);
            Assert.True(rowsRemoved > 0);

            // Re-read every player and confirm no Reactors/Residence rows remain anywhere,
            // while early Office/MF rows survive on at least one save.
            var allAfter = Directory.GetFiles(playerDataDir.FullName, "Player_*.sav")
                .Select(PlayerSaveReader.ReadFromFile).ToList();
            Assert.All(allAfter, p =>
            {
                Assert.DoesNotContain("Reactors_AbeJanet", p.Journals);
                Assert.DoesNotContain("Email_Reactors_Waterbot", p.EmailsRead);
                Assert.DoesNotContain("Res_Abe_WakemansThermalCell", p.Journals);
            });
            Assert.Contains(allAfter, p => p.Journals.Any(j => j.StartsWith("Office", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
