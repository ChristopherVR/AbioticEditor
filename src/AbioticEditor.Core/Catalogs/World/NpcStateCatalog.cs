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
    // This vocabulary comes from a UEnum, not a DataTable, so the struct-based ModTableDiscovery
    // does not apply. A mod that changes NPC states does so by overriding E_NarrativeNPCStates,
    // and the mounted mod pak's enum is what loads here.
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
            return result.Count > 0 ? result : NativeStates(provider);
        }
        catch
        {
            return NativeStates(provider);
        }
    }

    // Native 1.5 replaced the Blueprint enum asset with ENarrativeNPCState, which has real names.
    private static IReadOnlyList<string> NativeStates(GameAssetProvider provider)
    {
        try
        {
            var values = provider.UseFileProvider(files => files.MappingsContainer?.MappingsForGame?.Enums.GetValueOrDefault("ENarrativeNPCState"));
            return values is null
                ? Array.Empty<string>()
                : values.OrderBy(v => v.Key).Select(v => v.Value).Where(v => !v.EndsWith("_MAX", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
