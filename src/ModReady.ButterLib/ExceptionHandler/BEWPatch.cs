// ModReady clean-room re-implementation of ButterLib's BEWPatch.
// MIT, copyright 2026 Maxfield Management Group.
//
// BetterExceptionWindow (BEW) finalizers: Harmony finalizers attached to
// the 5 tick methods where engine exceptions are most disruptive. The
// finalizer logs the exception and returns null, which tells Harmony to
// swallow it. The game continues. Loses one tick of work in exchange for
// staying up.
//
// Targets (same set CREST's runtime.log validated):
//   TaleWorlds.DotNet.Managed.ApplicationTick
//   TaleWorlds.MountAndBlade.Module.OnApplicationTick
//   TaleWorlds.ScreenSystem.ScreenManager.Tick
//   TaleWorlds.Engine.ManagedScriptHolder.TickComponents
//   TaleWorlds.MountAndBlade.Mission.Tick

using System;
using System.Reflection;

using ModReady.Foundation;

using HarmonyLib;

namespace Bannerlord.ButterLib.ExceptionHandler;

public static class BEWPatch
{
    private const string Tag = "BEWPatch";
    private const string HarmonyId = "modready.butterlib.bewpatch";

    private static bool _enabled;

    /// <summary>
    /// Install the 5 finalizers. Idempotent. Each bind logs its result so
    /// CrestPatchSelfTest can verify they took effect.
    /// </summary>
    public static void Enable()
    {
        if (_enabled) return;
        _enabled = true;

        DiagLog.Log(Tag, "Enable() invoked -- attempting 5 finalizer binds");

        var harmony = new HarmonyLib.Harmony(HarmonyId);

        TryBind(harmony, "TaleWorlds.DotNet.Managed",            "ApplicationTick",          label: "Managed.ApplicationTick");
        TryBind(harmony, "TaleWorlds.MountAndBlade.Module",      "OnApplicationTick",        label: "Module.OnApplicationTick");
        TryBind(harmony, "TaleWorlds.ScreenSystem.ScreenManager","Tick",                     label: "ScreenManager.Tick");
        TryBind(harmony, "TaleWorlds.Engine.ManagedScriptHolder","TickComponents",           label: "ManagedScriptHolder.TickComponents");
        TryBind(harmony, "TaleWorlds.MountAndBlade.Mission",     "Tick",                     label: "Mission.Tick");
    }

