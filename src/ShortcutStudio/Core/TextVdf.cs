using System.Text;

namespace ShortcutStudio.Core;

/// <summary>
/// Steam's text KeyValues (localconfig.vdf, loginusers.vdf). Values are kept exactly as written (escapes included),
/// and the writer uses Steam's own layout, so a load/save round trip is byte-identical.
/// </summary>
public sealed class TextVdf
{
    public List<KeyValuePair<string, object>> Items { get; } = new(); // value: string (raw) or TextVdf

    public TextVdf? Child(string key) =>
        Items.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value as TextVdf;

    public string? Get(string key) =>
        Items.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value as string;

    public void Set(string key, string value)
    {
        int i = Items.FindIndex(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Items[i] = new(Items[i].Key, value);
        else Items.Add(new(key, value));
    }

    public TextVdf GetOrAddChild(string key)
    {
        if (Child(key) is { } c) return c;
        var n = new TextVdf();
        Items.Add(new(key, n));
        return n;
    }

    public static TextVdf Parse(string text)
    {
        int pos = 0;
        var root = new TextVdf();
        ParseInto(text, ref pos, root, topLevel: true);
        return root;
    }

    static void ParseInto(string s, ref int pos, TextVdf node, bool topLevel)
    {
        while (true)
        {
            SkipSpace(s, ref pos);
            if (pos >= s.Length) return;
            if (s[pos] == '}')
            {
                if (topLevel) throw new InvalidDataException("Unexpected '}'");
                pos++;
                return;
            }
            var key = ReadString(s, ref pos);
            SkipSpace(s, ref pos);
            if (pos < s.Length && s[pos] == '{')
            {
                pos++;
                var child = new TextVdf();
                ParseInto(s, ref pos, child, topLevel: false);
                node.Items.Add(new(key, child));
            }
            else node.Items.Add(new(key, ReadString(s, ref pos)));
        }
    }

    static void SkipSpace(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) pos++;
            else if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/') { while (pos < s.Length && s[pos] != '\n') pos++; }
            else break;
        }
    }

    /// <summary>Reads a quoted token, returning it raw (escape sequences left as written).</summary>
    static string ReadString(string s, ref int pos)
    {
        if (pos >= s.Length || s[pos] != '"') throw new InvalidDataException($"Expected '\"' at {pos}");
        int start = ++pos;
        while (pos < s.Length && s[pos] != '"')
        {
            if (s[pos] == '\\' && pos + 1 < s.Length) pos++;
            pos++;
        }
        var raw = s.Substring(start, pos - start);
        pos++; // closing quote
        return raw;
    }

    public string Write()
    {
        var sb = new StringBuilder();
        WriteItems(sb, this, 0);
        return sb.ToString();
    }

    static void WriteItems(StringBuilder sb, TextVdf node, int depth)
    {
        var tabs = new string('\t', depth);
        foreach (var (key, value) in node.Items)
        {
            if (value is TextVdf child)
            {
                sb.Append(tabs).Append('"').Append(key).Append("\"\n");
                sb.Append(tabs).Append("{\n");
                WriteItems(sb, child, depth + 1);
                sb.Append(tabs).Append("}\n");
            }
            else sb.Append(tabs).Append('"').Append(key).Append("\"\t\t\"").Append((string)value).Append("\"\n");
        }
    }
}

/// <summary>
/// Steam's own playtime for a game, kept in userdata\&lt;id&gt;\config\localconfig.vdf under
/// UserLocalConfigStore/Software/Valve/Steam/apps/&lt;appid&gt; (shortcuts use their appid as a signed number).
/// Steam rewrites this file on exit, so it must only be written while Steam is closed.
/// </summary>
public static class SteamPlaytime
{
    static string FilePath(uint accountId) => Path.Combine(Steam.UserConfigDir(accountId), "localconfig.vdf");
    static string Key(uint appId) => unchecked((int)appId).ToString();

    static TextVdf? Apps(TextVdf root, bool create)
    {
        var store = root.Child("UserLocalConfigStore");
        if (store == null) return null;
        var node = store;
        foreach (var k in new[] { "Software", "Valve", "Steam", "apps" })
        {
            var next = node.Child(k);
            if (next == null)
            {
                if (!create) return null;
                next = node.GetOrAddChild(k);
            }
            node = next;
        }
        return node;
    }

    /// <summary>Total minutes Steam has recorded for the game.</summary>
    public static int GetMinutes(uint accountId, uint appId)
    {
        try
        {
            var path = FilePath(accountId);
            if (!File.Exists(path)) return 0;
            var app = Apps(TextVdf.Parse(File.ReadAllText(path)), false)?.Child(Key(appId));
            return int.TryParse(app?.Get("Playtime"), out var m) ? m : 0;
        }
        catch { return 0; }
    }

    /// <summary>Sets Steam's total minutes for the game. Call only while Steam is closed.</summary>
    public static void SetMinutes(uint accountId, uint appId, int minutes)
    {
        var path = FilePath(accountId);
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var root = TextVdf.Parse(text);
        var apps = Apps(root, true);
        if (apps == null) return;
        apps.GetOrAddChild(Key(appId)).Set("Playtime", Math.Max(0, minutes).ToString());

        File.Copy(path, path + ".bak", true);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.Write(), new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }
}
