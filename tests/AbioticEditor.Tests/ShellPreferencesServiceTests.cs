using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class ShellPreferencesServiceTests
{
    [Fact]
    public void Pane_preferences_are_clamped_persisted_and_reloaded()
    {
        var directory = Directory.CreateTempSubdirectory("abiotic-shell-");
        try
        {
            var path = Path.Combine(directory.FullName, "shell.json");
            var service = new ShellPreferencesService(path);
            var changes = 0;
            service.Changed += () => changes++;

            service.SetFilePaneWidth(9999);
            service.SetDetailsPaneWidth(1);
            service.ToggleFilePane();
            service.ToggleDetailsPane();

            // Clamp bounds mirror the native splitter limits: file 220-600, slot 260-680.
            Assert.Equal(600, service.State.FilePaneWidth);
            Assert.Equal(260, service.State.DetailsPaneWidth);
            Assert.True(service.State.FilePaneCollapsed);
            Assert.True(service.State.DetailsPaneCollapsed);
            Assert.Equal(4, changes);
            var reloaded = new ShellPreferencesService(path);
            Assert.Equal(service.State, reloaded.State);
        }
        finally { directory.Delete(recursive: true); }
    }
}