    private static void TryBind(HarmonyLib.Harmony harmony, string typeName, string methodName, string label)
    {
        try
        {
            var t = ModReady.Foundation.ReflectionUtils.ResolveTypeByFullName(typeName);
            if (t == null)
            {
                DiagLog.Log(Tag, $"{label} bind skipped -- type {typeName} not found");
                return;
            }
            // Find any method with the given name. Most have one overload; if
            // there are several, we attach to all to keep behavior identical
            // across game versions.
            var matches = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            int boundCount = 0;
            foreach (var m in matches)
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;
                try
                {
                    var finalizer = new HarmonyMethod(typeof(BEWPatch), nameof(Finalizer));
                    harmony.Patch(m, finalizer: finalizer);
                    boundCount++;
                }
                catch (Exception inner)
                {
                    DiagLog.LogCaught(Tag, $"{label} bind on overload", inner);
                }
            }
            if (boundCount > 0)
                DiagLog.Log(Tag, $"{label} bound -- finalizer active on {t.FullName}.{methodName}");
            else
                DiagLog.Log(Tag, $"{label} bind skipped -- no method named '{methodName}' on {t.FullName}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryBind({label})", ex);
        }
    }

    /// <summary>
    /// Harmony finalizer body. Receives the in-flight exception via
    /// __exception, logs it, and returns null to swallow.
    /// </summary>
    // v0.7.5 (post-ship-prep): per-(method,exception) log dedup so a tight
    // engine-tick swallow doesn't fill the log with the same line 60
    // times/second. Key is "{Method}|{ExceptionTypeName}|{first 200 chars
    // of message}" so distinct LoaderExceptions still surface independently.
    // M8 (Phase 3, 2026-06-11): once-only became invisible for persistent
    // faults -- a swallow that keeps firing now re-logs every 500th hit
    // with its running count, so "still happening, 10000 times by now"
    // is visible in runtime.log instead of one line from 40 minutes ago.
    private static readonly System.Collections.Generic.Dictionary<string, long> _swallowCounts
        = new System.Collections.Generic.Dictionary<string, long>(System.StringComparer.Ordinal);
    private static readonly object _swallowLogLock = new object();
    private const int SwallowRelogEvery = 500;

    /// <summary>
    /// Harmony finalizer body. Receives the in-flight exception via
    /// __exception, logs it, and returns null to swallow.
    ///
    /// v0.7.5 (post-ship-prep): when the exception is a
    /// ReflectionTypeLoadException, unwrap LoaderExceptions and log the
    /// FIRST few inner exceptions so the actual missing-type / missing-
    /// method culprit is visible. Without this, "swallowed
    /// ReflectionTypeLoadException -- retrieve LoaderExceptions for more
    /// information" was completely opaque and hid Eagle Rising's per-tick
    /// type-load failure during new-campaign init.
    /// </summary>
    public static Exception? Finalizer(MethodBase __originalMethod, Exception? __exception)
    {
        if (__exception == null) return null;
        try
        {
            var methodSig = $"{__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}";
            // Leak/throttle fix: key on (method, exception type) ONLY -- NOT the
            // message. A persistent fault whose Message varies per occurrence
            // (entity ids, frame numbers, ReflectionTypeLoadException LoaderException
            // text) otherwise minted a brand-new dictionary key every tick on this
            // 60Hz finalizer path: an unbounded growth of _swallowCounts AND a
            // defeated throttle (every distinct message re-logged at count==1). With
            // a (method|type) key the dictionary cardinality is bounded by
            // methods x exception-types; the full message is still written below.
            var key = $"{methodSig}|{__exception.GetType().Name}";
            long count;
            lock (_swallowLogLock)
            {
                _swallowCounts.TryGetValue(key, out count);
                count++;
                _swallowCounts[key] = count;
            }
            if (count != 1 && count % SwallowRelogEvery != 0) return null; // throttled; don't spam

            DiagLog.Log(Tag,
                $"swallowed engine exception in {methodSig}: " +
                $"{__exception.GetType().Name} -- {__exception.Message}" +
                (count > 1 ? $" (seen {count} times this session)" : string.Empty));

            // Unwrap ReflectionTypeLoadException: its LoaderExceptions
            // property carries the actual culprit exceptions. Log up to
            // the first 5 with full type+message so we can see what mod
            // assembly's type-load is failing.
            if (__exception is System.Reflection.ReflectionTypeLoadException rtle && rtle.LoaderExceptions != null)
            {
                int shown = 0;
                int total = rtle.LoaderExceptions.Length;
                foreach (var le in rtle.LoaderExceptions)
                {
                    if (le == null) continue;
                    if (shown >= 5) break;
                    DiagLog.Log(Tag, $"  LoaderException[{shown}/{total}]: {le.GetType().Name} -- {le.Message}");
                    shown++;
                }
                if (total > 5)
                    DiagLog.Log(Tag, $"  (+ {total - 5} more LoaderException(s) suppressed)");
            }
            // Also unwrap a single InnerException for non-RTLE wrapping.
            else if (__exception.InnerException != null)
            {
                DiagLog.Log(Tag, $"  InnerException: {__exception.InnerException.GetType().Name} -- {__exception.InnerException.Message}");
            }

            // v0.7.6 (2026-07-04): log the STACK TRACE of the (inner) exception on the
            // first sighting. Without this a swallowed NullReferenceException is
            // undiagnosable -- the message is just "Object reference not set..." with no
            // hint of which submodule/tick method threw it. This blindspot is why
            // ModReady loading-bug reports were unactionable: the fault was caught and
            // hidden, not surfaced. One stack per (method,type) per session; throttled
            // like the message above, so no 60Hz spam.
            if (count == 1)
            {
                var origin = __exception.InnerException ?? __exception;
                var trace = origin.StackTrace;
                if (!string.IsNullOrEmpty(trace))
                {
                    var lines = trace.Split('\n');
                    int n = Math.Min(lines.Length, 15);
                    DiagLog.Log(Tag, $"  stack ({origin.GetType().Name}, first {n} frame(s)):");
                    for (int i = 0; i < n; i++)
                        DiagLog.Log(Tag, "    " + lines[i].TrimEnd());
                }
                else
                {
                    DiagLog.Log(Tag, "  (no stack trace on the exception -- likely thrown from native/engine code)");
                }
            }
        }
        catch { }
        return null;
    }

}
