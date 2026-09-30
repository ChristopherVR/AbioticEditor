using CUE4Parse.UE4.Assets.Exports;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Property reads that honour Unreal's delta serialization. A cooked object only stores the
/// properties that differ from its archetype, so a missing value is looked up on the archetype
/// (the <c>Template</c>), and then on the next object in an override chain.
/// </summary>
internal static class Props
{
    private const int MaxArchetypeDepth = 10;

    /// <summary>The first object in <paramref name="chain"/> (or its archetypes) that stores <paramref name="name"/>.</summary>
    public static bool TryGet<T>(IEnumerable<UObject> chain, string name, out T value)
    {
        foreach (var start in chain)
        {
            if (TryGet(start, name, out value)) return true;
        }
        value = default!;
        return false;
    }

    /// <summary><paramref name="obj"/>'s own value for <paramref name="name"/>, else its archetype's.</summary>
    public static bool TryGet<T>(UObject obj, string name, out T value)
    {
        var current = obj;
        for (var depth = 0; current is not null && depth < MaxArchetypeDepth; depth++)
        {
            if (current.TryGetValue(out value, name)) return true;
            current = current.Template is { } template && template.TryLoad(out var next) ? next : null;
        }
        value = default!;
        return false;
    }

    public static T Get<T>(UObject obj, string name, T fallback) => TryGet(obj, name, out T value) ? value : fallback;

    public static T Get<T>(IEnumerable<UObject> chain, string name, T fallback) => TryGet(chain, name, out T value) ? value : fallback;
}
