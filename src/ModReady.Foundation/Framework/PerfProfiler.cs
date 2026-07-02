// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// PerfProfiler -- ModReady v2.0 framework primitive #3.
//
// Answers "which mod is costing me frames?" with two complementary surfaces:
//
//   1. Manual scope (any mod, any code path):
//          using (PerfProfiler.Measure("MyMod", "RecalcInfluence"))
//          {
//              // ... work ...
//          }
//      Accumulates call count + total/avg/max time per (owner, label).
//
//   2. Auto-instrument (opt-in, attributes engine-method cost to the mods that
//      patched it):
//          PerfProfiler.InstrumentAllPatchedMethods();
//      Wraps every Harmony-patched method with a Stopwatch prefix + finalizer.
//      The finalizer (not a postfix) does the accounting so the timing stays
//      balanced even when the original method throws. Each instrumented method
//      records its owners, so the report reads "Mission.OnTick -- 8.4ms/1000
//      calls -- patched by [ModA, ModB]".
//
// Overhead control: a global Enabled flag short-circuits both surfaces so a
// shipped build pays ~nothing until a user turns profiling on. Timing uses
// Stopwatch.GetTimestamp() (raw ticks) and only converts to ms at report time.
//
// Engine-free: System.Diagnostics + HarmonyLib only. Fully exercised off-engine
// by the framework self-test (both the manual scope and the Harmony auto-path).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using ModReady.Foundation;   // DiagLog

using HarmonyLib;

namespace ModReady.Framework
{
    /// <summary>One aggregated timing bucket (a manual label or an instrumented method).</summary>
    public sealed class ProfileEntry
    {
        public string Key { get; }
        /// <summary>Owners credited with this cost (mod ids). Empty for manual scopes that only named an owner in the key.</summary>
        public IReadOnlyList<string> Owners { get; }
        public long Calls { get; }
        public double TotalMs { get; }
        public double AvgMs => Calls > 0 ? TotalMs / Calls : 0.0;
        public double MaxMs { get; }

        public ProfileEntry(string key, IReadOnlyList<string> owners, long calls, double totalMs, double maxMs)
        {
            Key = key; Owners = owners; Calls = calls; TotalMs = totalMs; MaxMs = maxMs;
        }

        public override string ToString()
        {
            var who = Owners.Count > 0 ? $"  [{string.Join(", ", Owners)}]" : "";
            return $"{TotalMs,9:F2}ms  {Calls,7} calls  avg {AvgMs,7:F4}ms  max {MaxMs,7:F3}ms  {Key}{who}";
        }
    }

    public static class PerfProfiler
    {
        private const string Tag = "ModReady.PerfProfiler";
        private const string HarmonyId = "ModReady.Foundation.PerfProfiler";

        /// <summary>Master switch. When false, Measure() is a no-op scope and the auto-instrument hooks return immediately.</summary>
        public static bool Enabled = true;

        private sealed class Bucket
        {
            public long Calls;
            public long TotalTicks;
            public long MaxTicks;
            public string[] Owners = Array.Empty<string>();
        }

        private static readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
        private static readonly double _ticksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        // ----------------------------------------------------------------
        // Manual scope
        // ----------------------------------------------------------------

        /// <summary>
        /// Begin a timed scope. Dispose (end of the using) records the elapsed
        /// time against "<paramref name="owner"/>::<paramref name="label"/>".
        /// Returns a no-op scope when profiling is disabled.
        /// </summary>
        public static IDisposable Measure(string owner, string label)
        {
            if (!Enabled) return NullScope.Instance;
            return new Scope($"{owner}::{label}", owner);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }

        private sealed class Scope : IDisposable
        {
            private readonly string _key;
            private readonly string? _owner;
            private readonly long _start;
            private bool _done;
            public Scope(string key, string? owner)
            {
                _key = key; _owner = owner;
                _start = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - _start;
                Accumulate(_key, elapsed, _owner == null ? null : new[] { _owner });
            }
        }

        // ----------------------------------------------------------------
        // Accumulation core
        // ----------------------------------------------------------------

        private static void Accumulate(string key, long elapsedTicks, string[]? owners)
        {
            if (elapsedTicks < 0) elapsedTicks = 0;
            var bucket = _buckets.GetOrAdd(key, _ => new Bucket());
            Interlocked.Increment(ref bucket.Calls);
            Interlocked.Add(ref bucket.TotalTicks, elapsedTicks);
            // max (lock-free CAS loop)
            long oldMax;
            do { oldMax = Interlocked.Read(ref bucket.MaxTicks); }
            while (elapsedTicks > oldMax &&
                   Interlocked.CompareExchange(ref bucket.MaxTicks, elapsedTicks, oldMax) != oldMax);
            if (owners != null && owners.Length > 0 && bucket.Owners.Length == 0)
                bucket.Owners = owners;
        }

        // ----------------------------------------------------------------
        // Reporting
        // ----------------------------------------------------------------

        /// <summary>Aggregated entries, highest total time first.</summary>
        public static IReadOnlyList<ProfileEntry> Snapshot()
        {
            return _buckets
                .Select(kv => new ProfileEntry(
                    kv.Key,
                    kv.Value.Owners,
                    Interlocked.Read(ref kv.Value.Calls),
                    Interlocked.Read(ref kv.Value.TotalTicks) * _ticksToMs,
                    Interlocked.Read(ref kv.Value.MaxTicks) * _ticksToMs))
                .OrderByDescending(e => e.TotalMs)
                .ToList();
        }

