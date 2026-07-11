namespace AbioticEditor.Core.Steam;

/// <summary>Parsed binary-KeyValues node: either a value or a dict of children.</summary>
public sealed class KvNode
{
    public KvNode(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public object? Value { get; set; }
    public List<KvNode> Children { get; } = new();

    public KvNode? Find(string key)
        => Children.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

    public KvNode? FindPath(params string[] keys)
    {
        var node = this;
        foreach (var k in keys)
        {
            node = node?.Find(k);
            if (node is null) return null;
        }
        return node;
    }

    /// <summary>Depth-first search for the first node with the given key.</summary>
    public KvNode? FindDeep(string key)
    {
        foreach (var c in Children)
        {
            if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return c;
            if (c.FindDeep(key) is { } hit) return hit;
        }
        return null;
    }

    public string? AsString() => Value?.ToString();
    public int AsInt() => Value switch { int i => i, uint u => (int)u, long l => (int)l, float f => (int)f, _ => 0 };
    public long AsLong() => Value switch { int i => i, uint u => u, long l => l, ulong ul => (long)ul, float f => (long)f, _ => 0 };
}

/// <summary>
/// Minimal Valve binary-KeyValues reader (the format used by Steam's appcache files).
/// Layout per entry: type byte, NUL-terminated key, then a type-specific payload;
/// 0x08 closes the current dict (0x0B in the v2 variant some files use).
/// </summary>
public static class BinaryKeyValues
{
    public static KvNode Parse(byte[] data)
    {
        var pos = 0;
        var root = new KvNode("(root)");
        ParseDict(data, ref pos, root);
        return root;
    }

    private static void ParseDict(byte[] d, ref int pos, KvNode parent)
    {
        while (pos < d.Length)
        {
            var type = d[pos++];
            if (type == 0x08 || type == 0x0B) return; // end of dict

            var key = ReadCString(d, ref pos);
            var node = new KvNode(key);
            parent.Children.Add(node);

            switch (type)
            {
                case 0x00: // nested dict
                    ParseDict(d, ref pos, node);
                    break;
                case 0x01: // string
                    node.Value = ReadCString(d, ref pos);
                    break;
                case 0x02: // int32
                    node.Value = BitConverter.ToInt32(d, pos); pos += 4;
                    break;
                case 0x03: // float32
                    node.Value = BitConverter.ToSingle(d, pos); pos += 4;
                    break;
                case 0x04: // pointer (int32)
                case 0x06: // color (int32)
                    node.Value = BitConverter.ToInt32(d, pos); pos += 4;
                    break;
                case 0x05: // wide string
                    node.Value = ReadWString(d, ref pos);
                    break;
                case 0x07: // uint64
                    node.Value = BitConverter.ToUInt64(d, pos); pos += 8;
                    break;
                case 0x0A: // int64
                    node.Value = BitConverter.ToInt64(d, pos); pos += 8;
                    break;
                default:
                    throw new InvalidDataException($"Unknown binary-KV type 0x{type:X2} at offset {pos - 1}.");
            }
        }
    }

    private static string ReadCString(byte[] d, ref int pos)
    {
        var start = pos;
        while (pos < d.Length && d[pos] != 0) pos++;
        var s = System.Text.Encoding.UTF8.GetString(d, start, pos - start);
        pos++; // NUL
        return s;
    }

    private static string ReadWString(byte[] d, ref int pos)
    {
        var start = pos;
        while (pos + 1 < d.Length && !(d[pos] == 0 && d[pos + 1] == 0)) pos += 2;
        var s = System.Text.Encoding.Unicode.GetString(d, start, pos - start);
        pos += 2;
        return s;
    }
}
