using UeSaveGame;

namespace AbioticEditor.Core.SaveClasses;

[SaveClassPath("/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C")]
internal sealed class AbioticCharacterSave : SaveClassBase
{
    public int Version { get; set; }
    public int DataLength { get; private set; }

    public override bool HasCustomHeader => true;

    public override long GetHeaderSize(PackageVersion packageVersion) => 8;

    public override void DeserializeHeader(BinaryReader reader, PackageVersion packageVersion)
    {
        Version = reader.ReadInt32();
        DataLength = reader.ReadInt32();
    }

    public override void SerializeHeader(BinaryWriter writer, long dataLength, PackageVersion packageVersion)
    {
        DataLength = checked((int)dataLength);
        writer.Write(Version);
        writer.Write(DataLength);
    }
}
