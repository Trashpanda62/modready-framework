// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// PatchShield: a generic Harmony finalizer installed on every currently-
// patched method to catch the three exceptions consumer-mod prefixes
// usually throw when they were built against an older TaleWorlds API:
//
//   - MissingMethodException  (called a method that no longer exists)
//   - MissingFieldException   (read/wrote a field that no longer exists)
//   - TypeLoadException       (referenced a type whose layout changed)
//
// Lifecycle: installed at OnBeforeInitialModuleScreenSetAsRoot AND every
// late lifecycle point so we catch patches that consumer mods defer past
// OnSubModuleLoad.
//
// When we catch one of the swallowable exceptions we (reworked Phase 3 /
// H1 + H4, 2026-06-11):
//   1. Attribute the failure to a culprit assembly via the exception's
//      stack (shared CulpritFinder core). No non-engine culprit frame =
//      the failure is TaleWorlds' own -- we DON'T swallow those.
//   2. Log it (throttled: first hit + every 500th, so a per-frame method
//      can't flood runtime.log).
//   3. Unpatch ONLY the culprit owner's patches (all patch types) from the
//      original method, leaving innocent mods' patches on the same method
//      intact. Keyed by full method signature; one retry allowed so a
//      second, different culprit on the same method can still be removed.
//   4. For value-type returns, default-initialize __result. Reference-type
//      returns stay null -- fabricating instances via non-public ctors ran
//      arbitrary ctor side effects on engine types and handed callers
//      half-initialized objects (H4).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class PatchShield
{
    private const string Tag = "BetaDeps.PatchShield";
    private const string HarmonyId = "BetaDeps.Foundation.PatchShield";

    private static readonly HashSet<MethodBase> _shielded = new();
    // Methods (full signature) where at least one culprit owner was unpatched.
    private static readonly HashSet<string> _unpatched = new();
    // Unpatch passes per method signature. Capped at 2 (initial + one retry)
    // so a second, different culprit on the same method still gets removed,
    // but a method that keeps failing can't churn unpatch passes forever.
    private static readonly Dictionary<string, int> _unpatchAttempts = new(StringComparer.Ordinal);
    private const int MaxUnpatchAttemptsPerMethod = 2;
    private static readonly object _lock = new();

    // Swallow-log throttle: identical (method, exception-type) swallows log
    // on the 1st hit and every 500th after that. A stale patch on a
    // per-frame method used to write one log line per call (H1).
    // Lock-free: the finalizer runs on the engine's tick threads.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _swallowLogCounts
        = new(StringComparer.Ordinal);
    private const int SwallowRelogEvery = 500;

    // Per-(method, exception-type) swallow/rethrow verdict cache. The
    // culprit walk allocates a StackTrace; doing it once per distinct
    // failure instead of once per call keeps a still-throwing patch on a
    // 60 Hz method from allocating every frame until the unpatch lands.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _verdictCache
        = new(StringComparer.Ordinal);

    // v4 #6: aggregate Harmony owner IDs we've had to unpatch this session,
    // with a count of how many methods each owner contributed. Selftest
    // surfaces this so mod authors see "AIInfluence patches: 4 unpatched"
    // instead of having to grep runtime.log line-by-line.
    private static readonly Dictionary<string, int> _ownerCounts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _ownerLock = new();

    /// <summary>
    /// Snapshot of (Harmony owner ID -> count of unpatched methods this session).
    /// Owner IDs are typically formatted as "ModNamespace.Component" by mod
    /// authors so the key is usually mod-identifiable on its own.
    /// </summary>
    public static IReadOnlyDictionary<string, int> UnpatchedOwnerCounts
    {
        get
        {
            lock (_ownerLock)
            {
                return _ownerCounts.ToDictionary(k => k.Key, v => v.Value);
            }
        }
    }

    // v0.7.2 (selftest): per-session counters so the selftest can quote a
    // single "this is what PatchShield has been doing for you" line. Each
    // counter increments inside the finalizer when we catch the matching
    // exception kind. Lifetime = AppDomain.
    private static long _swallowedMissingMethod;
    private static long _swallowedMissingField;
    private static long _swallowedTypeLoad;
    private static long _swallowedOther;

    /// <summary>Total methods PatchShield has finalizer-wrapped this session.</summary>
    public static int ShieldedCount { get { lock (_lock) return _shielded.Count; } }

    /// <summary>Total methods PatchShield has unpatched a culprit owner from after a throw.</summary>
    public static int UnpatchedCount { get { lock (_lock) return _unpatched.Count; } }

    /// <summary>Exceptions PatchShield has swallowed since the AppDomain started.</summary>
    public static long SwallowedMissingMethod => System.Threading.Interlocked.Read(ref _swallowedMissingMethod);
    public static long SwallowedMissingField  => System.Threading.Interlocked.Read(ref _swallowedMissingField);
    public static long SwallowedTypeLoad      => System.Threading.Interlocked.Read(ref _swallowedTypeLoad);
    public static long SwallowedOther         => System.Threading.Interlocked.Read(ref _swallowedOther);
    public static long SwallowedTotal =>
        SwallowedMissingMethod + SwallowedMissingField + SwallowedTypeLoad + SwallowedOther;

    /// <summary>
    /// One-liner session-summary, written to runtime.log at AppDomain.ProcessExit
    /// so users grepping runtime.log get a single tidy summary line instead
    /// of needing to scan the whole file. Idempotent + cheap; safe to call
    /// from a process-exit handler even if PatchShield never installed.
    /// </summary>
    public static void WriteSessionSummary()
    {
        try
        {
            string topOwner = "(none)";
            lock (_ownerLock)
            {
                if (_ownerCounts.Count > 0)
                {
                    var top = _ownerCounts.OrderByDescending(k => k.Value).First();
                    topOwner = $"{top.Key} ({top.Value})";
                }
            }
            DiagLog.Log(Tag,
                $"SESSION SUMMARY: shielded {ShieldedCount} method(s), " +
                $"unpatched culprits on {UnpatchedCount} method(s), " +
                $"swallowed {SwallowedTotal} exception(s) " +
                $"(MissingMethod {SwallowedMissingMethod}, MissingField {SwallowedMissingField}, " +
                $"TypeLoad {SwallowedTypeLoad}, other {SwallowedOther}). " +
                $"Top unpatched owner: {topOwner}.");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "WriteSessionSummary", ex); } catch { }
        }
    }

    // Opt-out marker file. Default behavior is PatchShield ON. If a user
    // wants to disable it (mod author debugging, suspected interference
    // with another shim, etc.) they click "Toggle PatchShield" in Mod
    // Config, which creates this file. The next launch's Install() then
    // bails early on every lifecycle pass.
    //
    // Note: opposite default from auto-disable. auto-disable is opt-IN
    // (file presence enables); PatchShield is opt-OUT (file presence
    // disables). PatchShield doesn't modify LauncherData.xml or game
    // state on disk — it only catches managed exceptions thrown from
    // already-installed Harmony patches — so the conservative default
    // is "on, catching things" rather than "off, let it crash".
    private const string DisableFlagName = "patchshield-disabled.flag";

    /// <summary>
    /// Returns true if the user has opted out of PatchShield via the
    /// Mod Config "Toggle PatchShield" button (which creates
    /// patchshield-disabled.flag in Modules\BetaDeps\).
    /// </summary>
    public static bool IsDisabled()
    {
        try
        {
            var rtPath = RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rtPath);
            if (string.IsNullOrEmpty(dir)) return false;
            return System.IO.File.Exists(System.IO.Path.Combine(dir!, DisableFlagName));
        }
        catch { return false; }
    }

    public static void Install()
    {
        if (IsDisabled())
        {
            // Log once per pass so users can see in runtime.log that the
            // shield is intentionally off (vs. quietly missing).
            DiagLog.Log(Tag, $"{DisableFlagName} present — PatchShield disabled; skipping install. Click 'Toggle PatchShield' in Mod Config to re-enable.");
            return;
        }

        try
        {
            var harmony = new Harmony(HarmonyId);
            var finalizerWithResult = typeof(PatchShield).GetMethod(
                nameof(ShieldFinalizerWithResult),
                BindingFlags.Static | BindingFlags.NonPublic);
            var finalizerVoid = typeof(PatchShield).GetMethod(
                nameof(ShieldFinalizerVoid),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (finalizerWithResult == null || finalizerVoid == null)
            {
                DiagLog.Log(Tag, "could not resolve shield finalizer methods; aborting install");
                return;
            }

            List<MethodBase> targets;
            try { targets = Harmony.GetAllPatchedMethods().ToList(); }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "GetAllPatchedMethods", ex);
                return;
            }

            int patched = 0;
            int skipped = 0;
            int alreadyShielded = 0;
            lock (_lock)
            {
                foreach (var method in targets)
                {
                    if (method == null) { skipped++; continue; }
                    if (_shielded.Contains(method)) { alreadyShielded++; continue; }

                    try
                    {
                        var declaring = method.DeclaringType;
                        if (declaring != null)
                        {
                            var asmName = declaring.Assembly.GetName().Name ?? string.Empty;
                            if (asmName.StartsWith("BetaDeps", StringComparison.OrdinalIgnoreCase))
                            {
                                _shielded.Add(method);
                                skipped++;
                                continue;
                            }
                        }
                    }
                    catch { /* fall through and try to patch */ }

                    try
                    {
                        bool isVoid = true;
                        if (method is MethodInfo mi)
                            isVoid = mi.ReturnType == typeof(void);

                        var fin = isVoid ? finalizerVoid : finalizerWithResult;
                        harmony.Patch(method, finalizer: new HarmonyMethod(fin));
                        _shielded.Add(method);
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        skipped++;
                        DiagLog.LogCaught(Tag,
                            $"shielding {method.DeclaringType?.FullName}.{method.Name}", ex);
                    }
                }
            }

            if (patched > 0 || alreadyShielded == 0)
            {
                DiagLog.Log(Tag,
                    $"shield pass: +{patched} new, {alreadyShielded} already-shielded, {skipped} skipped (total shielded: {_shielded.Count})");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    private static bool ShouldSwallow(MethodBase __originalMethod, Exception __exception, out Exception unwrapped)
    {
        unwrapped = __exception;
        if (__exception == null) return false;

        var ex = __exception;
        while (ex is TargetInvocationException && ex.InnerException != null)
            ex = ex.InnerException;
        unwrapped = ex;

        if (ex is MissingMethodException
            || ex is MissingFieldException
            || ex is TypeLoadException)
        {
            var sigKey = FullSignature(__originalMethod);
            var verdictKey = sigKey + "|" + ex.GetType().Name;

            // Repeat of an already-judged failure: reuse the verdict without
            // re-walking the stack (the culprit walk allocates a StackTrace;
            // this path can run per-frame until the unpatch lands).
            if (_verdictCache.TryGetValue(verdictKey, out var cachedSwallow))
            {
                if (!cachedSwallow) return false;
                CountSwallow(ex);
                LogThrottled("swallow|" + verdictKey,
                    $"swallowed {ex.GetType().Name} from a patch on {sigKey}: {ex.Message}");
                return true;
            }

            // H4: only swallow when a NON-engine frame threw. These exception
            // types from pure engine/framework frames mean TaleWorlds' own
            // code path is broken -- masking that corrupts state invisibly.
            var culprit = CulpritFinder.FindCulpritFrame(ex);
            if (!culprit.HasCulprit)
            {
                _verdictCache.TryAdd(verdictKey, false);
                LogThrottled("rethrow|" + verdictKey,
                    $"NOT swallowing {ex.GetType().Name} on {sigKey}: no non-engine culprit frame (failure is engine/framework-internal): {ex.Message}");
                return false;
            }
            _verdictCache.TryAdd(verdictKey, true);

            CountSwallow(ex);
            LogThrottled("swallow|" + verdictKey,
                $"swallowed {ex.GetType().Name} from a patch on {sigKey} (culprit: {culprit.AssemblyName}): {ex.Message}");

            TryUnpatchOffendingPatches(__originalMethod, culprit);
            return true;
        }
        return false;
    }

    /// <summary>v0.7.2 selftest hook: per-kind counters so the selftest can
    /// report "PatchShield swallowed N missing-method exceptions this
    /// session". Only the three swallowable types reach here, so the else
    /// branch is exactly TypeLoadException (and subclasses).</summary>
    private static void CountSwallow(Exception ex)
    {
        if (ex is MissingMethodException) System.Threading.Interlocked.Increment(ref _swallowedMissingMethod);
        else if (ex is MissingFieldException) System.Threading.Interlocked.Increment(ref _swallowedMissingField);
        else System.Threading.Interlocked.Increment(ref _swallowedTypeLoad);
    }

    /// <summary>Full signature key including parameter types, so overloads
    /// don't collapse onto one retry-limit slot (H1).</summary>
    private static string FullSignature(MethodBase? m)
    {
        if (m == null) return "?";
        try
        {
            var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
            return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}({ps})";
        }
        catch
        {
            return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}";
        }
    }

    /// <summary>Log the first occurrence of a repeating shield event and
    /// every 500th after, with the running count. Lock-free counter.</summary>
    private static void LogThrottled(string key, string message)
    {
        try
        {
            var count = _swallowLogCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
            if (count == 1)
                DiagLog.Log(Tag, message);
            else if (count % SwallowRelogEvery == 0)
                DiagLog.Log(Tag, $"{message} (seen {count} times this session)");
        }
        catch { /* logging shouldn't poison the shield */ }
    }

    // Phase 3 / H1 rework: unpatch ONLY the culprit assembly's patches.
    // The v5 behavior stripped every non-BetaDeps prefix on the method, so
    // one broken mod's exception removed innocent mods' patches -- and
    // postfix/transpiler/finalizer culprits were never removed at all.
    private static void TryUnpatchOffendingPatches(MethodBase originalMethod, CulpritFinder.CulpritInfo culprit)
    {
        if (originalMethod == null) return;

        var sigKey = FullSignature(originalMethod);

        // One lock across gate + scan + unpatch + bookkeeping: unpatching is
        // rare, and holding the lock end-to-end (a) makes the 2-attempt cap
        // a real invariant under the engine's parallel tick threads, and
        // (b) serializes our Harmony.Unpatch calls.
        lock (_lock)
        {
            _unpatchAttempts.TryGetValue(sigKey, out var attempts);
            if (attempts >= MaxUnpatchAttemptsPerMethod) return;
            _unpatchAttempts[sigKey] = attempts + 1;

            TryUnpatchOffendingPatchesLocked(originalMethod, culprit, sigKey);
        }
    }

    private static void TryUnpatchOffendingPatchesLocked(MethodBase originalMethod, CulpritFinder.CulpritInfo culprit, string sigKey)
    {
        try
        {
            var info = Harmony.GetPatchInfo(originalMethod);
            if (info == null) return;

            // Owners whose patch methods live in the culprit assembly.
            var culpritOwners = new HashSet<string>(StringComparer.Ordinal);
            void ScanPatches(System.Collections.Generic.IEnumerable<Patch> patches)
            {
                foreach (var p in patches)
                {
                    if (p?.PatchMethod == null) continue;
                    var asmName = p.PatchMethod.DeclaringType?.Assembly?.GetName()?.Name;
                    if (string.Equals(asmName, culprit.AssemblyName, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(p.owner))
                    {
                        culpritOwners.Add(p.owner);
                    }
                }
            }
            ScanPatches(info.Prefixes);
            ScanPatches(info.Postfixes);
            ScanPatches(info.Transpilers);
            ScanPatches(info.Finalizers);

            if (culpritOwners.Count == 0)
            {
                DiagLog.Log(Tag, $"culprit '{culprit.AssemblyName}' has no patch on {sigKey}; leaving all patches in place (no innocent unpatching)");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            foreach (var owner in culpritOwners)
            {
                if (owner == HarmonyId) continue;
                if (owner.StartsWith("BetaDeps", StringComparison.OrdinalIgnoreCase))
                {
                    DiagLog.Log(Tag, $"refusing to unpatch BetaDeps owner '{owner}' on {sigKey}");
                    continue;
                }

                try
                {
                    harmony.Unpatch(originalMethod, HarmonyPatchType.All, owner);
                    DiagLog.Log(Tag, $"unpatched ALL patch types from culprit owner '{owner}' ({culprit.AssemblyName}) on {sigKey}");
                    _unpatched.Add(sigKey); // caller holds _lock
                    lock (_ownerLock)
                    {
                        _ownerCounts.TryGetValue(owner, out var c);
                        _ownerCounts[owner] = c + 1;
                    }
                }
                catch (Exception unpatchEx)
                {
                    DiagLog.LogCaught(Tag, $"Unpatch({owner} on {sigKey})", unpatchEx);
                }
            }
        }
        catch (Exception outerEx)
        {
            DiagLog.LogCaught(Tag, $"TryUnpatchOffendingPatches({sigKey})", outerEx);
        }
    }

#pragma warning disable IDE0051, IDE1006
    private static Exception ShieldFinalizerWithResult(MethodBase __originalMethod, ref object __result, Exception __exception)
#pragma warning restore IDE0051, IDE1006
    {
        if (__exception == null) return null;
        if (!ShouldSwallow(__originalMethod, __exception, out _)) return __exception;

        try
        {
            // H4: only default-initialize value-type returns (the finalizer
            // must hand back SOMETHING for a struct slot). Reference types
            // stay null -- the old Activator.CreateInstance(rt, nonPublic:
            // true) path ran arbitrary non-public ctor side effects on
            // engine types and returned half-initialized objects, which is
            // worse than a null the caller can at least null-check.
            if (__result == null && __originalMethod is MethodInfo mi)
            {
                var rt = mi.ReturnType;
                if (rt != typeof(void) && rt.IsValueType)
                {
                    __result = Activator.CreateInstance(rt);
                }
            }
        }
        catch (Exception synthEx)
        {
            try { DiagLog.LogCaught(Tag, "synthesize-default-result", synthEx); } catch { }
        }

        return null;
    }

#pragma warning disable IDE0051, IDE1006
    private static Exception ShieldFinalizerVoid(MethodBase __originalMethod, Exception __exception)
#pragma warning restore IDE0051, IDE1006
    {
        if (__exception == null) return null;
        return ShouldSwallow(__originalMethod, __exception, out _) ? null : __exception;
    }
}
