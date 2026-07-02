// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// WidgetPrefabPatch is the Harmony patch class at the namespace path
// (Bannerlord.UIExtenderEx.Patches.WidgetPrefabPatch) that consumer mods
// and diagnostic tools (CrestPatchSelfTest) look for as the "active
// WidgetPrefab.LoadFrom hook owner". Our actual hook implementation lives
// in Runtime/WidgetPrefabHook for separation of concerns; this class is
// the named entry point so reflection-based discovery resolves to it.

using System.Collections.Generic;

using Bannerlord.UIExtenderEx.Runtime;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.UIExtenderEx.Patches;

public static class WidgetPrefabPatch
{
    /// <summary>
    /// Identity transpiler at the expected name. Forces JIT and lets
    /// CrestPatchSelfTest's "is this method patched?" probe succeed.
    /// </summary>
    public static IEnumerable<CodeInstruction> WidgetPrefab_LoadFrom_Transpiler(IEnumerable<CodeInstruction> instructions)
        => instructions;

    /// <summary>
    /// Install the transpiler patch on WidgetPrefab.LoadFrom. Called by
    /// UIExtenderEx SubModule load alongside the WidgetPrefabHook prefix
    /// that does the actual XML rewriting.
    /// </summary>
    public static void Patch(global::HarmonyLib.Harmony harmony)
    {
        var widgetPrefab = AccessTools2.TypeByName("TaleWorlds.GauntletUI.PrefabSystem.WidgetPrefab");
        if (widgetPrefab == null) return;
        var target = AccessTools2.Method(widgetPrefab, "LoadFrom");
        if (target == null) return;
        var transpiler = AccessTools2.Method(typeof(WidgetPrefabPatch), nameof(WidgetPrefab_LoadFrom_Transpiler));
        if (transpiler == null) return;
        harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
    }
}
