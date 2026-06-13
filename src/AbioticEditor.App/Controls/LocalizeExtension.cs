using AbioticEditor.App.Services;
using Microsoft.Maui.Controls.Xaml;

namespace AbioticEditor.App.Controls;

/// <summary>
/// XAML markup extension for localized text: <c>Text="{loc:Localize Settings_Title}"</c>. It
/// binds to the shared <see cref="LocalizationResourceManager"/> indexer, so the text updates
/// live when the language changes (no page rebuild needed). Add
/// <c>xmlns:loc="clr-namespace:AbioticEditor.App.Controls"</c> to a page to use it.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class LocalizeExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The resource key to look up.</summary>
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
        => new Binding
        {
            Mode = BindingMode.OneWay,
            Path = $"[{Key}]",
            Source = LocalizationResourceManager.Instance,
        };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ((IMarkupExtension<BindingBase>)this).ProvideValue(serviceProvider);
}
