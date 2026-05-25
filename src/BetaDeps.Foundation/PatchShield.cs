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
// When we catch one of the swallowable exceptions we:
//   1. Log it.
//   2. Synthesize a sensible default __result via Activator.CreateInstance
//      so downstream callers don't NRE on a null we returned.
//   3. Unpatch the offending consumer prefix from the original method's
//      Harmony patch chain so subsequent calls don't replay the broken
//      patch hundreds of times per second.

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
    private static readonly HashSet<string> _unpatched = new();
    private static readonly object _lock = new();

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

    /// <summary>Total prefixes PatchShield has UNPATCHED after they threw.</summary>
    public static int UnpatchedCount { get { lock (_lock) return _unpatched.Count; } }

    /// <summary>Exceptions PatchShield has swallowed since the AppDomain started.</summary>
    public static long SwallowedMissingMethod => System.Threading.Interlocked.Read(ref _swallowedMissingMethod);
    public static long SwallowedMissingField  => System.Threading.Interlocked.Read(ref _swallowedMissingField);
    public static long SwallowedTypeLoad      => System.Threading.Interlocked.Read(ref _swallowedTypeLoad);
    public static long SwallowedOther         => System.Threading.Interlocked.Read(ref _swallowedOther);
    public static long SwallowedTotal =>
        SwallowedMissingMethod + SwallowedMissingField + SwallowedTypeLoad + SwallowedOther;

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
            // v0.7.2 selftest hook: per-kind counter so the selftest can
            // report "PatchShield swallowed N missing-method exceptions
            // this session". Useful signal for users + maintainers.
            if (ex is MissingMethodException) System.Threading.Interlocked.Increment(ref _swallowedMissingMethod);
            else if (ex is MissingFieldException) System.Threading.Interlocked.Increment(ref _swallowedMissingField);
            else System.Threading.Interlocked.Increment(ref _swallowedTypeLoad);

            try
            {
                var ownerType = __originalMethod?.DeclaringType?.FullName ?? "?";
                var ownerName = __originalMethod?.Name ?? "?";
                DiagLog.Log(Tag,
                    $"swallowed {ex.GetType().Name} from a patch on {ownerType}.{ownerName}: {ex.Message}");
            }
            catch { /* logging shouldn't poison the shield */ }

            TryUnpatchOffendingPatches(__originalMethod, ex);
            return true;
        }
        return false;
    }

    // v5: brute-force unpatch ALL non-BetaDeps prefixes on the original
    // method when we catch a swallowable exception. Targeted unpatch by
    // ex.TargetSite didn't fire reliably (MethodInfo identity mismatch),
    // so we walk Harmony.GetPatchInfo and remove by owner Harmony ID.
    private static void TryUnpatchOffendingPatches(MethodBase originalMethod, Exception ex)
    {
        if (originalMethod == null) return;

        string originalKey;
        try
        {
            originalKey = (originalMethod.DeclaringType?.FullName ?? "?") + "::" + originalMethod.Name;
        }
        catch { return; }

        lock (_lock)
        {
            if (_unpatched.Contains(originalKey)) return;
            _unpatched.Add(originalKey);
        }

        try
        {
            var info = Harmony.GetPatchInfo(originalMethod);
            if (info == null) return;

            var harmony = new Harmony(HarmonyId);

            // Collect distinct non-BetaDeps owner IDs from all patch
            // categories on this method.
            var owners = new HashSet<string>();
            foreach (var p in info.Prefixes)  if (p != null) owners.Add(p.owner ?? string.Empty);
            foreach (var p in info.Postfixes) if (p != null) owners.Add(p.owner ?? string.Empty);
            foreach (var p in info.Transpilers) if (p != null) owners.Add(p.owner ?? string.Empty);
            foreach (var p in info.Finalizers) if (p != null) owners.Add(p.owner ?? string.Empty);

            foreach (var owner in owners)
            {
                if (string.IsNullOrEmpty(owner)) continue;
                if (owner == HarmonyId) continue;
                if (owner.StartsWith("BetaDeps", StringComparison.OrdinalIgnoreCase))
                {
                    DiagLog.Log(Tag, $"refusing to unpatch BetaDeps owner '{owner}' on {originalKey}");
                    continue;
                }

                try
                {
                    harmony.Unpatch(originalMethod, HarmonyPatchType.Prefix, owner);
                    DiagLog.Log(Tag, $"unpatched prefixes from owner '{owner}' on {originalKey}");
                    lock (_ownerLock)
                    {
                        _ownerCounts.TryGetValue(owner, out var c);
                        _ownerCounts[owner] = c + 1;
                    }
                }
                catch (Exception unpatchEx)
                {
                    DiagLog.LogCaught(Tag, $"Unpatch({owner} on {originalKey})", unpatchEx);
                }
            }
        }
        catch (Exception outerEx)
        {
            DiagLog.LogCaught(Tag, $"TryUnpatchOffendingPatches({originalKey})", outerEx);
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
            if (__result == null && __originalMethod is MethodInfo mi)
            {
                var rt = mi.ReturnType;
                if (rt != typeof(void))
                {
                    if (rt.IsValueType)
                    {
                        __result = Activator.CreateInstance(rt);
                    }
                    else if (!rt.IsAbstract
                             && !rt.IsInterface
                             && rt != typeof(string)
                             && rt.GetConstructor(
                                 BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                 binder: null,
                                 types: Type.EmptyTypes,
                                 modifiers: null) != null)
                    {
                        __result = Activator.CreateInstance(rt, nonPublic: true);
                    }
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
