// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Stub declarations for the BUTR.DependencyInjection.* interfaces that
// consumer mods (and the upstream BUTR ButterLib runtime) reference at
// type-load time. v0.7.2 added these to stop the wall of TypeLoadException
// entries in user runtime.logs (1000+ per session on heavy modlists).
//
// We're not implementing DI semantics yet -- consumer mods that actually
// resolve services through these interfaces still won't get useful behavior,
// but at least the CLR type-load step succeeds and the mod's submodule
// can construct without throwing.

using System;

namespace BUTR.DependencyInjection
{
    /// <summary>Container that holds a registered service map. Stub.</summary>
    public interface IGenericServiceContainer
    {
        void AddSingleton(Type serviceType, object instance);
        void AddTransient(Type serviceType, Func<object> factory);
    }

    /// <summary>Factory that produces service instances. Stub.</summary>
    public interface IGenericServiceFactory
    {
        object? Create(Type serviceType);
    }

    /// <summary>Resolver for registered services. Stub.</summary>
    public interface IGenericServiceProvider
    {
        object? GetService(Type serviceType);
    }

    /// <summary>Scoped resolver (per-request lifetime). Stub.</summary>
    public interface IGenericServiceProviderScope : IDisposable
    {
        IGenericServiceProvider ServiceProvider { get; }
    }
}
