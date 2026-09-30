using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;

namespace AbioticEditor.Core.PlayerSaves;

/// <summary>One row of the game's buff table (<c>DT_BuffsDebuffs</c>).</summary>
/// <param name="Row">Row name, e.g. <c>Debuff_Stinky</c> (what a character save stores).</param>
/// <param name="DisplayName">In-game name, or empty when the row has none.</param>
/// <param name="Description">In-game description, or empty.</param>
/// <param name="DefaultDuration">Seconds the effect lasts when applied normally.</param>
/// <param name="NoExpiration">True when the effect stays until something removes it (saved with expiry -1).</param>
/// <param name="IsSaved">True when the row carries the <c>Buff.Save</c> tag: only these are written to a character save.</param>
public sealed record BuffInfo(string Row, string DisplayName, string Description, double DefaultDuration, bool NoExpiration, bool IsSaved);

/// <summary>
/// The game's own buff/debuff table, used to give a character's saved active effects real names
/// and to tell which saved rows the current game still defines. See
/// <c>docs/reference/research/research-active-effects.md</c>.
/// </summary>
public sealed class BuffCatalog
{
    private const string BuffTable = "AbioticFactor/Content/Blueprints/DataTables/BuffsDebuffs/DT_BuffsDebuffs";
    private readonly Dictionary<string, BuffInfo> _byRow;

    private BuffCatalog(IEnumerable<BuffInfo> rows)
        => _byRow = rows.GroupBy(r => r.Row, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static BuffCatalog Empty { get; } = new([]);

    public int Count => _byRow.Count;

    public BuffInfo? Find(string? row) => row is not null && _byRow.TryGetValue(row, out var info) ? info : null;

    public static BuffCatalog LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Empty;
        try
        {
            var table = provider.TryLoadDataTable(BuffTable);
            if (table is null) return Empty;
            return new BuffCatalog(Rows(table));
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    private static IEnumerable<BuffInfo> Rows(UDataTable table)
    {
        foreach (var kv in table.RowMap)
        {
            string name = string.Empty, description = string.Empty, tags = string.Empty;
            double duration = 0;
            var noExpiration = false;
            foreach (var p in kv.Value.Properties)
            {
                var n = p.Name.Text;
                var value = p.Tag?.GenericValue;
                if (n.StartsWith("DisplayName", StringComparison.Ordinal)) name = value?.ToString() ?? string.Empty;
                else if (n.StartsWith("DisplayDescription", StringComparison.Ordinal)) description = value?.ToString() ?? string.Empty;
                else if (n.StartsWith("BuffTags", StringComparison.Ordinal)) tags = value?.ToString() ?? string.Empty;
                else if (n.StartsWith("DefaultDuration", StringComparison.Ordinal)) duration = Convert.ToDouble(value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
                else if (n.StartsWith("bNoExpiration", StringComparison.Ordinal)) noExpiration = value is true;
            }
            yield return new BuffInfo(kv.Key.Text, name, description, duration, noExpiration,
                tags.Contains("Buff.Save", StringComparison.Ordinal));
        }
    }
}
