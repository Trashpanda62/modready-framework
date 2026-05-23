// BetaDeps clean-room. MIT, copyright 2026 Maxfield Management Group.
//
// Consumer mods are compiled against specific historical AssemblyVersions of
// BUTR-ecosystem assemblies. This static initializer installs an
// AppDomain.AssemblyResolve handler that returns whatever loaded assembly
// matches the requested SIMPLE NAME, ignoring version. Currently NOT called
// from any SubModule.OnSubModuleLoad path -- only the class is shipped, so
// consumer mods that explicitly invoke AssemblyVersionShim.Install() can
// opt in.

using System;
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
            var match = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, requested.Name, StringComparison.Ordinal));
            if (match != null)
            {
                DiagLog.Log(Tag, $"redirect {requested.Name} v{requested.Version} -> already-loaded v{match.GetName().Version}");
                return match;
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"OnAssemblyResolve({args.Name})", ex); }
        return null;
    }
}
