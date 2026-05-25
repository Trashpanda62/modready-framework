// BetaDeps Tactics Editor -- SubModule entry point.
//
// Activated as a BetaDeps consumer-mod. On every battle mission start:
//   - Attaches TacticsEditorMissionLogic (F11 to enter edit mode, see
//     Editor\TacticsEditorMissionLogic.cs for the full key map).
//   - Attaches TacticApplyMissionLogic (auto-applies any tactic.json
//     plans found in Modules\<exported-tactic>\ to matching teams).
//
// The two logics are independent. A user who only wants to APPLY a
// downloaded tactic doesn't need to know the editor exists. A user
// who only wants to EDIT can choose not to install any exported
// tactic mods. Both happen via the same hook.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

using BetaDeps.Foundation;
using BetaDeps.TacticsEditor.Apply;
using BetaDeps.TacticsEditor.Editor;

using TaleWorlds.MountAndBlade;

namespace BetaDeps.TacticsEditor;

public sealed class TacticsEditorSubModule : MBSubModuleBase
{
    private const string Tag = "BetaDeps.TacticsEditor";

    protected override void OnSubModuleLoad()
    {
        try
        {
            base.OnSubModuleLoad();
            DiagLog.Log(Tag, "OnSubModuleLoad: tactics editor + apply logic registered for battle missions");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex);
        }
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        try
        {
            base.OnMissionBehaviorInitialize(mission);
            if (mission == null || !IsBattleMission(mission)) return;

            mission.AddMissionBehavior(new TacticsEditorMissionLogic());
            mission.AddMissionBehavior(new TacticApplyMissionLogic());

            DiagLog.Log(Tag,
                $"attached editor + apply to mission '{mission.SceneName ?? "<unnamed>"}'; press F11 to edit");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnMissionBehaviorInitialize", ex);
        }
    }

    private static bool IsBattleMission(Mission mission)
    {
        try
        {
            return mission.Teams != null && mission.Teams.Count >= 2;
        }
        catch
        {
            return false;
        }
    }
}