        /// <summary>Clear all accumulated buckets.</summary>
        public static void Reset() => _buckets.Clear();

        /// <summary>Plain-text report (top <paramref name="top"/> entries).</summary>
        public static string ToText(int top = 25)
        {
            var snap = Snapshot();
            if (snap.Count == 0) return "PerfProfiler: no samples recorded.";
            var sb = new StringBuilder();
            sb.AppendLine($"ModReady perf report -- {snap.Count} bucket(s), top {Math.Min(top, snap.Count)} by total time:");
            foreach (var e in snap.Take(top)) sb.AppendLine("  " + e);
            return sb.ToString();
        }

        // ----------------------------------------------------------------
        // Auto-instrument (Harmony)
        // ----------------------------------------------------------------

        private static readonly ThreadLocal<Stack<long>> _starts =
            new(() => new Stack<long>());
        // method -> stable key + credited owners, computed once at instrument time.
        private static readonly ConcurrentDictionary<MethodBase, (string key, string[] owners)> _meta =
            new();
        private static readonly HashSet<MethodBase> _instrumented = new();
        private static readonly object _instrumentLock = new();

        /// <summary>Number of methods currently auto-instrumented.</summary>
        public static int InstrumentedCount { get { lock (_instrumentLock) return _instrumented.Count; } }

        /// <summary>
        /// Instrument every method currently in Harmony's global registry. Safe
        /// to call repeatedly (idempotent per method). Returns the count newly
        /// instrumented this call.
        /// </summary>
        public static int InstrumentAllPatchedMethods()
        {
            IEnumerable<MethodBase> methods;
            try { methods = Harmony.GetAllPatchedMethods().ToList(); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "GetAllPatchedMethods", ex); return 0; }
            return Instrument(methods);
        }

        /// <summary>Instrument a specific set of methods. Returns count newly instrumented.</summary>
        public static int Instrument(IEnumerable<MethodBase> methods)
        {
            if (methods == null) return 0;
            var harmony = new Harmony(HarmonyId);
            var pre = typeof(PerfProfiler).GetMethod(nameof(TimingPrefix), BindingFlags.Static | BindingFlags.NonPublic);
            var fin = typeof(PerfProfiler).GetMethod(nameof(TimingFinalizer), BindingFlags.Static | BindingFlags.NonPublic);
            if (pre == null || fin == null)
            {
                DiagLog.Log(Tag, "could not resolve timing hooks; aborting instrument");
                return 0;
            }

            int added = 0;
            lock (_instrumentLock)
            {
                foreach (var m in methods)
                {
                    if (m == null || _instrumented.Contains(m)) continue;
                    // Don't instrument ModReady' own methods -- it would charge
                    // our shields' overhead back to us and add noise.
                    try
                    {
                        var asm = m.DeclaringType?.Assembly?.GetName()?.Name ?? "";
                        if (asm.StartsWith("ModReady", StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    catch { }

                    try
                    {
                        _meta[m] = (FullSignature(m), OwnersOf(m));
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(pre),
                            finalizer: new HarmonyMethod(fin));
                        _instrumented.Add(m);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        DiagLog.LogCaught(Tag, $"instrument {FullSignature(m)}", ex);
                    }
                }
            }
            if (added > 0) DiagLog.Log(Tag, $"instrumented {added} method(s) (total {_instrumented.Count})");
            return added;
        }

        /// <summary>Remove all auto-instrument hooks (manual buckets are kept).</summary>
        public static void RemoveInstrumentation()
        {
            try { new Harmony(HarmonyId).UnpatchAll(HarmonyId); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "RemoveInstrumentation", ex); }
            lock (_instrumentLock) { _instrumented.Clear(); }
            _meta.Clear();
        }

#pragma warning disable IDE0051, IDE1006
        // Push UNCONDITIONALLY (not gated on Enabled). If the prefix skipped the
        // push when disabled but Enabled flipped true before the finalizer, the
        // finalizer would pop a DIFFERENT (outer) frame's start and corrupt the
        // per-thread stack permanently. By always pushing in the prefix and
        // always popping in the finalizer, stack balance is invariant regardless
        // of Enabled toggling mid-call; Enabled only gates whether we RECORD.
        private static void TimingPrefix()
        {
            _starts.Value!.Push(System.Diagnostics.Stopwatch.GetTimestamp());
        }

        // Finalizer runs on BOTH normal return and exception, so the push/pop
        // stays balanced even when an instrumented method throws. Returns the
        // exception unchanged -- this profiler never swallows.
        private static Exception? TimingFinalizer(MethodBase __originalMethod, Exception? __exception)
        {
            var stack = _starts.Value!;
            if (stack.Count == 0) return __exception; // defensive: nothing to pair
            long start = stack.Pop();
            // Only ACCOUNT when enabled -- the pop above already kept the stack
            // balanced. (Records nothing if Enabled is false right now.)
            if (Enabled && _meta.TryGetValue(__originalMethod, out var meta))
            {
                long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                Accumulate(meta.key, elapsed, meta.owners);
            }
            return __exception;
        }
#pragma warning restore IDE0051, IDE1006

        private static string[] OwnersOf(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return Array.Empty<string>();
                return info.Owners
                    .Where(o => !string.IsNullOrEmpty(o) && !o.StartsWith("ModReady", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private static string FullSignature(MethodBase m)
        {
            try
            {
                var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
                return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}({ps})";
            }
            catch { return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}"; }
        }
    }
}
