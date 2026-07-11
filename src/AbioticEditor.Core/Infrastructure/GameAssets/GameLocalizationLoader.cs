using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Localization;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// Primes CUE4Parse's <c>Internationalization</c> dictionary with Abiotic Factor's own shipped
/// translations (the game's <c>Content/Localization/Game/&lt;culture&gt;/Game.locres</c>, one per
/// supported language) so item/trait/skill/recipe display names resolve in a non-English culture.
/// </summary>
/// <remarks>
/// CUE4Parse's own <see cref="AbstractFileProvider.ChangeCulture"/> requires
/// <c>AvailableCultures</c> to be populated from <c>DefaultGame.ini</c>'s
/// <c>CulturesToStage</c> setting, which Abiotic Factor's cooked build does not set even though
/// the locres files genuinely exist in the pak - a probe against the real install confirmed
/// <c>TryChangeCulture</c> is rejected outright while the locres data itself parses fine and is
/// extensive (50k+ translated strings for Russian). So this locates the culture's <c>.locres</c>
/// files directly and feeds them to <c>Internationalization</c> via its public
/// <see cref="InternationalizationDictionary.Override"/> API, which <c>FText</c> resolution reads
/// regardless of whether <c>ChangeCulture</c> "officially" succeeded.
///
/// Must run before any DataTable package is loaded: <c>FText.Base.LocalizedString</c> is resolved
/// once, at deserialize time, not looked up live - so the culture has to be primed while the
/// provider is still cold.
/// </remarks>
internal static class GameLocalizationLoader
{
    public static void Apply(DefaultFileProvider provider, string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture) || string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var pattern = new Regex($@"/{Regex.Escape(culture)}/[^/]+\.locres$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var merged = new Dictionary<string, IDictionary<string, string>>();

        foreach (var file in provider.Files)
        {
            if (!pattern.IsMatch(file.Key) || !file.Value.TryCreateReader(out var archive))
            {
                continue;
            }

            FTextLocalizationResource locres;
            try
            {
                locres = new FTextLocalizationResource(archive);
            }
            catch (Exception ex)
            {
                Diagnostics.EditorLog.Warn("Assets", $"Failed to parse locres '{file.Key}' for culture '{culture}': {ex.Message}");
                continue;
            }

            foreach (var ns in locres.Entries)
            {
                if (!merged.TryGetValue(ns.Key.Str, out var dict))
                {
                    merged[ns.Key.Str] = dict = new Dictionary<string, string>();
                }
                foreach (var entry in ns.Value)
                {
                    dict[entry.Key.Str] = entry.Value.LocalizedString;
                }
            }
        }

        if (merged.Count == 0)
        {
            Diagnostics.EditorLog.Warn("Assets", $"No .locres files found for culture '{culture}' - item/trait/skill names will stay English.");
            return;
        }

        provider.Internationalization.Override(merged);
        Diagnostics.EditorLog.Info(
            "Assets",
            $"Loaded {merged.Sum(m => m.Value.Count)} localized string(s) for culture '{culture}' from {merged.Count} namespace(s).");
    }
}
