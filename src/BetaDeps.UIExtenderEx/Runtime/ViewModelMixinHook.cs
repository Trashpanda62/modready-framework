// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Harmony postfix on TaleWorlds.Library.ViewModel's constructor (the base
// of every Gauntlet ViewModel). On each VM construction we ask
// ViewModelMixinHost to attach any matching mixins from the enabled
// UIExtender registries.
//
// Binding integration -- making the mixin's [DataSourceProperty] and
// [DataSourceMethod] members reachable from Gauntlet bindings -- is the
// follow-on piece. TaleWorlds.Library.ViewModel caches its data source
// table at construction; injecting our mixin members into that table
// requires a deeper patch that's version-fragile, so it's split into a
// separate task and only enabled when SafeBind confirms the internal
// shape on the running build.

using System;
using System.Threading;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class ViewModelMixinHook
{
    private const string Tag = "ViewModelMixinHook";
    private const string HarmonyId = "betadeps.uiextenderex.vmmixin";

    private static int _installed;

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

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            var postfix = new HarmonyMethod(typeof(ViewModelMixinHook), nameof(ViewModelCtorPostfix));
            harmony.Patch(ctor, postfix: postfix);
            DiagLog.Log(Tag, $"installed postfix on {ctor.DeclaringType?.FullName}..ctor()");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    /// <summary>Postfix on ViewModel..ctor that dispatches mixin attachment.</summary>
    public static void ViewModelCtorPostfix(ViewModel __instance)
    {
        // __instance is the just-constructed VM (or subclass). Forward to the
        // host; it iterates the enabled registries and attaches any matching
        // mixin instances.
        ViewModelMixinHost.Attach(__instance);
    }
}
