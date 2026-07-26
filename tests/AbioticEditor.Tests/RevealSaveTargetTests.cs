using AbioticEditor.Web.Components.Shared;

namespace AbioticEditor.Tests;

/// <summary>
/// "Show in File Explorer" on a save in the sidebar. The Game Pass case is the one that matters:
/// those worlds are edited through a temp copy unpacked from the Xbox container, so revealing
/// the save's own path would open a throwaway folder that the editor deletes when it closes.
/// </summary>
public class RevealSaveTargetTests
{
    private const string SavePath = @"C:\temp\AbioticEditor\GamePass\abc123\WorldSave_Facility.sav";
    private const string Container = @"C:\Users\Someone\AppData\Local\Packages\PlayStack.AbioticFactor_3wcqaesafpzfy\SystemAppData\wgs\000901FB9727E122_0000";

    [Fact]
    public void An_ordinary_save_reveals_the_save_file_itself()
    {
        var steamSave = @"C:\Users\Someone\AppData\Local\AbioticFactor\Saved\SaveGames\765611\Worlds\Cascade\WorldSave_Facility.sav";

        Assert.Equal(steamSave, WorkspaceShell.ResolveRevealTarget(null, steamSave));
    }

    [Fact]
    public void A_game_pass_save_reveals_the_container_folder_not_the_temp_copy()
    {
        var target = WorkspaceShell.ResolveRevealTarget(Container, SavePath);

        Assert.Equal(Container, target);
        Assert.DoesNotContain("GamePass", target, StringComparison.Ordinal);
        Assert.DoesNotContain("temp", target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A blank folder is not a folder; fall back rather than opening nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_container_falls_back_to_the_save_path(string container)
        => Assert.Equal(SavePath, WorkspaceShell.ResolveRevealTarget(container, SavePath));
}
