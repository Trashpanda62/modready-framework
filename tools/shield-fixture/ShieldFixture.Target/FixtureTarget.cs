// Synthetic fixture for the Phase 3 PatchShield gate (see plan 3.x
// verification). This assembly deliberately has a NON-BetaDeps name so
// PatchShield treats its method as a shieldable consumer-mod target.
//
// MIT, copyright 2026 Maxfield Management Group.

namespace ShieldFixture.Target;

public static class FixtureTarget
{
    /// <summary>The method both fixture patches target. Returns 1 so the
    /// driver can tell "original ran" (1) from "finalizer default" (0).</summary>
    public static int Compute() => 1;
}
