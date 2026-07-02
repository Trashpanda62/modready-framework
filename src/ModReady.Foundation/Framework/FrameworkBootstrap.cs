// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FrameworkBootstrap -- in-game wiring for the v2.0 framework primitives that
// benefit from running automatically once consumer mods have applied their
// Harmony patches. Called from ModReadyHarmonySubModule's late lifecycle hooks
// (the same idempotent path that re-installs PatchShield/SaveShield), so by the
// time it runs the global patch registry is populated.
//
// What it does (all read-only / opt-in, never mutates another mod's state):
//   - ModConflictDetector scan: logs inter-mod Harmony conflicts to runtime.log.
//     Re-runs across the late hooks but only LOGS when the conflict count grows,
//     so deferred-patch mods (AIInfluence et al.) still get captured without
//     spamming the log.
//   - PerfProfiler auto-instrument: OFF by default. Enabled only when the user
//     drops a `perf-profiler.flag` file in Modules\ModReady\ (same flag-file
//     convention as patchshield-disabled.flag). Then every patched method is
//     wrapped with timing so the user can see which mods cost frames.
//
// EventBus and the manual PerfProfiler scope need no bootstrap -- they're pull
// APIs consumer mods call directly.

using System;
using System.IO;
using System.Linq;
using System.Threading;

using ModReady.Foundation;   // DiagLog, RuntimeLog

namespace ModReady.Framework
{
    public static class FrameworkBootstrap
    {
        private const string Tag = "ModReady.Framework";
        private const string PerfFlagName = "perf-profiler.flag";

        private static int _lastLoggedConflicts = -1;
        private static int _perfInstrumented;   // 0/1: only announce the enable once

        /// <summary>
        /// Run the auto-wired framework passes. Idempotent and cheap; safe to
        /// call from every late lifecycle hook.
        /// </summary>
        public static void RunLateInit(string from)
        {
            ScanConflicts(from);
            MaybeInstrumentPerf(from);
        }

        private static void ScanConflicts(string from)
        {
            try
            {
                var conflicts = ModConflictDetector.Scan();
                // Log only when the picture changes (count grows as deferred
                // patches land) so we don't re-emit the same report 4x.
                if (conflicts.Count == _lastLoggedConflicts) return;
                _lastLoggedConflicts = conflicts.Count;

                if (conflicts.Count == 0)
                {
                    DiagLog.Log(Tag, $"conflict scan ({from}): no inter-mod Harmony conflicts");
                    return;
                }

                int high = conflicts.Count(c => c.Severity == ConflictSeverity.High);
                int med = conflicts.Count(c => c.Severity == ConflictSeverity.Medium);
                int low = conflicts.Count(c => c.Severity == ConflictSeverity.Low);
                DiagLog.Log(Tag, $"conflict scan ({from}): {conflicts.Count} contested method(s) " +
                                 $"(High {high}, Medium {med}, Low {low})");
                foreach (var c in conflicts.Where(c => c.Severity >= ConflictSeverity.Medium))
                    DiagLog.Log(Tag, "  " + c.ToString().Replace("\n", " "));
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "ScanConflicts", ex); }
        }

        private static void MaybeInstrumentPerf(string from)
        {
            try
            {
                if (!PerfFlagPresent()) return;
                int added = PerfProfiler.InstrumentAllPatchedMethods();
                if (Interlocked.Exchange(ref _perfInstrumented, 1) == 0)
                    DiagLog.Log(Tag, $"{PerfFlagName} present -- PerfProfiler auto-instrument ON " +
                                     $"({PerfProfiler.InstrumentedCount} method(s) so far)");
                else if (added > 0)
                    DiagLog.Log(Tag, $"PerfProfiler instrumented {added} newly-patched method(s) ({from})");
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "MaybeInstrumentPerf", ex); }
        }

        private static bool PerfFlagPresent()
        {
            try
            {
                var rtPath = RuntimeLog.Path;
                var dir = Path.GetDirectoryName(rtPath);
                if (string.IsNullOrEmpty(dir)) return false;
                return File.Exists(Path.Combine(dir!, PerfFlagName));
            }
            catch { return false; }
        }
    }
}
