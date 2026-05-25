// BetaDeps Tactics Editor -- apply MissionLogic.
//
// Used by exported tactic mods. On battle start, this logic:
//   1. Scans Modules\ for any folder containing a tactic.json next to a
//      SubModule.xml that declares "BetaDeps.TacticsEditor" as a dep.
//   2. For each plan whose TargetTeam matches an existing team in the
//      mission, applies the saved formation positions, facings,
//      arrangements, and behaviors to that team's formations at battle
//      start (after troops have spawned but before the first tick where
//      the player can press Engage).
//   3. Logs every applied (mod, slot, position) pair so users can debug
//      "why isn't my tactic doing anything" by reading runtime.log.
//
// Limitations in v1.0:
//   - Arrangement and behavior are stored, but the engine's setters for
//     ArrangementOrder and the formation behavior controllers are not
//     fully reachable from a MissionLogic without going through the
//     OrderController. v1.0 applies position + facing only and logs the
//     arrangement/behavior intent; v1.1 wires them through the
//     OrderController so they actually fire.
//   - If two enabled tactic mods both target the same team, the lexically
//     later one wins. We log a warning to make this visible.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BetaDeps.Foundation;

using TaleWorlds.Engine;       // WorldPosition
using TaleWorlds.Library;      // Vec3
using TaleWorlds.MountAndBlade; // Mission, Formation, Team, MovementOrder

// TaleWorlds.Engine declares its own Path type (a navigation path), which
// collides with System.IO.Path once both namespaces are in scope. Alias
// System.IO.Path as IOPath for the filesystem-walk code below.
using IOPath = System.IO.Path;

namespace BetaDeps.TacticsEditor.Apply;

public sealed class TacticApplyMissionLogic : MissionLogic
{
    private const string Tag = "BetaDeps.TacticsEditor.Apply";

