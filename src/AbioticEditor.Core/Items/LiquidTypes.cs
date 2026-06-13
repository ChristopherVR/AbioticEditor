namespace AbioticEditor.Core.Items;

/// <summary>One fillable liquid choice for the slot editor.</summary>
/// <param name="Enumerator">E_LiquidType enumerator number (what saves store).</param>
public sealed record LiquidOption(int Enumerator, string DisplayName)
{
    /// <summary>The string form saves carry in <c>CurrentLiquid_</c>.</summary>
    public string SaveValue => $"E_LiquidType::NewEnumerator{Enumerator}";

    public override string ToString() => DisplayName;
}

/// <summary>
/// E_LiquidType display names, from the enum's DisplayNameMap (LiquidDoorProbeTests).
/// Items declare per-row which of these they accept (LiquidData.AllowedLiquids) and how
/// much they hold (LiquidData.MaxLiquid).
/// </summary>
public static class LiquidTypes
{
    public static readonly IReadOnlyList<LiquidOption> All = new LiquidOption[]
    {
        new(0, "Empty"),
        new(1, "Water"),
        new(2, "Feces"),
        new(3, "Radioactive Waste"),
        new(4, "Molten Material"),
        new(6, "Fuel"),
        new(7, "Vomit"),
        new(8, "Battery"),
        new(9, "Blood"),
        new(11, "Antejuice"),
        new(13, "Tainted Water"),
        new(14, "Soup"),
        new(15, "Laser"),
        new(16, "Ink"),
    };

    public static string NameFor(int enumerator)
    {
        if (All.FirstOrDefault(l => l.Enumerator == enumerator) is { } known) return known.DisplayName;
        Diagnostics.EditorLog.UnknownData(
            "LiquidType",
            enumerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "enumerator not in E_LiquidType display map - newer game version?");
        return $"Liquid #{enumerator}";
    }

    /// <summary>Parses a save's <c>CurrentLiquid_</c> string back to the enumerator (-1 unknown).</summary>
    public static int ParseSaveValue(string? saveValue)
    {
        if (string.IsNullOrEmpty(saveValue)) return -1;
        var s = saveValue;
        var end = s.Length;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(s[start - 1])) start--;
        return start < end && int.TryParse(s[start..end], out var n) ? n : -1;
    }
}
