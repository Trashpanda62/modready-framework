// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

using System;

using BetaDeps.Foundation;

using MCM.Internal;

using TaleWorlds.MountAndBlade;

namespace MCM;

/// <summary>
/// MBSubModuleBase entry registered in Modules\BetaDeps\SubModule.xml.
/// </summary>
public class MCMSubModule : MBSubModuleBase
{
    private const string Tag = "MCMSubModule";

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            var asmName = typeof(MCMSubModule).Assembly.GetName();
            DiagLog.Log(Tag, $"OnSubModuleLoad: {asmName.Name} v{asmName.Version}. Settings declaration surface live; UI tab pending (task #22).");
            // Install the Harmony patch that saves all MCM settings when Done is clicked.
            SaveOnDonePatch.Install();
            // v1.0: install the Q/E tab-switch guard so typing letters in the
            // inline Mod Config search field doesn't rotate away from our tab.
            TabSwitchGuardPatch.Install();
            // v0.5.5 REVERTED (second attempt): ROTSafeMode.Apply() — even the
            // metadata-only list-mutation approach crashed the game in testing.
            // The Module.CurrentModule property getter or the SubModules
            // reflection apparently has side effects at the point BetaDeps.MCM
            // runs. Two failed approaches teach us this needs a static analysis
            // of TaleWorlds.MountAndBlade.Module structure (which requires
            // reference-assembly access we don't have right now). The
            // ROTSafeMode.cs source is kept for the v0.6 attempt.
            //
            // For v0.5.4: ROT remains marked incompatible in the Nexus
            // description. Useful data point from testing: ROT *does* load
            // successfully in some sessions (the 01:41 archive shows a full
            // run with all 24 mods including RealmOfThrones_v1 saving
            // settings) — so the crash is intermittent / load-order dependent,
            // not deterministic.
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex);
        }
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        try
        {
            SettingsRegistry.DiscoverAll();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot/Discover", ex);
        }
    }
}
