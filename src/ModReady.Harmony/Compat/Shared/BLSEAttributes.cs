// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// BLSEExceptionHandlerAttribute and BLSEInterceptorAttribute -- marker
// attributes BLSE (Bannerlord Software Extender) reads at boot to wire up
// per-mod exception handler / loader-interceptor hooks. Consumer mods
// declare them; BLSE finds them via reflection.

using System;

namespace Bannerlord.BUTR.Shared.Helpers;

/// <summary>
/// Marks a static method as a per-mod exception handler. BLSE invokes
/// the method with the exception when an unhandled exception occurs in
/// the marked module's code paths.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BLSEExceptionHandlerAttribute : Attribute { }

/// <summary>
/// Marks a static method as a BLSE loader interceptor. Called once at
/// game launch before the rest of the module loads. Lets mods inject
/// early diagnostics or environment checks.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class BLSEInterceptorAttribute : Attribute { }
