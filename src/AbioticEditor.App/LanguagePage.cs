using AbioticEditor.App.Services;

namespace AbioticEditor.App;

/// <summary>
/// Language chooser. Shown automatically on first run (no language stored yet), pre-selecting
/// the OS language, and also reachable from Settings. Built in code (like SettingsPage); picking
/// a language applies it live and rebuilds this page so its own text switches immediately.
/// </summary>
public sealed class LanguagePage : ContentPage
{
    private string _selected;

    // The language code on entry: lets us tell whether the user actually changed it, so we only
    // pay for a full root rebuild (to re-localize the live UI) when something really changed.
    private readonly string _initialCode;

    // First-run path: this page is the top modal over the main page, so on continue it must
    // rebuild the root tree itself to re-localize. When opened from Settings this is false -
    // SettingsPage owns the rebuild on close (it must not be torn down while still open).
    private readonly bool _rebuildRootOnDone;

    public LanguagePage(bool rebuildRootOnDone = false)
    {
        _rebuildRootOnDone = rebuildRootOnDone;
        _initialCode = LocalizationService.CurrentCode;
        _selected = LocalizationService.HasChosenLanguage
            ? LocalizationService.CurrentCode
            : LocalizationService.OsDefaultCode;
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        Build();
    }

    private static string T(string key) => LocalizationResourceManager.Instance[key];

    private void Build()
    {
        Title = T("Settings_Language");
        var accent = (Color)Application.Current!.Resources["AfAccentOrange"];
        var muted = (Color)Application.Current.Resources["AfTextSecondary"];
        var panel = (Color)Application.Current.Resources["AfPanelElevated"];
        var primary = (Color)Application.Current.Resources["AfTextPrimary"];
        var onAccent = (Color)Application.Current.Resources["AfTextOnAccent"];

        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(28, 32),
            Spacing = 8,
            MaximumWidthRequest = 520,
        };
        stack.Children.Add(new Label
        {
            Text = T("Language_Title"),
            FontFamily = "OpenSansSemibold",
            FontSize = 22,
            CharacterSpacing = 1,
        });
        stack.Children.Add(new Label
        {
            Text = T("Language_Subtitle"),
            FontSize = 12,
            TextColor = muted,
            Margin = new Thickness(0, 0, 0, 12),
        });

        foreach (var lang in LocalizationService.Available)
        {
            var isSelected = lang.Code == _selected;
            var isOsDefault = lang.Code == LocalizationService.OsDefaultCode;
            var name = isOsDefault ? $"{lang.NativeName}  ·  {T("Language_SystemDefault")}" : lang.NativeName;

            var button = new Button
            {
                Text = (isSelected ? "●  " : "○  ") + name,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = isSelected ? accent : panel,
                TextColor = isSelected ? onAccent : primary,
            };
            var code = lang.Code;
            button.Clicked += (_, _) =>
            {
                _selected = code;
                LocalizationService.SetLanguage(code); // applies live + persists
                Build();                                // rebuild so this page re-localizes too
            };
            stack.Children.Add(button);
        }

        var continueButton = new Button { Text = T("Language_Continue"), Margin = new Thickness(0, 16, 0, 0) };
        continueButton.Clicked += async (_, _) =>
        {
            LocalizationService.SetLanguage(_selected); // confirm (persists even if unchanged)
            if (Navigation.ModalStack.Count > 0)
            {
                await Navigation.PopModalAsync();
            }
            // The {loc:Localize} bindings don't refresh live on a change of culture, so rebuild
            // the page tree to re-localize everything - but only when we own the rebuild and the
            // language actually changed (from Settings, SettingsPage rebuilds on close instead).
            if (_rebuildRootOnDone && _selected != _initialCode)
            {
                App.RebuildRootPage();
            }
        };
        stack.Children.Add(continueButton);

        Content = new ScrollView { Content = stack };
    }
}
