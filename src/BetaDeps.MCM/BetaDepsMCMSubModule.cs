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

        // v0.6: Auto-disable detection. Scan launcher_data.xml vs the
        // engine's actually-loaded SubModules and report any mods the
        // user has enabled that didn't construct (Banner Kings on a
        // newer game version is the canonical case). Findings are
        // written to runtime.log and to Modules\BetaDeps\incompatible-mods.log.
        try
        {
            IncompatibleModDetector.ScanAndReport();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot/IncompatScan", ex);
        }

        // v0.6.1: Mark this session's boot as successful. Writes the
        // current loaded-mod list to last-good-modlist.txt and deletes
        // the session-launching marker. Next launch uses this baseline
        // to identify mods that failed to load, so any future game
        // update that breaks a mod gets auto-handled without us having
        // to maintain a hardcoded incompatibility list.
        try
        {
            IncompatibleModDetector.MarkBootSuccessful();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot/MarkBootSuccessful", ex);
        }

        // v0.8: auto-run McmSelfTest if `betadeps-run-selftest.flag` is
        // present in Modules\BetaDeps\. Replaces the "Run Self-Test" UI
        // button that was removed in the v0.8 UI cleanup. Use cases:
        //   - Quick-Test dev loop drops the flag before each launch so
        //     selftest.log is always fresh when the script copies logs
        //     back to C:\dev\bannerlord.
        //   - Modders writing CI / automated test harnesses can drop the
        //     flag and parse selftest.json after boot.
        //   - End users will rarely hit this path — Report-a-Bug button
        //     in Mod Config triggers RunSelfTestQuiet() directly without
        //     needing the flag.
        // The flag deletes itself after a successful run so repeated
        // launches don't keep re-running.
        try
        {
            TryAutoRunSelfTestFromFlag();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot/AutoRunSelfTest", ex);
        }
    }

    private const string RunSelfTestFlagName = "betadeps-run-selftest.flag";

    private static void TryAutoRunSelfTestFromFlag()
    {
        var rtPath = RuntimeLog.Path;
        var dir = System.IO.Path.GetDirectoryName(rtPath);
        if (string.IsNullOrEmpty(dir)) return;

        var flagPath = System.IO.Path.Combine(dir!, RunSelfTestFlagName);
        if (!System.IO.File.Exists(flagPath)) return;

        DiagLog.Log("MCMSubModule",
            $"betadeps-run-selftest.flag detected at {flagPath} — auto-running McmSelfTest.RunAll()");

        try
        {
            McmSelfTest.RunAll();
            DiagLog.Log("MCMSubModule", "auto-run McmSelfTest.RunAll() complete; deleting flag");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("MCMSubModule", "auto-run McmSelfTest.RunAll()", ex);
        }
        finally
        {
            // Delete the flag whether the test succeeded or not, so a
            // crashing self-test doesn't put the user in an infinite
            // boot-then-crash loop.
            try { System.IO.File.Delete(flagPath); }
            catch (Exception ex) { DiagLog.LogCaught("MCMSubModule", "delete run-selftest flag", ex); }
        }
    }
}
