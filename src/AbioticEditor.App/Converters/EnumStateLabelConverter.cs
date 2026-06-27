using System.Globalization;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.Converters;

/// <summary>
/// Turns a raw Unreal enum value into a friendly UI label so the editor never shows the
/// meaningless <c>E_DoorStates::NewEnumeratorN</c> form. Door states map to Closed / Open / Locked
/// (etc.) via <see cref="DoorStateNames"/>; any other enum (e.g. <c>E_NarrativeNPCStates</c>) is
/// reduced to a plain "State N". Used as a Picker's <c>ItemDisplayBinding</c> so the underlying
/// value stays the raw enum for write-back while only the displayed text is cleaned up.
/// </summary>
public sealed class EnumStateLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string raw || raw.Length == 0) return value ?? string.Empty;

        // Door states have curated names (Closed/Open/Locked/...).
        if (raw.Contains("DoorState", StringComparison.OrdinalIgnoreCase))
        {
            return DoorStateNames.Friendly(raw);
        }

        // Any other "E_Something::NewEnumeratorN" -> "State N"; otherwise echo it unchanged.
        var marker = raw.IndexOf("NewEnumerator", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0
            && int.TryParse(raw.AsSpan(marker + "NewEnumerator".Length), out var n))
        {
            return $"State {n}";
        }
        return raw;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value ?? string.Empty;
}
