// Synthetic fixture for the Phase 3 PatchShield gate: the INNOCENT mod.
// Patches FixtureTarget.Compute with a harmless counting prefix. The gate
// asserts this prefix SURVIVES after the broken sibling's exception gets
// its owner unpatched.
//
// MIT, copyright 2026 Maxfield Management Group.

using HarmonyLib;

namespace ShieldFixture.Innocent;

public static class InnocentPatch
{
    public const string HarmonyId = "ShieldFixture.Innocent";

    /// <summary>Incremented by the prefix on every FixtureTarget.Compute
    /// call. The driver reads this via reflection to prove the innocent
    /// prefix kept running after the broken owner was removed.</summary>
    public static int HitCount;

    public static void Install()
    {
        var harmony = new Harmony(HarmonyId);
        var target = AccessTools.Method(typeof(Target.FixtureTarget), nameof(Target.FixtureTarget.Compute));
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(InnocentPatch), nameof(Prefix)));
    }

    public static void Prefix()
    {
        HitCount++;
    }
}
