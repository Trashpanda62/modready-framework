// Synthetic fixture for the Phase 3 PatchShield gate: the BROKEN mod.
// Patches FixtureTarget.Compute with a prefix that always throws
// MissingMethodException -- the canonical stale-mod failure PatchShield
// exists to contain. The gate asserts the shield swallows the throw,
// attributes THIS assembly as the culprit, and unpatches only this owner.
//
// MIT, copyright 2026 Maxfield Management Group.

using System;

using HarmonyLib;

namespace ShieldFixture.Broken;

public static class BrokenPatch
{
    public const string HarmonyId = "ShieldFixture.Broken";

    public static void Install()
    {
        var harmony = new Harmony(HarmonyId);
        var target = AccessTools.Method(typeof(Target.FixtureTarget), nameof(Target.FixtureTarget.Compute));
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(BrokenPatch), nameof(Prefix)));
    }

    public static void Prefix()
    {
        throw new MissingMethodException("ShieldFixture synthetic stale-mod failure (deliberate)");
    }
}
