using AbioticEditor.Core.Assets;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// The value vocabulary of <c>E_NarrativeNPCStates</c> (the enum behind
/// <c>NarrativeNPCMap.NarrativeState_</c>). The game's enum entries are compiler
/// artifacts (<c>NewEnumeratorN</c>) - there are no friendly names to show.
/// </summary>
public static class NpcStateCatalog
{
    public static IReadOnlyList<string> LoadFrom(GameAssetProvider provider)
    {
        if (!provider.HasMappings) return Array.Empty<string>();
        try
        {
            var pkg = provider.LoadPackageInternal("AbioticFactor/Content/Blueprints/Data/E_NarrativeNPCStates");
            var result = new List<string>();
            foreach (var e in pkg.GetExports().OfType<UEnum>())
            {
                foreach (var (name, _) in e.Names)
                {
                    var text = name.Text;
                    if (text.EndsWith("_MAX", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(text);
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
