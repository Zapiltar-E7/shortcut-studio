using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

/// <summary>
/// The look of the Steam plugin's own windows (achievement list, launch-option dialog, unlock popup, tiles).
/// Kept in extras\plugin_settings.json, which the plugin reads every few seconds and can change from its
/// Millennium settings page, so it lives apart from the app's settings.json.
/// </summary>
public static class PluginStyle
{
    public static readonly (string Value, string Label)[] Options =
    [
        ("auto", "Auto (match the Steam theme)"),
        ("stock", "Stock"),
        ("simply-dark", "Simply Dark"),
    ];

    static string FilePath => Path.Combine(ExtrasStore.Dir, "plugin_settings.json");

    public static string Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject o
                && o["style"]?.GetValue<string>() is { } s && Options.Any(x => x.Value == s))
                return s;
        }
        catch { /* unreadable: fall back to auto */ }
        return "auto";
    }

    public static void Save(string style)
    {
        if (!Options.Any(x => x.Value == style)) return;
        Directory.CreateDirectory(ExtrasStore.Dir);
        JsonObject o;
        try { o = File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? [] : []; }
        catch { o = []; }
        o["style"] = style;
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, true);
    }
}
