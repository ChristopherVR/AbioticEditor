using Newtonsoft.Json;
using UeSaveGame.Json;

namespace AbioticEditor.Core.SaveClasses;

// Without these, SaveGameSerializer has no JSON representation for the games' custom
// ABF_SAVE_VERSION headers: export drops Version/Id and import writes them back as 0:
// same file length, but the game then treats the save as "version 0" (silent
// corruption found by the round-trip deep dive; see docs/research-new-save-gaps.md).

/// <summary>JSON round-trip for <see cref="AbioticWorldSave"/>'s custom header.</summary>
internal sealed class AbioticWorldSaveJsonSerializer : SaveClassSerializerBase<AbioticWorldSave>
{
    public override bool HasCustomHeader => true;

    public override void HeaderToJson(JsonWriter writer, AbioticWorldSave saveClass)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Version");
        writer.WriteValue(saveClass.Version);
        writer.WritePropertyName("Id");
        writer.WriteValue(saveClass.Id);
        writer.WriteEndObject();
    }

    public override void HeaderFromJson(JsonReader reader, AbioticWorldSave saveClass)
    {
        var token = Newtonsoft.Json.Linq.JToken.ReadFrom(reader);
        saveClass.Version = token.Value<int?>("Version") ?? saveClass.Version;
        saveClass.Id = token.Value<int?>("Id") ?? saveClass.Id;
    }
}

/// <summary>JSON round-trip for <see cref="AbioticCharacterSave"/>'s custom header.</summary>
internal sealed class AbioticCharacterSaveJsonSerializer : SaveClassSerializerBase<AbioticCharacterSave>
{
    public override bool HasCustomHeader => true;

    public override void HeaderToJson(JsonWriter writer, AbioticCharacterSave saveClass)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Version");
        writer.WriteValue(saveClass.Version);
        writer.WriteEndObject();
    }

    public override void HeaderFromJson(JsonReader reader, AbioticCharacterSave saveClass)
    {
        var token = Newtonsoft.Json.Linq.JToken.ReadFrom(reader);
        saveClass.Version = token.Value<int?>("Version") ?? saveClass.Version;
    }
}
