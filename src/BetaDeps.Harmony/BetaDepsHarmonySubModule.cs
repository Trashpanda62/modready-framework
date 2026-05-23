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
        // Hook for future load-order validation. Intentionally empty
        // in Phase 1 -- the validation logic isn't worth porting before
        // we have a self-test suite to drive it.
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
