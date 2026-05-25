// BetaDeps Tactics Editor -- JSON serializer.
//
// Newtonsoft.Json is already shipped in Modules\BetaDeps\bin\ so we
// reference it as an indirect dep rather than copy our own. The file
// format is human-readable, mod authors can tweak by hand.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;

using BetaDeps.Foundation;

using Newtonsoft.Json;

namespace BetaDeps.TacticsEditor;

public static class TacticPlanSerializer
{
    private const string Tag = "BetaDeps.TacticsEditor.Serializer";

    private static readonly JsonSerializerSettings _settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>
    /// Serialize a plan to JSON. Updates SavedAt to "now" before writing.
    /// </summary>
    public static string ToJson(TacticPlan plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        plan.SavedAt = DateTime.UtcNow.ToString("o");
        return JsonConvert.SerializeObject(plan, _settings);
    }

    /// <summary>
    /// Deserialize a plan from JSON. Returns null on parse error and logs it.
    /// </summary>
    public static TacticPlan? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonConvert.DeserializeObject<TacticPlan>(json);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "FromJson", ex);
            return null;
        }
    }

    /// <summary>
    /// Write a plan to disk. Creates the parent directory if missing.
    /// Returns true on success; logs and returns false on failure.
    /// </summary>
    public static bool WriteToFile(TacticPlan plan, string path)
    {
        if (plan == null || string.IsNullOrEmpty(path)) return false;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            File.WriteAllText(path, ToJson(plan));
            DiagLog.Log(Tag, $"wrote tactic plan '{plan.Name}' with {plan.Formations.Count} formation(s) to {path}");
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"WriteToFile({path})", ex);
            return false;
        }
    }

    /// <summary>
    /// Read a plan from disk. Returns null on missing-file or parse-error.
    /// </summary>
    public static TacticPlan? ReadFromFile(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ReadFromFile({path})", ex);
            return null;
        }
    }
}
