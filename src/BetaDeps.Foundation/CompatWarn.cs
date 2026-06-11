// BetaDeps.Foundation -- CompatWarn
//
// Phase 0.3 of the 2026-06-10 remediation plan: the single warning channel
// for "compat shim invoked but not (fully) implemented".
//
// BetaDeps' contract is "consumer mods work unmodified". The worst failure
// mode we have is a shim that satisfies the type-loader/JIT but silently
// does nothing at runtime (fluent MCM IRefs, v1 prefab patches, hotkeys,
// custom widget registration, the DI gateway...). Users can't report what
// they can't see. Every such surface calls CompatWarn.Once() at its
// point-of-use so the gap shows up, exactly once per (area, member,
// consumer), in BOTH:
//
//   1. runtime.log via DiagLog (tag "COMPAT"), and
//   2. betadeps-compat-warnings.log next to runtime.log -- a small,
//      dedicated file users can paste into a bug report without hunting.
//
// Usage from inside BetaDeps assemblies that reference Foundation:
//
//   CompatWarn.Once("ButterLib.HotKeys", "HotKeyManager.Create",
//                   consumerAssembly.GetName().Name,
//                   "hotkeys register but will not fire (input wiring not implemented)");
//
// Assemblies that avoid a hard Foundation reference use the same
// reflection pattern as DiagLog (see DiagLog.cs header).
//
// Never throws. Diagnostics must never CTD the game.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.IO;

namespace BetaDeps.Foundation;

public static class CompatWarn
{
    private const string Tag = "COMPAT";
    private const string FileName = "betadeps-compat-warnings.log";

    private static readonly object _gate = new object();
    private static readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
    private static string? _filePath; // resolved lazily, next to runtime.log

    /// <summary>Total distinct compat gaps hit this session (for the session summary / About panel).</summary>
    public static int Count
    {
        get { lock (_gate) return _seen.Count; }
    }

    /// <summary>
    /// Record that a consumer touched a shim surface that is missing or
    /// incomplete. Logs once per (area, member, consumer) per session;
    /// subsequent identical calls are free no-ops.
    /// </summary>
    /// <param name="area">Subsystem, e.g. "ButterLib.HotKeys", "UIExtenderEx.Prefabs(v1)", "MCM.Fluent".</param>
    /// <param name="member">The API surface touched, e.g. "HotKeyManager.Create".</param>
    /// <param name="consumer">Consumer assembly name if known, else null.</param>
    /// <param name="detail">One human sentence: what silently doesn't work and why it matters.</param>
    public static void Once(string area, string member, string? consumer = null, string? detail = null)
    {
        try
        {
            var key = area + "|" + member + "|" + (consumer ?? "?");
            lock (_gate)
            {
                if (!_seen.Add(key))
                    return;
            }

            var line = $"shim not fully implemented: [{area}] {member}"
                       + (consumer is null ? "" : $" (consumer: {consumer})")
                       + (detail is null ? "" : $" -- {detail}");

            DiagLog.Log(Tag, line);
            AppendToWarningsFile(line);
        }
        catch
        {
            // Swallow everything -- same policy as DiagLog. A diagnostics
            // channel that can crash the game is worse than no channel.
        }
    }

    private static void AppendToWarningsFile(string line)
    {
        try
        {
            if (_filePath is null)
            {
                var dir = Path.GetDirectoryName(RuntimeLog.Path);
                if (string.IsNullOrEmpty(dir))
                    return;
                _filePath = Path.Combine(dir!, FileName);
            }

            File.AppendAllText(_filePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // best-effort only
        }
    }
}
