// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Zero-dependency bridge between ModReady.ButterLib and ModReady.MCM.
// Neither assembly references the other (Foundation is the only shared base),
// so this static relay lets ButterLib publish its subsystem roster at load time
// and MCM consume it when building the Mod Config settings page.
//
// ButterLib wires all four delegates in its OnSubModuleLoad (before MCM's
// DiscoverAll runs). MCM checks IsAvailable in SettingsRegistry.DiscoverAll and
// creates the SubSystem settings page only if the bridge is populated.

using System;
using System.Collections.Generic;

namespace ModReady.Foundation;

public static class SubSystemBridge
{
    /// <summary>Returns the current subsystem roster. Called by MCM at page build time.</summary>
    public static Func<IReadOnlyList<(string Id, string Name, string Desc, bool IsEnabled, bool CanBeDisabled)>>? GetAll { get; set; }

    /// <summary>Returns whether the subsystem with the given id is currently enabled.</summary>
    public static Func<string, bool>? GetEnabled { get; set; }

    /// <summary>Enables or disables the subsystem with the given id.</summary>
    public static Action<string, bool>? SetEnabled { get; set; }

    /// <summary>Flushes subsystem enabled state to disk (called by MCM after each toggle).</summary>
    public static Action? Save { get; set; }

    /// <summary>True once ButterLib has wired the bridge.</summary>
    public static bool IsAvailable => GetAll != null && SetEnabled != null;
}
