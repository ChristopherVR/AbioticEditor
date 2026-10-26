using System.Runtime.CompilerServices;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.Saves;

/// <summary>Tracks the native 1.5 struct owning a property list without changing the raw save.</summary>
internal static class NativeSaveSchema
{
    private sealed record Owner(string Type);
    private static readonly ConditionalWeakTable<object, Owner> Owners = new();
    private static readonly Dictionary<string, string> CompendiumNames = new(StringComparer.Ordinal)
    {
        ["Compendium_EmailSections"] = "EmailSections", ["Compendium_NarrativeSections"] = "NarrativeSections",
        ["Compendium_ExplorationSections"] = "ExplorationSections", ["Compendium_Fish"] = "Fish",
        ["Compendium_KillCount"] = "KillCounts", ["Compendium_Unread"] = "Unread",
        ["Journal_Unread"] = "JournalUnread", ["Fish_Unread"] = "FishUnread",
        ["JournalEntries"] = "JournalEntries", ["EmailsRead"] = "EmailsRead",
    };

    public static bool IsNative(object tags) => Owners.TryGetValue(tags, out _);

    public static void Register(SaveGame save)
    {
        if (save.Properties is null) return;
        foreach (var tag in save.Properties) Visit(tag.Property);
        // Native saves still use Blueprint save classes, but SaveVersion 4 marks the migration.
        if (save.Properties.Any(t => t.Name?.Value == "SaveVersion" && t.Property?.Value is int version && version >= 4))
            Owners.GetValue(save.Properties, _ => new Owner("AbioticSave"));
    }

    private static void Visit(FProperty? property)
    {
        switch (property)
        {
            case StructProperty { Value: PropertiesStruct ps } sp:
                var type = sp.StructType?.Name.Value ?? string.Empty;
                if (sp.StructType?.Parameters.Any(p => p.Name.Value == "/Script/AbioticFactor") == true
                    || type.StartsWith("Abiotic", StringComparison.Ordinal) && !type.Contains('_'))
                    Owners.GetValue(ps.Properties, _ => new Owner(type));
                foreach (var child in ps.Properties) Visit(child.Property);
                break;
            case ArrayProperty { Value: { } values }:
                foreach (var value in values) if (value is FProperty child) Visit(child);
                break;
            case MapProperty { Value: { } pairs }:
                foreach (var pair in pairs) { Visit(pair.Key); Visit(pair.Value); }
                break;
        }
    }

    public static string Name(object tags, string prefix)
    {
        if (!Owners.TryGetValue(tags, out var owner)) return prefix;
        var stem = prefix.TrimEnd('_');
        if (owner.Type == "AbioticCompendiumSave" && CompendiumNames.TryGetValue(stem, out var field)) return field;
        return (owner.Type, stem) switch
        {
            ("AbioticCharacterSaveData", "CurrentSurvivalStats") => "SurvivalStats",
            ("AbioticCharacterSaveData", "RecipesUnlock") => "RecipesUnlocked",
            ("AbioticCharacterSaveData", "TransmogDisabledArray") => "TransmogDisabled",
            ("AbioticCharacterSaveData", "CompletedIntro") => "bCompletedIntro",
            ("AbioticCharacterSkill", "CurrentSkillXP") => "CurrentXP",
            ("AbioticCharacterSkill", "CurrentXPMultiplier") => "XPMultiplier",
            ("AbioticNPCStateSave", "IsDead") => "bDead",
            ("AbioticSave", "SimpleDoorMap") => "DoorMap",
            _ => stem,
        };
    }

    public static IList<FPropertyTag> ResolveOwner(IList<FPropertyTag> tags, string prefix, bool create = false)
    {
        if (!Owners.TryGetValue(tags, out var owner) || owner.Type != "AbioticCharacterSaveData"
            || !CompendiumNames.ContainsKey(prefix.TrimEnd('_'))) return tags;
        if (tags.FirstOrDefault(t => t.Name?.Value == "Compendium")?.Property is StructProperty { Value: PropertiesStruct ps }) return ps.Properties;
        return create ? CreateStruct(tags, "Compendium", "AbioticCompendiumSave") : tags;
    }

    public static IList<FPropertyTag> CreateStruct(IList<FPropertyTag> tags, string name, string structName)
    {
        var type = new FPropertyTypeName(new FString(structName), [new(new FString("/Script/AbioticFactor"))]);
        var value = new PropertiesStruct();
        var property = new StructProperty(new FString(name)) { StructType = type, StructGuid = Guid.Empty, Value = value };
        tags.Add(new FPropertyTag(new FString(name), new(new FString("StructProperty"), [type]), EPropertyTagFlags.None) { Property = property });
        Owners.GetValue(value.Properties, _ => new Owner(structName));
        return value.Properties;
    }
}
