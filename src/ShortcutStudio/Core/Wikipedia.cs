using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShortcutStudio.Core;

public sealed record WikiResult(string Title, string Snippet);

public sealed class WikiGame
{
    public string Title { get; init; } = "";
    public string ShortDescription { get; set; } = "";
    public string Description { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public List<string> Genres { get; set; } = new();
}

/// <summary>Game descriptions and details from English Wikipedia (for games that aren't on Steam).</summary>
public static class Wikipedia
{
    const string Api = "https://en.wikipedia.org/w/api.php";

    static async Task<JsonNode?> Get(string query, CancellationToken ct) =>
        JsonNode.Parse(await SteamStore.Http.GetStringAsync(Api + "?format=json&formatversion=2&" + query, ct));

    public static async Task<List<WikiResult>> SearchAsync(string name, CancellationToken ct = default)
    {
        // "video game" steers the search to the game's article rather than a film or book of the same name.
        var json = await Get("action=query&list=search&srlimit=8&srsearch=" + Uri.EscapeDataString(name + " video game"), ct);
        return json?["query"]?["search"]?.AsArray().Select(r => new WikiResult(
            (string?)r?["title"] ?? "",
            WebUtility.HtmlDecode(Regex.Replace((string?)r?["snippet"] ?? "", "<[^>]+>", "")))).Where(r => r.Title != "").ToList() ?? new();
    }

    public static async Task<WikiGame> GetAsync(string title, CancellationToken ct = default)
    {
        var game = new WikiGame { Title = title };
        var t = Uri.EscapeDataString(title);

        // Article body as HTML; keep the intro plus the sections a store page would have.
        var ex = await Get($"action=query&prop=extracts&redirects=1&titles={t}", ct);
        var html = (string?)ex?["query"]?["pages"]?[0]?["extract"] ?? "";
        var parts = Regex.Split(html, "(?=<h2)");
        var intro = parts.FirstOrDefault() ?? "";
        var keep = new List<string> { CleanHtml(intro) };
        foreach (var p in parts.Skip(1))
        {
            var heading = Regex.Match(p, "<h2[^>]*>(.*?)</h2>", RegexOptions.Singleline).Groups[1].Value;
            heading = WebUtility.HtmlDecode(Regex.Replace(heading, "<[^>]+>", "")).Trim();
            if (Regex.IsMatch(heading, "^(Gameplay|Plot|Synopsis|Story|Premise|Setting)$", RegexOptions.IgnoreCase))
                keep.Add($"<h2>{heading}</h2>\n" + CleanHtml(Regex.Replace(p, "<h2[^>]*>.*?</h2>", "", RegexOptions.Singleline)));
        }
        game.Description = string.Join("\n", keep.Where(s => s.Trim() != "")).Trim();

        // Short description: the opening sentences of the intro, as plain text.
        var plain = WebUtility.HtmlDecode(Regex.Replace(intro, "<[^>]+>", " "));
        plain = Regex.Replace(plain, "\\s+", " ").Trim();
        var sentences = Regex.Matches(plain, "[^.!?]+[.!?]+(\\s|$)").Select(m => m.Value).ToList();
        var shortText = "";
        foreach (var s in sentences) { if ((shortText + s).Length > 300 && shortText != "") break; shortText += s; }
        game.ShortDescription = (shortText == "" ? plain : shortText).Trim();

        // Developer, publisher, release date and genre from the infobox wikitext.
        var wt = await Get($"action=parse&prop=wikitext&section=0&redirects=1&page={t}", ct);
        var wikitext = (string?)wt?["parse"]?["wikitext"] ?? "";
        game.Developer = InfoboxList(wikitext, "developer");
        game.Publisher = InfoboxList(wikitext, "publisher");
        game.ReleaseDate = InfoboxDate(wikitext);
        game.Genres = InfoboxList(wikitext, "genre").Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
        return game;
    }

    static string CleanHtml(string html)
    {
        html = Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline);
        html = Regex.Replace(html, "<(span|link|meta)[^>]*>|</span>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<p[^>]*>\\s*</p>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "\\s*\\n\\s*", "\n");
        return html.Trim();
    }

/// <summary>The raw value of "| field = ..." in the infobox, up to the next top-level "|" (nested templates and links are kept whole).</summary>
    static string Field(string wikitext, params string[] names)
    {
        foreach (var name in names)
        {
            var m = Regex.Match(wikitext, $@"\n\s*\|\s*{Regex.Escape(name)}\s*=", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            int i = m.Index + m.Length, depth = 0;
            var start = i;
            for (; i < wikitext.Length; i++)
            {
                if (wikitext[i] is '{' or '[') depth++;
                else if (wikitext[i] is '}' or ']') { if (depth == 0) break; depth--; }
                else if (wikitext[i] == '|' && depth == 0) break;
            }
            var value = wikitext[start..i].Trim();
            if (value != "") return value;
        }
        return "";
    }

    /// <summary>{{Video game release|JP|date|NA|date}} style templates: the values without the region codes.</summary>
    static IEnumerable<string> RegionValues(string raw)
    {
        foreach (Match m in Regex.Matches(raw, @"\{\{\s*(?:Video game release|vgrelease|vgr)\s*\|(.*?)\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var args = m.Groups[1].Value.Split('|').Select(a => a.Trim()).ToList();
            for (int k = 1; k < args.Count; k += 2) yield return args[k];
        }
    }

    static string StripMarkup(string s)
    {
        s = Regex.Replace(s, "<ref[^>]*/>|<ref[^>]*>.*?</ref>|<!--.*?-->", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\{\{\s*(efn|refn|sfn|cn|citation needed|nbsp)[^{}]*\}\}", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\[\[(?:[^|\]]*\|)?([^\]]+)\]\]", "$1");          // [[Target|Text]] -> Text
        s = Regex.Replace(s, "<br\\s*/?>", ", ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = s.Replace("'''", "").Replace("''", "");
        return WebUtility.HtmlDecode(s);
    }

    /// <summary>Lists like {{ubl|A|B}}, {{plainlist|* A * B}} or "A<br>B", flattened to "A, B".</summary>
    static string InfoboxList(string wikitext, string field)
    {
        var raw = StripMarkup(Field(wikitext, field, field + "s"));
        var items = new List<string>(RegionValues(raw));
        foreach (Match m in Regex.Matches(raw, @"\{\{\s*(?:ubl|unbulleted list|plainlist|flatlist|hlist)\s*\|?(.*?)\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            items.AddRange(m.Groups[1].Value.Split(new[] { '|', '*', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        raw = Regex.Replace(raw, @"\{\{.*?\}\}", "", RegexOptions.Singleline);
        items.AddRange(raw.Split(new[] { ',', '*', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.Join(", ", items.Select(i => i.Trim()).Where(i => i.Length > 1 && !i.Contains('=')).Distinct());
    }

    /// <summary>The first date in the release field, e.g. from {{Video game release|WW|March 24, 2015}}.</summary>
    static string InfoboxDate(string wikitext)
    {
        var raw = StripMarkup(Field(wikitext, "released", "release", "release_date"));
        var date = Regex.Match(raw, @"(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}|\d{1,2}\s+(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}|(January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4}|\b(19|20)\d{2}\b");
        return date.Success ? date.Value : "";
    }
}
