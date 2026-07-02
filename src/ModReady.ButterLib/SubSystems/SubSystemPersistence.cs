// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Persists subsystem enabled/disabled state to
//   Modules\ModReady\subsystems.json
// so user toggle choices survive game restarts. The format is a minimal hand-
// written JSON object to avoid adding Newtonsoft.Json as a ButterLib dependency.
//
// Save() is called by MCM's subsystem page after each toggle (via SubSystemBridge.Save).
// Load() is called by ButterLibSubModule.OnSubModuleLoad() before SubSystemManager.EnableAll()
// so disabled subsystems never start up.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.SubSystems;

internal static class SubSystemPersistence
{
    private const string Tag = "SubSystemPersistence";
    private const string FileName = "subsystems.json";

    private static string? TryGetPath()
    {
        try
        {
            var loc = typeof(SubSystemPersistence).Assembly.Location;
            if (string.IsNullOrEmpty(loc)) return null;
            // DLL lives at Modules\ModReady\bin\Win64_Shipping_Client\*.dll
            var dir = Path.GetDirectoryName(loc);   // Win64_Shipping_Client
            dir     = Path.GetDirectoryName(dir);   // bin
            dir     = Path.GetDirectoryName(dir);   // ModReady
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
        }
        catch { return null; }
    }

    // ---- minimal JSON serializer (avoids Newtonsoft dependency) ----

    private static string SerializeMap(IReadOnlyList<(string Id, bool Enabled)> entries)
    {
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < entries.Count; i++)
        {
            var (id, enabled) = entries[i];
            sb.Append("  \"").Append(id.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\": ")
              .Append(enabled ? "true" : "false");
            if (i < entries.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static Dictionary<string, bool> DeserializeMap(string json)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        // Strip comments and whitespace-tolerant parse of simple {"key":bool} JSON.
        var span = json.AsSpan().Trim();
        if (span.Length < 2 || span[0] != '{') return result;
        span = span.Slice(1, span.Length - 2).Trim(); // strip {}
        if (span.Length == 0) return result;
        foreach (var rawPair in Split(span.ToString(), ','))
        {
            var pair = rawPair.Trim();
            if (pair.Length == 0) continue;
            var colon = pair.IndexOf(':');
            if (colon < 0) continue;
            var key = pair.Substring(0, colon).Trim().Trim('"');
            var val = pair.Substring(colon + 1).Trim();
            if (string.Equals(val, "true",  StringComparison.OrdinalIgnoreCase)) result[key] = true;
            if (string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)) result[key] = false;
        }
        return result;
    }

    // Split by delimiter while respecting quoted strings (values are just 'true'/'false' so
    // there's no quote nesting here, but keep the guard for safety).
    private static IEnumerable<string> Split(string s, char delim)
    {
        int start = 0;
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') inString = !inString;
            if (!inString && s[i] == delim)
            {
                yield return s.Substring(start, i - start);
                start = i + 1;
            }
        }
        if (start < s.Length) yield return s.Substring(start);
    }

    // ----------------------------------------------------------------

    /// <summary>Writes the enabled state of every subsystem to subsystems.json.</summary>
    public static void Save()
    {
        try
        {
            var path = TryGetPath();
            if (path == null) return;

            var entries = new List<(string Id, bool Enabled)>();
            foreach (var s in SubSystemManager.All)
                entries.Add((s.Id, s.IsEnabled));

            File.WriteAllText(path, SerializeMap(entries), Encoding.UTF8);
            DiagLog.Log(Tag, $"saved {entries.Count} subsystem state(s) to {path}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Save", ex);
        }
    }

    /// <summary>
    /// Reads subsystems.json and applies deferred enable/disable state to SubSystemManager.
    /// Must be called BEFORE SubSystemManager.EnableAll() so disabled subsystems never start.
    /// </summary>
    public static void Load()
    {
        try
        {
            var path = TryGetPath();
            if (path == null || !File.Exists(path)) return;

            var json = File.ReadAllText(path, Encoding.UTF8);
            var map  = DeserializeMap(json);

            int applied = 0;
            foreach (var kv in map)
            {
                SubSystemManager.SetDeferredEnabled(kv.Key, kv.Value);
                applied++;
            }
            DiagLog.Log(Tag, $"loaded {applied} deferred subsystem state(s) from {path}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Load", ex);
        }
    }
}
