// ModReady.Harmony -- SubModule entry point
//
// The MBSubModuleBase that Bannerlord's launcher instantiates when
// Modules\ModReady\SubModule.xml references this assembly.
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

using ModReady.Foundation;

using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ModReady.Harmony;

public class ModReadyHarmonySubModule : MBSubModuleBase
{
    private const string Tag = "ModReady.Harmony";
    private static bool _unhandledHookInstalled;

    // [ThreadStatic] guard so a recursive failure inside our own log writer
    // (e.g. RuntimeLog.Write itself throws TypeLoadException somehow) doesn't
    // produce an infinite recursion inside FirstChanceException.
    [System.ThreadStatic] private static bool _inFirstChance;

    // Gate for this ctor's early shim installs. (Phase 1.2: RunEarlyPhase
    // owns its OWN one-shot gate internally now -- calling it from several
    // places is safe and later calls log a no-op line.) Constructor (not
    // OnSubModuleLoad) so this hook runs DURING the SubModule construction
    // phase, before any other mod's class has been instantiated. That's the
    // only phase that survives if a later mod's ctor throws and Bannerlord
    // aborts the sequence.
    private static int _ctorEarlyDetectionRan;

    public ModReadyHarmonySubModule()
    {
        // Game Pass / MS Store (.NET 6): the umbrella module is Win64-only by
        // design, but its DLLs may be bridged onto the Gaming.Desktop folder so
        // the engine can find them (no "cannot find/load" popup). If it loads
        // here on CoreCLR, no-op entirely -- every Steam-only hook below
        // (PatchShield, SaveShield, alias bootstrap, sigsafe patches) runs from
        // the .NET 6 Harmony host instead, and running the net472 paths on
        // CoreCLR is exactly what crashed startup (2026-07-05).
        if (RuntimeEnv.IsNetCore) return;

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
            // from ModReady's constructor (not OnSubModuleLoad) because by the
            // time OnSubModuleLoad fires, other mods' constructors have
            // already run -- and a broken constructor is exactly what we're
            // trying to block. ModReady loads before every consumer mod by
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

        // Game Pass no-op (see ctor): the .NET 6 host owns all of this on GP.
        if (RuntimeEnv.IsNetCore) return;

        // Bootstrap alias folders (Bannerlord.Harmony / UIExtenderEx / ButterLib /
        // MBOptionScreen). The public zip ships them nested under
        // Modules\ModReady\aliases\ so end users see a single module on disk;
        // on first launch we materialise them as siblings of ModReady so the
        // launcher sees them and consumer mods that DependedModule on those
        // IDs can find them. Idempotent — skips folders that already exist.
        try { BootstrapAliasFolders(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "BootstrapAliasFolders", ex); }

        // v0.8.2: unconditionally write the User-scope CREST_SHOW_STUBS=1 env
        // var so BLSE LauncherEx's hide-stubs filter (which would otherwise
        // hide our 4 dependency modules from the launcher modlist) bails out
        // on the NEXT BLSE launch. BLSE reads this env var once at launcher
        // startup, before ModReady loads, so on a fresh install the dep
        // modules become visible starting on the second launch.
        //
        // There is no opt-out toggle: modders who want the raw 4-dep stack
        // without ModReady's PatchShield/SaveShield/MCM extras can simply
        // disable the ModReady module in the launcher — the 4 dependency
        // modules ship the canonical DLLs and run standalone (v0.8+).
        try
        {
            const string envName = "CREST_SHOW_STUBS";
            var current = System.Environment.GetEnvironmentVariable(envName, System.EnvironmentVariableTarget.User);
            if (current != "1")
            {
                System.Environment.SetEnvironmentVariable(envName, "1", System.EnvironmentVariableTarget.User);
                DiagLog.Log(Tag, $"set User env {envName}=1 (was '{current ?? "<null>"}'); BLSE will surface dep modules from the next launch");
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "Set CREST_SHOW_STUBS", ex); }

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

        // v0.7.4: hook AppDomain.ProcessExit so PatchShield + SaveShield each
        // write a one-line session-summary to runtime.log on game shutdown.
        // Users grepping runtime.log for the diagnostic gist get a single
        // tidy line per shield instead of scanning the full file.
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                try { PatchShield.WriteSessionSummary(); } catch { }
                try { SaveShield.WriteSessionSummary(); } catch { }
            };
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "ProcessExit-summary-hook", ex); } catch { }
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
                // We only log if the exception escapes ModReady boundaries; otherwise
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
            var asmName = typeof(ModReadyHarmonySubModule).Assembly.GetName();
            DiagLog.Log(Tag, $"OnSubModuleLoad: {asmName.Name} v{asmName.Version} on branch {VersionProbe.Branch} (v{VersionProbe.Major}.{VersionProbe.Minor})");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad/banner", ex);
        }

        // v0.6: Preemptive disable of known-incompatible mods. Runs as the
        // very first ModReady action (after alias bootstrap + version shim)
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
        if (RuntimeEnv.IsNetCore) return; // Game Pass no-op
        TryInstallPatchShield("OnBeforeInitialModuleScreenSetAsRoot");

        // v1.0.6: fix the main-menu "Continue" save-load loop (native
        // PreloadScreen re-ticks TryLoadSave on a module-mismatched save).
        // SandBox is loaded and the main menu isn't up yet -- ideal install
        // point. Idempotent; guards only the Continue path.
        try { ContinueLoadGuard.Install(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ContinueLoadGuard.Install", ex); }

        // Phase 3 gate (dev-only; no-op unless shieldfixture-path.flag
        // exists): synthetic broken-mod fixture proving culprit-targeted
        // unpatching leaves innocent patches intact.
        ShieldFixtureSelfTest.RunIfRequested();
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
        if (RuntimeEnv.IsNetCore) return; // Game Pass no-op (host owns GP)

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

        // v2.0: framework auto-wiring. Runs AFTER the shields so the global
        // Harmony registry is fully populated. Read-only conflict scan +
        // flag-gated perf instrumentation; see FrameworkBootstrap.
        try
        {
            ModReady.Framework.FrameworkBootstrap.RunLateInit(from);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"{from}/FrameworkLateInit", ex);
        }
    }

    /// <summary>
    /// One-time bootstrap. Looks at our own assembly location
    /// (Modules\ModReady\bin\Win64_Shipping_Client\ModReady.Harmony.dll), walks
    /// up to the Modules\ root, and for each known alias folder name (the four
    /// modules consumer mods declare as DependedModule), copies
    /// `Modules\ModReady\aliases\&lt;Name&gt;\` into `Modules\&lt;Name&gt;\` if the
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
        var ownPath = typeof(ModReadyHarmonySubModule).Assembly.Location;
        if (string.IsNullOrEmpty(ownPath)) return;

        // ownPath = ...\Modules\ModReady\bin\Win64_Shipping_Client\ModReady.Harmony.dll
        var binDir         = System.IO.Path.GetDirectoryName(ownPath);          // Win64_Shipping_Client
        var modReadyBin    = System.IO.Path.GetDirectoryName(binDir);            // bin
        var modReadyModule = System.IO.Path.GetDirectoryName(modReadyBin);       // ModReady
        var modulesRoot    = System.IO.Path.GetDirectoryName(modReadyModule);    // Modules
        if (string.IsNullOrEmpty(modReadyModule) || string.IsNullOrEmpty(modulesRoot)) return;

        // v0.8 task #14: under MO2/USVFS the modulesRoot derived above is the
        // VIRTUALISED path. Writes there land in MO2's Overwrite folder, not
        // on the real game disk, so on the next real launch the launcher
        // doesn't see the alias folders we materialise. MO2Bootstrap detects
        // USVFS and resolves the real on-disk Modules path via the kernel-
        // provided MainModule.FileName (which USVFS doesn't hook). If
        // detected, we redirect the materialisation target there.
        if (ModReady.Foundation.MO2Bootstrap.IsUnderMO2())
        {
            var realModules = ModReady.Foundation.MO2Bootstrap.TryGetRealModulesPath();
            if (!string.IsNullOrEmpty(realModules))
            {
                DiagLog.Log(Tag, $"BootstrapAliasFolders: MO2 detected ({ModReady.Foundation.MO2Bootstrap.DetectionReason}); " +
                                  $"redirecting materialisation from virtual '{modulesRoot}' to real '{realModules}'");
                modulesRoot = realModules!;
            }
            else
            {
                DiagLog.Log(Tag, $"BootstrapAliasFolders: MO2 detected ({ModReady.Foundation.MO2Bootstrap.DetectionReason}) " +
                                  "but real Modules path could not be derived; falling back to virtualised path (alias folders may not survive)");
            }
        }

        var aliasStaging = System.IO.Path.Combine(modReadyModule!, "aliases");
        if (!System.IO.Directory.Exists(aliasStaging))
        {
            // Not a single-folder install; nothing to bootstrap. Local-dev
            // builds via Ralph-Loop already deploy alias folders as siblings.
            return;
        }

        // Phase 1.6 / finding H3 (2026-06-10 review): alias folders used to be
        // created once and never touched again ("if exists -> continue").
        // After any ModReady update the alias folders kept the OLD build's
        // DLLs, and -- with AssemblyVersion pinned across releases -- the CLR
        // happily bound new ModReady code against a stale Foundation/Harmony
        // copy that loaded first from an alias folder. That produced exactly
        // the MissingMethodException class this stack exists to prevent, with
        // no diagnostic pointing at the stale alias. Each materialised folder
        // now carries a modready-version.txt stamp; on FileVersion mismatch
        // the folder is re-copied. Refreshed DLLs take effect NEXT launch
        // (this session already loaded the old ones), which is exactly the
        // window the old code left permanently broken.
        var stampVersion = TryGetOwnFileVersion();

        int created = 0, refreshed = 0;
        foreach (var sub in System.IO.Directory.GetDirectories(aliasStaging))
        {
            var name = System.IO.Path.GetFileName(sub);
            var dest = System.IO.Path.Combine(modulesRoot!, name);
            var stampPath = System.IO.Path.Combine(dest, AliasStampFile);
            try
            {
                if (!System.IO.Directory.Exists(dest))
                {
                    CopyDirectoryRecursive(sub, dest);
                    TryWriteStamp(stampPath, stampVersion);
                    created++;
                    DiagLog.Log(Tag, $"BootstrapAliasFolders: created Modules\\{name} (stamp {stampVersion})");
                    continue;
                }

                var existing = System.IO.File.Exists(stampPath)
                    ? System.IO.File.ReadAllText(stampPath).Trim()
                    : "(no stamp -- pre-1.0 install)";

                // Old .stale-* shunt files from a previous refresh are
                // unlocked by now; garbage-collect them opportunistically.
                CleanupStaleFiles(dest);

                if (!string.IsNullOrEmpty(stampVersion) &&
                    string.Equals(existing, stampVersion, StringComparison.Ordinal))
                    continue; // up to date

                RefreshDirectoryRecursive(sub, dest);
                TryWriteStamp(stampPath, stampVersion);
                refreshed++;
                DiagLog.Log(Tag, $"BootstrapAliasFolders: refreshed Modules\\{name} ({existing} -> {stampVersion}); takes effect next launch");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"BootstrapAliasFolders/{name}", ex);
            }
        }

        if (created > 0 || refreshed > 0)
        {
            DiagLog.Log(Tag, $"BootstrapAliasFolders: materialised {created}, refreshed {refreshed} alias folder(s). " +
                              "Consumer mods that depend on Bannerlord.Harmony / UIExtenderEx / ButterLib / MBOptionScreen " +
                              "will see the current binaries on the next start.");
        }
    }

    // Phase 1.6 / H3 -----------------------------------------------------

    private const string AliasStampFile = "modready-version.txt";

    private static string TryGetOwnFileVersion()
    {
        try
        {
            var loc = typeof(ModReadyHarmonySubModule).Assembly.Location;
            if (string.IsNullOrEmpty(loc)) return string.Empty;
            return System.Diagnostics.FileVersionInfo.GetVersionInfo(loc).FileVersion ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static void TryWriteStamp(string stampPath, string version)
    {
        try
        {
            if (!string.IsNullOrEmpty(version))
                System.IO.File.WriteAllText(stampPath, version + Environment.NewLine);
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, $"TryWriteStamp({stampPath})", ex); } catch { }
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

    /// <summary>
    /// Like CopyDirectoryRecursive, but tolerant of in-use targets. By the
    /// time this runs, the alias modules' DLLs are LOADED in this process
    /// (the Bannerlord.Harmony host constructs before ModReady), so
    /// File.Copy(overwrite:true) gets a sharing violation. Windows does
    /// allow RENAMING a loaded DLL, so locked targets are shunted aside to
    /// "*.stale-&lt;timestamp&gt;" and the fresh copy lands under the real name.
    /// The shunt files are deleted by CleanupStaleFiles on a later launch.
    /// </summary>
    private static void RefreshDirectoryRecursive(string src, string dst)
    {
        System.IO.Directory.CreateDirectory(dst);
        foreach (var f in System.IO.Directory.GetFiles(src))
        {
            var target = System.IO.Path.Combine(dst, System.IO.Path.GetFileName(f));
            try
            {
                System.IO.File.Copy(f, target, overwrite: true);
            }
            catch (Exception)
            {
                // Per-file guard: if the shunt rename ALSO fails (e.g. AV holds the
                // target so even a rename is blocked), don't let it throw out and
                // abort the rest of this folder's files -- skip just this one and
                // log it. Without this, a single un-renamable DLL would stop every
                // remaining file in the folder from refreshing.
                try
                {
                    var stale = target + ".stale-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    System.IO.File.Move(target, stale);
                    System.IO.File.Copy(f, target);
                }
                catch (Exception shuntEx)
                {
                    DiagLog.LogCaught(Tag, $"RefreshDirectoryRecursive/shunt({System.IO.Path.GetFileName(f)})", shuntEx);
                }
            }
        }
        foreach (var d in System.IO.Directory.GetDirectories(src))
            RefreshDirectoryRecursive(d, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(d)));
    }

    private static void CleanupStaleFiles(string dir)
    {
        try
        {
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.stale-*", System.IO.SearchOption.AllDirectories))
            {
                try { System.IO.File.Delete(f); } catch { /* still locked; next launch */ }
            }
        }
        catch { }
    }
}
