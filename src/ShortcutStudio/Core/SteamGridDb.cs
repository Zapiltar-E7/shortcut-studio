using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

public sealed record SgdbGame(int Id, string Name, string Year)
{
    public override string ToString() => string.IsNullOrEmpty(Year) ? Name : $"{Name} ({Year})";
}

public sealed record SgdbImage(int Id, string Url, string Thumb, int Width, int Height, string Style, string Author)
{
    public string Info => $"{Width}×{Height} · {Style}" + (string.IsNullOrEmpty(Author) ? "" : $" · {Author}");
}

/// <summary>SteamGridDB API v2 (needs a free key from steamgriddb.com/profile/preferences/api).</summary>
public static class SteamGridDb
{
    const string Base = "https://www.steamgriddb.com/api/v2";

    static async Task<JsonNode?> GetAsync(string apiKey, string path, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Base + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var resp = await SteamStore.Http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("SteamGridDB rejected the API key. Check it in Settings.");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
        {
            // SteamGridDB explains rejected requests in {"errors": [...]}; show that rather than a bare status code.
            string detail = "";
            try { detail = string.Join("; ", JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["errors"]?.AsArray().Select(e => e?.ToString()) ?? []); }
            catch { }
            throw new InvalidOperationException($"SteamGridDB: {(int)resp.StatusCode} {resp.ReasonPhrase}" + (detail != "" ? $" ({detail})" : ""));
        }
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    public static async Task<List<SgdbGame>> SearchAsync(string apiKey, string term, CancellationToken ct = default)
    {
        var json = await GetAsync(apiKey, "/search/autocomplete/" + Uri.EscapeDataString(term), ct);
        return json?["data"]?.AsArray().Where(g => g != null).Select(g =>
        {
            var ts = (long?)g!["release_date"];
            return new SgdbGame((int)g["id"]!, (string?)g["name"] ?? "", ts is > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts.Value).Year.ToString() : "");
        }).ToList() ?? new();
    }

    /// <summary>The SteamGridDB game for a real Steam appid, if it has one.</summary>
    public static async Task<SgdbGame?> GameForSteamAppAsync(string apiKey, int steamAppId, CancellationToken ct = default)
    {
        var g = (await GetAsync(apiKey, $"/games/steam/{steamAppId}", ct))?["data"];
        return g == null ? null : new SgdbGame((int)g["id"]!, (string?)g["name"] ?? "", "");
    }

    public static async Task<List<SgdbImage>> ImagesAsync(string apiKey, int gameId, ArtKind kind, CancellationToken ct = default)
    {
        var path = kind switch
        {
            ArtKind.Capsule => $"/grids/game/{gameId}?dimensions=600x900,342x482,660x930",
            ArtKind.Wide => $"/grids/game/{gameId}?dimensions=920x430,460x215",
            ArtKind.Hero => $"/heroes/game/{gameId}",
            ArtKind.Logo => $"/logos/game/{gameId}",
            _ => $"/icons/game/{gameId}",
        };
        // Static images only: Steam can't show animated WebP/APNG on non-Steam games without extra mods.
        // Each endpoint only accepts its own file types, and any other value is a 400 Bad Request:
        // grids/heroes allow png/jpeg/webp, logos png/webp, icons png/ico. Ask for the ones this app and Steam can show.
        var mimes = kind is ArtKind.Logo or ArtKind.Icon ? "image/png" : "image/png,image/jpeg";
        path += (path.Contains('?') ? "&" : "?") + "types=static&nsfw=false&mimes=" + mimes;
        var json = await GetAsync(apiKey, path, ct);
        return json?["data"]?.AsArray().Where(i => i != null).Select(i => new SgdbImage(
            (int)i!["id"]!,
            (string?)i["url"] ?? "",
            (string?)i["thumb"] ?? (string?)i["url"] ?? "",
            (int?)i["width"] ?? 0,
            (int?)i["height"] ?? 0,
            (string?)i["style"] ?? "",
            (string?)i["author"]?["name"] ?? "")).ToList() ?? new();
    }
}
