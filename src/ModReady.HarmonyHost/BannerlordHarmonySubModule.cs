// ModReady v0.8 step 1: real Bannerlord.Harmony module host.
//
// MIT, copyright 2026 Maxfield Management Group.
//
// PURPOSE
//   This SubModule sits inside Modules\Bannerlord.Harmony — promoted from an
//   alias-stub (which previously pointed at ModReady.Foundation.AliasStubSubModule)
//   to a real, self-contained module that ships its own copy of the Harmony
//   binary fan (0Harmony.dll + Mono.Cecil.dll + the five MonoMod.*.dll files).
//
//   By the time the engine resolves Modules\ModReady\SubModule.xml, this
//   module has ALREADY loaded — ModReady's SubModule.xml carries a
//   <DependedModule Id="Bannerlord.Harmony" /> entry that pins the load order.
//   That means ModReady's own SubModule classes JIT against the SAME Harmony
//   AppDomain copy that consumer mods will pick up (because consumer mods
//   also DependedModule us via Bannerlord.Harmony, and BLSE's preflight is
//   satisfied by a real module sitting on disk).
//
// WHY MINIMAL
//   Zero shim installation happens here. PatchShield, SaveShield, MCM stubs,
//   ButterLib DI, UIExtenderEx — all of that stays in Modules\ModReady\.
//   This host's only job is "be a real module folder that loads early so the
//   Harmony stack is in the AppDomain before anything that depends on it."
//
//   The OnSubModuleLoad override does one thing: log a banner line via
//   DiagLog so we can confirm in runtime.log that the early-load actually
//   fired (and at what timestamp relative to ModReady's own OnSubModuleLoad).
//   Any exception is swallowed and logged — a logging failure in the host
//   must not block the engine from continuing to load ModReady.

using TaleWorlds.MountAndBlade;

using ModReady.Foundation;

namespace Bannerlord.Harmony;

public sealed class BannerlordHarmonySubModule : MBSubModuleBase
{
    private const string Tag = "Bannerlord.Harmony.Host";

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        try
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();

            // v1.0.6: Continue save-load loop fix. MUST live in this host --
            // the ModReady umbrella module is optional (deps-standalone users
            // disable it) but every configuration loads Bannerlord.Harmony.
            // This hook runs after all module assemblies (incl. SandBox.View)
            // are loaded, which Install() needs to resolve its targets.
            ContinueLoadGuard.Install();
        }
        catch (System.Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "OnBeforeInitialModuleScreenSetAsRoot", ex); }
            catch { /* nothing we can do */ }
        }
    }

    protected override void OnSubModuleLoad()
    {
        try
        {
            base.OnSubModuleLoad();
            DiagLog.Log(Tag,
                "Bannerlord.Harmony module (v0.8 step 1 host) loaded -- " +
                "Harmony binary fan is now in the AppDomain before ModReady's SubModule constructs.");

            // v1.0.6: blocked-DLL fix. Writes loadFromRemoteSources into the
            // game/launcher exe.config so web-marked mod DLLs load from the
            // next launch onward. Idempotent + fully best-effort inside.
            RemoteSourcesFix.Apply();
        }
        catch (System.Exception ex)
        {
            // Best-effort: try to log the failure, but if DiagLog itself is
            // unavailable we have no fallback that's safer than swallowing.
            // Letting an exception escape OnSubModuleLoad would propagate to
            // the engine and abort the module chain, which is exactly the
            // failure mode v0.8 step 1 is meant to remove.
            try { DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex); }
            catch { /* nothing we can do */ }
        }
    }
}
