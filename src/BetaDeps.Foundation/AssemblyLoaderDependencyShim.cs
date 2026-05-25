// BetaDeps.Foundation -- AssemblyLoaderDependencyShim
//
// v0.7 hotfix for consumer-mod load failure with "dependency conflict" dialog.
//
// ROOT CAUSE (discovered via IL inspection of TaleWorlds.MountAndBlade.dll):
//   - TaleWorlds.MountAndBlade.Module.LoadSubModules() iterates each mod's
//     SubModuleInfo and calls TaleWorlds.Library.AssemblyLoader.LoadFrom on
//     the consumer mod's DLL.
//   - LoadFrom signature: (string assemblyFile, out AssemblyLoadResult result,
//     bool showError). The nested enum AssemblyLoadResult has values
//     Success=0, LoadedWithErrors=1, CriticalError=2.
//   - When LoadFrom decides the verdict is CriticalError (2), LoadSubModules
//     skips AddSubModule for that consumer (the SubModule never enters the
//     module dictionary) and calls HandleSubmoduleLoadError, which formats
//     the "{ModuleName}.{SubModuleName} could not be loaded correctly due to
//     a dependency conflict" message, calls ShowMessageBox, and throws.
//   - CriticalError fires when the consumer mod's AssemblyReferences pin
//     a version that doesn't match the on-disk AssemblyVersion of the
//     reference target. Example: Character Development Editor v1.0.10 was
//     compiled against `Bannerlord.ButterLib v2.10.3.0` but BetaDeps's
//     ButterLib DLL self-reports a different version.
//
//   The standard CLR AssemblyResolve fallback (AssemblyVersionShim) does
//   NOT catch this -- TaleWorlds' loader runs its own pre-check on the
//   assembly metadata and decides CriticalError before CLR fallback fires.
//
// THE FIX:
//   Harmony Postfix on the 3-arg overload of AssemblyLoader.LoadFrom. If
//   the verdict is CriticalError (2) AND the loaded assembly references one
//   of our impersonated names (Bannerlord.Harmony / UIExtenderEx /
//   ButterLib / MCMv5 / 0Harmony), we override the verdict to Success.
//   LoadSubModules then proceeds with AddSubModule as if the load succeeded
//   -- because functionally it DID succeed; the assembly is in the
//   AppDomain, and references resolve via simple-name binding to our
//   impersonated DLLs.
//
// IMPLEMENTATION NOTES:
//   AssemblyLoader and AssemblyLoadResult are both INTERNAL types in
//   TaleWorlds.Library.dll, so we can't reference them at compile time
//   (Bannerlord.ReferenceAssemblies.Core strips internals). We do all
//   resolution via reflection (AccessTools.TypeByName) and modify the out
//   parameter via Harmony's object[] __args (Harmony 2.x propagates
//   Postfix mutations of __args back to the original ref/out parameters).
//
// Original work. MIT, copyright 2026 Trashpanda62.

using System;
using System.Linq;
using System.Reflection;
using System.Threading;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class AssemblyLoaderDependencyShim
{
    private const string Tag = "AssemblyLoaderDependencyShim";
    private const string HarmonyId = "betadeps.foundation.assemblyloaderdepshim";
    private static int _installed;

    // Simple names of assemblies we impersonate. If a consumer mod's failed
    // CriticalError load references any of these, we infer the failure is
    // due to our version impersonation and override to Success.
    private static readonly string[] _impersonatedAssemblies = new[]
    {
        "Bannerlord.Harmony",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.ButterLib",
        "MCMv5",
        "0Harmony",
    };

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            // Resolve the internal type via reflection.
            var assemblyLoaderType = AccessTools.TypeByName("TaleWorlds.Library.AssemblyLoader");
            if (assemblyLoaderType == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.Library.AssemblyLoader type not found via AccessTools.TypeByName; patch not installed.");
                return;
            }

            // Find the 3-arg LoadFrom overload: (string, out AssemblyLoadResult, bool).
            // Include NonPublic since the method may be non-public.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var loadFrom = assemblyLoaderType
                .GetMethods(flags)
                .FirstOrDefault(m => m.Name == "LoadFrom" && m.GetParameters().Length == 3);

            if (loadFrom == null)
            {
                DiagLog.Log(Tag, "AssemblyLoader.LoadFrom(string, out, bool) not found; patch not installed.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            var postfix = AccessTools.Method(typeof(AssemblyLoaderDependencyShim), nameof(LoadFromPostfix));
            if (postfix == null)
            {
                DiagLog.Log(Tag, "internal error: LoadFromPostfix not found via reflection.");
                return;
            }

            // DIAG: also install a Prefix that logs every LoadFrom call so
            // we can see if our patch is even being invoked. Remove once
            // CDE-class crashes are confirmed resolved.
            var prefix = AccessTools.Method(typeof(AssemblyLoaderDependencyShim), nameof(LoadFromPrefix));

            harmony.Patch(loadFrom,
                prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                postfix: new HarmonyMethod(postfix));
            DiagLog.Log(Tag, $"installed: AssemblyLoader.LoadFrom prefix+postfix patched. Method = {loadFrom.DeclaringType?.FullName}.{loadFrom.Name}({string.Join(", ", loadFrom.GetParameters().Select(p => p.ParameterType.Name + (p.IsOut ? " out" : "") + " " + p.Name))})");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Install", ex); } catch { }
        }
    }

    /// <summary>
    /// Harmony Prefix. Diagnostic — logs every LoadFrom invocation so we
    /// can confirm the patch is wired correctly.
    /// </summary>
    private static void LoadFromPrefix(object[] __args)
    {
        try
        {
            var path = __args != null && __args.Length > 0 ? __args[0] as string : "(null)";
            DiagLog.Log(Tag, $"LoadFromPrefix fired: path={path}");
        }
        catch { }
    }

    /// <summary>
    /// Harmony Postfix. Uses __args (object[]) to access the out parameter
    /// without needing a compile-time reference to the internal enum type.
    /// Harmony 2.x propagates __args mutations from Postfix back to the
    /// original ref/out parameters.
    /// </summary>
    private static void LoadFromPostfix(Assembly __result, object[] __args)
    {
        try
        {
            var path = __args != null && __args.Length > 0 ? __args[0] as string : "(null)";
            var resultStr = __args != null && __args.Length > 1 && __args[1] != null
                ? __args[1].ToString()
                : "(null)";
            var asmName = __result?.GetName()?.Name ?? "(null)";
            DiagLog.Log(Tag, $"LoadFromPostfix fired: path={path} resultBefore={resultStr} loaded={asmName}");
        }
        catch { }

        try
        {
            if (__args == null || __args.Length < 2) return;
            var resultObj = __args[1];
            if (resultObj == null) return;

            int code;
            try { code = Convert.ToInt32(resultObj); }
            catch { return; }

            if (code != 2) return; // only override CriticalError
            if (__result == null) return;

            var refs = __result.GetReferencedAssemblies();
            for (int i = 0; i < refs.Length; i++)
            {
                var name = refs[i].Name;
                for (int j = 0; j < _impersonatedAssemblies.Length; j++)
                {
                    if (string.Equals(name, _impersonatedAssemblies[j], StringComparison.Ordinal))
                    {
                        DiagLog.Log(Tag, $"override CriticalError->Success for assembly '{__result.GetName().Name}' (references impersonated '{name}')");
                        __args[1] = Enum.ToObject(resultObj.GetType(), 0); // Success = 0
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "LoadFromPostfix", ex); } catch { }
        }
    }
}
