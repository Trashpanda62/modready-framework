// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// The ButterLib SubSystem contract. Consumer mods (and ButterLib itself)
// register units of optional functionality as SubSystems. Each subsystem
// has an Id, can be enabled/disabled independently, and exposes lifecycle
// hooks the framework calls in order during ButterLibSubModule load.

namespace Bannerlord.ButterLib.SubSystems;

/// <summary>
/// Marker interface for a discrete unit of ButterLib functionality. The
/// framework discovers ISubSystem instances via DI and orchestrates their
/// lifecycle.
/// </summary>
public interface ISubSystem
{
    /// <summary>Stable identifier (e.g. "ExceptionHandler", "HotKeys").</summary>
    string Id { get; }

    /// <summary>Display name suitable for diagnostic output.</summary>
    string Name { get; }

    /// <summary>Description shown to the user.</summary>
    string Description { get; }

    /// <summary>Whether the subsystem is currently enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Whether the user can toggle this subsystem at runtime. Some subsystems
    /// (exception handler) require a restart to take effect.
    /// </summary>
    bool CanBeDisabled { get; }

    /// <summary>Bring the subsystem online.</summary>
    void Enable();

    /// <summary>Tear the subsystem down.</summary>
    void Disable();
}
