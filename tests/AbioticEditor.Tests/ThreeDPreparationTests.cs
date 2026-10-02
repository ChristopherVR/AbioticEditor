using AbioticEditor.Core.Assets;
using AbioticEditor.Plugins.Scene;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// Getting the 3D view ready up front: its progress, where it shows, and that it does not hold up
/// the 3D view while it reads (a big level holds the game files for minutes).
/// </summary>
public sealed class ThreeDPreparationTests
{
    [Fact]
    public void Progress_counts_the_level_in_hand_by_how_far_into_it()
    {
        Assert.Equal(0, ScenePreparationProgress.None.Fraction);
        Assert.Equal(0.25, new ScenePreparationProgress(1, 4, null, 0, 0).Fraction, 3);
        Assert.Equal(0.375, new ScenePreparationProgress(1, 4, "Facility", 50, 100).Fraction, 3);
        Assert.Equal(1, new ScenePreparationProgress(4, 4, null, 0, 0).Fraction, 3);
    }

    [Fact]
    public void Area_names_and_running_time_read_plainly()
    {
        Assert.Equal("Facility Dam Hydroplant", SceneModelHostService.AreaName("Facility_Dam_Hydroplant"));
        Assert.Equal(string.Empty, SceneModelHostService.AreaName(null));
        Assert.Equal("3:07", SceneModelHostService.ElapsedText(TimeSpan.FromSeconds(187)));
        Assert.Equal("1:02:03", SceneModelHostService.ElapsedText(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Progress_shows_in_the_footer_on_every_page_and_on_the_start_page()
    {
        Assert.Contains("<ThreeDPrepareStatus />", UiSource.ReadAllText("Components", "Pages", "MainLayout.razor"), StringComparison.Ordinal);
        var card = UiSource.ReadAllText("Components", "Pages", "ThreeDPrepareCard.razor");
        Assert.Contains("data-prepare=\"current\"", card, StringComparison.Ordinal);
        Assert.Contains("data-prepare=\"stop\"", card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_long_read_steps_aside_for_others_waiting_on_the_game_files()
    {
        using var assets = GameAssetProvider.CreateForLocalInstall();
        if (assets is not { HasMappings: true }) return;

        using var otherWaiting = new ManualResetEventSlim();
        using var otherDone = new ManualResetEventSlim();
        var longReadFinished = false;
        var longRead = Task.Run(() => assets.UseFileProvider(_ =>
        {
            // A level read: many steps, stepping aside between them, until the other read got in.
            var deadline = DateTime.UtcNow.AddSeconds(20);
            otherWaiting.Wait(TimeSpan.FromSeconds(10));
            while (!otherDone.IsSet && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(5);
                assets.YieldToWaitingReaders();
            }
            longReadFinished = true;
            return 0;
        }));
        Thread.Sleep(100);
        var other = Task.Run(() =>
        {
            otherWaiting.Set();
            assets.UseFileProvider(_ => 0);
            otherDone.Set();
        });

        var first = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(first == other, "The other read never got the game files while the long read ran.");
        await longRead.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(longReadFinished);
    }
}
