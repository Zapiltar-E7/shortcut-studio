using System.Text.Json;

namespace ShortcutStudio.Core;

public sealed class AppSettings
{
    public string SteamGridDbKey { get; set; } = "";
    /// <summary>RetroAchievements username and Web API key (the Steam plugin reads these too, for live sync).</summary>
    public string RaUser { get; set; } = "";
    public string RaKey { get; set; } = "";
    public uint LastAccountId { get; set; }
    /// <summary>The editor's hero banner is hidden, to show more of the lists below it.</summary>
    public bool HideBanner { get; set; }
    /// <summary>Look for a newer release on GitHub each time the app starts.</summary>
    public bool CheckForUpdates { get; set; } = true;

    static string FilePath => Path.Combine(ExtrasStore.Root, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(ExtrasStore.Root);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
