using System.Text;

namespace ShortcutStudio.Core;

/// <summary>
/// Steam's binary KeyValues format, as used by shortcuts.vdf.
/// Keeps every field (including ones this app doesn't know) and their order, so a round trip is lossless.
/// </summary>
public sealed class VdfNode
{
    public const byte TypeMap = 0x00, TypeString = 0x01, TypeInt = 0x02, TypeFloat = 0x03, TypeEnd = 0x08;

    public List<KeyValuePair<string, object>> Items { get; } = new();

    public object? this[string key]
    {
        get => Items.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        set
        {
            int i = Items.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            if (value == null) { if (i >= 0) Items.RemoveAt(i); return; }
            if (i >= 0) Items[i] = new(Items[i].Key, value);
            else Items.Add(new(key, value));
        }
    }

    public string GetString(string key) => this[key] as string ?? "";
    public int GetInt(string key) => this[key] is int i ? i : 0;
    public VdfNode? GetMap(string key) => this[key] as VdfNode;

    public static VdfNode Read(byte[] data)
    {
        int pos = 0;
        // File is one top-level map: 00 "shortcuts" 00 { ... } 08 08
        var root = new VdfNode();
        ReadMap(data, ref pos, root);
        return root;
    }

    static void ReadMap(byte[] d, ref int pos, VdfNode node)
    {
        while (pos < d.Length)
        {
            byte type = d[pos++];
            if (type == TypeEnd) return;
            string key = ReadCString(d, ref pos);
            switch (type)
            {
                case TypeMap:
                    var child = new VdfNode();
                    ReadMap(d, ref pos, child);
                    node.Items.Add(new(key, child));
                    break;
                case TypeString:
                    node.Items.Add(new(key, ReadCString(d, ref pos)));
                    break;
                case TypeInt:
                    node.Items.Add(new(key, BitConverter.ToInt32(d, pos)));
                    pos += 4;
                    break;
                case TypeFloat:
                    node.Items.Add(new(key, BitConverter.ToSingle(d, pos)));
                    pos += 4;
                    break;
                default:
                    throw new InvalidDataException($"Unknown VDF field type 0x{type:X2} at byte {pos - 1}");
            }
        }
    }

    static string ReadCString(byte[] d, ref int pos)
    {
        int start = pos;
        while (pos < d.Length && d[pos] != 0) pos++;
        string s = Encoding.UTF8.GetString(d, start, pos - start);
        pos++; // skip terminator
        return s;
    }

    public byte[] Write()
    {
        using var ms = new MemoryStream();
        WriteMap(ms, this);
        ms.WriteByte(TypeEnd); // closes the implicit top-level map
        return ms.ToArray();
    }

    static void WriteMap(Stream s, VdfNode node)
    {
        foreach (var (key, value) in node.Items)
        {
            switch (value)
            {
                case VdfNode child:
                    s.WriteByte(TypeMap); WriteCString(s, key);
                    WriteMap(s, child);
                    s.WriteByte(TypeEnd);
                    break;
                case string str:
                    s.WriteByte(TypeString); WriteCString(s, key); WriteCString(s, str);
                    break;
                case int i:
                    s.WriteByte(TypeInt); WriteCString(s, key); s.Write(BitConverter.GetBytes(i));
                    break;
                case float f:
                    s.WriteByte(TypeFloat); WriteCString(s, key); s.Write(BitConverter.GetBytes(f));
                    break;
            }
        }
    }

    static void WriteCString(Stream s, string str)
    {
        s.Write(Encoding.UTF8.GetBytes(str));
        s.WriteByte(0);
    }
}
