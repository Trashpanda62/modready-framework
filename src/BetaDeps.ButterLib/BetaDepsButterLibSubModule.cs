// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// BetaDeps.ButterLib SubModule entry. Installs BEW finalizers, builds the
// DI container, seeds default services (HotKeyManager wrapper, etc.),
// publishes the root IServiceProvider via GenericServiceProvider, and
// enables every registered subsystem.

using System;
using System.Linq;
using System.Reflection;

using Bannerlord.ButterLib.Common.Extensions;
using Bannerlord.ButterLib.ExceptionHandler;
using Bannerlord.ButterLib.HotKeys;
using Bannerlord.ButterLib.SubSystems;

using BetaDeps.Foundation;

using Microsoft.Extensions.DependencyInjection;

using TaleWorlds.MountAndBlade;

namespace Bannerlord.ButterLib;

public class ButterLibSubModule : MBSubModuleBase
{
    private const string Tag = "ButterLibSubModule";

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            var thisAsm = typeof(ButterLibSubModule).Assembly;
            DiagLog.Log(Tag, $"OnSubModuleLoad: {thisAsm.GetName().Name} v{thisAsm.GetName().Version}");

            // BEW finalizers.
            BEWPatch.Enable();

            // Bind ExceptionHandlerSubSystem.Instance early so consumer mods
            // (notably AdmiralNelson's) that call .Instance?.Disable() in their
            // own OnSubModuleLoad don't NRE and pop the "unable to disable
            // butterlib exception" warning dialog.
            try
            {
                ExceptionHandlerSubSystem._BindEarly();
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "ExceptionHandlerSubSystem._BindEarly", ex);
            }

            // Install the default IHotKeyManagerStatic so HotKeyManager.Create /
            // CreateWithOwnCategory return a usable manager. Without this, FCL's
            // OnBeforeInitialModuleScreenSetAsRoot calls HotKeyManager.CreateWithOwnCategory(...)
            // and gets null back, then NREs on the next chained .Add<T>() call.
            try
            {
                HotKeyManager.StaticInstance ??= new DefaultHotKeyManagerStatic();
                DiagLog.Log(Tag, $"HotKeyManager.StaticInstance installed ({HotKeyManager.StaticInstance.GetType().Name})");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "HotKeyManager.StaticInstance install", ex);
            }

            // Build the root DI container and publish via GenericServiceProvider.
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IHotKeyManagerStatic>(_ => HotKeyManager.StaticInstance!);
                var serviceProvider = services.BuildServiceProvider();
                GenericServiceProvider.SetServiceProvider(serviceProvider);
                DiagLog.Log(Tag, "DI container built and published");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "DI bootstrap", ex);
            }

            // Enable every registered subsystem. SubSystemManager is empty by
            // default; ButterLib subsystems we ship + consumer mods register
            // their own before this point via static initializers or DI.
            SubSystemManager.EnableAll();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex);
        }
    }

    /// <summary>
    /// Runs once, after every consumer mod's OnSubModuleLoad has finished
    /// installing its Harmony patches but before the main menu is rendered.
    /// We use it to quarantine a few specific patches from consumer mods that
    /// are known-broken on current Bannerlord versions, so the rest of those
    /// mods can keep working without taking down the whole game.
    /// </summary>
    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        try { QuarantineXorberaxShoulderCamera(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "QuarantineXorberaxShoulderCamera", ex); }
    }

    /// <summary>
    /// XorberaxLegacy.Patches.ShoulderCameraPatch.Prefix NREs every camera-tick
    /// on Bannerlord v1.2.5 (its agent / weapon-state assumptions don't hold
    /// in current TaleWorlds code). The bug is inside the compiled XorberaxLegacy
    /// DLL and we can't fix it from the outside. To keep the rest of XorberaxLegacy
    /// working (banks, items, etc.), we walk Harmony's global patch index, find
    /// any method patched by a class whose full name starts with
    /// "XorberaxLegacy.Patches.ShoulderCameraPatch", and unpatch JUST those
    /// prefixes -- leaving every other XorberaxLegacy patch untouched.
    /// </summary>
    private static void QuarantineXorberaxShoulderCamera()
    {
        // Skip silently if XorberaxLegacy isn't loaded.
        var xorberax = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "XorberaxLegacy");
        if (xorberax == null) return;

        int removed = 0;
        var harmony = new HarmonyLib.Harmony("BetaDeps.ButterLib.Quarantine.XorberaxShoulderCamera");
        foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods())
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info == null) continue;
            foreach (var patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
            {
                var declaring = patch.PatchMethod?.DeclaringType?.FullName ?? string.Empty;
                if (declaring.StartsWith("XorberaxLegacy.Patches.ShoulderCameraPatch"))
                {
                    try
                    {
                        harmony.Unpatch(method, patch.PatchMethod);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        DiagLog.LogCaught(Tag, $"Unpatch {declaring} on {method.DeclaringType?.FullName}.{method.Name}", ex);
                    }
                }
            }
        }

        if (removed > 0)
        {
            DiagLog.Log(Tag, $"Quarantined {removed} XorberaxLegacy ShoulderCameraPatch entry(ies). The rest of XorberaxLegacy is still active; only the broken over-the-shoulder camera prefix is disabled.");
        }
    }
}
