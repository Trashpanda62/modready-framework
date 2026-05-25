// BetaDeps.Harmony -- SubModule entry point
//
// The MBSubModuleBase that Bannerlord's launcher instantiates when
// Modules\BetaDeps\SubModule.xml references this assembly.
//
// Responsibilities (only these -- no gameplay logic):
//   1. Install the AssemblyVersionShim AssemblyResolve handler so any
//      later-loaded consumer-mod copy of MCMv5/0Harmony/UIExtenderEx/
//      ButterLib/Newtonsoft.Json redirects to our already-loaded copy.
//      Idempotent -- if an alias module ran first, this is a no-op.
//   2. Open the Harmony runtime gate (loads 0Harmony.dll).
//   3. Apply beta sigsafe patches (no-op on public branch).
//   4. Log a startup line so users can confirm the module loaded.
//
// All gameplay features belong in consumer mods (CREST, etc.), not
// here. This is purely the foundation.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

using BetaDeps.Foundation;

using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace BetaDeps.Harmony;

public class BetaDepsHarmonySubModule : MBSubModuleBase
{
    private const string Tag = "BetaDeps.Harmony";
    private static bool _unhandledHookInstalled;

    // [ThreadStatic] guard so a recursive failure inside our own log writer
    // (e.g. RuntimeLog.Write itself throws TypeLoadException somehow) doesn't
    // produce an infinite recursion inside FirstChanceException.
    [System.ThreadStatic] private static bool _inFirstChance;

    // Shared one-shot gate for IncompatibleModDetector.RunEarlyPhase. Same
    // mechanism as AliasStubSubModule -- whichever runs first wins, the
    // other is a no-op. Constructor (not OnSubModuleLoad) so this hook
    // runs DURING the SubModule construction phase, before any other mod's
    // class has been instantiated. That's the only phase that survives if
    // a later mod's ctor throws and Bannerlord aborts the sequence.
    private static int _ctorEarlyDetectionRan;

    public BetaDepsHarmonySubModule()
    {
        if (System.Threading.Interlocked.Exchange(ref _ctorEarlyDetectionRan, 1) == 0)
        {
            // v0.7 hotfix: install the AssemblyResolve shim FIRST -- before
            // RunEarlyPhase or anything else. The engine constructs every
            // SubModule class during the same phase (before any OnSubModuleLoad
            // fires), so consumer-mod constructors that touch ButterLib /
            // UIExtenderEx / Harmony assemblies need our redirect handler in
            // place during their own ctor.
            try
            {
                AssemblyVersionShim.Install();
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "ctor/AssemblyShim", ex); } catch { }
            }

            // NOTE: AssemblyLoaderDependencyShim was an earlier approach that
            // proved unnecessary -- LoadFrom returns Success for affected
            // consumer mods; the CriticalError verdict actually comes from
            // CollectModuleAssemblyTypes (see CollectAssemblyTypesShim).
            // Disabled here to avoid spurious Postfix work on every LoadFrom
            // call. Source file kept in repo as documentation of the
            // investigation path.

