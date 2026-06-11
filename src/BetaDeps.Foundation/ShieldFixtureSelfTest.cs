// BetaDeps.Foundation -- ShieldFixtureSelfTest
//
// Phase 3 verification gate: drive the synthetic broken-mod fixture and
// assert PatchShield's culprit-targeted unpatching (H1/H4 rework).
//
// The fixture is three deliberately non-BetaDeps assemblies built from
// tools\shield-fixture\:
//   ShieldFixture.Target   -- FixtureTarget.Compute() => 1 (the patched method)
//   ShieldFixture.Innocent -- harmless counting prefix on Compute
//   ShieldFixture.Broken   -- prefix that always throws MissingMethodException
//
// Expected behavior: call 1 hits the broken prefix; PatchShield's finalizer
// swallows the throw (culprit attributed to ShieldFixture.Broken via the
// exception stack) and unpatches ONLY that owner. Call 2 then runs the
// original cleanly with the innocent prefix still attached.
//
// Dev-only: runs ONLY when shieldfixture-path.flag exists next to
// runtime.log; the flag's content is the absolute path of the fixture bin
// folder. The flag and the fixture DLLs are never shipped.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;
using System.Reflection;
using System.Threading;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class ShieldFixtureSelfTest
{
    private const string Tag = "ShieldFixture";
    private const string FlagName = "shieldfixture-path.flag";
    private static int _ran;

    /// <summary>Run the gate once per session, and only when the dev flag
    /// is present. Never throws.</summary>
    public static void RunIfRequested()
    {
        if (Interlocked.CompareExchange(ref _ran, 1, 0) != 0) return;
        try
        {
            var dir = Path.GetDirectoryName(RuntimeLog.Path);
            if (string.IsNullOrEmpty(dir)) return;
            var flag = Path.Combine(dir!, FlagName);
            if (!File.Exists(flag)) return;
            var fixtureDir = File.ReadAllText(flag).Trim();
            if (string.IsNullOrEmpty(fixtureDir) || !Directory.Exists(fixtureDir))
            {
                DiagLog.Log(Tag, $"{FlagName} present but fixture dir '{fixtureDir}' not found; skipping gate");
                return;
            }
            Run(fixtureDir);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "RunIfRequested", ex);
        }
    }

    private static void Run(string dir)
    {
        DiagLog.Log(Tag, $"==== PatchShield synthetic gate (fixtures from {dir}) ====");

        var targetAsm = Assembly.LoadFrom(Path.Combine(dir, "ShieldFixture.Target.dll"));
        var innocentAsm = Assembly.LoadFrom(Path.Combine(dir, "ShieldFixture.Innocent.dll"));
        var brokenAsm = Assembly.LoadFrom(Path.Combine(dir, "ShieldFixture.Broken.dll"));

        var innocentType = innocentAsm.GetType("ShieldFixture.Innocent.InnocentPatch", throwOnError: true)!;
        var brokenType = brokenAsm.GetType("ShieldFixture.Broken.BrokenPatch", throwOnError: true)!;
        innocentType.GetMethod("Install")!.Invoke(null, null);
        brokenType.GetMethod("Install")!.Invoke(null, null);

        // Shield the freshly patched fixture method (idempotent pass).
        PatchShield.Install();

        var compute = targetAsm.GetType("ShieldFixture.Target.FixtureTarget", throwOnError: true)!
            .GetMethod("Compute")!;
        var hitField = innocentType.GetField("HitCount")!;

        int failures = 0;
        void Check(string name, bool ok, string detail)
        {
            DiagLog.Log(Tag, $"  [{(ok ? "PASS" : "FAIL")}] {name} -- {detail}");
            if (!ok) failures++;
        }

        // Call 1: broken prefix throws MissingMethodException. PatchShield
        // must swallow (culprit: ShieldFixture.Broken) and unpatch that
        // owner; the int result defaults to 0 because the original never ran.
        object? r1 = null;
        Exception? thrown = null;
        try { r1 = compute.Invoke(null, null); }
        catch (Exception ex) { thrown = ex; }
        Check("swallow", thrown == null,
            thrown == null ? $"call 1 completed (result={r1})" : $"call 1 threw {thrown.GetBaseException().GetType().Name}");

        // Call 2: broken owner gone -> original runs -> 1; innocent prefix
        // must still increment.
        int hitsBefore = (int)hitField.GetValue(null)!;
        object? r2 = null;
        thrown = null;
        try { r2 = compute.Invoke(null, null); }
        catch (Exception ex) { thrown = ex; }
        int hitsAfter = (int)hitField.GetValue(null)!;
        Check("recover", thrown == null && r2 is int result && result == 1,
            $"call 2 result={(thrown != null ? thrown.GetBaseException().GetType().Name : r2)} (expected 1)");
        Check("innocent-survives", hitsAfter == hitsBefore + 1,
            $"innocent prefix hits {hitsBefore} -> {hitsAfter} (expected +1)");

        // Patch-chain assertions: culprit owner fully gone, innocent intact.
        var info = Harmony.GetPatchInfo(compute);
        bool brokenGone = true, innocentPresent = false;
        if (info != null)
        {
            foreach (var p in info.Prefixes)
            {
                if (p?.owner == "ShieldFixture.Broken") brokenGone = false;
                if (p?.owner == "ShieldFixture.Innocent") innocentPresent = true;
            }
        }
        Check("culprit-unpatched", brokenGone, "no ShieldFixture.Broken prefixes remain on the method");
        Check("innocent-still-patched", innocentPresent, "ShieldFixture.Innocent prefix still in the chain");

        DiagLog.Log(Tag, failures == 0
            ? "==== PatchShield synthetic gate: ALL PASS ===="
            : $"==== PatchShield synthetic gate: {failures} FAILURE(S) ====");
    }
}
