// BetaDeps.Foundation -- ReflectionUtils
//
// Small reflection helpers shared across BetaDeps assemblies. Consolidates
// the three near-identical ResolveType helpers that previously lived in
// BetaDeps.ButterLib.ExceptionHandler.BEWPatch, BetaDeps.Harmony.BetaSigSafePatches,
// and BetaDeps.UIExtenderEx.Runtime.WidgetPrefabHook.
//
// All swallowing of exceptions is intentional -- callers want a Type?
// answer, not a stack trace, when looking up TaleWorlds types whose
// namespace shifts between game versions.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace BetaDeps.Foundation;

public static class ReflectionUtils
{
    /// <summary>
    /// Walk every currently-loaded assembly and return the first match
    /// for a fully-qualified type name. Returns null on miss. Never throws.
    /// Used to locate TaleWorlds types whose namespace can drift between
    /// game versions (e.g. PrefabSystem.BrushFactory vs GauntletUI.BrushFactory).
    /// </summary>
    public static Type? ResolveTypeByFullName(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return null;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }
}
