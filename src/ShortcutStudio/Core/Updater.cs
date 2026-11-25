using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

/// <summary>A newer release on GitHub, with the app's download.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Url, long Size, string PageUrl);

/// <summary>
/// Finds newer releases on GitHub and swaps the running .exe for the new one. Windows lets a running .exe be
/// renamed (not overwritten), so the old one moves aside to .old, the new one takes its name, and the new copy
/// deletes the .old file when it starts. The plugin follows on its own: a newer app installs its built-in plugin on
/// the next save.
/// </summary>
public static class Updater
{
    const string Repo = "zapiltar/shortcut-studio";
    const string AssetName = "ShortcutStudio.exe";

    /// <summary>This build's version, from the project's &lt;Version&gt;.</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => Current.ToString(3);

    static string? ExePath => Environment.ProcessPath;
    static string? OldPath => ExePath is { } p ? p + ".old" : null;
    static string? NewPath => ExePath is { } p ? p + ".new" : null;

    /// <summary>The latest release if it's newer than this build, else null. Throws when GitHub can't be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await SteamStore.Http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct));
        var tag = (string?)json?["tag_name"] ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        if (latest <= Current) return null;

        var asset = json?["assets"]?.AsArray().FirstOrDefault(a => string.Equals((string?)a?["name"], AssetName, StringComparison.OrdinalIgnoreCase));
        var url = (string?)asset?["browser_download_url"];
        if (url == null) return null;
        return new UpdateInfo(latest, tag, url, (long?)asset?["size"] ?? 0, (string?)json?["html_url"] ?? $"https://github.com/{Repo}/releases/latest");
    }

    /// <summary>
    /// Downloads the update next to the running .exe and swaps it in. Returns the path to start; the caller then
    /// starts it and closes this copy. Throws with a readable message if anything goes wrong, leaving the old .exe
    /// in place.
    /// </summary>
    public static async Task<string> DownloadAndSwapAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (ExePath is not { } exe || NewPath is not { } newPath || OldPath is not { } oldPath)
            throw new InvalidOperationException("Couldn't tell where Shortcut Studio is running from.");

        try
        {
            using (var res = await SteamStore.Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? update.Size;
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(newPath);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(newPath);
            throw new InvalidOperationException($"Shortcut Studio can't write to its own folder ({Path.GetDirectoryName(exe)}). Download the new version from GitHub instead.");
        }
        catch
        {
            TryDelete(newPath);
            throw;
        }

        // Make sure it's the whole, right file before touching the running one.
        var info = new FileInfo(newPath);
        var version = FileVersionInfo.GetVersionInfo(newPath);
        bool sizeOk = update.Size <= 0 || info.Length == update.Size;
        bool versionOk = Version.TryParse(version.FileVersion, out var fv) && new Version(fv.Major, fv.Minor, Math.Max(0, fv.Build)) == update.Version;
        if (!sizeOk || !versionOk)
        {
            TryDelete(newPath);
            throw new InvalidOperationException("The download didn't match the release, so nothing was changed. Try again later, or download it from GitHub.");
        }

        TryDelete(oldPath);
        File.Move(exe, oldPath);
        try { File.Move(newPath, exe); }
        catch
        {
            File.Move(oldPath, exe); // put the running one back
            TryDelete(newPath);
            throw;
        }
        return exe;
    }

    /// <summary>Deletes the previous version left behind by an update. The old copy may take a moment to exit.</summary>
    public static async Task CleanUpAsync()
    {
        if (OldPath is not { } old) return;
        for (int i = 0; i < 10 && File.Exists(old); i++)
        {
            if (TryDelete(old)) return;
            await Task.Delay(1000);
        }
    }

    static bool TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return true; }
        catch { return false; }
    }
}
