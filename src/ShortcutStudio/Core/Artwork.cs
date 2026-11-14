using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

public enum ArtKind { Capsule, Wide, Hero, Logo, Icon }

public sealed record ArtSpec(ArtKind Kind, string Label, string Suffix, int Width, int Height, string Hint);

/// <summary>An image held in memory until Save, with its file extension (".png" / ".jpg").</summary>
public sealed record ArtImage(byte[] Bytes, string Extension);

public sealed class LogoPosition
{
    /// <summary>BottomLeft, UpperLeft, CenterCenter, UpperCenter, BottomCenter (Steam's own names).</summary>
    public string Pinned { get; set; } = "BottomLeft";
    public double WidthPct { get; set; } = 50;
    public double HeightPct { get; set; } = 50;
}

/// <summary>Custom library art in userdata/&lt;id&gt;/config/grid, named the way Steam looks it up.</summary>
public static class Artwork
{
    public static readonly ArtSpec[] Specs =
    {
        new(ArtKind.Capsule, "Library capsule", "p", 600, 900, "Portrait cover in the library grid (600×900)"),
        new(ArtKind.Wide, "Wide capsule", "", 920, 430, "Header shown in Recent games and Big Picture (920×430)"),
        new(ArtKind.Hero, "Hero", "_hero", 3840, 1240, "Banner across the top of the game page (3840×1240)"),
        new(ArtKind.Logo, "Logo", "_logo", 1280, 720, "Transparent logo drawn over the hero"),
        new(ArtKind.Icon, "Icon", "_icon", 256, 256, "Small square icon in the library list"),
    };

    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };

    public static ArtSpec Spec(ArtKind k) => Specs.First(s => s.Kind == k);

    public static string? FindFile(uint accountId, uint appId, ArtKind kind)
    {
        var dir = Steam.GridDir(accountId);
        var stem = Path.Combine(dir, appId + Spec(kind).Suffix);
        return Extensions.Select(e => stem + e).FirstOrDefault(File.Exists);
    }

    /// <summary>Writes art for one slot, removing any older file of a different type in that slot. Returns the path.</summary>
    public static string Write(uint accountId, uint appId, ArtKind kind, ArtImage img)
    {
        var dir = Steam.GridDir(accountId);
        Directory.CreateDirectory(dir);
        var stem = Path.Combine(dir, appId + Spec(kind).Suffix);
        foreach (var e in Extensions) if (File.Exists(stem + e)) File.Delete(stem + e);
        var path = stem + img.Extension;
        File.WriteAllBytes(path, img.Bytes);
        return path;
    }

    public static void Delete(uint accountId, uint appId, ArtKind kind)
    {
        var stem = Path.Combine(Steam.GridDir(accountId), appId + Spec(kind).Suffix);
        foreach (var e in Extensions) if (File.Exists(stem + e)) File.Delete(stem + e);
    }

    public static LogoPosition? ReadLogoPosition(uint accountId, uint appId)
    {
        var path = Path.Combine(Steam.GridDir(accountId), appId + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            var lp = JsonNode.Parse(File.ReadAllText(path))?["logoPosition"];
            if (lp == null) return null;
            return new LogoPosition
            {
                Pinned = (string?)lp["pinnedPosition"] ?? "BottomLeft",
                WidthPct = (double?)lp["nWidthPct"] ?? 50,
                HeightPct = (double?)lp["nHeightPct"] ?? 50,
            };
        }
        catch { return null; }
    }

    public static void WriteLogoPosition(uint accountId, uint appId, LogoPosition? pos)
    {
        var path = Path.Combine(Steam.GridDir(accountId), appId + ".json");
        if (pos == null) { if (File.Exists(path)) File.Delete(path); return; }
        var json = new JsonObject
        {
            ["nVersion"] = 1,
            ["logoPosition"] = new JsonObject
            {
                ["pinnedPosition"] = pos.Pinned,
                ["nWidthPct"] = pos.WidthPct,
                ["nHeightPct"] = pos.HeightPct,
            },
        };
        File.WriteAllText(path, json.ToJsonString());
    }

    /// <summary>Moves every art file from one appid to another (used when a new appid is assigned).</summary>
    public static void Rename(uint accountId, uint from, uint to)
    {
        var dir = Steam.GridDir(accountId);
        if (!Directory.Exists(dir) || from == to) return;
        foreach (var file in Directory.GetFiles(dir, from + "*"))
        {
            var name = Path.GetFileName(file);
            var rest = name.Substring(from.ToString().Length);
            if (rest.Length > 0 && char.IsDigit(rest[0])) continue; // a different, longer appid
            File.Move(file, Path.Combine(dir, to + rest), true);
        }
    }

    public static string ExtensionFor(byte[] bytes)
    {
        if (bytes.Length > 3 && bytes[0] == 0x89 && bytes[1] == 'P') return ".png";
        if (bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8) return ".jpg";
        if (bytes.Length > 12 && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B') return ".webp";
        return ".png";
    }
}
