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
    private static ServiceCollection? _openCollection = new();
    private static bool _sealed;
    private static readonly object _gate = new();

    /// <summary>
    /// The open registration collection. Consumer mods reach it through
    /// DependencyInjectionExtensions.GetServices() during the load phase to
    /// register their own services -- the H14 "sealed DI" fix. Null once the
    /// container has been built (Seal), matching upstream's contract.
    /// </summary>
    internal static IServiceCollection? OpenCollection
    {
        get { lock (_gate) { return _sealed ? null : _openCollection; } }
    }

    /// <summary>
    /// Build the root provider from everything registered so far and close
    /// the collection. Called once from ButterLibSubModule's
    /// OnBeforeInitialModuleScreenSetAsRoot -- after every consumer mod's
    /// OnSubModuleLoad has had its chance to register.
    /// </summary>
    internal static void Seal()
    {
        lock (_gate)
        {
            if (_sealed) return;
            _sealed = true;
            var collection = _openCollection;
            _openCollection = null;
            try
            {
                if (collection != null)
                {
                    _serviceProvider = collection.BuildServiceProvider();
                    DiagLog.Log(Tag, $"container sealed with {collection.Count} service registration(s)");
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "Seal", ex);
            }
        }
    }

    /// <summary>
    /// Resolve a service of type T. Returns null if no service is registered
    /// or if the DI container hasn't been built yet.
    /// </summary>
    public static T? GetService<T>() where T : class => GetService(typeof(T)) as T;

    /// <summary>Resolve a service by Type. Returns null on miss.</summary>
    public static object? GetService(Type type)
    {
        var sp = _serviceProvider;
        if (sp == null) return null;
        try
        {
            var result = sp.GetService(type);
            if (result == null) WarnMissingButterLibService(type);
            return result;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetService({type.FullName})", ex);
            return null;
        }
    }

    /// <summary>
    /// A ButterLib-owned service resolving to null after seal means the shim
    /// surface exists but no implementation backs it (ObjectSystem store,
    /// IDistanceMatrixStatic, ...). Report once per type so consumers' dead
    /// features are diagnosable instead of silently absent (H14).
    /// </summary>
    private static void WarnMissingButterLibService(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        if (!ns.StartsWith("Bannerlord.ButterLib", StringComparison.Ordinal) &&
            !ns.StartsWith("BUTR.DependencyInjection", StringComparison.Ordinal)) return;
        CompatWarn.Once("ButterLib.DI", type.FullName ?? type.Name, null,
            "no implementation registered for this ButterLib service; GetService returned null");
    }

    /// <summary>
    /// Resolve every registered service of type T. Returns an empty list if
    /// none are registered. This is the RESOLUTION variant -- distinct from
    /// DependencyInjectionExtensions.GetServices(this MBSubModuleBase), the
    /// REGISTRATION variant that returns the open IServiceCollection.
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
    /// Replace the root service provider directly, bypassing the open
    /// collection. Kept for API compatibility; the normal path is now
    /// register-into-OpenCollection + Seal. Setting a provider here also
    /// closes the collection (further GetServices registrations would never
    /// be visible through the externally supplied provider).
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
            _sealed = true;
            _openCollection = null;
            DiagLog.Log(Tag, "root service provider set externally; open collection closed");
        }
    }

    /// <summary>Whether the DI container is built and ready.</summary>
    public static bool IsReady => _serviceProvider != null;
}
