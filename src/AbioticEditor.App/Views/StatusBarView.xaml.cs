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

    private void OnSettingsTapped(object? sender, TappedEventArgs e)
        => SettingsRequested?.Invoke(this, EventArgs.Empty);
}
