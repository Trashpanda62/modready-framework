// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// BrushFactoryManager hooks TaleWorlds's BrushFactory so consumer mods can
// register custom Gauntlet brushes by name. The hook is structurally
// identical to WidgetFactoryManager but operates on brush types.

using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

using ModReady.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.UIExtenderEx.ResourceManager;

public static class BrushFactoryManager
{
    private const string Tag = "BrushFactoryManager";
    private const string HarmonyId = "modready.uiextenderex.brushfactory";

    private static int _installed;
    private static readonly Dictionary<string, object> _customBrushes = new(StringComparer.Ordinal);
    private static readonly object _gate = new();

    /// <summary>Register a custom brush by name.</summary>
    public static void Register(string name, object brush)
    {
        if (string.IsNullOrEmpty(name) || brush == null) return;
        lock (_gate) { _customBrushes[name] = brush; }
        DiagLog.Log(Tag, $"registered custom brush '{name}'");
    }

    /// <summary>Look up a registered brush by name. Returns null on miss.</summary>
    public static object? GetCustomBrush(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        lock (_gate) { return _customBrushes.TryGetValue(name, out var b) ? b : null; }
    }

    /// <summary>Snapshot of every registered brush.</summary>
    public static IReadOnlyDictionary<string, object> All
    {
        get { lock (_gate) { return _customBrushes.ToDictionary(kv => kv.Key, kv => kv.Value); } }
    }

    /// <summary>
    /// Install Harmony patches on BrushFactory.GetBrush so brush lookups
    /// consult our custom registry before the engine's built-in catalog.
    /// </summary>
    public static void Patch(global::HarmonyLib.Harmony harmony)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var brushFactory = AccessTools2.TypeByName("TaleWorlds.GauntletUI.BrushFactory")
                            ?? AccessTools2.TypeByName("TaleWorlds.GauntletUI.PrefabSystem.BrushFactory");
            if (brushFactory == null)
            {
                DiagLog.Log(Tag, "BrushFactory type not found; brush-factory patches disabled");
                return;
            }

            var getBrush = AccessTools2.Method(brushFactory, "GetBrush");
            if (getBrush == null) { DiagLog.Log(Tag, "BrushFactory.GetBrush not found; skipped"); return; }

            harmony.Patch(getBrush, prefix: new HarmonyMethod(AccessTools2.Method(typeof(BrushFactoryManager), nameof(GetBrushPrefix))!));
            DiagLog.Log(Tag, $"patched {brushFactory.FullName}.GetBrush (prefix)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Patch", ex);
        }
    }

    public static bool GetBrushPrefix(string name, ref object? __result)
    {
        var b = GetCustomBrush(name);
        if (b == null) return true;
        __result = b;
        return false;
    }
}
