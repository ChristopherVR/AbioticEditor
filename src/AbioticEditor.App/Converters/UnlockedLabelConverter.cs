using System.Globalization;
using AbioticEditor.App.Services;

namespace AbioticEditor.App.Converters;

/// <summary>true -> "UNLOCKED", false -> "LOCKED" (achievement status chips), localized.</summary>
public sealed class UnlockedLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? LocalizationResourceManager.Instance["PlayerAchievements_BadgeUnlocked"]
            : LocalizationResourceManager.Instance["PlayerAchievements_BadgeLocked"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
