namespace AbioticEditor.App.Views;

/// <summary>
/// Full-screen modal that blocks all interaction with whatever is underneath it until the
/// caller's work finishes - used for a game-data reload, which used to run behind an inline
/// spinner that a user could dodge around (switch Settings tabs, hit CLOSE, back out) mid-reload
/// and land the picker/state in an inconsistent spot. Pushing this on top makes that impossible:
/// nothing beneath it is reachable, and the hardware/Escape back button is swallowed.
/// </summary>
public sealed class BlockingBusyPage : ContentPage
{
    public BlockingBusyPage(string message)
    {
        BackgroundColor = ModalChrome.Col("AfPageBackground").WithAlpha(0.97f);
        var spinner = new ActivityIndicator
        {
            IsRunning = true,
            Color = ModalChrome.Col("AfAccentOrange"),
            HeightRequest = 40,
            WidthRequest = 40,
        };
        var label = new Label
        {
            Text = message,
            Style = ModalChrome.St("AfFieldValue"),
            HorizontalTextAlignment = TextAlignment.Center,
        };
        Content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 16,
            Padding = new Thickness(32),
            Children = { spinner, label },
        };
    }

    /// <summary>Swallows the hardware/Escape back button so the overlay can't be dismissed early.</summary>
    protected override bool OnBackButtonPressed() => true;

    /// <summary>
    /// Pushes this overlay on top of <paramref name="host"/>'s modal stack, runs
    /// <paramref name="work"/>, then pops it again - even if the work throws, so the caller's own
    /// error handling still runs with the overlay gone.
    /// </summary>
    public static async Task RunAsync(Page host, string message, Func<Task> work)
    {
        var overlay = new BlockingBusyPage(message);
        await host.Navigation.PushModalAsync(overlay, animated: false);
        try
        {
            await work();
        }
        finally
        {
            await host.Navigation.PopModalAsync(animated: false);
        }
    }
}
