using UeSaveGame;
using UeSaveGame.Util;

namespace AbioticEditor.Core.SaveClasses;

[SaveClassPath("/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C")]
[SaveClassPath("/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C")]
internal sealed class AbioticWorldSave : SaveClassBase
{
    private static readonly FString VersionPropertyName = new("ABF_SAVE_VERSION");

    public int Version { get; set; }
    public int Id { get; set; }
    public int DataLength { get; private set; }

    public override bool HasCustomHeader => true;

    public override long GetHeaderSize(PackageVersion packageVersion)
    {
        return 4 + VersionPropertyName.SizeInBytes + 4 + 4 + 4;
    }

    public override void DeserializeHeader(BinaryReader reader, PackageVersion packageVersion)
    {
        var name = reader.ReadUnrealString();
        if (name is null || !name.Equals(VersionPropertyName))
        {
            throw new InvalidDataException(
                $"Expected header marker '{VersionPropertyName}' but got '{name}'");
        }

        Version = reader.ReadInt32();
        Id = reader.ReadInt32();
        DataLength = reader.ReadInt32();
    }

    public override void SerializeHeader(BinaryWriter writer, long dataLength, PackageVersion packageVersion)
    {
        DataLength = checked((int)dataLength);

        writer.WriteUnrealString(VersionPropertyName);
        writer.Write(Version);
        writer.Write(Id);
        writer.Write(DataLength);
    }
}
