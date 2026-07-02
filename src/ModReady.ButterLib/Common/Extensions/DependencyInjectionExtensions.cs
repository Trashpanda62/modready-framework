// ModReady clean-room.
//
// Bannerlord.ButterLib.Common.Extensions.DependencyInjectionExtensions
//
// Extension methods on MBSubModuleBase that consumer mods use to wire up
// their DI + logging. Only the methods actively called by mods in the
// user's load list are implemented; everything else falls back to no-op
// shims returning fresh ServiceCollections so call chains don't NRE.

using System;
using System.Collections.Generic;

using ModReady.Foundation;

using Microsoft.Extensions.DependencyInjection;
// Do NOT 'using Microsoft.Extensions.Logging' at the top -- it imports ILogger
// which collides with Serilog.ILogger. We fully-qualify Microsoft.Extensions.Logging
// types below to avoid the ambiguity.

using Serilog;

using TaleWorlds.MountAndBlade;

// Aliases for the M.E.Logging types we use, to keep the call sites tidy.
using MELogger = Microsoft.Extensions.Logging.ILogger;
using MELoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;
using NullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger;
using NullLoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory;

namespace Bannerlord.ButterLib.Common.Extensions;

public static class DependencyInjectionExtensions
{
    private const string Tag = "ButterLib.DI";

    /// <summary>
    /// Returns the shared service provider published by ButterLibSubModule.
    /// Consumer mods call this to look up registered services from any
    /// context (gameplay event handlers, save game events, etc.).
    ///
    /// Upstream BUTR ButterLib v2.7.x declared this as an extension method
    /// on MBSubModuleBase (i.e. `this MBSubModuleBase subModule`), so FCL
    /// and similar consumer mods call it as `this.GetTempServiceProvider()`
    /// and the IL records the call signature with an MBSubModuleBase param.
    /// We match that signature here, and keep a no-arg overload too so
    /// internal/older callers keep working.
    /// </summary>
    public static IServiceProvider GetTempServiceProvider(this MBSubModuleBase subModule) => _SharedProvider;

    /// <summary>Backward-compat no-arg overload (rarely used but cheap to provide).</summary>
    public static IServiceProvider GetTempServiceProvider() => _SharedProvider;

    /// <summary>
    /// Upstream BUTR ButterLib distinguishes the TEMP service provider
    /// (available during OnSubModuleLoad before the real DI container has
    /// been built) from the LIVE service provider returned by this method,
    /// which becomes available once ButterLibSubModule.OnSubModuleLoad
    /// finishes its container build. Diplomacy and several other mods call
    /// `this.GetServiceProvider()` from their own SubModule.OnSubModuleLoad
    /// to obtain an ILogger, and throw MissingMethodException without this
    /// signature. We return the same shared provider as GetTempServiceProvider
    /// — both go through ServiceProviderShim, which resolves ILogger&lt;T&gt; to
    /// NullLogger&lt;T&gt; so the consumer's GetRequiredService doesn't throw.
    /// </summary>
    public static IServiceProvider GetServiceProvider(this MBSubModuleBase subModule) => _SharedProvider;

    /// <summary>No-arg overload for older callers.</summary>
    public static IServiceProvider GetServiceProvider() => _SharedProvider;

    /// <summary>
    /// Upstream BUTR API: the open IServiceCollection consumer mods register
    /// their services into during the load phase (their OnSubModuleLoad).
    /// Returns null once ButterLib has built the container at
    /// OnBeforeInitialModuleScreenSetAsRoot -- same lifecycle contract as
    /// upstream. Before Phase 2C the container was built (and sealed) during
    /// ButterLib's own load, so consumer registration was impossible (H14).
    /// </summary>
    public static IServiceCollection? GetServices(this MBSubModuleBase subModule)
    {
        var open = GenericServiceProvider.OpenCollection;
        if (open == null)
        {
            CompatWarn.Once("ButterLib.DI", "GetServices (after container sealed)",
                subModule?.GetType().Assembly.GetName().Name,
                "service registration attempted after the container was built; returns null per upstream contract");
        }
        return open;
    }

    /// <summary>
    /// Upstream BUTR ButterLib signature; consumer mods like Fluid Combat Lite
    /// call this from their SubModule.OnSubModuleLoad to set up a per-mod
    /// Serilog file logger. We stub it as a no-op that returns a fresh
    /// ServiceCollection so the consumer's call chain succeeds without
    /// throwing MissingMethodException.
    ///
    /// If consumer mods need actual logging in the future, this is the
    /// place to wire up a real Serilog -> ILoggerProvider pipeline. For now
    /// the diagnostic value is captured via ModReady's own runtime.log.
    /// </summary>
    public static IServiceCollection AddSerilogLoggerProvider(
        this MBSubModuleBase subModule,
        string fileName,
        IEnumerable<string>? alsoEnableInputCategories = null,
        Action<LoggerConfiguration>? configureExtra = null)
    {
        try
        {
            DiagLog.Log(Tag, $"AddSerilogLoggerProvider stub: subModule={subModule?.GetType().Name ?? "?"}, file={fileName ?? "?"}");
        }
        catch { }
        return new ServiceCollection();
    }

    /// <summary>
    /// Three-arg overload (no Action). Some older consumer mods bind to this
    /// signature.
    /// </summary>
    public static IServiceCollection AddSerilogLoggerProvider(
        this MBSubModuleBase subModule,
        string fileName,
        IEnumerable<string>? alsoEnableInputCategories = null)
        => AddSerilogLoggerProvider(subModule, fileName, alsoEnableInputCategories, null);

    /// <summary>Two-arg overload.</summary>
    public static IServiceCollection AddSerilogLoggerProvider(
        this MBSubModuleBase subModule,
        string fileName)
        => AddSerilogLoggerProvider(subModule, fileName, null, null);

    // ------------------------------------------------------------------
    // Internal shared service provider plumbing.
    // ------------------------------------------------------------------
    private static readonly IServiceProvider _SharedProvider = new ServiceProviderShim();
    private sealed class ServiceProviderShim : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == null) return null;

            // Consumer mods (FCL etc.) call `provider.GetRequiredService<ILogger<MyType>>()`
            // right after AddSerilogLoggerProvider returns. Default to NullLogger<T>
            // so GetRequiredService doesn't throw InvalidOperationException. NullLogger
            // swallows all log calls; if we ever wire up real Serilog->ILogger plumbing,
            // this is the place to plug it in.
            if (serviceType.IsGenericType
                && serviceType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>))
            {
                try
                {
                    var argType = serviceType.GetGenericArguments()[0];
                    var nullLoggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>).MakeGenericType(argType);
                    return Activator.CreateInstance(nullLoggerType);
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"GetService(ILogger<{serviceType.GetGenericArguments()[0].FullName}>)", ex);
                }
            }

            if (serviceType == typeof(MELoggerFactory))
                return NullLoggerFactory.Instance;

            if (serviceType == typeof(MELogger))
                return NullLogger.Instance;

            return GenericServiceProvider.GetService(serviceType);
        }
    }
}
