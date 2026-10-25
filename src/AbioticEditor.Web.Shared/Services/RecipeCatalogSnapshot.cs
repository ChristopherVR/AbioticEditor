using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Items;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

public enum RecipeCatalogSource { InstalledGame, UserSnapshot, BundledSnapshot, SuppliedSnapshot, Unavailable }

/// <summary>Provenance of one coherent recipe-workflow load. A snapshot's version is its recorded version, not the installed game's version.</summary>
public sealed record RecipeCatalogMetadata(
    long Revision, RecipeCatalogSource Source, DateTimeOffset CheckedAt,
    string? GameVersion, string Culture, bool HasFallback)
{
    public bool IsSnapshot => Source is RecipeCatalogSource.UserSnapshot or RecipeCatalogSource.BundledSnapshot or RecipeCatalogSource.SuppliedSnapshot;
}

/// <summary>Recipes, all item definitions and discovery tables loaded from one provider or one registry.</summary>
public sealed record RecipeCatalogSnapshot(
    IReadOnlyList<RecipeInfo> Recipes,
    IReadOnlyList<ItemCatalogEntry> Items,
    CodexVocabulary Codex,
    RecipeCatalogMetadata Metadata)
{
    internal GameDataRegistry? Registry { get; init; }
    public IReadOnlyList<ItemVariantDefinition>? ItemVariants { get; init; }
    /// <summary>Stock from the same installed load or registry; absent evidence never uses the unrelated built-in roster.</summary>
    public IReadOnlyList<TraderInfo> Traders { get; init; } = [];
}
