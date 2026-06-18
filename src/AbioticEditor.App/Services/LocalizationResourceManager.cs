using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace AbioticEditor.App.Services;

/// <summary>
/// Live, observable wrapper around the app's <c>.resx</c> string table. Look up a key with the
/// indexer; the active <see cref="CurrentCulture"/> chooses which language's value is returned
/// (falling back to the neutral/English file for anything not translated). Changing the culture
/// raises <c>PropertyChanged("Item[]")</c>, which refreshes every <c>{loc:Localize}</c> binding
/// in the open UI so the language switches live without an app restart.
/// </summary>
public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    /// <summary>The single instance bindings and the markup extension point at.</summary>
    public static LocalizationResourceManager Instance { get; } = new();

    // Manifest name = RootNamespace + folder path. AppResources.<code>.resx are compiled into
    // per-culture satellite assemblies the ResourceManager resolves automatically.
    private readonly ResourceManager _resources = new(
        "AbioticEditor.App.Localization.AppResources", typeof(LocalizationResourceManager).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private LocalizationResourceManager()
    {
        // A plugin localization pack (or a code/JS plugin's AddLocalization) can land after the
        // first strings resolve, so refresh every binding when the contributed table changes.
        Core.Plugins.PluginLocalizations.Changed += OnPluginLocalizationsChanged;
    }

    /// <summary>
    /// The localized string for <paramref name="key"/>. Resolution order: a plugin-contributed
    /// value for the active culture (so a pack can add a new language or override a key), then the
    /// built-in resx (falling back to the neutral/English file), then the key itself if nothing
    /// supplies it.
    /// </summary>
    public string this[string key]
    {
        get
        {
            var contributed = Core.Plugins.PluginLocalizations.Lookup(_culture.Name, key);
            if (contributed is not null)
            {
                return contributed;
            }
            return _resources.GetString(key, _culture) ?? key;
        }
    }

    private void OnPluginLocalizationsChanged()
    {
        // Binding updates must run on the UI thread; plugin loads can complete off it.
        if (MainThread.IsMainThread)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(
                () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")));
        }
    }

    /// <summary>The active UI culture. Setting it re-localizes the whole open UI.</summary>
    public CultureInfo CurrentCulture
    {
        get => _culture;
        set
        {
            if (Equals(_culture, value))
            {
                return;
            }
            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            // "Item[]" is the binding path token for an indexer - this refreshes every [key] binding.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
