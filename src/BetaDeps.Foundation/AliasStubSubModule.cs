// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// AliasStubSubModule -- a near-no-op MBSubModuleBase used by the four alias
// modules (Modules\Bannerlord.Harmony, .UIExtenderEx, .ButterLib,
// .MBOptionScreen) so each alias has a real <SubModule> entry that the
// launcher (LauncherEx, BLSE) recognizes. Without a real SubModule entry
// the launcher silently hides the alias from the Mods tab, breaking
// dependency resolution for consumer mods.
//
// One thing this class DOES do: install the AssemblyVersionShim
// AssemblyResolve hook on first invocation. Whichever module (BetaDeps or
// an alias) the launcher constructs first will install the hook, so any
// consumer mod that later loads a bundled MCMv5 / 0Harmony / etc. copy
// gets redirected to our already-loaded version. AssemblyVersionShim is
// idempotent (Install() is a no-op after the first call).

using System;

using TaleWorlds.MountAndBlade;

namespace BetaDeps.Foundation;

public class AliasStubSubModule : MBSubModuleBase
{
    private const string Tag = "AliasStub";

    // Idempotent gate -- runs once across all AliasStub instances and across
    // BetaDepsHarmonySubModule's own constructor. We do this in the instance
    // constructor (not OnSubModuleLoad) because OnSubModuleLoad doesn't fire
    // if another module's SubModule.ctor throws during the construction phase
    // and Bannerlord aborts the sequence -- which is exactly what Banner Kings
    // does on a newer game version. Constructors run during construction, so
    // this hook lands BEFORE BK's class is even instantiated (BetaDeps's
    // aliases load first per SubModule.xml ordering).
    private static int _earlyDetectionRan;

    public AliasStubSubModule()
    {
        if (System.Threading.Interlocked.Exchange(ref _earlyDetectionRan, 1) == 0)
        {
            try
            {
                IncompatibleModDetector.RunEarlyPhase();
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "ctor/IncompatEarly", ex); } catch { }
            }
        }
    }

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            AssemblyVersionShim.Install();
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "AssemblyVersionShim.Install", ex); } catch { }
        }
        try
        {
            DiagLog.Log(Tag, $"alias stub loaded: {this.GetType().Assembly.GetName().Name}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex);
        }
    }
}
