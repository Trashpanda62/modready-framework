// BetaDeps.Foundation -- CollectAssemblyTypesShim
//
// v0.7 hotfix #3: ROOT-CAUSE FIX for the entire class of "submodule could
// not be loaded correctly due to a dependency conflict" dialogs that
// happen when a consumer mod has been compiled against a different
// version of our impersonated assemblies than what we ship.
//
// ROOT CAUSE:
//   TaleWorlds.MountAndBlade.Module.CollectModuleAssemblyTypes() calls
//   assembly.GetTypes() inside a try block. If ANY type in the assembly
//   fails to load (because it references a member that doesn't exist on
//   our impersonated DLL), GetTypes() throws ReflectionTypeLoadException.
//   The engine catches that, sets `out types = null`, and returns
//   CriticalError. LoadSubModules then calls HandleSubmoduleLoadError,
//   which shows the "dependency conflict" dialog and throws.
//
//   This happens to ANY consumer mod with even one type that depends on
//   a missing member, even if the SubModule class itself loaded cleanly.
//
// THE FIX (transpiler on CollectModuleAssemblyTypes):
//   Replace the catch-block tail's "out types = null; return CriticalError"
//   with a call to our helper that:
//     1. Re-runs assembly.GetTypes() with proper ReflectionTypeLoadException
//        handling, pulling ex.Types.Where(t => t != null).
//     2. CRITICALLY: looks up the specific SubModule class named in
//        subInfo.SubModuleClassTypeName. If that type didn't load OR
//        doesn't have a usable parameterless constructor, returns
//        CriticalError -- because forcing Success when the SubModule
//        class itself is broken would just crash AddSubModule's
//        constructor invoke and surface as an unhandled exception.
//     3. If the SubModule type loaded cleanly, populates the types
//        dictionary with all MBSubModuleBase-derived types from the
//        partial load and returns Success.
//
//   Result: mods like Character Development Editor (SubModule class is
//   fine, only OTHER types reference missing API) load normally.
//   Mods where the SubModule class itself is broken still get the
//   original CriticalError verdict so the engine cleanly skips them
//   without crashing downstream.
//
// Original work. MIT, copyright 2026 Trashpanda62.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class CollectAssemblyTypesShim
{
    private const string Tag = "CollectAssemblyTypesShim";
    private const string HarmonyId = "betadeps.foundation.collectassemblytypesshim";
    private static int _installed;

    private static Type? _mbSubModuleBaseType;

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var moduleType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Module");
            if (moduleType == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.MountAndBlade.Module type not found; patch not installed.");
                return;
            }

            _mbSubModuleBaseType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MBSubModuleBase");
            if (_mbSubModuleBaseType == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.MountAndBlade.MBSubModuleBase type not found; patch not installed.");
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var collectMethod = moduleType
                .GetMethods(flags)
                .FirstOrDefault(m => m.Name == "CollectModuleAssemblyTypes");
            if (collectMethod == null)
            {
                DiagLog.Log(Tag, "CollectModuleAssemblyTypes not found; patch not installed.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            var transpiler = AccessTools.Method(typeof(CollectAssemblyTypesShim), nameof(CollectTranspiler));
            harmony.Patch(collectMethod, transpiler: new HarmonyMethod(transpiler!));
            DiagLog.Log(Tag, $"installed: transpiler on {collectMethod.DeclaringType?.FullName}.{collectMethod.Name} -- catch-block tail rewritten to validate SubModule type before overriding verdict.");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Install", ex); } catch { }
        }
    }

    /// <summary>
    /// Transpiler. Rewrites:
    ///   ldarg.3 ; ldnull ; stind.ref ; ldc.i4.2 ; stloc.3
    /// to:
    ///   ldarg.3 ; ldarg.1 ; ldarg.2 ; call PopulateAndValidate(ref Dict, SubModuleInfo, Assembly) -> int ; stloc.3
    /// The helper returns the verdict (0 or 2) based on whether the
    /// SubModule type itself loaded cleanly.
    /// </summary>
    private static IEnumerable<CodeInstruction> CollectTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var helper = AccessTools.Method(typeof(CollectAssemblyTypesShim), nameof(PopulateAndValidate));

        if (helper == null)
        {
            DiagLog.Log(Tag, "transpiler: PopulateAndValidate helper not found; leaving IL unchanged.");
            return list;
        }

        int patchedAt = -1;
        for (int i = 0; i < list.Count - 4; i++)
        {
            if (list[i].opcode == OpCodes.Ldarg_3
                && list[i + 1].opcode == OpCodes.Ldnull
                && list[i + 2].opcode == OpCodes.Stind_Ref
                && list[i + 3].opcode == OpCodes.Ldc_I4_2
                && list[i + 4].opcode == OpCodes.Stloc_3)
            {
                // Rewrite:
                //   [i+0] ldarg.3       (unchanged -- address of out param)
                //   [i+1] ldnull   -->  ldarg.1 (SubModuleInfo subInfo)
                //   [i+2] stind.ref --> ldarg.2 (Assembly asm)
                //   [i+3] ldc.i4.2 -->  call PopulateAndValidate(ref, SubModuleInfo, Assembly) -> int
                //   [i+4] stloc.3       (unchanged -- stores helper's return into result local)
                list[i + 1] = new CodeInstruction(OpCodes.Ldarg_1);
                list[i + 2] = new CodeInstruction(OpCodes.Ldarg_2);
                list[i + 3] = new CodeInstruction(OpCodes.Call, helper);
                patchedAt = i;
                break;
            }
        }

        if (patchedAt < 0)
        {
            DiagLog.Log(Tag, "transpiler: target pattern not found in IL; leaving unchanged.");
        }
        else
        {
            DiagLog.Log(Tag, $"transpiler: rewrote catch-block tail at IL index {patchedAt}.");
        }

        return list;
    }

    /// <summary>
    /// Called from patched IL. Returns 0 (Success) only if the specific
    /// SubModule class named in subInfo.SubModuleClassTypeName loaded
    /// cleanly AND has an invokable parameterless constructor. Otherwise
    /// returns 2 (CriticalError) and leaves the out dict at null --
    /// matching the original engine behavior so downstream AddSubModule
    /// cleanly skips the broken mod.
    /// </summary>
    public static int PopulateAndValidate(ref Dictionary<string, Type>? types, object? subInfo, Assembly assembly)
    {
        try
        {
            if (assembly == null || subInfo == null)
            {
                types = null;
                return 2;
            }

            var asmName = assembly.GetName().Name ?? "(unknown)";

            // 1. Lenient GetTypes
            Type?[] loadedTypes;
            try { loadedTypes = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException rtle)
            {
                loadedTypes = rtle.Types ?? Array.Empty<Type?>();
                DiagLog.Log(Tag, $"lenient pass for '{asmName}': {loadedTypes.Count(t => t != null)} of {loadedTypes.Length} types resolved, {rtle.LoaderExceptions?.Length ?? 0} loader exception(s)");

                // First 3 LoaderExceptions logged for diagnostic value -- they're how we
                // identified the XmlDocument vs XmlNode ABI mismatch in v0.7.
                if (rtle.LoaderExceptions != null)
                {
                    int n = 0;
                    foreach (var le in rtle.LoaderExceptions)
                    {
                        if (le == null) continue;
                        DiagLog.Log(Tag, $"  LE[{n}] {le.GetType().Name}: {(le.Message ?? "").Replace('\n', ' ').Replace('\r', ' ')}");
                        if (++n >= 3) break;
                    }
                }
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, $"GetTypes for {asmName}", ex); } catch { }
                types = null;
                return 2;
            }

            // 2. Get the SubModule class type name from SubModuleInfo via reflection
            //    (SubModuleInfo is in TaleWorlds.ModuleManager, can't reference directly).
            string? subModuleClassName = null;
            try
            {
                var prop = subInfo.GetType().GetProperty("SubModuleClassTypeName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                    subModuleClassName = prop.GetValue(subInfo) as string;
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "get SubModuleClassTypeName", ex); } catch { }
            }

            if (string.IsNullOrEmpty(subModuleClassName))
            {
                DiagLog.Log(Tag, $"'{asmName}': could not read SubModuleClassTypeName from subInfo -- keeping CriticalError verdict.");
                types = null;
                return 2;
            }

            // 3. CRITICAL: verify the specific SubModule class loaded cleanly
            //    by checking if it's in the loadedTypes list (the non-null entries).
            Type? subModuleType = null;
            foreach (var t in loadedTypes)
            {
                if (t == null) continue;
                if (string.Equals(t.FullName, subModuleClassName, StringComparison.Ordinal))
                {
                    subModuleType = t;
                    break;
                }
            }

            if (subModuleType == null)
            {
                DiagLog.Log(Tag, $"'{asmName}': SubModule class '{subModuleClassName}' is NOT among the cleanly-loaded types -- keeping CriticalError verdict. Engine will skip this mod cleanly.");
                types = null;
                return 2;
            }

            // 4. Verify the SubModule type has a usable parameterless constructor
            //    (AddSubModule will Invoke it; if it throws here, that's a hard
            //    crash we should head off by signaling CriticalError instead).
            try
            {
                var ctor = subModuleType.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (ctor == null)
                {
                    DiagLog.Log(Tag, $"'{asmName}': SubModule class '{subModuleClassName}' has no public parameterless ctor -- keeping CriticalError verdict.");
                    types = null;
                    return 2;
                }
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, $"GetConstructor for {subModuleClassName}", ex); } catch { }
                types = null;
                return 2;
            }

            // 5. SubModule class is usable. Populate the types dictionary
            //    with all MBSubModuleBase-derived types and return Success.
            if (_mbSubModuleBaseType == null)
            {
                types = null;
                return 2;
            }

            var dict = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in loadedTypes)
            {
                if (t == null) continue;
                try
                {
                    // Critical: skip abstract types and open-generic types.
                    // The engine's downstream AddTypes / construction code
                    // hits "Instances of abstract classes cannot be created"
                    // if we include them. Only concrete instantiable types
                    // belong in this dictionary.
                    if (t.IsAbstract) continue;
                    if (t.IsGenericTypeDefinition) continue;
                    if (t.IsInterface) continue;
                    if (!_mbSubModuleBaseType.IsAssignableFrom(t)) continue;

                    // Also verify the type has a public parameterless ctor;
                    // anything without one will crash downstream Invoke.
                    var pubCtor = t.GetConstructor(
                        BindingFlags.Public | BindingFlags.Instance,
                        binder: null,
                        types: Type.EmptyTypes,
                        modifiers: null);
                    if (pubCtor == null) continue;

                    var key = t.FullName ?? t.Name;
                    if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                        dict[key] = t;
                }
                catch { /* skip individual broken type */ }
            }

            types = dict;
            DiagLog.Log(Tag, $"'{asmName}': SubModule class '{subModuleClassName}' validated, {dict.Count} types stored -- override CriticalError->Success.");
            return 0;
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "PopulateAndValidate", ex); } catch { }
            types = null;
            return 2;
        }
    }
}
