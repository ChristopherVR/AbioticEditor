using System.Security.Cryptography;
using System.Text;
using UeSaveGame;

namespace AbioticEditor.Core.Compatibility;

/// <summary>
/// The version evidence carried in a GVAS file header: container format version, package
/// versions, engine version and changelist, branch, and the custom-version table. It says
/// which engine build wrote the save. It does NOT carry the game's own version string, so it
/// can never by itself establish the exact Abiotic Factor build.
/// </summary>
/// <param name="SaveGameFileVersion">GVAS container version (2 or 3).</param>
/// <param name="PackageVersionUe4">UE4 object version.</param>
/// <param name="PackageVersionUe5">UE5 object version, or null for container version 2.</param>
/// <param name="EngineMajor">Engine major version.</param>
/// <param name="EngineMinor">Engine minor version.</param>
/// <param name="EnginePatch">Engine patch version.</param>
/// <param name="EngineChangelist">Engine changelist with the licensee bit (0x80000000) removed.</param>
/// <param name="EngineBranch">Engine branch string, e.g. <c>++DF+ABF</c>.</param>
/// <param name="CustomVersionCount">Number of entries in the custom-version table.</param>
/// <param name="CustomVersionFingerprint">Stable hash over the (guid, value) table, for comparing builds.</param>
/// <param name="SaveClass">Save class path following the header.</param>
public sealed record SaveHeaderEvidence(
    int SaveGameFileVersion,
    uint PackageVersionUe4,
    uint? PackageVersionUe5,
    int EngineMajor,
    int EngineMinor,
    int EnginePatch,
    int EngineChangelist,
    string EngineBranch,
    int CustomVersionCount,
    string CustomVersionFingerprint,
    string? SaveClass)
{
    private const uint LicenseeBit = 0x80000000;

    /// <summary>Compact label such as <c>5.4.4-1030002+++DF+ABF</c> (engine-level, not game-level).</summary>
    public string EngineLabel => $"{EngineMajor}.{EngineMinor}.{EnginePatch}-{EngineChangelist}+{EngineBranch}";

    /// <summary>Parses the header of a save held in memory. Returns null when it is not a readable GVAS header.</summary>
    public static SaveHeaderEvidence? TryParse(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        return TryParse(stream);
    }

    /// <summary>Parses only the header of the file at <paramref name="path"/>; null when unreadable.</summary>
    public static SaveHeaderEvidence? TryParseFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryParse(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the header evidence of a loaded save by re-emitting just its header parts
    /// (public <c>WritePart</c>), so no file and no body re-serialization is needed.
    /// </summary>
    public static SaveHeaderEvidence? FromLoaded(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
            {
                save.WritePart(SaveGamePart.Magic, writer);
                save.WritePart(SaveGamePart.Versions, writer);
                save.WritePart(SaveGamePart.CustomFormats, writer);
                save.WritePart(SaveGamePart.SaveClass, writer);
            }
            stream.Position = 0;
            return TryParse(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static SaveHeaderEvidence? TryParse(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (reader.ReadUInt32() != 0x53415647) return null; // "GVAS"
            var container = reader.ReadInt32();
            if (container is not (2 or 3)) return null;
            var ue4 = reader.ReadUInt32();
            uint? ue5 = container == 3 ? reader.ReadUInt32() : null;
            var major = reader.ReadInt16();
            var minor = reader.ReadInt16();
            var patch = reader.ReadInt16();
            var build = reader.ReadInt32();
            var branch = ReadFString(reader);
            _ = reader.ReadInt32(); // custom format table version
            var count = reader.ReadUInt32();
            if (count > 4096) return null;
            var table = reader.ReadBytes(checked((int)count * 20));
            if (table.Length != count * 20) return null;
            var fingerprint = Convert.ToHexString(SHA256.HashData(table))[..16];
            var saveClass = ReadFString(reader);
            return new SaveHeaderEvidence(
                container, ue4, ue5, major, minor, patch,
                unchecked((int)((uint)build & ~LicenseeBit)), branch, (int)count, fingerprint, saveClass);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or IOException or OverflowException)
        {
            return null;
        }
    }

    private static string ReadFString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0) return string.Empty;
        if (length is < -4096 or > 4096) throw new InvalidDataException("FString length out of range.");
        if (length > 0)
        {
            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes, 0, Math.Max(0, bytes.Length - 1));
        }
        var wide = reader.ReadBytes(-length * 2);
        return Encoding.Unicode.GetString(wide, 0, Math.Max(0, wide.Length - 2));
    }
}

/// <summary>How much validation backs a recorded engine build.</summary>
public enum EngineBuildStatus
{
    /// <summary>Mappings, catalogs and write paths were validated against this build.</summary>
    Validated,

    /// <summary>Seen in fixtures with a proven byte-exact round-trip; no per-area edit evidence.</summary>
    ObservedRoundTripOnly,
}

/// <summary>An engine version + changelist + branch observed in real save headers.</summary>
/// <param name="Major">Engine major.</param>
/// <param name="Minor">Engine minor.</param>
/// <param name="Patch">Engine patch.</param>
/// <param name="Changelist">Changelist (licensee bit removed).</param>
/// <param name="Branch">Branch string.</param>
/// <param name="Status">How much validation backs this build.</param>
/// <param name="Note">Where it was observed.</param>
public sealed record KnownEngineBuild(
    int Major, int Minor, int Patch, int Changelist, string Branch, EngineBuildStatus Status, string Note)
{
    /// <summary>True when the header's engine version, changelist and branch equal this row.</summary>
    public bool Matches(SaveHeaderEvidence header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.EngineMajor == Major && header.EngineMinor == Minor && header.EnginePatch == Patch
               && header.EngineChangelist == Changelist
               && string.Equals(header.EngineBranch, Branch, StringComparison.Ordinal);
    }
}

/// <summary>What a save header can and cannot say about the game build that wrote it.</summary>
public enum BuildIdentification
{
    /// <summary>
    /// No header evidence was available (or it could not be parsed). The exact game build is
    /// not known. This is also the honest answer whenever only the installed game could say.
    /// </summary>
    Unknown = 0,

    /// <summary>The header's engine changelist matches the validated build.</summary>
    ValidatedEngineBuild,

    /// <summary>The engine changelist was seen in fixtures but is not the validated build.</summary>
    ObservedEngineBuild,

    /// <summary>The header parsed, but its engine build is not one this editor has ever seen.</summary>
    UnrecognizedEngineBuild,
}
