using System.Text;
using AbioticEditor.GamePass.Storage;

namespace AbioticEditor.Core.GamePass;

/// <summary>
/// Abiotic Factor's payload recogniser for the generic container layer: reads the world name out of
/// an <c>ABF_SAVE_VERSION</c> bundle by looking at its table of contents only.
///
/// <para>The TOC (member paths, sizes and save classes) sits uncompressed at the front of the
/// blob; only the member bodies go through Oodle. That matters here: identifying a leftover
/// world has to work on a machine with no Oodle library at all, which is exactly the machine
/// where a player is least able to check for themselves what they lost.</para>
///
/// <para>Returns null for anything that is not a world bundle (profile containers, settings)
/// or that cannot be read.</para>
/// </summary>
internal sealed class AbfBlobInspector : IWgsBlobInspector
{
    /// <summary>The TOC's first member path is a few hundred bytes in at most; reading the whole of a
    /// multi-megabyte world blob to find it would make listing orphans feel broken.</summary>
    public int HeadBytes => 8192;

    public WgsBlobDescription? Inspect(ReadOnlySpan<byte> head)
    {
        var world = ReadWorldName(head);
        return world is null ? null : new WgsBlobDescription(world, $"{world}-WC");
    }

    private static string? ReadWorldName(ReadOnlySpan<byte> data)
    {
        var pos = 0;
        if (ReadBundleString(data, ref pos) != "ABF_SAVE_VERSION") return null;
        pos += 16; // version, uncompressed size, opaque field, member count
        if (pos >= data.Length) return null;
        var first = ReadBundleString(data, ref pos);
        if (first is null) return null;

        // Members are recorded as "Profile/Worlds/<World>/WorldSave_..." (or with backslashes).
        var parts = first.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Worlds", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(parts[i + 1]))
            {
                return parts[i + 1];
            }
        }
        return null;
    }

    /// <summary>
    /// One UE FString from a bundle header: a positive length counts ASCII bytes, a negative one
    /// counts UTF-16 characters (which is what the engine writes as soon as a world name leaves
    /// ASCII). Returns null rather than throwing when the bytes do not describe a string.
    /// </summary>
    private static string? ReadBundleString(ReadOnlySpan<byte> data, ref int pos)
    {
        if (pos + 4 > data.Length) return null;
        var length = BitConverter.ToInt32(data[pos..]);
        pos += 4;
        if (length == 0) return string.Empty;
        if (length > 0)
        {
            if (length > 4096 || pos + length > data.Length) return null;
            var ascii = Encoding.ASCII.GetString(data.Slice(pos, length)).TrimEnd('\0');
            pos += length;
            return ascii;
        }
        if (length == int.MinValue) return null;
        var bytes = -length * 2;
        if (bytes > 8192 || pos + bytes > data.Length) return null;
        var wide = Encoding.Unicode.GetString(data.Slice(pos, bytes)).TrimEnd('\0');
        pos += bytes;
        return wide;
    }
}
