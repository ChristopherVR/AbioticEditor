using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class HostSpoilerPreferencesTests
{
    [Fact]
    public void Reseal_clears_persisted_reveals_without_changing_protection()
    {
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditor.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "spoilers.json");
        try
        {
            var preferences = new HostSpoilerPreferences(path);
            preferences.Enabled = false;
            preferences.Reveal("story.ending");
            preferences.Reveal("trader.late-game");
            preferences.Reseal();

            var reloaded = new HostSpoilerPreferences(path);
            Assert.False(reloaded.Enabled);
            Assert.Equal(0, reloaded.RevealedCount);
            Assert.False(reloaded.IsRevealed("story.ending"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
