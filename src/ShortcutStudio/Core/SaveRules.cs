using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShortcutStudio.Core;

/// <summary>
/// The rules behind a game's save-file achievements (extras\saverules\&lt;id&gt;.json, see the Steam plugin).
/// Shortcut Studio only edits each rule's "progress" switch, which makes Steam show "12 / 900" on the locked
/// achievement; everything else in the file is kept as it is.
/// </summary>
public sealed class SaveRuleSet
{
    readonly string _path;
    readonly JsonObject _root;
    bool _dirty;

    SaveRuleSet(string path, JsonObject root) { _path = path; _root = root; }

    public static string PathFor(string rulesId) => Path.Combine(ExtrasStore.Dir, "saverules", rulesId + ".json");

    /// <summary>The rules for a trophy source, or null when it isn't a save-file source or has no readable rules.</summary>
    public static SaveRuleSet? For(TrophySource? src)
    {
        if (src is not { Emulator: "savefile" } || string.IsNullOrWhiteSpace(src.NpCommId)) return null;
        if (src.NpCommId.IndexOfAny(new[] { '/', '\\' }) >= 0 || src.NpCommId.Contains("..")) return null;
        var path = PathFor(src.NpCommId);
        try
        {
            if (!File.Exists(path)) return null;
            return JsonNode.Parse(File.ReadAllText(path)) is JsonObject root ? new SaveRuleSet(path, root) : null;
        }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
    }

    JsonObject? Rule(int trophyId) => _root["achievements"]?[trophyId.ToString()] as JsonObject;

    /// <summary>
    /// How many steps the rule counts (e.g. 900 Korok seeds), the same way the plugin does: each "count" condition adds
    /// its required number of flags, each flag condition its required value, and an either-or its largest alternative.
    /// Rules of 1 step (a single yes/no flag) have nothing to count.
    /// </summary>
    public int MaxProgress(int trophyId)
    {
        if (Rule(trophyId)?["all"] is not JsonArray all) return 0;
        return all.Sum(Steps);
    }

    static int Steps(JsonNode? c)
    {
        if (c is not JsonObject o) return 0;
        // A final step that the bar shouldn't count (e.g. claiming Hestu's gift after the 900 seeds).
        if (o["noProgress"] is JsonValue np && np.TryGetValue<bool>(out var skip) && skip) return 0;
        if (o["any"] is JsonArray any) return any.Count == 0 ? 0 : any.Max(Steps);
        if (o["count"] is JsonArray list) return Num(o["min"]) ?? list.Count;
        // A flag compared against a huge value (e.g. a float's raw bits) isn't a count: treat it as one yes/no step.
        if (o["flag"] != null) return Num(o["min"]) is int m && m <= MaxCountableValue ? m : 1;
        return 0;
    }

    /// <summary>Thresholds above this are raw encodings (floats), not counts. Kept in step with the Steam plugin.</summary>
    public const int MaxCountableValue = 100_000;

    static int? Num(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return (int)Math.Min(d, int.MaxValue);
        return v.TryGetValue<int>(out var i) ? i : null;
    }

    public bool ShowsProgress(int trophyId) => Rule(trophyId)?["progress"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    readonly Dictionary<int, bool> _changes = new();

    public void SetProgress(int trophyId, bool on)
    {
        if (Rule(trophyId) is not { } rule || ShowsProgress(trophyId) == on) return;
        rule["progress"] = on;
        _changes[trophyId] = on;
        _dirty = true;
    }

    /// <summary>
    /// Writes only the switches changed here onto the file as it is now, so rules updated by something else while
    /// the app was open (e.g. a regenerated rule set) are never replaced by this copy.
    /// </summary>
    public void SaveIfChanged()
    {
        if (!_dirty) return;
        if (JsonNode.Parse(File.ReadAllText(_path)) is not JsonObject current) throw new InvalidDataException($"{_path} couldn't be read");
        foreach (var (id, on) in _changes)
            if (current["achievements"]?[id.ToString()] is JsonObject rule) rule["progress"] = on;
        var tmp = _path + $".{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, true);
        _changes.Clear();
        _dirty = false;
    }
}
