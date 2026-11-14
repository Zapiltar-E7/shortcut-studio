using System.Reflection;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

/// <summary>Installs the bundled companion plugin into Millennium and turns it on.</summary>
public static class PluginInstaller
{
    public const string PluginId = "shortcut-studio";
    const string ResourcePrefix = "PluginFiles/";

    public static string PluginDir => Path.Combine(Steam.MillenniumDir, "plugins", PluginId);
    static string ConfigFile => Path.Combine(Steam.MillenniumDir, "config", "config.json");

    public static string? BundledVersion
    {
        get
        {
            using var s = Open("plugin.json");
            if (s == null) return null;
            return (string?)JsonNode.Parse(s)?["version"];
        }
    }

    public static string? InstalledVersion
    {
        get
        {
            var f = Path.Combine(PluginDir, "plugin.json");
            try { return File.Exists(f) ? (string?)JsonNode.Parse(File.ReadAllText(f))?["version"] : null; }
            catch { return null; }
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                var list = JsonNode.Parse(File.ReadAllText(ConfigFile))?["plugins"]?["enabledPlugins"]?.AsArray();
                return list?.Any(n => (string?)n == PluginId) == true;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Install when the plugin is missing, turned off, or older than the copy built into this app. Never when the
    /// installed one is newer: an older app build must not downgrade a plugin that was updated separately.
    /// </summary>
    public static bool NeedsInstall
    {
        get
        {
            if (!Steam.HasMillennium || BundledVersion is not { } bundled) return false;
            if (!IsEnabled) return true;
            if (InstalledVersion is not { } installed) return true;
            return Version.TryParse(bundled, out var b) && Version.TryParse(installed, out var i) ? b > i : bundled != installed;
        }
    }

    /// <summary>Copies the plugin files and adds it to Millennium's enabled list. Steam must be closed for the config change to stick.</summary>
    public static void Install()
    {
        var asm = Assembly.GetExecutingAssembly();
        if (Directory.Exists(PluginDir)) Directory.Delete(PluginDir, true);

        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)))
        {
            var rel = ResourceToPath(name.Substring(ResourcePrefix.Length));
            var dest = Path.Combine(PluginDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var src = asm.GetManifestResourceStream(name)!;
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }

        var config = File.Exists(ConfigFile) ? JsonNode.Parse(File.ReadAllText(ConfigFile))!.AsObject() : new JsonObject();
        var plugins = config["plugins"] as JsonObject ?? new JsonObject();
        config["plugins"] = plugins;
        var enabled = plugins["enabledPlugins"] as JsonArray ?? new JsonArray();
        plugins["enabledPlugins"] = enabled;
        if (!enabled.Any(n => (string?)n == PluginId)) enabled.Add(PluginId);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        File.WriteAllText(ConfigFile, config.ToJsonString(options));
    }

    static Stream? Open(string relPath)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.StartsWith(ResourcePrefix) && ResourceToPath(n.Substring(ResourcePrefix.Length)) == relPath);
        return name == null ? null : asm.GetManifestResourceStream(name);
    }

    /// <summary>Resource names keep the plugin's folders ("PluginFiles/backend\main.lua").</summary>
    static string ResourceToPath(string name) => name.Replace('\\', '/');
}
