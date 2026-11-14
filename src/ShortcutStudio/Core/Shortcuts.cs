using System.Text;

namespace ShortcutStudio.Core;

/// <summary>One non-Steam game entry in shortcuts.vdf.</summary>
public sealed class Shortcut
{
    /// <summary>The raw VDF entry; unknown fields are preserved on save.</summary>
    public VdfNode Node { get; }

    public Shortcut(VdfNode node) => Node = node;

    public uint AppId
    {
        get => unchecked((uint)Node.GetInt("appid"));
        set => Node["appid"] = unchecked((int)value);
    }
    public string Name { get => Node.GetString("AppName"); set => Node["AppName"] = value; }
    public string Exe { get => Node.GetString("Exe"); set => Node["Exe"] = value; }
    public string StartDir { get => Node.GetString("StartDir"); set => Node["StartDir"] = value; }
    public string Icon { get => Node.GetString("icon"); set => Node["icon"] = value; }
    public string LaunchOptions { get => Node.GetString("LaunchOptions"); set => Node["LaunchOptions"] = value; }
    public bool IsHidden { get => Node.GetInt("IsHidden") != 0; set => Node["IsHidden"] = value ? 1 : 0; }
    public bool AllowOverlay { get => Node.GetInt("AllowOverlay") != 0; set => Node["AllowOverlay"] = value ? 1 : 0; }
    public bool AllowDesktopConfig { get => Node.GetInt("AllowDesktopConfig") != 0; set => Node["AllowDesktopConfig"] = value ? 1 : 0; }

    public List<string> Tags
    {
        get => Node.GetMap("tags")?.Items.Select(kv => kv.Value as string ?? "").Where(s => s != "").ToList() ?? new();
        set
        {
            var map = new VdfNode();
            for (int i = 0; i < value.Count; i++) map.Items.Add(new(i.ToString(), value[i]));
            Node["tags"] = map;
        }
    }

    /// <summary>Exe path without the quotes Steam wraps it in.</summary>
    public string ExePath => Exe.Trim().Trim('"');

    public static Shortcut Create(string name, string exePath)
    {
        // Same fields, in the same order, as Steam writes for a shortcut it adds itself.
        var n = new VdfNode();
        n.Items.Add(new("appid", 0));
        n.Items.Add(new("AppName", name));
        n.Items.Add(new("Exe", Quote(exePath)));
        n.Items.Add(new("StartDir", Quote(Path.GetDirectoryName(exePath) ?? "")));
        n.Items.Add(new("icon", ""));
        n.Items.Add(new("ShortcutPath", ""));
        n.Items.Add(new("LaunchOptions", ""));
        n.Items.Add(new("IsHidden", 0));
        n.Items.Add(new("AllowDesktopConfig", 1));
        n.Items.Add(new("AllowOverlay", 1));
        n.Items.Add(new("OpenVR", 0));
        n.Items.Add(new("Devkit", 0));
        n.Items.Add(new("DevkitGameID", ""));
        n.Items.Add(new("DevkitOverrideAppID", 0));
        n.Items.Add(new("LastPlayTime", 0));
        n.Items.Add(new("FlatpakAppID", ""));
        n.Items.Add(new("sortas", ""));
        n.Items.Add(new("tags", new VdfNode()));
        var s = new Shortcut(n);
        s.AppId = GenerateAppId(s.Exe, name);
        return s;
    }

    public static string Quote(string path) => string.IsNullOrEmpty(path) ? "" : $"\"{path.Trim('"')}\"";

    /// <summary>The appid Steam and other tools (Steam ROM Manager, SteamGridDB) use for a shortcut.</summary>
    public static uint GenerateAppId(string exe, string name)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in Encoding.UTF8.GetBytes(exe + name))
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc | 0x80000000;
    }
}

/// <summary>Reads and writes a user's shortcuts.vdf.</summary>
public sealed class ShortcutFile
{
    readonly VdfNode _root;
    public string Path { get; }
    public List<Shortcut> Items { get; }

    ShortcutFile(string path, VdfNode root)
    {
        Path = path;
        _root = root;
        var list = root.GetMap("shortcuts") ?? new VdfNode();
        Items = list.Items.Select(kv => kv.Value).OfType<VdfNode>().Select(n => new Shortcut(n)).ToList();
    }

    public static ShortcutFile Load(string path)
    {
        if (!File.Exists(path))
        {
            var root = new VdfNode();
            root.Items.Add(new("shortcuts", new VdfNode()));
            return new ShortcutFile(path, root);
        }
        return new ShortcutFile(path, VdfNode.Read(File.ReadAllBytes(path)));
    }

    public void Save()
    {
        // Steam numbers entries "0", "1", ... in order.
        var list = new VdfNode();
        for (int i = 0; i < Items.Count; i++) list.Items.Add(new(i.ToString(), Items[i].Node));
        _root["shortcuts"] = list;

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (File.Exists(Path))
        {
            // One rolling backup plus a dated one per day, so a bad save is always recoverable.
            File.Copy(Path, Path + ".bak", true);
            var daily = Path + $".{DateTime.Now:yyyyMMdd}.bak";
            if (!File.Exists(daily)) File.Copy(Path, daily);
        }
        var tmp = Path + ".tmp";
        File.WriteAllBytes(tmp, _root.Write());
        File.Move(tmp, Path, true);
    }
}
