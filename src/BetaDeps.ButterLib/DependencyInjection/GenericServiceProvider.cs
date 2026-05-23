// BetaDeps clean-room re-implementation of Bannerlord.ButterLib's
// GenericServiceProvider. MIT, copyright 2026 Maxfield Management Group.
//
// The DI gateway consumer mods use to resolve services configured by
// ButterLib at startup. Wraps Microsoft.Extensions.DependencyInjection.
// Typical consumer pattern:
//
//   var logger = GenericServiceProvider.GetService<ILogger<MyMod>>();
//   var settings = GenericServiceProvider.GetService<IBUTRGlobalSettings>();
//
// BetaDeps maintains a single root ServiceProvider that ButterLibSubModule
// builds during OnSubModuleLoad. Consumer mods register services into
// IServiceCollection at the right lifecycle point.

using System;
using System.Collections.Generic;

using BetaDeps.Foundation;

using Microsoft.Extensions.DependencyInjection;

namespace Bannerlord.ButterLib;

/// <summary>
/// Static gateway over the root DI service provider. Consumer mods call
/// GetService&lt;T&gt; to resolve a registered service.
/// </summary>
public static class GenericServiceProvider
{
    private const string Tag = "GenericServiceProvider";

    private static IServiceProvider? _serviceProvider;
    private static readonly object _gate = new();

    /// <summary>
    /// Resolve a service of type T. Returns null if no service is registered
    /// or if the DI container hasn't been built yet.
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        var sp = _serviceProvider;
        if (sp == null) return null;
        try { return sp.GetService<T>(); }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetService<{typeof(T).Name}>", ex);
            return null;
        }
    }

    /// <summary>Resolve a service by Type. Returns null on miss.</summary>
    public static object? GetService(Type type)
    {
        var sp = _serviceProvider;
        if (sp == null) return null;
        try { return sp.GetService(type); }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetService({type.FullName})", ex);
            return null;
        }
    }

    /// <summary>
    /// Resolve every registered service of type T. Returns an empty list if
    /// none are registered.
    /// </summary>
    public static IEnumerable<T> GetServices<T>() where T : class
    {
        var sp = _serviceProvider;
        if (sp == null) return Array.Empty<T>();
        try { return sp.GetServices<T>(); }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetServices<{typeof(T).Name}>", ex);
            return Array.Empty<T>();
        }
    }

    /// <summary>
    /// Replace the root service provider. Called once from ButterLibSubModule
    /// after configuring services. Idempotent attempts are silently ignored.
    /// </summary>
    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
        lock (_gate)
        {
            if (_serviceProvider != null)
            {
                DiagLog.Log(Tag, "SetServiceProvider called twice; second call ignored");
                return;
            }
            _serviceProvider = serviceProvider;
            DiagLog.Log(Tag, "root service provider set");
        }
    }

    /// <summary>Whether the DI container is built and ready.</summary>
    public static bool IsReady => _serviceProvider != null;
}
