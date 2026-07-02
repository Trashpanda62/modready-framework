// BetaDeps.Harmony -- BetaSigSafePatches
//
// CTD-prevention patches that the CREST sprint identified as
// necessary on beta-branch (e1.4.x). These are NOT gameplay patches;
// they're defensive shims that swallow engine-side null derefs and
// signature-drift cases so the game stays up while community mods
// load against unstable beta APIs.
//
// On public branch (e1.3.x) these are no-ops -- VersionProbe returns
// GameBranch.Public and we skip the apply.
//
// The patches themselves are concept-level rewrites of the
// CrestBattleSizeBetaPatches work, re-authored against the public
// Harmony 2.x API. No source is copied from any Aragas-authored file.
//
// MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;

namespace BetaDeps.Harmony;

internal static class BetaSigSafePatches
{
    private const string Tag = "BetaSigSafePatches";
    private const string HarmonyId = "betadeps.harmony.sigsafe";

    private static bool _applied;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        if (!VersionProbe.IsBeta)
        {
            DiagLog.Log(Tag, $"Not on beta branch (detected {VersionProbe.Branch}, v{VersionProbe.Major}.{VersionProbe.Minor}) -- sigsafe patches not needed.");
            return;
        }

        DiagLog.Log(Tag, $"Beta branch detected (v{VersionProbe.Major}.{VersionProbe.Minor}). Applying sigsafe defensive patches.");

        try
        {
            var harmony = new HarmonyLib.Harmony(HarmonyId);
            int applied = 0;

            applied += TryPatchAgentTickFinalizer(harmony) ? 1 : 0;
            applied += TryPatchBrushRaceFinalizer(harmony) ? 1 : 0;

            DiagLog.Log(Tag, $"sigsafe pass complete: {applied} patches active.");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Apply", ex);
        }
    }

    // ---- patch slots --------------------------------------------------
    //
    // Each slot follows the same template:
    //   1. SafeBind.Method(...) to resolve & verify the target.
    //   2. If null, log and return false (the game stays up; we just
    //      don't get the defensive shim on this build).
    //   3. SafeBind.TryPatch to attach the finalizer.

    /// <summary>
    /// Mission.TickAgentsAndTeamsImp can NRE under parallel agent tick
    /// race on beta. The finalizer eats the exception so the worker
    /// thread doesn't abort the process. Loses one tick of agent
    /// updates; keeps the game running.
    /// </summary>
    private static bool TryPatchAgentTickFinalizer(HarmonyLib.Harmony harmony)
    {
        var missionType = BetaDeps.Foundation.ReflectionUtils.ResolveTypeByFullName("TaleWorlds.MountAndBlade.Mission");
        if (missionType == null)
        {
            DiagLog.Log(Tag, "skip AgentTickFinalizer: TaleWorlds.MountAndBlade.Mission not found");
            return false;
        }

        // Signature on the current target (verified against decomp + installed 1.4.6):
        //   public void TickAgentsAndTeamsImp(float dt, bool tickPaused)  -- TWO params.
        // The old expectedParamCount:1 never matched, so SafeBind logged "signature
        // drift" and this flagship CTD finalizer was silently never applied.
        var target = SafeBind.Method(
            missionType,
            "TickAgentsAndTeamsImp",
            expectedReturnType: typeof(void),
            expectedParamCount: 2,
            expectedParamTypes: new[] { typeof(float), typeof(bool) });
        if (target == null) return false;

        var finalizer = new HarmonyMethod(typeof(BetaSigSafePatches), nameof(SwallowEngineException));
        return SafeBind.TryPatch(harmony, target, finalizer: finalizer);
    }

    /// <summary>
    /// UpdateBrushesWidget AOORE under parallel brush update on beta.
    /// </summary>
    private static bool TryPatchBrushRaceFinalizer(HarmonyLib.Harmony harmony)
    {
        var widgetType = BetaDeps.Foundation.ReflectionUtils.ResolveTypeByFullName("TaleWorlds.GauntletUI.PrefabSystem.WidgetFactory")
                      ?? BetaDeps.Foundation.ReflectionUtils.ResolveTypeByFullName("TaleWorlds.GauntletUI.UIContext");
        if (widgetType == null)
        {
            DiagLog.Log(Tag, "skip BrushRaceFinalizer: target type not found");
            return false;
        }

        var target = SafeBind.Method(
            widgetType,
            "UpdateBrushesWidget",
            expectedReturnType: typeof(void),
            expectedParamCount: 1);
        if (target == null)
        {
            DiagLog.Log(Tag, "skip BrushRaceFinalizer: UpdateBrushesWidget not found with expected signature -- harmless on this build");
            return false;
        }

        var finalizer = new HarmonyMethod(typeof(BetaSigSafePatches), nameof(SwallowEngineException));
        return SafeBind.TryPatch(harmony, target, finalizer: finalizer);
    }

    // M8 (Phase 3, 2026-06-11): the swallow is now targeted and throttled.
    // These finalizers exist for parallel-tick RACE faults; only the race
    // exception classes are eaten. Everything else propagates -- eating
    // every exception type from Mission.TickAgentsAndTeamsImp masked real
    // engine faults. Per-method counter: after MaxSwallowsPerMethod the
    // finalizer rethrows from then on (a fault that fires hundreds of
    // times per second is not a transient race), and logging is capped at
    // the 1st hit + every 100th so the per-tick disk writes stop.
    private const int MaxSwallowsPerMethod = 200;
    private const int RelogEvery = 100;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _swallowCounts
        = new(StringComparer.Ordinal);

    private static bool IsRaceExceptionClass(Exception ex)
        => ex is NullReferenceException
        || ex is ArgumentOutOfRangeException
        || ex is IndexOutOfRangeException
        || ex is InvalidOperationException; // "collection was modified" -- the classic parallel-tick race

    /// <summary>
    /// Shared Harmony finalizer for the race-prone engine methods above.
    /// Swallows only race-class exceptions, with a per-method cap; returns
    /// the exception (= propagate) for anything else or once the cap trips.
    /// </summary>
    public static Exception? SwallowEngineException(MethodBase __originalMethod, Exception? __exception)
    {
        if (__exception == null) return null;
        try
        {
            var methodSig = $"{__originalMethod?.DeclaringType?.FullName}::{__originalMethod?.Name}";

            if (!IsRaceExceptionClass(__exception))
            {
                DiagLog.Log(Tag,
                    $"NOT swallowing {__exception.GetType().Name} in {methodSig} (not a race-class exception): {__exception.Message}");
                return __exception;
            }

            var count = _swallowCounts.AddOrUpdate(methodSig, 1, (_, c) => c + 1);
            if (count > MaxSwallowsPerMethod)
            {
                if (count == MaxSwallowsPerMethod + 1)
                    DiagLog.Log(Tag,
                        $"{methodSig} exceeded {MaxSwallowsPerMethod} swallowed exception(s) this session -- this is not a transient race; rethrowing from now on");
                return __exception;
            }

            if (count == 1 || count % RelogEvery == 0)
                DiagLog.Log(Tag,
                    $"swallowed engine exception in {methodSig}: " +
                    $"{__exception.GetType().Name} -- {__exception.Message}" +
                    (count > 1 ? $" (seen {count} times)" : string.Empty));
        }
        catch { }
        return null;
    }

    // ---- helpers ------------------------------------------------------

}
