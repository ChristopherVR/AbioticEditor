using AbioticEditor.Core.GamePass;

namespace AbioticEditor.Tests;

/// <summary>
/// Re-homing a player to another account id inside a Game Pass world.
///
/// <para>The container keeps its own list of the saves it holds, and the repack walks that list
/// looking on disk for each recorded name. Renaming only the unpacked file therefore lost the
/// change silently: the repack stopped finding it and the old player came back under the old id.
/// The name has to change in the container too.</para>
/// </summary>
public class GamePassRenamePlayerTests
{
    private static void WithFixtureCopy(Action<GamePassSaveSet, string> body)
    {
        if (Fixtures.GamePassWgsDir is null) return; // fixture absent - skip
        if (!OodleCodec.IsAvailable) return;         // no native Oodle (non-Windows) - skip

        var work = Directory.CreateTempSubdirectory("gp-rename");
        try
        {
            CopyTree(Fixtures.GamePassWgsDir!, work.FullName);
            body(GamePassSaveSet.Open(work.FullName), work.FullName);
        }
        finally
        {
            work.Delete(recursive: true);
            if (Directory.Exists(work.FullName + ".bak")) Directory.Delete(work.FullName + ".bak", recursive: true);
        }
    }

    [Fact]
    public void Renaming_a_player_changes_the_name_the_container_records()
    {
        WithFixtureCopy((set, dir) =>
        {
            var player = set.Entries().First(entry => entry.Kind == GamePassSaveKind.Player);
            var oldName = player.FileName;
            const string NewName = "Player_76561199999999999.sav";

            Assert.True(set.RenamePlayerSave(player.ContainerName, oldName, NewName));

            // Reopened from disk: the rename has to be in the written container, not just memory.
            var reopened = GamePassSaveSet.Open(dir);
            var players = reopened.Entries().Where(entry => entry.Kind == GamePassSaveKind.Player).ToList();
            Assert.Contains(players, entry => entry.FileName == NewName);
            Assert.DoesNotContain(players, entry => entry.FileName == oldName);
        });
    }

    /// <summary>The save itself must come through the rename untouched.</summary>
    [Fact]
    public void Renaming_a_player_keeps_its_contents()
    {
        WithFixtureCopy((set, dir) =>
        {
            var player = set.Entries().First(entry => entry.Kind == GamePassSaveKind.Player);
            var before = set.ReadSave(player);

            set.RenamePlayerSave(player.ContainerName, player.FileName, "Player_76561199999999999.sav");

            var reopened = GamePassSaveSet.Open(dir);
            var renamed = reopened.Entries().First(entry => entry.Kind == GamePassSaveKind.Player);
            Assert.Equal(before, reopened.ReadSave(renamed));
        });
    }

    /// <summary>
    /// Renaming onto something already in the world would have two saves claiming one name.
    /// Checked against every member, not just players: colliding with the world's own metadata
    /// save would be just as destructive.
    /// </summary>
    [Fact]
    public void Renaming_onto_a_name_already_in_the_world_is_refused()
    {
        WithFixtureCopy((set, _) =>
        {
            var entries = set.Entries();
            var player = entries.First(entry => entry.Kind == GamePassSaveKind.Player);
            var taken = entries.First(entry => entry.ContainerName == player.ContainerName && entry.FileName != player.FileName);

            Assert.Throws<InvalidOperationException>(
                () => set.RenamePlayerSave(player.ContainerName, player.FileName, taken.FileName));
        });
    }

    /// <summary>The extension is the editor's convention; inside the bundle it is not there.</summary>
    [Fact]
    public void The_sav_extension_is_optional_on_both_names()
    {
        WithFixtureCopy((set, dir) =>
        {
            var player = set.Entries().First(entry => entry.Kind == GamePassSaveKind.Player);
            var bare = player.FileName[..^4];

            Assert.True(set.RenamePlayerSave(player.ContainerName, bare, "Player_76561199999999999"));

            var reopened = GamePassSaveSet.Open(dir);
            Assert.Contains(reopened.Entries(), entry => entry.FileName == "Player_76561199999999999.sav");
        });
    }

    [Fact]
    public void Renaming_to_the_same_name_does_nothing()
    {
        WithFixtureCopy((set, _) =>
        {
            var player = set.Entries().First(entry => entry.Kind == GamePassSaveKind.Player);

            Assert.False(set.RenamePlayerSave(player.ContainerName, player.FileName, player.FileName));
        });
    }

    [Fact]
    public void Renaming_a_player_that_is_not_there_reports_nothing_was_renamed()
    {
        WithFixtureCopy((set, _) =>
        {
            var container = set.Entries().First(entry => entry.Kind == GamePassSaveKind.Player).ContainerName;

            Assert.False(set.RenamePlayerSave(container, "Player_00000000000000000.sav", "Player_11111111111111111.sav"));
        });
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.Ordinal));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, dest, StringComparison.Ordinal), overwrite: true);
    }
}
