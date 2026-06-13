using AbioticEditor.App.Services;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>One trait chip on the player editor: internal id, display name, wiki info.</summary>
public sealed class TraitItemViewModel
{
    public TraitItemViewModel(string id)
    {
        Id = id;
        GameDataServices.TraitDetails.TryGetValue(id, out var detail);
        Detail = detail;
        DisplayName = detail?.DisplayName ?? TraitCatalog.DisplayNameFor(id);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public TraitDetail? Detail { get; }

    /// <summary>In-game description (tooltip), with the trait-point cost when known.</summary>
    public string Tooltip
    {
        get
        {
            if (Detail is null) return Id;
            var cost = Detail.PointCost != 0 ? $"  [{Detail.PointCost:+#;-#;0} pts]" : string.Empty;
            return string.IsNullOrEmpty(Detail.Description) ? Id : $"{Detail.Description}{cost}";
        }
    }

    public bool IsKnown => TraitCatalog.Traits.ContainsKey(Id) || TraitCatalog.Backgrounds.ContainsKey(Id);
}
