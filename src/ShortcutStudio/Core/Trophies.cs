using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ShortcutStudio.Core;

/// <summary>One trophy. <paramref name="Dlc"/> is its group's name for DLC trophy packs (group 0 is the base game), or "".</summary>
public sealed record TrophyDef(int Id, string Name, string Detail, bool Hidden, string Grade, int Group, byte[]? Icon, string Dlc = "");

/// <summary>A PlayStation trophy set found in an emulator's files, plus where that emulator records unlocks.</summary>
public sealed class TrophySet
{
    public required string Emulator { get; init; }      // "shadps4" or "rpcs3"
    public required string NpCommId { get; init; }      // e.g. NPWR05818_00
    public required string Title { get; init; }
    public required List<TrophyDef> Trophies { get; init; }
    /// <summary>shadPS4: user\home\&lt;id&gt;\trophy\&lt;NPWR&gt;.xml. RPCS3: ...\trophy\&lt;NPWR&gt;\TROPUSR.DAT. May not exist yet.</summary>
    public required string UnlockFile { get; init; }
    /// <summary>Emulator user this set belongs to (shadPS4 user id, RPCS3 home folder).</summary>
    public required string User { get; init; }
    public required string Origin { get; init; }        // where the list was read from, for display

    public int UnlockedCount => Trophies.Count(t => Unlocks.ContainsKey(t.Id));
    public Dictionary<int, long> Unlocks => EmulatorTrophies.ReadUnlocks(Emulator, UnlockFile, NpCommId);

    public override string ToString() =>
        $"{Title} · {EmulatorTrophies.EmulatorName(Emulator)} · {UnlockedCount}/{Trophies.Count} unlocked";
}

/// <summary>Where Shortcut Studio reads live unlocks from. Stored on the game's extras; the plugin reads it too.</summary>
public sealed class TrophySource
{
    public string Emulator { get; set; } = "";
    public string UnlockFile { get; set; } = "";
    public string NpCommId { get; set; } = "";
    public string Title { get; set; } = "";
    public string User { get; set; } = "";
}

/// <summary>Reads PS3 (RPCS3) and PS4 (shadPS4) trophy data from emulator folders.</summary>
public static class EmulatorTrophies
{
    public const string IdPrefix = "trophy-";
    public static string AchievementId(int trophyId) => $"{IdPrefix}{trophyId:000}";

    public static string GradeName(string g) => g switch { "P" => "Platinum", "G" => "Gold", "S" => "Silver", "B" => "Bronze", _ => "" };

    // ---------------- Discovery ----------------

