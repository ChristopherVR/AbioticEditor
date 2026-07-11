using AbioticEditor.Core.Codex;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// The single list of <i>verified</i> abioticfactor.wiki.gg image file names the editor knows
/// it can show: the curated fish item icons (<see cref="FishWikiImages"/>), vehicle renders
/// (<see cref="VehicleCatalog"/>), world-feature pictures (<see cref="FeatureWikiImageCatalog"/>),
/// and door renders (<see cref="DoorWikiImageCatalog"/>).
///
/// These are the names worth pre-downloading into the offline fallback bundle (the CLI's
/// <c>download-wiki-images</c> command fetches exactly this set into <c>assets/wiki/</c>, which
/// <see cref="WikiImageCache"/> falls back to when the live wiki is unreachable). The speculative,
/// runtime-only guesses each catalog appends (e.g. <c>Item Icon - &lt;name&gt;.png</c>) are
/// excluded - they are best-effort and resolve to no image when wrong, so there is nothing to
/// bundle for them.
/// </summary>
public static class WikiImageManifest
{
    /// <summary>
    /// All verified wiki File names across every catalog, de-duplicated (case-insensitive)
    /// and ordered, so the offline fallback bundle is reproducible.
    /// </summary>
    public static IReadOnlyList<string> AllFiles { get; } =
        FishWikiImages.AllWikiFiles
            .Concat(VehicleCatalog.CuratedWikiFiles)
            .Concat(FeatureWikiImageCatalog.AllWikiFiles)
            .Concat(DoorWikiImageCatalog.MappedClasses.Values)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
