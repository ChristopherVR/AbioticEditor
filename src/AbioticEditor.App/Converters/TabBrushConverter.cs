using System.Globalization;

namespace AbioticEditor.App.Converters;

/// <summary>Maps a bool to a Color: AF accent orange when active, panel elevated when not.</summary>
public sealed class TabBrushConverter : IValueConverter
{
    public Color ActiveColor { get; set; } = Color.FromArgb("#E37A22");
    public Color InactiveColor { get; set; } = Color.FromArgb("#2C2820");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? ActiveColor : InactiveColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
