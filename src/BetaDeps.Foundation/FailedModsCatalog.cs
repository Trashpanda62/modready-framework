// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FailedModsCatalog: append-only ledger of every mod that's been a
// SaveShield CULPRIT this install. Dedupes by (mod, exception type) so
// a repeat-crash session doesn't spam the file. Survives across game
// runs so we can build a personal "mods needing fixes" inventory that
// also feeds the public Nexus compatibility list.

using System;
using System.Collections.Generic;
using System.IO;

namespace BetaDeps.Foundation;

public static class FailedModsCatalog
{
    private const string Tag = "BetaDeps.SaveShield.Catalog";
    private const string CatalogFileName = "failed-mods-catalog.txt";

    private static readonly object _lock = new();
    private static readonly HashSet<string> _sessionSeen = new();

    /// <summary>Path on disk where the catalog lives, next to runtime.log.</summary>
    public static string ResolvePath()
    {
        try
        {
            var rt = RuntimeLog.Path;
            var dir = Path.GetDirectoryName(rt);
            return string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir!, CatalogFileName);
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Append a one-line entry for this failure. Idempotent within a session
    /// (a repeat crash from the same mod+exception doesn't append a second
    /// line) but each new session gets a fresh entry so we can track timing.
    /// </summary>
    public static void Append(FailureRecord rec)
    {
        if (rec == null || string.IsNullOrEmpty(rec.CulpritAssembly)) return;
        var key = $"{rec.CulpritAssembly}|{rec.ExceptionType}|{rec.OwnerType}.{rec.OwnerMethod}";
        lock (_lock)
        {
            if (_sessionSeen.Contains(key)) return;
            _sessionSeen.Add(key);
        }

        try
        {
            var path = ResolvePath();
            if (string.IsNullOrEmpty(path)) return;

            // Header on first write so the file is grep-able even if you open
            // it without context.
            if (!File.Exists(path))
            {
                File.AppendAllText(path,
                    "# BetaDeps failed-mods catalog -- one line per (mod, exception type) seen by SaveShield.\n" +
                    "# Format: <UTC timestamp> | <CULPRIT> | <category> | <ExceptionType> | <owner method> | <message head>\n");
            }

            var line = string.Format(
                "{0} | {1,-32} | {2,-12} | {3,-40} | {4} | {5}\n",
                rec.When.ToString("yyyy-MM-dd HH:mm:ss"),
                Clip(rec.CulpritAssembly, 32),
                Clip(rec.Category, 12),
                Clip(rec.ExceptionType, 40),
                Clip(rec.OwnerType + "." + rec.OwnerMethod, 80),
                Clip(rec.Message?.Replace('\n', ' ').Replace('\r', ' ') ?? string.Empty, 200));
            File.AppendAllText(path, line);
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Append", ex); } catch { }
        }
    }

    private static string Clip(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max - 1) + "~";
    }
}