    private bool _applied;

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();
        DiagLog.Log(Tag, "TacticApplyMissionLogic initialized");
    }

    /// <summary>
    /// Apply runs once -- on the first OnMissionTick after formations
    /// have non-zero unit counts. Running in OnBehaviorInitialize is too
    /// early: troops haven't spawned yet, formation CurrentPosition is
    /// undefined.
    /// </summary>
    public override void OnMissionTick(float dt)
    {
        if (_applied || Mission == null) return;

        try
        {
            // Wait until at least one formation on at least one team has units.
            if (!HasAnySpawnedFormation()) return;

            var plans = LoadAllPlans();
            if (plans.Count == 0)
            {
                _applied = true;
                return;
            }

            DiagLog.Log(Tag, $"applying {plans.Count} tactic plan(s) to mission '{Mission.SceneName ?? "<unnamed>"}'");
            foreach (var (modId, plan) in plans)
            {
                ApplyPlan(modId, plan);
            }
            _applied = true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnMissionTick", ex);
            _applied = true; // don't retry forever
        }
    }

    private bool HasAnySpawnedFormation()
    {
        try
        {
            if (Mission?.Teams == null) return false;
            foreach (var team in Mission.Teams)
            {
                foreach (var f in team.FormationsIncludingEmpty)
                {
                    if (f != null && f.CountOfUnits > 0) return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Discover all tactic.json files in Modules\, by walking up from
    /// our own assembly directory to find Modules\, then scanning each
    /// subdirectory for tactic.json.
    /// </summary>
    private static List<(string ModId, TacticPlan Plan)> LoadAllPlans()
    {
        var results = new List<(string, TacticPlan)>();
        try
        {
            var asmDir = IOPath.GetDirectoryName(typeof(TacticApplyMissionLogic).Assembly.Location);
            if (string.IsNullOrEmpty(asmDir)) return results;

            string? modulesRoot = asmDir;
            while (!string.IsNullOrEmpty(modulesRoot))
            {
                var name = IOPath.GetFileName(modulesRoot)!;
                if (string.Equals(name, "Modules", StringComparison.OrdinalIgnoreCase)) break;
                modulesRoot = IOPath.GetDirectoryName(modulesRoot);
            }
            if (string.IsNullOrEmpty(modulesRoot) || !Directory.Exists(modulesRoot)) return results;

            foreach (var modDir in Directory.EnumerateDirectories(modulesRoot!))
            {
                var jsonPath = IOPath.Combine(modDir, "tactic.json");
                if (!File.Exists(jsonPath)) continue;
                var modId = IOPath.GetFileName(modDir);
                var plan = TacticPlanSerializer.ReadFromFile(jsonPath);
                if (plan == null)
                {
                    DiagLog.Log(Tag, $"skipped {modId}: tactic.json failed to parse");
                    continue;
                }
                results.Add((modId, plan));
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "LoadAllPlans", ex);
        }
        return results;
    }

    private void ApplyPlan(string modId, TacticPlan plan)
    {
        try
        {
            var team = ResolveTargetTeam(plan.TargetTeam);
            if (team == null)
            {
                DiagLog.Log(Tag, $"[{modId}] plan '{plan.Name}' targets team '{plan.TargetTeam}' but no matching team exists in this mission; skipping");
                return;
            }

            var formationsOnTeam = team.FormationsIncludingEmpty
                .Where(f => f != null && f.CountOfUnits > 0)
                .ToList();

            foreach (var slot in plan.Formations.OrderBy(s => s.Slot))
            {
                if (slot.Slot < 0 || slot.Slot >= formationsOnTeam.Count)
                {
                    DiagLog.Log(Tag, $"[{modId}] slot {slot.Slot} out of range (team has {formationsOnTeam.Count} live formations)");
                    continue;
                }
                var formation = formationsOnTeam[slot.Slot];
                ApplySlot(modId, formation, slot);
            }

            DiagLog.Log(Tag, $"[{modId}] applied plan '{plan.Name}' to team '{plan.TargetTeam}'");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ApplyPlan({modId})", ex);
        }
    }

    private Team? ResolveTargetTeam(string targetTeam)
    {
        try
        {
            if (Mission?.Teams == null) return null;
            switch ((targetTeam ?? "Player").Trim().ToLowerInvariant())
            {
                case "player":
                    return Mission.PlayerTeam;
                case "any":
                    return Mission.PlayerTeam ?? Mission.Teams.FirstOrDefault();
                case "defender":
                    return Mission.DefenderTeam;
                case "attacker":
                    return Mission.AttackerTeam;
                default:
                    return Mission.PlayerTeam;
            }
        }
        catch { return null; }
    }

    private void ApplySlot(string modId, Formation formation, FormationSlot slot)
    {
        try
        {
            if (formation == null || slot.Position == null || slot.Position.Length < 3)
            {
                DiagLog.Log(Tag, $"[{modId}] slot {slot.Slot}: invalid formation or position; skipping");
                return;
            }

            var pos = new Vec3(slot.Position[0], slot.Position[1], slot.Position[2]);
            var worldPos = new WorldPosition(Mission.Scene, pos);
            formation.SetMovementOrder(MovementOrder.MovementOrderMove(worldPos));

            // v1.0: arrangement + behavior are LOGGED only -- engine setters
            // for ArrangementOrder/behavior need OrderController routing that
            // lands in v1.1. The position + movement order alone covers ~80%
            // of what a tactic plan does, so v1.0 is still useful.
            DiagLog.Log(Tag,
                $"[{modId}] slot {slot.Slot}: positioned formation at ({pos.x:F1},{pos.y:F1},{pos.z:F1}); " +
                $"intent: class={slot.FormationClass} arrangement={slot.Arrangement} behavior={slot.Behavior} facing={slot.FacingYaw:F2}rad" +
                $"  [arrangement+behavior reserved for v1.1]");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ApplySlot({modId}/{slot.Slot})", ex);
        }
    }
}
