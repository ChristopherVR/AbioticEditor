using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UeSaveGame;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.Saves;

/// <summary>
/// Shares the names a loaded save repeats. The save library gives every property its own copy of its
/// name and type name, so the 16 MB Facility save held about 714,000 strings: one "Transform_..." or
/// "StructProperty" per property instead of one per spelling. After a load this pass points every
/// repeat at a single shared instance, which roughly halves the memory a large save takes.
/// </summary>
/// <remarks>
/// Only names are shared: property and type names, and string values that repeat (item ids, class
/// paths). All of these are immutable (<see cref="FString"/> and <see cref="FPropertyTypeName"/> are
/// never changed after reading), so sharing one instance cannot change what is written back. The
/// library exposes them read-only, so the fields are set through <see cref="UnsafeAccessorAttribute"/>.
/// </remarks>
internal static class SaveTreeCompactor
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Name>k__BackingField")]
    private static extern ref FString TagName(FPropertyTag tag);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Type>k__BackingField")]
    private static extern ref FPropertyTypeName TagType(FPropertyTag tag);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "mPropertyName")]
    private static extern ref FString PropertyName(FProperty property);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "mHashSeed")]
    private static extern ref ulong HashSeed(FString value);

    /// <summary>Shares the repeated names of <paramref name="save"/> in place. Safe to call on any loaded save.</summary>
    public static void Compact(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (save.Properties is null) return;
        var pool = new Pool();
        pool.Tags(save.Properties);
    }

    private sealed class Pool
    {
        private readonly Dictionary<(string Value, int CodePage, ulong Seed), FString> _strings = new();
        private readonly Dictionary<FPropertyTypeName, FPropertyTypeName> _types = new(TypeNameComparer.Instance);

        // Index loops rather than foreach: the tree's lists are typed as interfaces, and enumerating
        // one through its interface allocates an enumerator per list, millions on a large save.
        public void Tags(IList<FPropertyTag> tags)
        {
            for (var i = 0; i < tags.Count; i++) Tag(tags[i]);
        }

        public void Tag(FPropertyTag tag)
        {
            ref var name = ref TagName(tag);
            if (name is not null) name = Share(name);
            ref var type = ref TagType(tag);
            if (type is not null) type = ShareType(type);
            if (tag.Property is { } property) Property(property);
        }

        private void Property(FProperty property)
        {
            ref var name = ref PropertyName(property);
            if (name is not null) name = Share(name);
            switch (property.Value)
            {
                case FString text:
                    property.Value = Share(text);
                    break;
                case PropertiesStruct ps:
                    if (ps.Properties is { } properties) Tags(properties);
                    break;
                case IList<KeyValuePair<FProperty, FProperty>> pairs:
                    for (var i = 0; i < pairs.Count; i++)
                    {
                        var pair = pairs[i];
                        Property(pair.Key);
                        Property(pair.Value);
                    }
                    break;
                case FProperty[] children:
                    foreach (var child in children)
                    {
                        if (child is not null) Property(child);
                    }
                    break;
                case FPropertyTag[] childTags:
                    foreach (var childTag in childTags)
                    {
                        if (childTag is not null) Tag(childTag);
                    }
                    break;
                // Arrays of plain values (bytes, numbers, names) hold no properties to visit; walking
                // them element by element would box every value for nothing.
                case Array items when MayHoldProperties(items):
                    foreach (var item in items)
                    {
                        if (item is FProperty child) Property(child);
                        else if (item is FPropertyTag childTag) Tag(childTag);
                    }
                    break;
                case Array:
                    break;
                case IEnumerable<FProperty> list:
                    foreach (var child in list) Property(child);
                    break;
            }
        }

        private static bool MayHoldProperties(Array items)
        {
            var element = items.GetType().GetElementType();
            return element is null
                || element.IsAssignableFrom(typeof(FProperty))
                || element.IsAssignableFrom(typeof(FPropertyTag))
                || typeof(FProperty).IsAssignableFrom(element)
                || typeof(FPropertyTag).IsAssignableFrom(element);
        }

        private FString Share(FString value)
        {
            if (value.Value is null) return value;
            var key = (value.Value, value.Encoding?.CodePage ?? 0, HashSeed(value));
            ref var shared = ref CollectionsMarshal.GetValueRefOrAddDefault(_strings, key, out var exists);
            if (!exists) shared = value;
            return shared!;
        }

        /// <summary>One instance per spelling of a type name (its name and parameters, recursively).</summary>
        private FPropertyTypeName ShareType(FPropertyTypeName type)
        {
            ref var shared = ref CollectionsMarshal.GetValueRefOrAddDefault(_types, type, out var exists);
            if (!exists) shared = type;
            return shared!;
        }
    }

    /// <summary>
    /// Compares type names by spelling: the name's text and, in order, the parameters' spellings.
    /// Comparing the structure directly avoids building a key string for every tag of a large save.
    /// </summary>
    private sealed class TypeNameComparer : IEqualityComparer<FPropertyTypeName>
    {
        public static readonly TypeNameComparer Instance = new();

        public bool Equals(FPropertyTypeName? x, FPropertyTypeName? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (!string.Equals(x.Name?.Value ?? string.Empty, y.Name?.Value ?? string.Empty, StringComparison.Ordinal)) return false;
            var xs = x.Parameters;
            var ys = y.Parameters;
            if (xs.Count != ys.Count) return false;
            for (var i = 0; i < xs.Count; i++)
            {
                if (!Equals(xs[i], ys[i])) return false;
            }
            return true;
        }

        public int GetHashCode(FPropertyTypeName type)
        {
            var hash = new HashCode();
            hash.Add(type.Name?.Value ?? string.Empty, StringComparer.Ordinal);
            hash.Add(type.Parameters.Count);
            foreach (var parameter in type.Parameters) hash.Add(GetHashCode(parameter));
            return hash.ToHashCode();
        }
    }
}
