using System.Runtime.CompilerServices;
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
        foreach (var tag in save.Properties) pool.Tag(tag);
    }

    private sealed class Pool
    {
        private readonly Dictionary<(string Value, int CodePage, ulong Seed), FString> _strings = new();
        private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FPropertyTypeName> _types = new(StringComparer.Ordinal);

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
                    foreach (var tag in ps.Properties) Tag(tag);
                    break;
                case IList<KeyValuePair<FProperty, FProperty>> pairs:
                    foreach (var pair in pairs)
                    {
                        Property(pair.Key);
                        Property(pair.Value);
                    }
                    break;
                case Array items:
                    foreach (var item in items)
                    {
                        if (item is FProperty child) Property(child);
                        else if (item is FPropertyTag childTag) Tag(childTag);
                    }
                    break;
                case IEnumerable<FProperty> list:
                    foreach (var child in list) Property(child);
                    break;
            }
        }

        private FString Share(FString value)
        {
            if (value.Value is null) return value;
            var key = (value.Value, value.Encoding?.CodePage ?? 0, HashSeed(value));
            if (_strings.TryGetValue(key, out var shared)) return shared;
            _strings[key] = value;
            return value;
        }

        /// <summary>One instance per spelling of a type name (its name and parameters, recursively).</summary>
        private FPropertyTypeName ShareType(FPropertyTypeName type)
        {
            var key = TypeKey(type);
            if (_types.TryGetValue(key, out var shared)) return shared;
            _types[key] = type;
            return type;
        }

        private string TypeKey(FPropertyTypeName type)
        {
            var name = type.Name?.Value ?? string.Empty;
            if (type.Parameters.Count == 0) return Text(name);
            return Text(name + "<" + string.Join(",", type.Parameters.Select(TypeKey)) + ">");
        }

        private string Text(string value)
        {
            if (_text.TryGetValue(value, out var shared)) return shared;
            _text[value] = value;
            return value;
        }
    }
}
