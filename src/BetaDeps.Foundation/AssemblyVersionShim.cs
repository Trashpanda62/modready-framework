// BetaDeps clean-room. MIT, copyright 2026 Maxfield Management Group.
//
// Consumer mods are compiled against specific historical AssemblyVersions of
// BUTR-ecosystem assemblies. This static initializer installs an
// AppDomain.AssemblyResolve handler that returns whatever loaded assembly
// matches the requested SIMPLE NAME, ignoring version. Installed during early
// load: AliasStubSubModule (ctor + OnSubModuleLoad) and BetaDepsHarmonySubModule
// both call AssemblyVersionShim.Install() so the AssemblyResolve handler is in
// place before consumer mods touch our impersonated assemblies. Install() is
// idempotent (CompareExchange guard).

using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Reflection;

namespace BetaDeps.Foundation;

public static class AssemblyVersionShim
{
    private const string Tag = "AssemblyVersionShim";

    private static readonly string[] _redirectedNames = new[]
    {
        "0Harmony", "MCMv5", "Bannerlord.UIExtenderEx", "Bannerlord.ButterLib",
        "Bannerlord.Harmony", "Newtonsoft.Json",
        "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives", "Serilog", "Serilog.Extensions.Logging",
        "Mono.Cecil", "MonoMod.Core", "MonoMod.Utils",
        "System.Buffers", "System.Memory", "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe", "System.Threading.Tasks.Extensions", "System.ValueTuple",
    };

    private static int _installed;

    public static void Install()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            DiagLog.Log(Tag, $"installed AssemblyResolve for {_redirectedNames.Length} simple names");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "Install", ex); }
    }

    private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        try
        {
            var requested = new AssemblyName(args.Name);
            if (Array.IndexOf(_redirectedNames, requested.Name) < 0) return null;
            // Collect ALL loaded assemblies matching the simple name. A consumer
            // mod may have LoadFrom'd its own copy of a redirected assembly (e.g.
            // Newtonsoft.Json, MCMv5, a Microsoft.Extensions.* lib) before this
            // fires -- SettingsRegistry.EagerLoadModuleAssemblies deliberately
            // LoadFrom's consumer DLLs, so duplicates are plausible. Returning an
            // arbitrary first-loaded copy defeats the shim's whole purpose (funnel
            // everyone onto OUR canonical copy) and can reintroduce the version-skew
            // MissingMethodException class this framework exists to prevent. So we
            // PREFER the assembly loaded from the BetaDeps-managed module tree, and
            // only fall back to the first match when none of them is ours.
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, requested.Name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 0)
            {
                var match = matches.FirstOrDefault(a => IsBetaDepsManagedDir(SafeDir(a))) ?? matches[0];
                if (matches.Count > 1)
                    DiagLog.Log(Tag, $"redirect {requested.Name} v{requested.Version} -> {matches.Count} loaded copies; chose v{match.GetName().Version} from '{SafeDir(match)}' (BetaDeps-managed preferred)");
                else
                    DiagLog.Log(Tag, $"redirect {requested.Name} v{requested.Version} -> already-loaded v{match.GetName().Version}");
                return match;
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"OnAssemblyResolve({args.Name})", ex); }
        return null;
    }

    // Mirror of HarmonyRuntimeGate.IsBetaDepsManagedDir -- duplicated here because
    // Foundation is the lowest assembly in the stack and cannot reference
    // BetaDeps.Harmony. KEEP THE TWO COPIES IN SYNC (the `managed` list below and
    // HarmonyRuntimeGate.cs's must match). True when the directory belongs to the
    // BetaDeps-managed module tree (Modules\BetaDeps\bin\... or one of the four
    // dependency modules).
    private static bool IsBetaDepsManagedDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        var norm = dir.Replace('/', '\\');
        string[] managed = {
            // Retained even though the ModReady installer ships no BetaDeps folder:
            // a user who ALSO installs the standalone BetaDeps framework mod will
            // have Modules\BetaDeps\, and its Foundation copy is equally canonical.
            "\\Modules\\BetaDeps\\",
            "\\Modules\\Bannerlord.Harmony\\",
            "\\Modules\\Bannerlord.UIExtenderEx\\",
            "\\Modules\\Bannerlord.ButterLib\\",
            "\\Modules\\Bannerlord.MBOptionScreen\\"
        };
        foreach (var seg in managed)
        {
            if (norm.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static string SafeDir(Assembly a)
    {
        try
        {
            var loc = a.Location;
            return string.IsNullOrEmpty(loc) ? string.Empty : (Path.GetDirectoryName(loc) ?? string.Empty);
        }
        catch { return string.Empty; }
    }
}
