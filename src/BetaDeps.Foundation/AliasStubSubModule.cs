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
