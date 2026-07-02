// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// The SubSystem-level settings contract. Each ButterLib subsystem exposes
// an ISubSystemSettings instance that ButterLib resolves to know which
// subsystems the user has enabled / disabled. The MCM UI binds against
// these to render the SubSystems tab.

namespace Bannerlord.ButterLib.SubSystems.Settings;

/// <summary>
/// Base settings shape: each subsystem typically exposes a single bool that
/// gates its enablement, plus optional per-subsystem config.
/// </summary>
public interface ISubSystemSettings
{
    bool IsEnabled { get; set; }
}

/// <summary>
/// Generic variant carrying the implementing type. Consumer subsystems
/// implement this with TSelf = their settings class, and add a static
/// Instance property on the implementing class itself (the interface can't
/// declare a static member on net472).
/// </summary>
public interface ISubSystemSettings<TSelf> : ISubSystemSettings where TSelf : class { }
