namespace AbioticEditor.Core.Saves;

/// <summary>
/// Lightweight metadata about a save file, suitable for sidebar listings.
/// </summary>
public sealed record SaveFileSummary(
    string FullPath,
    string DisplayName,
    long SizeBytes,
    string? SaveClass,
    int PropertyCount,
    string? LoadError)
{
    /// <summary>
    /// Steam/in-game display name of the owning player, when one could be resolved
    /// (bed claims or the machine's Steam accounts). Player saves only.
    /// </summary>
    public string? PlayerName { get; init; }

    /// <summary>Coarse save type, for the sidebar chip.</summary>
    public string KindLabel => LoadError is not null ? "ERROR"
        : SaveClass switch
        {
            var s when s?.Contains("CharacterSave", StringComparison.Ordinal) == true => "PLAYER",
            var s when s?.Contains("WorldMetadataSave", StringComparison.Ordinal) == true => "META",
            var s when s?.Contains("WorldSave", StringComparison.Ordinal) == true => "WORLD",
            null => "?",
            _ => "OTHER",
        };

    /// <summary>Human-readable size, e.g. "13.2 MB" / "48 KB".</summary>
    public string SizeText => SizeBytes >= 1024 * 1024
        ? $"{SizeBytes / 1024.0 / 1024.0:F1} MB"
        : $"{Math.Max(1, SizeBytes / 1024)} KB";

    /// <summary>Chip colour per kind (hazard-orange players, green world, yellow meta).</summary>
    public string KindColor => KindLabel switch
    {
        "PLAYER" => "#E37A22",
        "WORLD" => "#7BB351",
        "META" => "#F2C82E",
        "ERROR" => "#C0392B",
        _ => "#6E6655",
    };
}
