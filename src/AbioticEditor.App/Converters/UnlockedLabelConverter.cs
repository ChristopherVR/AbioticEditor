using System.Globalization;

namespace AbioticEditor.App.Converters;

/// <summary>true -> "UNLOCKED", false -> "LOCKED" (achievement status chips).</summary>
public sealed class UnlockedLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "UNLOCKED" : "LOCKED";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