    /// <summary>Every trophy set reachable from a shortcut's program, start folder and launch options.</summary>
    public static List<TrophySet> FindForShortcut(string exe, string startDir, string launchOptions)
    {
        var paths = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p)) paths.Add(p.Trim().Trim('"')); }
        Add(exe); Add(startDir);
        // Paths inside the launch options, quoted or not (e.g. -g "E:/.../eboot.bin", or rpcs3.exe "...EBOOT.BIN").
        foreach (Match m in Regex.Matches(launchOptions + " " + exe, "\"([^\"]+)\"|(\\S*[\\\\/]\\S+)")) Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        var sets = new List<TrophySet>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            foreach (var root in Ancestors(p))
            {
                if (!seenRoots.Add(root)) continue;
                if (Directory.Exists(Path.Combine(root, "dev_hdd0", "home"))) sets.AddRange(Rpcs3Sets(root));
                if (Directory.Exists(Path.Combine(root, "user", "home"))) sets.AddRange(ShadPs4Sets(root, paths));
                if (Directory.Exists(Path.Combine(root, "setup", "ps3recomp"))) sets.AddRange(Ps3RecompSets(root));
            }
        }
        // Same set found twice (e.g. extracted and from the .trp): keep the first.
        return sets.GroupBy(s => (s.Emulator, s.NpCommId, s.User)).Select(g => g.First()).ToList();
    }

    /// <summary>Trophy sets under a folder the user picked by hand (an emulator folder or a game folder).</summary>
    public static List<TrophySet> FindInFolder(string folder) => FindForShortcut(folder, folder, "");

    static IEnumerable<string> Ancestors(string path)
    {
        string? dir;
        try { dir = File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path)) : Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch { yield break; }
        // "E:\Game\" and "E:\Game" are the same folder.
        if (dir != null && dir.Length > 3) dir = Path.TrimEndingDirectorySeparator(dir);
        for (int i = 0; dir != null && i < 8; i++, dir = Path.GetDirectoryName(dir)) yield return dir;
    }

    // ---------------- RPCS3 ----------------

    static IEnumerable<TrophySet> Rpcs3Sets(string root)
    {
        var home = Path.Combine(root, "dev_hdd0", "home");
        foreach (var userDir in SafeDirs(home))
        {
            foreach (var setDir in SafeDirs(Path.Combine(userDir, "trophy")))
            {
                var conf = Path.Combine(setDir, "TROPCONF.SFM");
                if (!File.Exists(conf)) continue;
                TrophySet? set = null;
                try
                {
                    var (title, defs) = ParseTrophyXml(File.ReadAllText(conf), n => ReadIfExists(Path.Combine(setDir, $"TROP{n:000}.PNG")));
                    set = new TrophySet
                    {
                        Emulator = "rpcs3", NpCommId = Path.GetFileName(setDir), Title = title, Trophies = defs,
                        UnlockFile = Path.Combine(setDir, "TROPUSR.DAT"), User = Path.GetFileName(userDir), Origin = setDir,
                    };
                }
                catch { }
                if (set != null) yield return set;
            }
        }
    }

    /// <summary>RPCS3's TROPUSR.DAT: big-endian tables; type 6 holds per-trophy unlock state and a CellRtc tick.</summary>
    public static Dictionary<int, long> ReadRpcs3Unlocks(string path)
    {
        var result = new Dictionary<int, long>();
        var b = ReadShared(path);
        if (b == null || b.Length < 48 || BeU32(b, 0) != 0x818F54AD) return result;
        uint tables = BeU32(b, 8);
        for (int i = 0; i < tables; i++)
        {
            int h = 48 + i * 32;
            if (h + 32 > b.Length) break;
            uint type = BeU32(b, h), size = BeU32(b, h + 4), count = BeU32(b, h + 12);
            long off = (long)BeU64(b, h + 16);
            if (type != 6) continue;
            for (int e = 0; e < count; e++)
            {
                long p = off + e * (16 + size);
                if (p + 48 > b.Length) break;
                int id = (int)BeU32(b, (int)p + 16);
                if (BeU32(b, (int)p + 20) == 0) continue;
                ulong tick = BeU64(b, (int)p + 32); // microseconds since 0001-01-01
                long ms = tick > 62135596800000000UL ? (long)((tick - 62135596800000000UL) / 1000) : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                result[id] = ms;
            }
        }
        return result;
    }

    // ---------------- shadPS4 ----------------

    static IEnumerable<TrophySet> ShadPs4Sets(string root, List<string> hints)
    {
        var user = Path.Combine(root, "user");
        var userIds = ShadUserIds(user);
        var sets = new List<(string npid, string title, List<TrophyDef> defs, string origin)>();

        // Lists shadPS4 already unpacked (after the game has run once).
        foreach (var setDir in SafeDirs(Path.Combine(user, "trophy")))
        {
            var xmlDir = Path.Combine(setDir, "Xml");
            var xml = new[] { "TROP_01.XML", "TROP.XML", "TROP_00.XML" }.Select(f => Path.Combine(xmlDir, f)).FirstOrDefault(File.Exists);
            if (xml == null) continue;
            try
            {
                var icons = Path.Combine(setDir, "Icons");
                var (title, defs) = ParseTrophyXml(File.ReadAllText(xml), n => ReadIfExists(Path.Combine(icons, $"TROP{n:000}.PNG")));
                sets.Add((Path.GetFileName(setDir), title, defs, setDir));
            }
            catch { }
        }

        // Games referenced by the shortcut (-g ".../eboot.bin"): decrypt their trophy00.trp directly.
        var key = ShadTrophyKey(user);
        if (key != null)
        {
            foreach (var gameDir in hints.SelectMany(h => Ancestors(h).Take(3)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var npbind = Path.Combine(gameDir, "sce_sys", "npbind.dat");
                if (!File.Exists(npbind)) continue;
                var npid = Regex.Match(Encoding.ASCII.GetString(File.ReadAllBytes(npbind)), "NPWR\\d{5}_\\d{2}").Value;
                if (npid == "" || sets.Any(s => s.npid == npid)) continue;
                // An update's trophy pack is newer than the base game's.
                var trp = new[] { gameDir + "-patch", gameDir }.Select(d => Path.Combine(d, "sce_sys", "trophy", "trophy00.trp")).FirstOrDefault(File.Exists);
                if (trp == null) continue;
                try
                {
                    var (title, defs) = ReadTrp(trp, npid, key);
                    sets.Add((npid, title, defs, trp));
                }
                catch { }
            }
        }

        foreach (var (npid, title, defs, origin) in sets)
            foreach (var uid in userIds)
                yield return new TrophySet
                {
                    Emulator = "shadps4", NpCommId = npid, Title = title, Trophies = defs,
                    UnlockFile = Path.Combine(user, "home", uid, "trophy", npid + ".xml"), User = uid, Origin = origin,
                };
    }

    static List<string> ShadUserIds(string user)
    {
        var ids = new List<string>();
        try
        {
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(user, "users.json")));
            foreach (var u in json.RootElement.GetProperty("Users").GetProperty("user").EnumerateArray())
                ids.Add(u.GetProperty("user_id").GetInt32().ToString());
        }
        catch { }
        if (ids.Count == 0) ids.AddRange(SafeDirs(Path.Combine(user, "home")).Select(Path.GetFileName).OfType<string>());
        if (ids.Count == 0) ids.Add("1000");
        // Users that already have progress for anything come first.
        return ids.OrderByDescending(id => SafeFiles(Path.Combine(user, "home", id, "trophy")).Any()).ThenBy(id => id).ToList();
    }

    static byte[]? ShadTrophyKey(string user)
    {
        try
        {
            var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(user, "keys.json")));
            var hex = json.RootElement.GetProperty("TrophyKeySet").GetProperty("ReleaseTrophyKey").GetString();
            if (hex is { Length: 32 }) return Convert.FromHexString(hex);
        }
        catch { }
        // Older builds kept it in config.toml as TrophyKey = "...".
        try
        {
            var toml = File.ReadAllText(Path.Combine(user, "config.toml"));
            var m = Regex.Match(toml, "TrophyKey\\s*=\\s*\"([0-9A-Fa-f]{32})\"");
            if (m.Success) return Convert.FromHexString(m.Groups[1].Value);
        }
        catch { }
        return null;
    }

    /// <summary>Decrypts a PS4 trophy00.trp the way shadPS4 does (see its trp.cpp).</summary>
    public static (string title, List<TrophyDef> defs) ReadTrp(string path, string npCommId, byte[] releaseKey)
    {
        var b = File.ReadAllBytes(path);
        if (BeU32(b, 0) != 0xDCA24D00) throw new InvalidDataException("Not a trophy pack");
        uint count = BeU32(b, 16), entrySize = BeU32(b, 20);

        // trpKey = AES-128(npCommId zero-padded to 16 bytes) under the release trophy key.
        var idBlock = new byte[16];
        Encoding.ASCII.GetBytes(npCommId).AsSpan(0, Math.Min(16, npCommId.Length)).CopyTo(idBlock);
        using var aes = Aes.Create();
        aes.Key = releaseKey;
        var trpKey = aes.EncryptEcb(idBlock, PaddingMode.None);

        var entries = new Dictionary<string, (long pos, long len, uint flag)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int p = 96 + i * (int)entrySize;
            var name = Encoding.ASCII.GetString(b, p, 32).Split('\0')[0];
            entries[name] = ((long)BeU64(b, p + 32), (long)BeU64(b, p + 40), BeU32(b, p + 48));
        }

        string? xml = null;
        foreach (var want in new[] { "TROP_01.ESFM", "TROP.ESFM", "TROP_00.ESFM" })
        {
            if (!entries.TryGetValue(want, out var e) || e.flag != 3 || e.len <= 16) continue;
            using var dec = Aes.Create();
            dec.Key = trpKey;
            var iv = b.AsSpan((int)e.pos, 16).ToArray();
            var plain = dec.DecryptCbc(b.AsSpan((int)e.pos + 16, (int)e.len - 16), iv, PaddingMode.None);
            var text = Encoding.UTF8.GetString(plain);
            xml = text[..(text.LastIndexOf('>') + 1)];
            break;
        }
        if (xml == null) throw new InvalidDataException("No trophy list in the pack");

        byte[]? Icon(int n)
        {
            if (!entries.TryGetValue($"TROP{n:000}.PNG", out var e) || e.flag != 0) return null;
            return b.AsSpan((int)e.pos, (int)e.len).ToArray();
        }
        return ParseTrophyXml(xml, Icon);
    }

    /// <summary>shadPS4's per-user progress file: &lt;trophy id="1" unlockstate="true" timestamp="unix seconds"&gt;.</summary>
    public static Dictionary<int, long> ReadShadPs4Unlocks(string path)
    {
        var result = new Dictionary<int, long>();
        var bytes = ReadShared(path);
        if (bytes == null) return result;
        var text = Encoding.UTF8.GetString(bytes);
        foreach (Match m in Regex.Matches(text, "<trophy\\s[^>]*>"))
        {
            var tag = m.Value;
            if (!Regex.IsMatch(tag, "unlockstate\\s*=\\s*\"true\"", RegexOptions.IgnoreCase)) continue;
            var id = Regex.Match(tag, "\\bid\\s*=\\s*\"(\\d+)\"");
            if (!id.Success) continue;
            var ts = Regex.Match(tag, "timestamp\\s*=\\s*\"(\\d+)\"");
            long secs = ts.Success ? long.Parse(ts.Groups[1].Value) : 0;
            // Seconds per shadPS4's code; tolerate milliseconds too.
            result[int.Parse(id.Groups[1].Value)] = secs > 100_000_000_000 ? secs : secs * 1000;
        }
        return result;
    }

    public static Dictionary<int, long> ReadUnlocks(string emulator, string path, string npCommId = "") => emulator switch
    {
        "rpcs3" => ReadRpcs3Unlocks(path),
        "retroachievements" => RetroAchievements.ReadUnlockCache(path),
        "ps3recomp" => ReadPs3RecompUnlocks(path),
        "savefile" or "dusklight" => ReadSaveFileUnlocks(npCommId),
        _ => ReadShadPs4Unlocks(path),
    };

    public static string EmulatorName(string emulator) => emulator switch
    {
        "rpcs3" => "RPCS3",
        "retroachievements" => "RetroAchievements",
        "ps3recomp" => "the PC port",
        "savefile" => "your save files",
        "dusklight" => "Dusklight",
        _ => "shadPS4",
    };

    /// <summary>
    /// Save-file achievements (games with no trophy system of their own): the Steam plugin checks the rules in
    /// extras\saverules\&lt;id&gt;.json against the game's saves and records unlocks in &lt;id&gt;.unlocks.json
    /// as { "&lt;trophy id&gt;": unix ms }.
    /// </summary>
    public static Dictionary<int, long> ReadSaveFileUnlocks(string rulesId)
    {
        var result = new Dictionary<int, long>();
        if (string.IsNullOrWhiteSpace(rulesId) || rulesId.IndexOfAny(new[] { '/', '\\' }) >= 0 || rulesId.Contains("..")) return result;
        var bytes = ReadShared(Path.Combine(ExtrasStore.Dir, "saverules", rulesId + ".unlocks.json"));
        if (bytes == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (int.TryParse(p.Name, out var id) && p.Value.TryGetInt64(out var ms)) result[id] = ms;
        }
        catch (JsonException) { }
        return result;
    }

    // ---------------- PS3 recompilations (ps3recomp) ----------------

    /// <summary>
    /// Native PC ports built on ps3recomp keep the PS3 trophy set (read from the disc's TROPDIR) and write unlocks
    /// to gamedata\trophies\&lt;comm id&gt;.json, relative to the folder the game runs in (or %PS3_TROPHY_DIR%).
    /// </summary>
    static IEnumerable<TrophySet> Ps3RecompSets(string root)
    {
        var sets = new List<TrophySet>();
        foreach (var tropDir in RecompTropDirs(root))
        {
            foreach (var setDir in SafeDirs(tropDir))
            {
                var trp = Path.Combine(setDir, "TROPHY.TRP");
                if (!File.Exists(trp)) continue;
                var commId = Path.GetFileName(setDir); // e.g. NPWR00881_00
                if (sets.Any(s => s.NpCommId == commId)) continue;
                try
                {
                    var (title, defs) = ReadPs3Trp(trp);
                    var m = Regex.Match(commId, "^(.+?)_(\\d{2})$");
                    var file = m.Success && m.Groups[2].Value != "00" ? $"{m.Groups[1].Value}_{m.Groups[2].Value}.json" : $"{(m.Success ? m.Groups[1].Value : commId)}.json";
                    var dir = Environment.GetEnvironmentVariable("PS3_TROPHY_DIR", EnvironmentVariableTarget.User) is { Length: > 0 } env
                        ? (Path.IsPathRooted(env) ? env : Path.Combine(root, env))
                        : Path.Combine(root, "gamedata", "trophies");
                    sets.Add(new TrophySet
                    {
                        Emulator = "ps3recomp", NpCommId = commId, Title = title, Trophies = defs,
                        UnlockFile = Path.Combine(dir, file), User = Path.GetFileName(root), Origin = trp,
                    });
                }
                catch { }
            }
        }
        return sets;
    }

    /// <summary>The disc's TROPDIR: the port logs where its game files are ("VFS root:") in its AppData logs.</summary>
    static IEnumerable<string> RecompTropDirs(string root)
    {
        var found = new List<string>();
        void Add(string? gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot)) return;
            foreach (var d in new[] { Path.Combine(gameRoot, "PS3_GAME", "TROPDIR"), Path.Combine(gameRoot, "TROPDIR") })
                if (Directory.Exists(d) && !found.Contains(d, StringComparer.OrdinalIgnoreCase)) found.Add(d);
        }
        Add(Path.Combine(root, "Build", "inputs"));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var top in SafeDirs(appData))
        {
            foreach (var logs in SafeDirs(top).Append(top).Select(d => Path.Combine(d, "logs")).Where(Directory.Exists))
            {
                foreach (var log in SafeFiles(logs).Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        using var sr = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                        for (int i = 0; i < 40 && sr.ReadLine() is { } line; i++)
                        {
                            var m = Regex.Match(line, @"\[boot\] VFS root: (.+)$");
                            if (m.Success) { Add(m.Groups[1].Value.Trim()); break; }
                        }
                    }
                    catch { }
                }
            }
        }
        return found;
    }

    /// <summary>A PS3 TROPHY.TRP: a 64-byte header, then 64-byte entries; TROP.SFM and the PNGs are stored as is.</summary>
    public static (string title, List<TrophyDef> defs) ReadPs3Trp(string path)
    {
        var b = File.ReadAllBytes(path);
        if (BeU32(b, 0) != 0xDCA24D00) throw new InvalidDataException("Not a trophy pack");
        uint count = BeU32(b, 16), entrySize = BeU32(b, 20);
        var entries = new Dictionary<string, (long pos, long len)>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int p = 64 + i * (int)entrySize;
            var name = Encoding.ASCII.GetString(b, p, 32).Split('\0')[0];
            entries[name] = ((long)BeU64(b, p + 32), (long)BeU64(b, p + 40));
        }
        var sfm = new[] { "TROP_01.SFM", "TROP.SFM", "TROP_00.SFM", "TROPCONF.SFM" }.FirstOrDefault(entries.ContainsKey)
                  ?? throw new InvalidDataException("No trophy list in the pack");
        var (pos, len) = entries[sfm];
        var xml = Encoding.UTF8.GetString(b, (int)pos, (int)len);
        byte[]? Icon(int n) => entries.TryGetValue($"TROP{n:000}.PNG", out var e) ? b.AsSpan((int)e.pos, (int)e.len).ToArray() : null;
        return ParseTrophyXml(xml[..(xml.LastIndexOf('>') + 1)], Icon);
    }

    /// <summary>ps3recomp's unlock file: { "trophies": [ { "id": N, "timestamp": unix seconds } ] }.</summary>
    public static Dictionary<int, long> ReadPs3RecompUnlocks(string path)
    {
        var result = new Dictionary<int, long>();
        var bytes = ReadShared(path);
        if (bytes == null) return result;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (!doc.RootElement.TryGetProperty("trophies", out var list) || list.ValueKind != JsonValueKind.Array) return result;
            foreach (var t in list.EnumerateArray())
            {
                if (!t.TryGetProperty("id", out var id) || !id.TryGetInt32(out var n)) continue;
                long ts = t.TryGetProperty("timestamp", out var tv) && tv.TryGetInt64(out var s) ? s : 0;
                result[n] = ts > 100_000_000_000 ? ts : ts > 0 ? ts * 1000 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }
        catch (JsonException) { }
        return result;
    }

    // ---------------- Shared ----------------

    /// <summary>Parses TROPCONF / TROP.XML (both PS3 and PS4 use the same &lt;trophyconf&gt; layout).</summary>
    public static (string title, List<TrophyDef> defs) ParseTrophyXml(string xml, Func<int, byte[]?> icon)
    {
        var start = xml.IndexOf("<trophyconf", StringComparison.Ordinal);
        var doc = XDocument.Parse(start > 0 ? xml[start..] : xml);
        var root = doc.Root!;
        var title = root.Element("title-name")?.Value.Trim() ?? "";
        var defs = new List<TrophyDef>();
        // DLC trophy packs are <group id="001"><name>…</name></group>; trophies name theirs with gid. Group 0 is the base game.
        var groups = new Dictionary<int, string>();
        foreach (var g in root.Elements("group"))
            if (int.TryParse(g.Attribute("id")?.Value, out var gn) && gn > 0)
                groups[gn] = OneLine(g.Element("name")?.Value) is { Length: > 0 } nm ? WithoutTitle(nm, title) : $"Add-on {gn}";
        foreach (var t in root.Elements("trophy"))
        {
            int id = int.Parse(t.Attribute("id")!.Value);
            defs.Add(new TrophyDef(
                id,
                OneLine(t.Element("name")?.Value) is { Length: > 0 } n ? n : $"Trophy {id}",
                OneLine(t.Element("detail")?.Value),
                string.Equals(t.Attribute("hidden")?.Value, "yes", StringComparison.OrdinalIgnoreCase),
                t.Attribute("ttype")?.Value ?? "",
                int.TryParse(t.Attribute("gid")?.Value, out var gid) ? gid : 0,
                icon(id),
                gid > 0 ? groups.GetValueOrDefault(gid, $"Add-on {gid}") : ""));
        }
        return (title, defs);
    }

    /// <summary>"Game: Expansion" → "Expansion" (Steam already says which game it is).</summary>
    static string WithoutTitle(string name, string title)
    {
        // Only when a separator follows the title, so "Doom Eternal" under "Doom" stays whole.
        var m = Regex.Match(name, "^" + Regex.Escape(title) + @"\s*[:\-–—·]\s*(.+)$", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : name;
    }

    /// <summary>Trophy text often has hard line breaks for the console's popup; Steam shows one line.</summary>
    static string OneLine(string? s) => Regex.Replace(s ?? "", "\\s*\\r?\\n\\s*", " ").Trim();

    /// <summary>Reads a file the emulator may have open for writing.</summary>
    static byte[]? ReadShared(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    static byte[]? ReadIfExists(string p) => File.Exists(p) ? File.ReadAllBytes(p) : null;
    static IEnumerable<string> SafeDirs(string p) { try { return Directory.Exists(p) ? Directory.GetDirectories(p) : Array.Empty<string>(); } catch { return Array.Empty<string>(); } }
    static IEnumerable<string> SafeFiles(string p) { try { return Directory.Exists(p) ? Directory.GetFiles(p) : Array.Empty<string>(); } catch { return Array.Empty<string>(); } }
    static uint BeU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    static ulong BeU64(byte[] b, int o) => (ulong)BeU32(b, o) << 32 | BeU32(b, o + 4);
}
