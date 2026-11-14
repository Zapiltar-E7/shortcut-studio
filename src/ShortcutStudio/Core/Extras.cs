using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortcutStudio.Core;

/// <summary>Store-page details and achievements for one shortcut, shown in Steam by the companion plugin.</summary>
public sealed class GameExtras
{
    /// <summary>The real Steam appid this was imported from, if any.</summary>
    public int SourceAppId { get; set; }
    public string ShortDescription { get; set; } = "";
    /// <summary>Long description as HTML (Steam store markup), or plain text.</summary>
    public string Description { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public List<string> Genres { get; set; } = new();
    public List<Achievement> Achievements { get; set; } = new();
    /// <summary>When set, unlocks are read live from this emulator file (by the app and the Steam plugin).</summary>
    public TrophySource? TrophySource { get; set; }
    /// <summary>Two or more entries make the plugin ask which one to launch, like Steam does for real games.</summary>
    public List<LaunchOption> LaunchOptions { get; set; } = new();
    /// <summary>
    /// The shortcut's own target, start folder and arguments, as set in the app. Launching a menu option changes
    /// what Steam has stored, so blanks in options (and the Details tab) refer to this instead.
    /// </summary>
    public LaunchOption? LaunchBase { get; set; }

    [JsonIgnore]
    public bool IsEmpty => SourceAppId == 0 && ShortDescription == "" && Description == "" && Developer == "" &&
                           Publisher == "" && ReleaseDate == "" && Genres.Count == 0 && Achievements.Count == 0 && TrophySource == null &&
                           LaunchOptions.Count == 0;
}

/// <summary>One entry in a shortcut's launch picker. Empty Exe / StartDir mean "the shortcut's own".</summary>
public sealed class LaunchOption
{
    public string Name { get; set; } = "";
    public string Exe { get; set; } = "";
    public string StartDir { get; set; } = "";
    public string Args { get; set; } = "";
}

public sealed class Achievement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Hidden { get; set; }
    /// <summary>File name inside the game's extras folder, or "".</summary>
    public string Icon { get; set; } = "";
    /// <summary>Locked (greyscale) icon file name, or "" to grey out <see cref="Icon"/>.</summary>
    public string IconGray { get; set; } = "";
    /// <summary>Global unlock percentage shown under the name, like Steam's rarity. Null hides it.</summary>
    public double? GlobalPercent { get; set; }
    /// <summary>PlayStation trophy grade (P, G, S, B) for emulator trophies, or "".</summary>
    public string Grade { get; set; } = "";
    /// <summary>The emulator's trophy id (or RetroAchievements achievement id) this mirrors, or null for hand-made ones.</summary>
    public int? TrophyId { get; set; }
    /// <summary>RetroAchievements points, or null.</summary>
    public int? Points { get; set; }
    /// <summary>
    /// The achievement set it belongs to, like Steam's achievement sets: "" for the base game, otherwise the DLC's name
    /// (achievements with the same name form one DLC set).
    /// </summary>
    public string Dlc { get; set; } = "";
}

