// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Phase 2.3/2.4 (findings H6 + H5, 2026-06-10 review): shared plumbing for
// the three fluent settings scopes.
//
//   IFluentSettings        -- what storage/UI/save-on-done need from ANY
//                             fluent settings instance, regardless of scope.
//                             Replaces the hardcoded `is FluentGlobalSettings`
//                             checks that made Global the only scope that
//                             persisted or rendered.
//   FluentRefs             -- the B2 IRef write-through helpers (coercion to
//                             the ref's exact type; ProxyRef<T> hard-casts).
//   ScopedSettingsTracker  -- reset actions for per-save/per-campaign
//                             singletons, fired on campaign start/end so
//                             scoped values can't bleed between campaigns
//                             within one game session.

using System;
using System.Collections.Generic;
using System.Globalization;

using BetaDeps.Foundation;

namespace MCM.Internal;

/// <summary>
/// Scope-agnostic surface of a fluent settings instance. Implemented by
/// FluentGlobalSettings, FluentPerSaveSettings, FluentPerCampaignSettings.
/// </summary>
internal interface IFluentSettings
{
    string Id { get; }
    /// <summary>Read a value: prefers the consumer's live IRef when bound (B2).</summary>
    T? Get<T>(string id);
    /// <summary>Write a value: updates the dictionary AND pushes through the
    /// consumer's IRef binding when one was declared (B2).</summary>
    void Set(string id, object? value);
    /// <summary>Snapshot of the current values for serialization.</summary>
    IReadOnlyDictionary<string, object?> ValuesSnapshot { get; }
}

internal static class FluentRefs
{
    /// <summary>
    /// Convert a value (often JSON-shaped: long/double/string) to the exact
    /// type an IRef expects. ProxyRef&lt;T&gt;'s setter does a hard (T) cast, so a
    /// boxed long handed to a T==int ref silently drops the write.
    /// </summary>
    public static object? CoerceForRef(object? value, Type targetType)
    {
        if (value == null || targetType == null) return value;
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (t.IsInstanceOfType(value)) return value;
        try
        {
            if (t.IsEnum)
            {
                if (value is string es) return Enum.Parse(t, es, ignoreCase: true);
                return Enum.ToObject(t, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            }
            return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
        }
        catch
        {
            // Hand the original through; the ref's own guard logs the cast
            // failure with the consumer type name, which is the useful signal.
            return value;
        }
    }

    /// <summary>
    /// B2 write-through used by all three fluent scopes' Set(): push the new
    /// value into the consumer's IRef binding (skipping buttons, whose "ref"
    /// wraps the click handler, not a value).
    /// </summary>
    public static void WriteThrough(string ownerTag, string settingsId, MCM.Abstractions.FluentBuilder.FluentProperty? prop, object? value)
    {
        if (prop?.Ref == null) return;
        if (string.Equals(prop.TypeKind, "button", StringComparison.Ordinal)) return;
        try
        {
            prop.Ref.Value = CoerceForRef(value, prop.Ref.Type);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(ownerTag, $"Set({settingsId}.{prop.Id}) ref write", ex);
        }
    }
}

/// <summary>
/// Registry of reset actions for scoped settings singletons
/// (PerSaveSettings&lt;T&gt;.Instance / PerCampaignSettings&lt;T&gt;.Instance and the
/// fluent per-scope instances). BetaDepsMCMSubModule fires ResetAll on game
/// start AND game end so the next Instance access reloads under the new
/// campaign's storage path (H5) instead of carrying the old campaign's
/// values across.
/// </summary>
internal static class ScopedSettingsTracker
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, Action> _resets = new(StringComparer.Ordinal);

    public static void Register(string key, Action reset)
    {
        if (string.IsNullOrEmpty(key) || reset == null) return;
        lock (_gate) { _resets[key] = reset; }
    }

    public static void ResetAll(string reason)
    {
        KeyValuePair<string, Action>[] snapshot;
        lock (_gate) { snapshot = new List<KeyValuePair<string, Action>>(_resets).ToArray(); }
        if (snapshot.Length == 0) return;
        DiagLog.Log("MCM.ScopedSettings", $"ResetAll ({reason}): resetting {snapshot.Length} scoped settings singleton(s)");
        foreach (var kv in snapshot)
        {
            try { kv.Value(); }
            catch (Exception ex) { DiagLog.LogCaught("MCM.ScopedSettings", $"reset {kv.Key}", ex); }
        }
    }
}
