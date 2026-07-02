// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Helpers that mirror upstream ButterLib's IServiceCollection extension methods
// so consumer mods can register their services with the same call shape:
//
//   services.AddSubSystems();
//   services.AddSettingsProvider();
//
// These are no-ops here at present; full implementation lands as the
// individual subsystems come online.

using Microsoft.Extensions.DependencyInjection;

namespace Bannerlord.ButterLib.Common.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ModReady default SubSystem services. Called by
    /// ButterLibSubModule during OnSubModuleLoad. Consumer mods may also
    /// extend the registration via their own AddSomething() helpers.
    /// </summary>
    public static IServiceCollection AddModReadyButterLib(this IServiceCollection services)
    {
        return services;
    }
}
