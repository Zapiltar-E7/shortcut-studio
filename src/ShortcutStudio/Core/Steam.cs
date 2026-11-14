using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ShortcutStudio.Core;

public sealed record SteamUser(uint AccountId, string AccountName, string PersonaName, bool MostRecent)
{
    public override string ToString() => string.IsNullOrEmpty(PersonaName) ? AccountId.ToString() : $"{PersonaName} ({AccountName})";
}

/// <summary>Where Steam lives, who is signed in, and starting/stopping the client.</summary>
public static class Steam
{
    const ulong SteamId64Base = 76561197960265728UL;

    public static string? InstallPath
    {
        get
        {
            var p = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                    ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            return p != null && Directory.Exists(p) ? Path.GetFullPath(p) : null;
        }
    }

    public static string UserConfigDir(uint accountId) => Path.Combine(InstallPath!, "userdata", accountId.ToString(), "config");
    public static string ShortcutsFile(uint accountId) => Path.Combine(UserConfigDir(accountId), "shortcuts.vdf");
    public static string GridDir(uint accountId) => Path.Combine(UserConfigDir(accountId), "grid");
    public static string MillenniumDir => Path.Combine(InstallPath!, "millennium");
    public static bool HasMillennium => InstallPath != null && Directory.Exists(MillenniumDir);

    /// <summary>Accounts that have a userdata folder, most recently signed-in first.</summary>
    public static List<SteamUser> Users()
    {
        var users = new List<SteamUser>();
        var root = InstallPath;
        if (root == null) return users;

        var known = new Dictionary<uint, SteamUser>();
        var login = Path.Combine(root, "config", "loginusers.vdf");
        if (File.Exists(login))
        {
            // Text KeyValues: "7656..." { "AccountName" "x" "PersonaName" "y" "MostRecent" "1" ... }
            var text = File.ReadAllText(login);
            foreach (Match m in Regex.Matches(text, "\"(\\d{17})\"\\s*\\{([^}]*)\\}"))
            {
                var id = (uint)(ulong.Parse(m.Groups[1].Value) - SteamId64Base);
                string Field(string k) => Regex.Match(m.Groups[2].Value, $"\"{k}\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                known[id] = new SteamUser(id, Field("AccountName"), Field("PersonaName"), Field("MostRecent") == "1");
            }
        }

        var userdata = Path.Combine(root, "userdata");
        if (Directory.Exists(userdata))
        {
            foreach (var dir in Directory.GetDirectories(userdata))
            {
                if (!uint.TryParse(Path.GetFileName(dir), out var id) || id == 0) continue;
                users.Add(known.TryGetValue(id, out var u) ? u : new SteamUser(id, "", "", false));
            }
        }

        // Who is signed in right now (set while Steam runs), else who Steam signs in automatically.
        var active = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0) is int a ? (uint)a : 0;
        var auto = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "AutoLoginUser", "") as string ?? "";
        int Rank(SteamUser u) =>
            u.AccountId == active ? 0 :
            auto != "" && string.Equals(u.AccountName, auto, StringComparison.OrdinalIgnoreCase) ? 1 :
            u.MostRecent ? 2 :
            known.ContainsKey(u.AccountId) ? 3 : 4;
        return users.OrderBy(Rank).ThenBy(u => u.PersonaName).ToList();
    }

    public static bool IsRunning => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>Asks Steam to exit cleanly (so it writes its own state first) and waits for it.</summary>
    public static async Task<bool> ShutdownAsync(TimeSpan timeout)
    {
        if (!IsRunning) return true;
        var exe = Path.Combine(InstallPath!, "steam.exe");
        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false });
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(500);
            if (!IsRunning) { await Task.Delay(1000); return true; } // let file handles close
        }
        return false;
    }

    public static void Start()
    {
        var exe = Path.Combine(InstallPath!, "steam.exe");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
