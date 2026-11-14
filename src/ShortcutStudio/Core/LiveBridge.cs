using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

/// <summary>
/// Sends changes to the Shortcut Studio plugin running inside Steam, which applies them through Steam's own
/// API, so Steam doesn't have to be closed. Requests and answers are files in extras\live.
/// </summary>
public static class LiveBridge
{
    static string Dir => Path.Combine(ExtrasStore.Dir, "live");

    /// <summary>The plugin writes a heartbeat every ~1.5 s while Steam is running with it loaded.</summary>
    public static bool IsAlive
    {
        get
        {
            try
            {
                var hb = Path.Combine(Dir, "heartbeat");
                if (!File.Exists(hb) || !long.TryParse(File.ReadAllText(hb).Trim(), out var ms)) return false;
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms < 8000 && Steam.IsRunning;
            }
            catch { return false; }
        }
    }

    public sealed record Result(bool Ok, List<JsonObject> Results);

    /// <summary>Sends ops and waits for the plugin's answer. Returns null if it didn't answer in time.</summary>
    public static async Task<Result?> SendAsync(IEnumerable<JsonObject> ops, TimeSpan timeout)
    {
        Directory.CreateDirectory(Dir);
        var id = Guid.NewGuid().ToString("N");
        var request = new JsonObject { ["id"] = id, ["ops"] = new JsonArray(ops.Select(o => (JsonNode)o).ToArray()) };
        var path = Path.Combine(Dir, $"request-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{id}.json");
        // The id the plugin answers with is the one inside the file; name the file so requests run in order.
        request["id"] = Path.GetFileNameWithoutExtension(path)["request-".Length..];
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, request.ToJsonString());
        File.Move(tmp, path, true);

        var response = Path.Combine(Dir, $"response-{(string)request["id"]!}.json");
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(250);
            if (!File.Exists(response)) continue;
            try
            {
                var node = JsonNode.Parse(await File.ReadAllTextAsync(response));
                File.Delete(response);
                var results = node?["results"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
                return new Result((bool?)node?["ok"] ?? false, results);
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        // Not picked up: withdraw it so it can't be applied later behind the user's back.
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        return null;
    }

    public static JsonObject Op(string op, uint appId, params (string key, JsonNode? value)[] fields)
    {
        var o = new JsonObject { ["op"] = op, ["appid"] = appId };
        foreach (var (k, v) in fields) o[k] = v;
        return o;
    }
}
