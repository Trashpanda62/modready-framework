// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Harmony hooks that drive the mixin lifecycle (H12, Phase 2B):
//
//   1. Postfix on TaleWorlds.Library.ViewModel's parameterless constructor
//      (the base of every Gauntlet VM). Runs before any derived ctor body,
//      so it only RECORDS which mixins match (ViewModelMixinHost.Register);
//      actual mixin construction is deferred to first touch, after the VM
//      is fully constructed.
//   2. Postfix on ViewModel.OnFinalize -> mixin OnFinalize + detach.
//   3. Per-target postfixes on each registered mixin's refresh method
//      (RefreshMethodName, default "RefreshValues"), installed when the
//      owning UIExtender registry is enabled -> mixin OnRefresh.
//
// Caveat shared by 2 and 3: Harmony patches a specific method body, so a
// derived VM override that never calls base.RefreshValues()/OnFinalize()
// won't trigger the forward. TaleWorlds VMs overwhelmingly call base; the
// weak-keyed side tables in ViewModelMixinHost keep the miss harmless.

using System;
using System.Collections.Generic;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class ViewModelMixinHook
{
    private const string Tag = "ViewModelMixinHook";
    private const string HarmonyId = "betadeps.uiextenderex.vmmixin";

    private static int _installed;
    private static readonly HarmonyLib.Harmony _harmony = new(HarmonyId);

    /// <summary>Refresh methods already patched, so two mixins naming the
    /// same target method don't double-patch (and double-fire) it.</summary>
    private static readonly HashSet<MethodBase> _refreshPatched = new();
    private static readonly object _refreshGate = new();

    public static void Install()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            // Harmony lets us patch a single shared method that runs in every
            // VM subclass constructor: the no-arg base ViewModel() ctor.
            var ctor = typeof(ViewModel).GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (ctor == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.Library.ViewModel has no parameterless constructor reachable by reflection; mixin attachment disabled.");
                return;
            }

            _harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(ViewModelMixinHook), nameof(ViewModelCtorPostfix)));
            DiagLog.Log(Tag, $"installed postfix on {ctor.DeclaringType?.FullName}..ctor()");

            // ViewModel.OnFinalize -> mixin OnFinalize + detach. SafeBind
            // style: a miss logs and degrades (mixins stay attached until the
            // weak table releases them) instead of failing the install.
            var onFinalize = AccessTools2.Method(typeof(ViewModel), "OnFinalize", Type.EmptyTypes);
            if (onFinalize != null)
            {
                _harmony.Patch(onFinalize, postfix: new HarmonyMethod(typeof(ViewModelMixinHook), nameof(OnFinalizePostfix)));
                DiagLog.Log(Tag, "installed postfix on ViewModel.OnFinalize()");
            }
            else
            {
                DiagLog.Log(Tag, "ViewModel.OnFinalize() not found; mixin OnFinalize forwarding disabled.");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    /// <summary>
    /// Install a postfix on each registered mixin's refresh method so the
    /// mixin's OnRefresh runs whenever the target VM refreshes. Called from
    /// UIExtenderEngine.OnEnable, once per newly enabled registry.
    /// </summary>
    public static void InstallRefreshHooks(UIExtenderRegistry registry)
    {
        foreach (var mixinReg in registry.Mixins)
        {
            try
            {
                var methodName = mixinReg.Attribute.RefreshMethodName ?? "RefreshValues";
                var targetType = !string.IsNullOrEmpty(mixinReg.Attribute.TargetTypeName)
                    ? BetaDeps.Foundation.ReflectionUtils.ResolveTypeByFullName(mixinReg.Attribute.TargetTypeName!)
                    : mixinReg.TargetViewModelType;
                if (targetType == null)
                {
                    DiagLog.Log(Tag, $"{mixinReg.MixinType.FullName}: target VM type '{mixinReg.Attribute.TargetTypeName}' not resolvable; refresh forwarding disabled for this mixin");
                    continue;
                }

                // AccessTools walks the hierarchy, so a target that doesn't
                // override RefreshValues resolves to the base implementation --
                // patching that is fine (the postfix no-ops on VMs without mixins).
                var method = AccessTools2.Method(targetType, methodName);
                // Harmony refuses an inherited slot resolved through the derived
                // type (ReflectedType != DeclaringType): "You can only patch
                // implemented methods". Re-resolve on the declaring type (found
                // live 2026-06-11 via Diplomacy's NavalKingdomManagementVMMixin).
                if (method != null && method.DeclaringType != null && method.ReflectedType != method.DeclaringType)
                {
                    method = AccessTools2.Method(method.DeclaringType, methodName) ?? method;
                }
                if (method == null || method.IsAbstract)
                {
                    DiagLog.Log(Tag, $"{mixinReg.MixinType.FullName}: refresh method {targetType.Name}.{methodName} {(method == null ? "not found" : "is abstract")}; refresh forwarding disabled for this mixin");
                    continue;
                }

                // The Patch call stays inside the gate: Harmony's Patch is not
                // safe against concurrent calls on the same instance, and this
                // also keeps _refreshPatched consistent if Patch throws.
                lock (_refreshGate)
                {
                    if (!_refreshPatched.Add(method)) continue; // already hooked
                    try
                    {
                        _harmony.Patch(method, postfix: new HarmonyMethod(typeof(ViewModelMixinHook), nameof(RefreshPostfix)));
                    }
                    catch
                    {
                        _refreshPatched.Remove(method);
                        throw;
                    }
                }
                DiagLog.Log(Tag, $"installed refresh postfix on {method.DeclaringType?.FullName}.{method.Name} (for {mixinReg.MixinType.Name})");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"InstallRefreshHooks({mixinReg.MixinType.FullName})", ex);
            }
        }
    }

    /// <summary>Postfix on ViewModel..ctor: record matching mixins for
    /// deferred attachment (no construction here -- the VM isn't done yet).</summary>
    public static void ViewModelCtorPostfix(ViewModel __instance)
    {
        ViewModelMixinHost.Register(__instance);
    }

    /// <summary>Postfix on a target VM refresh method.</summary>
    public static void RefreshPostfix(object __instance, MethodBase __originalMethod)
    {
        if (__instance is ViewModel vm)
            ViewModelMixinHost.OnVmRefresh(vm, __originalMethod);
    }

    /// <summary>Postfix on ViewModel.OnFinalize.</summary>
    public static void OnFinalizePostfix(ViewModel __instance)
    {
        ViewModelMixinHost.OnVmFinalize(__instance);
    }
}
