using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

public sealed record RaConsole(int Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record RaGameEntry(int Id, string Title, int ConsoleId, string ConsoleName, string Icon, int NumAchievements)
{
    public string IconUrl => RetroAchievements.Media + Icon;
}

public sealed record RaAchievement(int Id, string Title, string Description, int Points, string BadgeName, int NumAwarded, int DisplayOrder, string Type, long? EarnedMs, bool Hardcore)
{
    public string BadgeUrl => $"{RetroAchievements.Media}/Badge/{BadgeName}.png";
    public string LockedBadgeUrl => $"{RetroAchievements.Media}/Badge/{BadgeName}_lock.png";
}

public sealed class RaGame
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string ConsoleName { get; init; } = "";
    public string Developer { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string Genre { get; init; } = "";
    public string Released { get; init; } = "";
    public int NumDistinctPlayers { get; init; }
    public List<RaAchievement> Achievements { get; init; } = new();
}

/// <summary>RetroAchievements Web API (needs the user's username and Web API key from retroachievements.org/settings).</summary>
public static class RetroAchievements
{
    public const string Media = "https://media.retroachievements.org";
    const string Api = "https://retroachievements.org/API/";

    static string CacheDir => Path.Combine(ExtrasStore.Root, "cache");

    /// <summary>Per-game unlock cache shared with the Steam plugin: { "&lt;achievement id&gt;": unixMs }.</summary>
    public static string UnlockCacheFile(int gameId) => Path.Combine(ExtrasStore.Dir, "ra", $"{gameId}.json");

    static async Task<JsonNode?> GetAsync(string endpoint, string key, string query, CancellationToken ct)
    {
        var url = $"{Api}{endpoint}?y={Uri.EscapeDataString(key)}" + (query == "" ? "" : "&" + query);
        using var resp = await SteamStore.Http.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("RetroAchievements rejected the Web API key. Check it in Settings.");
        if ((int)resp.StatusCode == 429)
            throw new InvalidOperationException("RetroAchievements is rate limiting requests. Wait a minute and try again.");
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    static async Task<JsonNode?> Cached(string name, TimeSpan maxAge, Func<Task<JsonNode?>> fetch)
    {
        var path = Path.Combine(CacheDir, name);
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < maxAge)
        {
            try { return JsonNode.Parse(await File.ReadAllTextAsync(path)); } catch { }
        }
        var node = await fetch();
        if (node != null)
        {
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllTextAsync(path, node.ToJsonString());
        }
        return node;
    }

    public static async Task<List<RaConsole>> ConsolesAsync(string key, CancellationToken ct = default)
    {
        var json = await Cached("ra_consoles.json", TimeSpan.FromDays(30), () => GetAsync("API_GetConsoleIDs.php", key, "a=1&g=1", ct));
        return json?.AsArray().Where(c => c != null).Select(c => new RaConsole((int)c!["ID"]!, (string?)c["Name"] ?? ""))
            .OrderBy(c => c.Name).ToList() ?? new();
    }

    /// <summary>Every game with achievements on a system (cached for a week; the list is large).</summary>
    public static async Task<List<RaGameEntry>> GamesAsync(string key, int consoleId, CancellationToken ct = default)
    {
        var json = await Cached($"ra_games_{consoleId}.json", TimeSpan.FromDays(7), () => GetAsync("API_GetGameList.php", key, $"i={consoleId}&f=1", ct));
        return json?.AsArray().Where(g => g != null).Select(g => new RaGameEntry(
            (int)g!["ID"]!, (string?)g["Title"] ?? "", (int?)g["ConsoleID"] ?? consoleId, (string?)g["ConsoleName"] ?? "",
            (string?)g["ImageIcon"] ?? "", (int?)g["NumAchievements"] ?? 0)).ToList() ?? new();
    }

    public static async Task<RaGame> GameProgressAsync(string key, string user, int gameId, CancellationToken ct = default)
    {
        var g = await GetAsync("API_GetGameInfoAndUserProgress.php", key, $"g={gameId}&u={Uri.EscapeDataString(user)}", ct)
                ?? throw new InvalidOperationException("RetroAchievements returned nothing for that game.");
        var list = new List<RaAchievement>();
        if (g["Achievements"] is JsonObject achievements)
        {
            foreach (var (_, a) in achievements)
            {
                if (a == null) continue;
                var hc = ParseDate((string?)a["DateEarnedHardcore"]);
                var sc = ParseDate((string?)a["DateEarned"]);
                list.Add(new RaAchievement(
                    (int)a["ID"]!, (string?)a["Title"] ?? "", (string?)a["Description"] ?? "", (int?)a["Points"] ?? 0,
                    (string?)a["BadgeName"] ?? "", (int?)a["NumAwarded"] ?? 0, (int?)a["DisplayOrder"] ?? 0, (string?)a["type"] ?? "",
                    hc ?? sc, hc != null));
            }
        }
        return new RaGame
        {
            Id = gameId,
            Title = (string?)g["Title"] ?? "",
            ConsoleName = (string?)g["ConsoleName"] ?? "",
            Developer = (string?)g["Developer"] ?? "",
            Publisher = (string?)g["Publisher"] ?? "",
            Genre = (string?)g["Genre"] ?? "",
            Released = ParseDate((string?)g["Released"]) is long r ? DateTimeOffset.FromUnixTimeMilliseconds(r).UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : "",
            NumDistinctPlayers = (int?)g["NumDistinctPlayers"] ?? 0,
            Achievements = list.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Id).ToList(),
        };
    }

    /// <summary>RA dates are "YYYY-MM-DD HH:MM:SS" in UTC.</summary>
    static long? ParseDate(string? s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeMilliseconds()
            : null;

    public static void WriteUnlockCache(RaGame game)
    {
        var map = game.Achievements.Where(a => a.EarnedMs != null).ToDictionary(a => a.Id.ToString(), a => a.EarnedMs!.Value);
        var path = UnlockCacheFile(game.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(map));
    }

    public static Dictionary<int, long> ReadUnlockCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var map = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path)) ?? new();
            return map.Where(kv => int.TryParse(kv.Key, out _)).ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        }
        catch { return new(); }
    }

    /// <summary>The RA system a shortcut most likely belongs to, from the emulator it launches.</summary>
    public static string? GuessConsole(string exe, string launchOptions)
    {
        var s = (exe + " " + launchOptions).ToLowerInvariant();
        (string hint, string console)[] map =
        {
            ("pcsx2", "PlayStation 2"), ("duckstation", "PlayStation"), ("swanstation", "PlayStation"), ("ppsspp", "PlayStation Portable"),
            ("dolphin", "GameCube"), ("flycast", "Dreamcast"), ("redream", "Dreamcast"), ("melonds", "Nintendo DS"), ("desmume", "Nintendo DS"),
            ("mgba", "Game Boy Advance"), ("snes9x", "SNES/Super Famicom"), ("bsnes", "SNES/Super Famicom"), ("mesen", "NES/Famicom"),
            ("fceux", "NES/Famicom"), ("project64", "Nintendo 64"), ("simple64", "Nintendo 64"), ("mupen64", "Nintendo 64"),
            ("gopher64", "Nintendo 64"), ("xemu", "Xbox"), ("azahar", "Nintendo 3DS"), ("citra", "Nintendo 3DS"), ("ares", ""),
            (".iso", ""), (".gba", "Game Boy Advance"), (".gbc", "Game Boy Color"), (".gb\"", "Game Boy"), (".nds", "Nintendo DS"),
            (".sfc", "SNES/Super Famicom"), (".smc", "SNES/Super Famicom"), (".nes", "NES/Famicom"), (".z64", "Nintendo 64"),
            (".n64", "Nintendo 64"), (".md", "Genesis/Mega Drive"), (".gen", "Genesis/Mega Drive"), (".cso", "PlayStation Portable"),
        };
        foreach (var (hint, console) in map)
            if (s.Contains(hint) && console != "") return console;
        return null;
    }
}
