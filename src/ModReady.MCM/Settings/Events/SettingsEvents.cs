// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Settings lifecycle events. Consumer mods (and CREST in particular) subscribe
// to these to wire crest.json sync, refresh visibility hooks, etc.
//
// Events fire from SettingsRegistry and SettingsStorage at the appropriate
// moments:
//   LoadingComplete  -> after DiscoverAll has run on game start
//   SaveTriggered    -> when SettingsStorage.Save is called from any source
//   PresetRegistered -> when a preset is added (deferred; presets land later)

using System;

namespace MCM.Abstractions.Events;

public sealed class SettingsLifecycleEventArgs : EventArgs
{
    public string SettingsId { get; }
    public SettingsLifecycleEventArgs(string settingsId) { SettingsId = settingsId ?? string.Empty; }
}

public static class SettingsEvents
{
    /// <summary>Fired after DiscoverAll registers every AttributeGlobalSettings instance.</summary>
    public static event EventHandler<SettingsLifecycleEventArgs>? LoadingComplete;

    /// <summary>Fired every time a settings instance is saved to JSON.</summary>
    public static event EventHandler<SettingsLifecycleEventArgs>? SaveTriggered;

    internal static void RaiseLoadingComplete(string settingsId)
    {
        try { LoadingComplete?.Invoke(null, new SettingsLifecycleEventArgs(settingsId)); }
        catch { /* never throw from event raise */ }
    }

    internal static void RaiseSaveTriggered(string settingsId)
    {
        try { SaveTriggered?.Invoke(null, new SettingsLifecycleEventArgs(settingsId)); }
        catch { /* never throw from event raise */ }
    }
}