/// <summary>
/// Shared data folder: %APPDATA%\ShortcutStudio\extras.
///   games.json   – written by this app: { "&lt;appid&gt;": GameExtras }
///   unlocks.json – written by the plugin (and this app): { "&lt;appid&gt;": { "&lt;achievement id&gt;": unixMs } }
///   &lt;appid&gt;\*.jpg – achievement icons
/// </summary>
public static class ExtrasStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Keep HTML descriptions readable in the file (no < escapes).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShortcutStudio");
    public static string Dir => Path.Combine(Root, "extras");
    static string GamesFile => Path.Combine(Dir, "games.json");
    static string UnlocksFile => Path.Combine(Dir, "unlocks.json");
    public static string GameDir(uint appId) => Path.Combine(Dir, appId.ToString());

    public static Dictionary<string, GameExtras> LoadAll() => Read<Dictionary<string, GameExtras>>(GamesFile) ?? new();

    public static GameExtras Load(uint appId) => LoadAll().TryGetValue(appId.ToString(), out var g) ? g : new GameExtras();

    public static void Save(uint appId, GameExtras extras)
    {
        var all = LoadAll();
        if (extras.IsEmpty) all.Remove(appId.ToString());
        else all[appId.ToString()] = extras;
        Write(GamesFile, all);
        PruneIcons(appId, extras);
    }

    public static void Delete(uint appId)
    {
        var all = LoadAll();
        all.Remove(appId.ToString());
        Write(GamesFile, all);
        var unlocks = LoadUnlocks();
        if (unlocks.Remove(appId.ToString())) Write(UnlocksFile, unlocks);
        if (Directory.Exists(GameDir(appId))) Directory.Delete(GameDir(appId), true);
    }

    /// <summary>Moves extras, unlocks and icons to a new appid.</summary>
    public static void Rename(uint from, uint to)
    {
        if (from == to) return;
        var all = LoadAll();
        if (all.Remove(from.ToString(), out var g)) { all[to.ToString()] = g; Write(GamesFile, all); }
        var unlocks = LoadUnlocks();
        if (unlocks.Remove(from.ToString(), out var u)) { unlocks[to.ToString()] = u; Write(UnlocksFile, unlocks); }
        if (Directory.Exists(GameDir(from)))
        {
            if (Directory.Exists(GameDir(to))) Directory.Delete(GameDir(to), true);
            Directory.Move(GameDir(from), GameDir(to));
        }
    }

    public static Dictionary<string, Dictionary<string, long>> LoadUnlocks() =>
        Read<Dictionary<string, Dictionary<string, long>>>(UnlocksFile) ?? new();

    public static Dictionary<string, long> Unlocks(uint appId) =>
        LoadUnlocks().TryGetValue(appId.ToString(), out var u) ? u : new();

    public static void SaveUnlocks(uint appId, Dictionary<string, long> unlocks)
    {
        var all = LoadUnlocks();
        if (unlocks.Count == 0) all.Remove(appId.ToString());
        else all[appId.ToString()] = unlocks;
        Write(UnlocksFile, all);
    }

    // "Always use this option" choices, written by the plugin's launch picker: { "<appid>": index }.
    static string LaunchChoiceFile => Path.Combine(Dir, "launch_choice.json");

    public static int? LaunchChoice(uint appId) =>
        (Read<Dictionary<string, int>>(LaunchChoiceFile) ?? new()).TryGetValue(appId.ToString(), out var i) ? i : null;

    /// <summary>Forgets the remembered launch option, so Steam asks again next time.</summary>
    public static void ClearLaunchChoice(uint appId)
    {
        var all = Read<Dictionary<string, int>>(LaunchChoiceFile) ?? new();
        if (all.Remove(appId.ToString())) Write(LaunchChoiceFile, all);
    }

    /// <summary>Saves an icon image for a game and returns the file name to store on the achievement.</summary>
    public static string SaveIcon(uint appId, byte[] bytes, string? preferredName = null)
    {
        Directory.CreateDirectory(GameDir(appId));
        var ext = Artwork.ExtensionFor(bytes);
        var name = (preferredName != null ? Sanitize(Path.GetFileNameWithoutExtension(preferredName)) : Guid.NewGuid().ToString("N")[..12]) + ext;
        File.WriteAllBytes(Path.Combine(GameDir(appId), name), bytes);
        return name;
    }

    public static string IconPath(uint appId, string file) => Path.Combine(GameDir(appId), file);

    static void PruneIcons(uint appId, GameExtras extras)
    {
        var dir = GameDir(appId);
        if (!Directory.Exists(dir)) return;
        var used = new HashSet<string>(extras.Achievements.SelectMany(a => new[] { a.Icon, a.IconGray }), StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir))
            if (!used.Contains(Path.GetFileName(f))) File.Delete(f);
    }

    static string Sanitize(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

/// <summary>
    /// Reads a shared data file. A missing file is "no data"; a file that exists but can't be read or parsed is an
    /// error. It must never be treated as empty, or the next save would wipe every game's data.
    /// </summary>
    static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        string? text = null;
        // The Steam plugin or another copy of the app may be replacing the file right now; wait for it.
        for (int attempt = 0; text == null; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                text = sr.ReadToEnd();
            }
            catch (IOException) when (attempt < 20) { Thread.Sleep(50); }
            catch (FileNotFoundException) { return null; }
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<T>(text, Json); }
        catch (JsonException e)
        {
            var keep = path + $".{DateTime.Now:yyyyMMdd-HHmmss}.unreadable";
            File.Copy(path, keep, true);
            throw new InvalidDataException($"{Path.GetFileName(path)} couldn't be read, so nothing was saved (to avoid losing other games' data). A copy is at {keep}.", e);
        }
    }

    static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Keep the last few versions in extras\backups, so a bad save can always be undone.
        if (File.Exists(path) && new FileInfo(path).Length > 2)
        {
            var dir = Path.Combine(Dir, "backups");
            Directory.CreateDirectory(dir);
            File.Copy(path, Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(path)}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), true);
            foreach (var old in Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(path) + ".*.json").OrderByDescending(f => f).Skip(20))
                File.Delete(old);
        }
        var tmp = path + $".{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(tmp, path, true); break; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(50); }
        }
    }
}
