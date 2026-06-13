using System.Globalization;

namespace AbioticEditor.App.Converters;

/// <summary>
/// Maps a bool (typically <c>HasValidationWarning</c>) to a Brush: hazard-yellow when
/// true, the subtle border colour otherwise.
/// </summary>
public sealed class WarningBorderBrushConverter : IValueConverter
{
    public Brush WarningBrush { get; set; } = new SolidColorBrush(Color.FromArgb("#F2C82E"));
    public Brush NormalBrush { get; set; } = new SolidColorBrush(Color.FromArgb("#2E2A22"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? WarningBrush : NormalBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
