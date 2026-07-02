// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

using System;

using ModReady.Foundation;

using MCM.Internal;

using TaleWorlds.MountAndBlade;

namespace MCM;

/// <summary>
/// MBSubModuleBase entry registered in Modules\ModReady\SubModule.xml.
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
            // ROTSafeMode (two reverted attempts to neutralize the Realm of
            // Thrones type-load crash by mutating the module list) was removed
            // in Phase 5 (M13): both approaches crashed the game in testing, and
            // a real fix needs static analysis of TaleWorlds.MountAndBlade.Module
            // internals we can't reach from reference assemblies. ROT stays
            // marked incompatible in the Nexus description; the crash is
            // intermittent / load-order dependent (some sessions load all mods
            // including RealmOfThrones_v1 fine), not deterministic.
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
            // v1.x modder layer: build MCM pages from any consumer mod's
            // declarative mod.json BEFORE DiscoverAll, so the fluent settings
            // they produce are merged into the registry in the same pass.
            try
            {
                int n = ModReady.Framework.ModJsonLoader.DiscoverAndLoad();
                if (n > 0) DiagLog.Log(Tag, $"declarative mod.json: built {n} settings page(s)");
            }
            catch (Exception mjEx) { DiagLog.LogCaught(Tag, "ModJson DiscoverAndLoad", mjEx); }

            SettingsRegistry.DiscoverAll();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot/Discover", ex);
        }

        // Slice 1 of "every mod on one page" — moved to OptionsVMMixin.RebuildModList
        // because OnBeforeInitialModuleScreenSetAsRoot only fires once at game-load
        // (a ~1.6 second window) and the user has to drop the flag file BEFORE
        // clicking PLAY in BLSE for it to be visible there. RebuildModList fires
        // every time the user opens Mod Configuration in-game, which is the
        // natural moment to drop the flag.

        // v0.6: Auto-disable detection. Scan launcher_data.xml vs the
        // engine's actually-loaded SubModules and report any mods the
        // user has enabled that didn't construct (Banner Kings on a
        // newer game version is the canonical case). Findings are
        // written to runtime.log and to Modules\ModReady\incompatible-mods.log.
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

        // v0.8: auto-run McmSelfTest if `modready-run-selftest.flag` is
        // present in Modules\ModReady\. Replaces the "Run Self-Test" UI
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

    // Phase 2.4 / finding H5 (2026-06-10 review): per-save and per-campaign
    // settings are scoped by campaign id on disk. These two hooks reset the
    // scoped singletons (and fluent per-scope instances) at the campaign
    // boundary so the next access reloads under the NEW campaign's path --
    // without this, a singleton created in campaign A would keep serving
    // A's values inside campaign B for the rest of the session.
    public override void OnGameInitializationFinished(TaleWorlds.Core.Game game)
    {
        base.OnGameInitializationFinished(game);
        try { ScopedSettingsTracker.ResetAll("game initialization finished"); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnGameInitializationFinished/ScopedReset", ex); }
    }

    /// <summary>
    /// Save-compat (v1.0.1, iOrNoTi report): register the per-save settings
    /// bridge with every campaign. It keeps upstream MCM's "_settings"
    /// payload alive across ModReady saves and syncs it with the per-save
    /// JSON store in both directions. Its nested definer also guarantees the
    /// Dictionary&lt;string,string&gt; container definition exists -- without
    /// it, deserializing any save created with upstream MCM v5 fails.
    /// See docs/SAVE-COMPAT-BUTR-INTEROP.md.
    /// </summary>
    protected override void OnGameStart(TaleWorlds.Core.Game game, TaleWorlds.Core.IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        try
        {
            if (game.GameType is TaleWorlds.CampaignSystem.Campaign
                && gameStarterObject is TaleWorlds.CampaignSystem.CampaignGameStarter starter)
            {
                starter.AddBehavior(new PerSaveCampaignBehavior());
                DiagLog.Log(Tag, "OnGameStart: PerSaveCampaignBehavior save-compat bridge registered");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnGameStart/PerSaveCampaignBehavior", ex);
        }
    }

    public override void OnGameEnd(TaleWorlds.Core.Game game)
    {
        base.OnGameEnd(game);
        try { ScopedSettingsTracker.ResetAll("game end"); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnGameEnd/ScopedReset", ex); }
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        // Headless visual verification: if a capture flag is present, this drives
        // Options -> Mod Config -> screenshot -> quit. No-op (cheap flag check)
        // otherwise, so it's safe to run every frame.
        try { ModConfigCapture.OnTick(dt); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnApplicationTick/ModConfigCapture", ex); }

        // v0.9.0 Slice 3: narrow the vanilla right description panel while our tab is active.
        try { ModConfigCapture.MaintainRightPanel(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnApplicationTick/MaintainRightPanel", ex); }
    }

    private const string RunSelfTestFlagName = "modready-run-selftest.flag";

    private static void TryAutoRunSelfTestFromFlag()
    {
        var rtPath = RuntimeLog.Path;
        var dir = System.IO.Path.GetDirectoryName(rtPath);
        if (string.IsNullOrEmpty(dir)) return;

        var flagPath = System.IO.Path.Combine(dir!, RunSelfTestFlagName);
        if (!System.IO.File.Exists(flagPath)) return;

        DiagLog.Log("MCMSubModule",
            $"modready-run-selftest.flag detected at {flagPath} — auto-running McmSelfTest.RunAll()");

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
