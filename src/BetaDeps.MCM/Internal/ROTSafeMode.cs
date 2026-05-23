// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ROTSafeMode v2 — neutralize the type-load crash that Realm of Thrones causes
// on Bannerlord v1.4.5+. v1 (v0.5.4 first attempt) tried to Harmony-patch
// ROT.SubModule's lifecycle methods directly; that failed because installing
// the patch requires JIT-compiling the patch target, and ROT.SubModule's
// method bodies reference the broken Model types -- so the patch installation
// triggered the same crash we were trying to prevent.
//
// v2 takes a different approach: find the engine's submodule list at runtime
// and REMOVE the ROT.SubModule instance from it, so the engine never invokes
// any lifecycle method on it in the first place. The broken Model types are
// never referenced because ROT.SubModule never runs.
//
// Critical: this only works if BetaDeps.MCM.OnSubModuleLoad runs BEFORE ROT's.
// If ROT's already executed, removing it from the list is too late. In that
// case the Apply() call no-ops cleanly and the existing crash repeats. The
// runtime.log tells us which one happened so we can iterate.
//
// Operations involved:
//   - sm.GetType() — metadata only, no JIT, safe even on ROT instances
//   - sm.GetType().Assembly.GetName().Name — metadata, safe
//   - submodules.RemoveAt(i) — pure list mutation, no JIT
//
// None of these touch ROT's broken IL.

using System;
using System.Collections;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

namespace MCM.Internal;

internal static class ROTSafeMode
{
    private const string Tag = "ROTSafeMode";

    private static bool _applied;

    /// <summary>
    /// Idempotent. Walks the engine's submodule list and removes any entry
    /// whose type lives in the "ROT" assembly. The engine never invokes
    /// lifecycle methods on the removed entry, so the JIT never compiles
    /// ROT.SubModule's body, so the broken Model types are never referenced,
    /// so no crash.
    /// </summary>
    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        try
        {
            // Find TaleWorlds.MountAndBlade.Module.CurrentModule (the engine's
            // active Module instance). The submodule list hangs off it.
            var moduleType = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.MountAndBlade.Module");
            if (moduleType == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.MountAndBlade.Module type not found in any loaded assembly. Safe mode skipped.");
                return;
            }

            var currentModuleProp = moduleType.GetProperty("CurrentModule",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var currentModule = currentModuleProp?.GetValue(null);
            if (currentModule == null)
            {
                DiagLog.Log(Tag, "Module.CurrentModule is null (engine not yet initialized?). Safe mode skipped.");
                return;
            }

            // The submodule list is typically a 'SubModules' property or field
            // returning IList of MBSubModuleBase. Try property first, then field.
            object? submodulesObj = null;
            var prop = currentModule.GetType().GetProperty("SubModules",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null) submodulesObj = prop.GetValue(currentModule);
            if (submodulesObj == null)
            {
                var field = currentModule.GetType().GetField("SubModules",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    field = currentModule.GetType().GetField("_subModules",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                submodulesObj = field?.GetValue(currentModule);
            }

            if (submodulesObj is not IList submodules)
            {
                DiagLog.Log(Tag, "Could not locate SubModules list on CurrentModule. Safe mode skipped. (Submodules property/field not IList or null.)");
                return;
            }

            DiagLog.Log(Tag, $"Engine submodule list has {submodules.Count} entries; scanning for ROT...");

            int removed = 0;
            // Iterate backwards because we're mutating the list.
            for (int i = submodules.Count - 1; i >= 0; i--)
            {
                var sm = submodules[i];
                if (sm == null) continue;
                string typeName, asmName;
                try
                {
                    var t = sm.GetType();         // metadata only
                    typeName = t.FullName ?? t.Name;
                    asmName = t.Assembly.GetName().Name ?? "(unknown)";
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"GetType() on submodule[{i}]", ex);
                    continue;
                }

                if (string.Equals(asmName, "ROT", StringComparison.Ordinal))
                {
                    DiagLog.Log(Tag, $"  removing submodule[{i}]: {typeName} (assembly={asmName})");
                    submodules.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                DiagLog.Log(Tag, $"Safe mode applied: {removed} ROT submodule entry/entries removed. ROT.SubModule lifecycle methods will not be invoked; broken Model types stay unloaded; game stays up.");
            }
            else
            {
                DiagLog.Log(Tag, "No ROT submodules found in the engine list at this point. Either (a) ROT not enabled, or (b) ROT's OnSubModuleLoad already ran before BetaDeps. If (b), the existing crash repeats — runtime.log will show that.");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Apply", ex);
        }
    }
}
