using AbioticEditor.Core.Codex;
using AbioticEditor.Core.Saves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace AbioticEditor.Core.PlayerSaves;

public sealed record PlayerQuestSelection(bool SupportsMultipleQuests, string? TrackedLine, string? ServerQuest);

public static class PlayerQuestSave
{
    public static PlayerQuestSelection Read(PlayerSaveData data)
    {
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        var compendium = root.FindByPrefix("Compendium")?.Property is StructProperty { Value: PropertiesStruct ps } ? ps.Properties : null;
        return new(NativeSaveSchema.IsNative(root), Row(compendium, "TrackedQuestLine"), Row(compendium, "ServerQuest"));
    }

    /// <summary>Changes only the personal map selection. Never completes quests or changes server progress.</summary>
    public static void TrackLine(PlayerSaveData data, string? lineId, QuestCatalogData catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (lineId is not null && !catalog.Lines.Any(line => line.Id == lineId))
            throw new ArgumentException("The quest line is not in the loaded game data.", nameof(lineId));
        var root = PlayerSaveReader.GetCharacterSaveData(data.Raw);
        if (!NativeSaveSchema.IsNative(root)) throw new InvalidOperationException("Multiple quest lines require a 1.5 player save.");
        var compendium = root.FindByPrefix("Compendium")?.Property is StructProperty { Value: PropertiesStruct ps }
            ? ps.Properties : NativeSaveSchema.CreateStruct(root, "Compendium", "AbioticCompendiumSave");
        var handle = compendium.FindByPrefix("TrackedQuestLine")?.Property is StructProperty { Value: PropertiesStruct row }
            ? row.Properties : NativeSaveSchema.CreateStruct(compendium, "TrackedQuestLine", "QuestLineRowHandle");
        GvasTags.SetName(handle, "RowName", lineId ?? "None", "RowName");
    }

    private static string? Row(IList<FPropertyTag>? tags, string name)
    {
        var value = tags.FindByPrefix(name)?.Property is StructProperty { Value: PropertiesStruct ps } ? ps.Properties.GetString("RowName") : null;
        return value is "None" or "" ? null : value;
    }
}