            // v0.7 hotfix #3: install lenient-types shim on
            // Module.CollectModuleAssemblyTypes. The real CDE-class failure
            // is here, not in LoadFrom: assembly.GetTypes() throws because
            // some types in the consumer assembly reference our impersonated
            // ButterLib/UIExt/etc members that may not exist on our build.
            // The lenient version pulls partial types from the exception.
            try
            {
                CollectAssemblyTypesShim.Install();
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "ctor/CollectAssemblyTypesShim", ex); } catch { }
            }

            try
            {
                IncompatibleModDetector.RunEarlyPhase();
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "ctor/IncompatEarly", ex); } catch { }
            }

            // v0.7: install the Harmony pre-construction guard. Must happen
            // from BetaDeps's constructor (not OnSubModuleLoad) because by the
            // time OnSubModuleLoad fires, other mods' constructors have
            // already run -- and a broken constructor is exactly what we're
            // trying to block. BetaDeps loads before every consumer mod by
            // virtue of its ModulesToLoadAfterThis list, so this is the first
            // moment Lib.Harmony is reachable AND no consumer mod has been
            // constructed yet.
            try
            {
                SubModuleConstructionGuard.Install();
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "ctor/SubModuleGuard", ex); } catch { }
            }
        }
    }

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        // Bootstrap alias folders (Bannerlord.Harmony / UIExtenderEx / ButterLib /
        // MBOptionScreen). The public zip ships them nested under
        // Modules\BetaDeps\aliases\ so end users see a single module on disk;
        // on first launch we materialise them as siblings of BetaDeps so the
        // launcher sees them and consumer mods that DependedModule on those
        // IDs can find them. Idempotent — skips folders that already exist.
        try { BootstrapAliasFolders(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "BootstrapAliasFolders", ex); }

        // Install AssemblyResolve redirect first so any subsequent assembly
        // load triggered downstream resolves to our already-loaded copies.
        try
        {
            AssemblyVersionShim.Install();
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "AssemblyVersionShim.Install", ex); } catch { }
        }

        // Install AppDomain unhandled-exception logger. BEW finalizers only
        // catch crashes that occur during Tick; consumer-mod SubModule
        // constructors run BEFORE the first Tick, so when one of them throws
        // BEW is bypassed and the runtime.log shows nothing useful. This
        // handler runs on EVERY thread's unhandled exception, regardless of
        // call-stack origin, so we always get a full stack in runtime.log.
        if (!_unhandledHookInstalled)
        {
            _unhandledHookInstalled = true;
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                {
                    try
                    {
                        var ex = args.ExceptionObject as Exception;
                        if (ex != null)
                            DiagLog.LogCaught("UnhandledException",
                                $"AppDomain.UnhandledException (terminating={args.IsTerminating})", ex);
                        else
                            DiagLog.Log("UnhandledException",
                                $"AppDomain.UnhandledException with non-Exception object: {args.ExceptionObject}");
                    }
                    catch { }
                };
                // First-chance fires for EVERY managed exception (even caught ones).
                // We only log if the exception escapes BetaDeps boundaries; otherwise
                // the log would be unreadable. Use a thread-local recursion guard.
                AppDomain.CurrentDomain.FirstChanceException += (sender, args) =>
                {
                    if (_inFirstChance) return;
                    _inFirstChance = true;
                    try
                    {
                        // Narrow filter: log only exceptions that signal an API
                        // surface mismatch between a consumer mod and our stubs.
                        // These are the truly-diagnostic ones; broader categories
                        // (NRE, InvalidOperation etc.) are routinely caught by
                        // consumer code as control flow and would drown the log.
                        // If you need to diagnose a new silent hang, widen this
                        // filter temporarily.
                        var ex = args.Exception;
                        if (ex is TypeLoadException
                            || ex is MissingMethodException
                            || ex is MissingFieldException
                            || ex is BadImageFormatException)
                        {
                            DiagLog.LogCaught("FirstChanceException", ex.GetType().Name, ex);
                        }
                    }
                    catch { }
                    finally { _inFirstChance = false; }
                };
                DiagLog.Log(Tag, "AppDomain.UnhandledException + FirstChanceException handlers installed");
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "InstallUnhandledHook", ex); } catch { }
            }
        }

        try
        {
            var asmName = typeof(BetaDepsHarmonySubModule).Assembly.GetName();
            DiagLog.Log(Tag, $"OnSubModuleLoad: {asmName.Name} v{asmName.Version} on branch {VersionProbe.Branch} (v{VersionProbe.Major}.{VersionProbe.Minor})");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad/banner", ex);
        }

        // v0.6: Preemptive disable of known-incompatible mods. Runs as the
        // very first BetaDeps action (after alias bootstrap + version shim)
        // so the SubModule.xml rename lands on disk even if a later mod
        // CTDs the game during load. Effect takes hold on the NEXT launch.
        // The current launch may still crash if the user just enabled the
        // bad mod -- but the next launch will be clean.
        try
        {
            IncompatibleModDetector.RunEarlyPhase();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad/IncompatEarly", ex);
        }

        // Open the Harmony runtime gate. If this fails, downstream
        // patches won't bind, but we don't take the game down.
        try
        {
            if (!HarmonyRuntimeGate.Open())
            {
                DiagLog.Log(Tag, "HarmonyRuntimeGate.Open returned false. Sigsafe patches will be skipped.");
                return;
            }
            HarmonyRuntimeGate.SnapshotPatches();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad/gate", ex);
            return;
        }

        // Apply the beta-branch sigsafe defensive patches.
        try
        {
            BetaSigSafePatches.Apply();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad/sigsafe", ex);
        }
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        TryInstallPatchShield("OnBeforeInitialModuleScreenSetAsRoot");
    }

    // Re-install the shield at every lifecycle point a consumer mod could
    // plausibly call Harmony.PatchAll. AIInfluence (and similar mods) defer
    // their patches past OnSubModuleLoad — they apply them during game
    // initialization, so a single install at OnBeforeInitialModuleScreenSetAsRoot
    // misses those patches. PatchShield.Install is idempotent and tracks
    // already-shielded methods, so re-running it just adds the new ones.

    public override void OnGameInitializationFinished(Game game)
    {
        base.OnGameInitializationFinished(game);
        TryInstallPatchShield("OnGameInitializationFinished");
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);
        TryInstallPatchShield("OnGameStart");
    }

    public override void OnAfterGameInitializationFinished(Game game, object starterObject)
    {
        base.OnAfterGameInitializationFinished(game, starterObject);
        TryInstallPatchShield("OnAfterGameInitializationFinished");
    }

    public override void OnNewGameCreated(Game game, object initializerObject)
    {
        base.OnNewGameCreated(game, initializerObject);
        TryInstallPatchShield("OnNewGameCreated");
    }

    private static void TryInstallPatchShield(string from)
    {
        // v0.7: install PatchShield (idempotent — only shields newly-patched
        // methods on each pass). Catches MissingMethodException /
        // MissingFieldException / TypeLoadException from consumer-mod
        // prefixes built against older TaleWorlds APIs (AIInfluence +
        // NavalDLC is the canonical case).
        try
        {
            PatchShield.Install();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"{from}/PatchShield", ex);
        }

        // v0.7.3: install SaveShield (also idempotent). Wraps the save-load
        // entry points so duplicate-key Dictionary.Add crashes during save
        // deserialization get logged with the full stack + SaveGameFileInfo
        // metadata before being rethrown. Lets users (and us) identify the
        // colliding mod from runtime.log instead of bisecting by hand.
        try
        {
            SaveShield.Install();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"{from}/SaveShield", ex);
        }
    }

    /// <summary>
    /// One-time bootstrap. Looks at our own assembly location
    /// (Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Harmony.dll), walks
    /// up to the Modules\ root, and for each known alias folder name (the four
    /// modules consumer mods declare as DependedModule), copies
    /// `Modules\BetaDeps\aliases\&lt;Name&gt;\` into `Modules\&lt;Name&gt;\` if the
    /// destination doesn't exist. Skips silently for folders that already exist
    /// (re-runs are safe and free).
    ///
    /// On the FIRST launch after a fresh install, the alias folders aren't on
    /// disk yet — consumer mods that DependedModule on Bannerlord.Harmony /
    /// UIExtenderEx / ButterLib / MBOptionScreen won't load, and BLSE may
    /// gate the launcher. We pop a TaleWorlds.Library InformationManager
    /// message asking the user to restart, then exit normally; on the SECOND
    /// launch the aliases are present and everything works.
    /// </summary>
    private static void BootstrapAliasFolders()
    {
        var ownPath = typeof(BetaDepsHarmonySubModule).Assembly.Location;
        if (string.IsNullOrEmpty(ownPath)) return;

        // ownPath = ...\Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Harmony.dll
        var binDir         = System.IO.Path.GetDirectoryName(ownPath);          // Win64_Shipping_Client
        var betaDepsBin    = System.IO.Path.GetDirectoryName(binDir);            // bin
        var betaDepsModule = System.IO.Path.GetDirectoryName(betaDepsBin);       // BetaDeps
        var modulesRoot    = System.IO.Path.GetDirectoryName(betaDepsModule);    // Modules
        if (string.IsNullOrEmpty(betaDepsModule) || string.IsNullOrEmpty(modulesRoot)) return;

        var aliasStaging = System.IO.Path.Combine(betaDepsModule!, "aliases");
        if (!System.IO.Directory.Exists(aliasStaging))
        {
            // Not a single-folder install; nothing to bootstrap. Local-dev
            // builds via Ralph-Loop already deploy alias folders as siblings.
            return;
        }

        int created = 0;
        foreach (var sub in System.IO.Directory.GetDirectories(aliasStaging))
        {
            var name = System.IO.Path.GetFileName(sub);
            var dest = System.IO.Path.Combine(modulesRoot!, name);
            if (System.IO.Directory.Exists(dest))
                continue; // already there from a previous launch
            try
            {
                CopyDirectoryRecursive(sub, dest);
                created++;
                DiagLog.Log(Tag, $"BootstrapAliasFolders: created Modules\\{name}");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"BootstrapAliasFolders/{name}", ex);
            }
        }

        if (created > 0)
        {
            DiagLog.Log(Tag, $"BootstrapAliasFolders: materialised {created} alias folder(s). " +
                              "Consumer mods that depend on Bannerlord.Harmony / UIExtenderEx / ButterLib / MBOptionScreen " +
                              "will be visible to the launcher on the next start.");
        }
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        System.IO.Directory.CreateDirectory(dst);
        foreach (var f in System.IO.Directory.GetFiles(src))
            System.IO.File.Copy(f, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f)), overwrite: true);
        foreach (var d in System.IO.Directory.GetDirectories(src))
            CopyDirectoryRecursive(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
    }
}
