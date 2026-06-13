namespace AbioticEditor.App.Views;

/// <summary>Bottom status bar: status text, logging state and the SETTINGS button.</summary>
public partial class StatusBarView : ContentView
{
    public StatusBarView()
    {
        InitializeComponent();
    }

    /// <summary>Raised by SETTINGS; the page owns modal navigation + theme rebuild.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised by COMPARE; the page owns modal navigation.</summary>
    public event EventHandler? CompareRequested;

    private void OnSettingsTapped(object? sender, TappedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnCompareTapped(object? sender, TappedEventArgs e)
        => CompareRequested?.Invoke(this, EventArgs.Empty);
}
