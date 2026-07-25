using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class WebPlayerIdentityTests
{
    [Fact]
    public async Task Workspace_identity_change_renames_rewrites_backs_up_and_reselects()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var directory = Directory.CreateTempSubdirectory("abiotic-web-identity-");
        var playerDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "PlayerData"));
        var copy = Path.Combine(playerDirectory.FullName, Path.GetFileName(source));
        File.Copy(source, copy);
        try
        {
            using var workspace = CreateWorkspace();
            await workspace.OpenAsync(directory.FullName);
            await workspace.SelectAsync(copy);

            await workspace.ChangeSelectedPlayerIdentifierAsync("web-parity-test");

            Assert.NotNull(workspace.Current?.PlayerSession);
            Assert.Equal("Player_web-parity-test.sav", workspace.Current!.SelectedSave!.Name);
            Assert.Equal("web-parity-test", workspace.Current.PlayerSession!.SteamIdentifier);
            Assert.True(File.Exists(copy + ".bak"));
            Assert.False(File.Exists(copy));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Workspace_identity_change_refuses_unsaved_player_edits()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var source = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        var directory = Directory.CreateTempSubdirectory("abiotic-web-identity-dirty-");
        var copy = Path.Combine(directory.FullName, Path.GetFileName(source));
        File.Copy(source, copy);
        try
        {
            using var workspace = CreateWorkspace();
            await workspace.OpenAsync(directory.FullName);
            await workspace.SelectAsync(copy);
            workspace.Current!.PlayerSession!.Vitals.Money++;

            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.ChangeSelectedPlayerIdentifierAsync("blocked-test"));
            Assert.True(File.Exists(copy));
        }
        finally { directory.Delete(recursive: true); }
    }

    private static SaveWorkspaceSessionService CreateWorkspace() => new(
        new RecipeVocabularyService(), new ProgressionVocabularyService(),
        new CodexVocabularyService());
}
