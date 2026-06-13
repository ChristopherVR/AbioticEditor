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
    }

    /// <summary>The localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    public string this[string key] => _resources.GetString(key, _culture) ?? key;

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
