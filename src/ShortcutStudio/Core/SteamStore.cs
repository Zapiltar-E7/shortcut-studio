using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShortcutStudio.Core;

public sealed record StoreSearchResult(int AppId, string Name, string ImageUrl)
{
    public override string ToString() => Name;
}

public sealed record AchievementSchema(string ApiName, string Name, string Description, bool Hidden, string IconUrl, string IconGrayUrl, double? Percent);

public sealed class StoreGame
{
    public int AppId { get; init; }
    public string Name { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string Description { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public List<string> Genres { get; set; } = new();
    /// <summary>Candidate URLs per art slot, best first; the first one that downloads wins.</summary>
    public Dictionary<ArtKind, List<string>> Art { get; } = new();
}

/// <summary>Public Steam endpoints that need no API key.</summary>
public static class SteamStore
{
    const string AssetBase = "https://shared.akamai.steamstatic.com/store_item_assets/";
    const string AchievementIconBase = "https://shared.fastly.steamstatic.com/community_assets/images/apps/";

    public static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ShortcutStudio/1.0");
        // Ask for the English store and skip the age gate on mature games.
        h.DefaultRequestHeaders.Add("Cookie", "birthtime=0; lastagecheckage=1-January-1970; wants_mature_content=1; Steam_Language=english");
        return h;
    }

    public static async Task<List<StoreSearchResult>> SearchAsync(string term, CancellationToken ct = default)
    {
        var results = new List<StoreSearchResult>();
        if (int.TryParse(term.Trim(), out var id))
        {
            var g = await GetGameAsync(id, ct);
            if (g != null) results.Add(new StoreSearchResult(id, g.Name, g.Art.GetValueOrDefault(ArtKind.Wide)?.LastOrDefault() ?? ""));
            return results;
        }
        var url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=US";
        var json = JsonNode.Parse(await Http.GetStringAsync(url, ct));
        foreach (var item in json?["items"]?.AsArray() ?? new JsonArray())
        {
            if (item == null) continue;
            results.Add(new StoreSearchResult((int)item["id"]!, (string?)item["name"] ?? "", (string?)item["tiny_image"] ?? ""));
        }
        return results;
    }

    public static async Task<StoreGame?> GetGameAsync(int appId, CancellationToken ct = default)
    {
        var request = new JsonObject
        {
            ["ids"] = new JsonArray(new JsonObject { ["appid"] = appId }),
            ["context"] = new JsonObject { ["language"] = "english", ["country_code"] = "US" },
            ["data_request"] = new JsonObject { ["include_assets"] = true, ["include_basic_info"] = true, ["include_release"] = true },
        };
        var url = "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(request.ToJsonString());
        var item = JsonNode.Parse(await Http.GetStringAsync(url, ct))?["response"]?["store_items"]?[0];
        if (item == null || (int?)item["success"] != 1) return null;

        var game = new StoreGame { AppId = appId, Name = (string?)item["name"] ?? "" };
        var basic = item["basic_info"];
        game.ShortDescription = WebUtility.HtmlDecode((string?)basic?["short_description"] ?? "");
        game.Developer = string.Join(", ", basic?["developers"]?.AsArray().Select(d => (string?)d?["name"]) ?? Array.Empty<string?>());
        game.Publisher = string.Join(", ", basic?["publishers"]?.AsArray().Select(d => (string?)d?["name"]) ?? Array.Empty<string?>());
        var released = (long?)item["release"]?["steam_release_date"] ?? (long?)item["release"]?["original_release_date"];
        if (released is > 0) game.ReleaseDate = DateTimeOffset.FromUnixTimeSeconds(released.Value).ToString("d MMM, yyyy");

        var assets = item["assets"];
        var format = (string?)assets?["asset_url_format"] ?? $"steam/apps/{appId}/${{FILENAME}}";
        string? Asset(string key)
        {
            var file = (string?)assets?[key];
            return string.IsNullOrEmpty(file) ? null : AssetBase + format.Replace("${FILENAME}", file);
        }
        string Plain(string file) => $"{AssetBase}steam/apps/{appId}/{file}";
        void Add(ArtKind k, params string?[] urls) => game.Art[k] = urls.Where(u => u != null).Select(u => u!).Distinct().ToList();

        Add(ArtKind.Capsule, Asset("library_capsule_2x"), Asset("library_capsule"), Plain("library_600x900_2x.jpg"), Plain("library_600x900.jpg"));
        Add(ArtKind.Hero, Asset("library_hero_2x"), Asset("library_hero"), Plain("library_hero_2x.jpg"), Plain("library_hero.jpg"));
        Add(ArtKind.Logo, Asset("library_logo_2x"), Asset("library_logo"), Plain("logo_2x.png"), Plain("logo.png"));
        Add(ArtKind.Wide, Asset("header_2x"), Asset("header"), Plain("header.jpg"));
        var icon = (string?)assets?["community_icon"];
        if (!string.IsNullOrEmpty(icon))
            Add(ArtKind.Icon, $"https://cdn.fastly.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon}.jpg");

        // The long description, genres and the store's own date text come from appdetails.
        try
        {
            var details = JsonNode.Parse(await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", ct))?[appId.ToString()];
            if ((bool?)details?["success"] == true)
            {
                var d = details["data"];
                game.Description = CleanHtml((string?)d?["about_the_game"] ?? (string?)d?["detailed_description"] ?? "");
                game.Genres = d?["genres"]?.AsArray().Select(g => (string?)g?["description"] ?? "").Where(s => s != "").ToList() ?? new();
                var date = (string?)d?["release_date"]?["date"];
                if (!string.IsNullOrWhiteSpace(date)) game.ReleaseDate = date;
            }
        }
        catch (HttpRequestException) { /* appdetails is rate-limited; the basic info above is still useful */ }

        return game;
    }

    /// <summary>Achievement list from the public community stats page (includes hidden ones and global percentages).</summary>
    public static async Task<List<AchievementSchema>> GetAchievementsAsync(int appId, CancellationToken ct = default)
    {
        var list = new List<AchievementSchema>();
        var html = await Http.GetStringAsync($"https://steamcommunity.com/stats/{appId}/achievements/?l=english", ct);
        var m = Regex.Match(html, "<script type=\"application/json\" id=\"valve-ssr-data\"[^>]*>([\\s\\S]*?)</script>");
        if (!m.Success) return list;

        var ssr = JsonNode.Parse(m.Groups[1].Value);
        var queryData = (string?)ssr?["renderContext"]?["queryData"];
        if (queryData == null) return list;
        var queries = JsonNode.Parse(queryData)?["queries"]?.AsArray();
        if (queries == null) return list;

        JsonNode? Query(string kind) => queries.FirstOrDefault(q => (string?)q?["queryKey"]?[0] == "achievements" && (string?)q?["queryKey"]?[2] == kind)?["state"]?["data"];
        var schema = Query("schema_v2");
        var percents = Query("globalpercentages")?["percentages"]?.AsObject();

        foreach (var group in schema?["groups"]?.AsArray() ?? new JsonArray())
        {
            foreach (var a in group?["achievements"]?.AsArray() ?? new JsonArray())
            {
                if (a == null || (bool?)a["archived"] == true) continue;
                var key = a["internal_key"]?.ToString() ?? "";
                double? pct = percents != null && percents.TryGetPropertyValue(key, out var p) && p != null ? Math.Round((double)p, 1) : null;
                string Icon(string field) { var f = (string?)a[field]; return string.IsNullOrEmpty(f) ? "" : $"{AchievementIconBase}{appId}/{f}"; }
                list.Add(new AchievementSchema(
                    (string?)a["api_name"] ?? key,
                    (string?)a["name"] ?? "",
                    (string?)a["description"] ?? "",
                    (bool?)a["hidden"] ?? false,
                    Icon("icon_achieved"),
                    Icon("icon_unachieved"),
                    pct));
            }
        }
        return list;
    }

    /// <summary>Downloads the first URL that works, or null.</summary>
    public static async Task<byte[]?> DownloadFirstAsync(IEnumerable<string> urls, CancellationToken ct = default)
    {
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) continue;
            try
            {
                using var resp = await Http.GetAsync(uri, ct);
                if (!resp.IsSuccessStatusCode) continue;
                var type = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.StartsWith("image/")) continue;
                return await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { } // timeout: try the next one
        }
        return null;
    }

    /// <summary>Steam's "About this game" HTML, with the inline images and videos removed (they point at Steam's CDN and bloat the page).</summary>
    static string CleanHtml(string html)
    {
        html = Regex.Replace(html, "<(img|video|source)[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "</video>", "", RegexOptions.IgnoreCase);
        // Steam's bb_* classes mean nothing outside the store; dropping them (and emptied image wrappers) keeps it editable.
        html = Regex.Replace(html, "\\s+class=\"bb_[^\"]*\"\\s*", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<span>\\s*</span>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<p>\\s*</p>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "[\\t\\r\\n]+", " ");
        html = Regex.Replace(html, "(<br\\s*/?>\\s*){3,}", "<br><br>", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "(</(p|h2|h3|ul|ol)>)\\s*", "$1\n", RegexOptions.IgnoreCase); // one block per line
        return html.Trim();
    }
}
