using System.Globalization;
using AbioticEditor.App.Services;
using Microsoft.Maui.Controls.Xaml;

namespace AbioticEditor.App.Controls;

/// <summary>
/// XAML markup extension for localized composite text: the resx value for <see cref="Key"/> is
/// used as a <c>string.Format</c> pattern and <see cref="Arg0"/>..<see cref="Arg2"/> supply the
/// placeholders. Replaces hardcoded English in binding <c>StringFormat</c>s
/// (<c>StringFormat='Family: {0}'</c> becomes
/// <c>{loc:LocalizeFormat WorldPets_FamilyFormat, Arg0={Binding ...}}</c>) while keeping the
/// text live on a language switch, because the format string itself is a binding to the
/// <see cref="LocalizationResourceManager"/> indexer.
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class LocalizeFormatExtension : IMarkupExtension<BindingBase>
{
    /// <summary>The resource key whose value is the format pattern.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Binding for placeholder <c>{0}</c>.</summary>
    public BindingBase? Arg0 { get; set; }

    /// <summary>Binding for placeholder <c>{1}</c>.</summary>
    public BindingBase? Arg1 { get; set; }

    /// <summary>Binding for placeholder <c>{2}</c>.</summary>
    public BindingBase? Arg2 { get; set; }

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
    {
        var multi = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = FormatConverter.Instance,
        };
        // Binding [0] is always the localized pattern; it re-resolves on language change.
        multi.Bindings.Add(new Binding
        {
            Mode = BindingMode.OneWay,
            Path = $"[{Key}]",
            Source = LocalizationResourceManager.Instance,
        });
        foreach (var arg in new[] { Arg0, Arg1, Arg2 })
        {
            if (arg is not null)
            {
                multi.Bindings.Add(arg);
            }
        }
        return multi;
    }

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ((IMarkupExtension<BindingBase>)this).ProvideValue(serviceProvider);

    private sealed class FormatConverter : IMultiValueConverter
    {
        public static FormatConverter Instance { get; } = new();

        public object? Convert(object?[]? values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is null || values.Length == 0 || values[0] is not string pattern)
            {
                return string.Empty;
            }
            var args = values.Skip(1).ToArray();
            try
            {
                return string.Format(LocalizationResourceManager.Instance.CurrentCulture, pattern, args);
            }
            catch (FormatException)
            {
                // A mistranslated pattern (bad placeholder) must never crash a binding;
                // show the raw pattern so the problem is visible and reportable.
                return pattern;
            }
        }

        public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
