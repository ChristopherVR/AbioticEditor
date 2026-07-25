using AbioticEditor.Web.Services;
using Xunit;

namespace AbioticEditor.Tests;

/// <summary>
/// Toast lifetime rules: auto-dismiss after the duration, a hover pause that freezes the
/// countdown, and manual dismissal via the close button.
/// </summary>
public sealed class ToastServiceTests
{
    [Fact]
    public async Task Toast_auto_dismisses_after_its_duration()
    {
        var toasts = new ToastService();
        toasts.Show("bye", ToastKind.Information, TimeSpan.FromMilliseconds(300));
        Assert.Single(toasts.Messages);
        await WaitUntilAsync(() => toasts.Messages.Count == 0, TimeSpan.FromSeconds(5));
        Assert.Empty(toasts.Messages);
    }

    [Fact]
    public async Task Hover_pause_freezes_the_countdown_until_resumed()
    {
        var toasts = new ToastService();
        toasts.Show("stay", ToastKind.Warning, TimeSpan.FromMilliseconds(300));
        var id = toasts.Messages.Single().Id;
        toasts.Pause(id);

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Single(toasts.Messages); // paused well past its duration

        toasts.Resume(id);
        await WaitUntilAsync(() => toasts.Messages.Count == 0, TimeSpan.FromSeconds(5));
        Assert.Empty(toasts.Messages);
    }

    [Fact]
    public void Manual_dismiss_removes_the_toast_immediately()
    {
        var toasts = new ToastService();
        toasts.Show("close me", ToastKind.Error);
        var id = toasts.Messages.Single().Id;
        toasts.Dismiss(id);
        Assert.Empty(toasts.Messages);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
    }
}
