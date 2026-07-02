// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FluentPerSaveSettings is the per-save-scope counterpart to
// FluentGlobalSettings. Consumer mods (notably ButterEquipped v1.3.13+)
// build their settings imperatively via ISettingsBuilder and bind them
// to the per-save scope:
//
//     var settings = BaseSettingsBuilder.Create("MyMod_save", "My Mod")
//         .CreateGroup("General", g => g.AddBool("foo", b => ...))
//         .BuildAsPerSave();
//
// Phase 2.3/2.4 (findings H6 + H5, 2026-06-10 review): this class used to be
// a type-load shim -- values were never loaded, never saved, never shown in
// the panel, and IRef bindings were ignored. It now rides the same pipeline
// as FluentGlobalSettings (IFluentSettings + FluentRefs write-through) and
// persists via SettingsStorage's per-save scoped path
// (Configs\ModSettings\PerSave\<campaignId>\<Id>.json).
//
// Scope note: true upstream per-save persistence lives INSIDE the save file.
// ModReady scopes by campaign id instead -- values can no longer bleed
// across campaigns (the H5 bug), but two manual saves within one campaign
// share values. Documented limitation; revisit if a consumer mod measurably
// depends on save-file granularity.

using System;
using System.Collections.Generic;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.PerSave
{

public sealed class FluentPerSaveSettings : BasePerSaveSettings, MCM.Internal.IFluentSettings
{
    private const string Tag = "MCM.FluentPerSaveSettings";

    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FluentProperty> _props = new(StringComparer.Ordinal);
    // Per-property compiled default, snapshotted in the ctor before Load runs.
    private readonly Dictionary<string, object?> _defaults = new(StringComparer.Ordinal);

    public override string Id { get; }
    public override string DisplayName { get; }

    internal FluentPerSaveSettings(SettingsBuilderImpl builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        Id = builder.Id;
        DisplayName = builder.DisplayName;

        foreach (var g in builder._groups)
        {
            foreach (var p in g._properties)
            {
                if (!_values.ContainsKey(p.Id))
                {
                    _values[p.Id] = p.Value;
                    _props[p.Id] = p;
                    // ctor runs before Load -> IRef live value is the compiled default.
                    try { _defaults[p.Id] = p.Ref != null ? p.Ref.Value : p.Value; }
                    catch { _defaults[p.Id] = p.Value; }
                }
            }
        }

        // H5: when the campaign changes, drop back to declaration defaults
        // and re-load from the new campaign's scoped file on next access.
        MCM.Internal.ScopedSettingsTracker.Register($"fluent-persave:{Id}", ReloadForCurrentScope);
    }

    public T? Get<T>(string id)
    {
        object? v;
        if (_props.TryGetValue(id, out var prop) && prop.Ref != null)
        {
            try { v = prop.Ref.Value; }
            catch (Exception ex)
            {
                ModReady.Foundation.DiagLog.LogCaught(Tag, $"Get({Id}.{id}) ref read", ex);
                _values.TryGetValue(id, out v);
            }
        }
        else if (!_values.TryGetValue(id, out v)) return default;
        if (v == null) return default;
        if (v is T typed) return typed;
        try { return (T)Convert.ChangeType(v, typeof(T), System.Globalization.CultureInfo.InvariantCulture); }
        catch { return default; }
    }

    public void Set(string id, object? value)
    {
        _values[id] = value;
        _props.TryGetValue(id, out var prop);
        MCM.Internal.FluentRefs.WriteThrough(Tag, Id, prop, value);
        OnPropertyChanged(id);
    }

    public int ResetToDefaults()
    {
        int n = 0;
        foreach (var kv in _defaults)
        {
            try { Set(kv.Key, kv.Value); n++; }
            catch (Exception ex) { ModReady.Foundation.DiagLog.LogCaught(Tag, $"ResetToDefaults({Id}.{kv.Key})", ex); }
        }
        return n;
    }

    // Legacy accessors kept for any existing internal callers.
    public object? GetValue(string id) => _values.TryGetValue(id, out var v) ? v : null;
    public void SetValue(string id, object? value) => Set(id, value);

    IReadOnlyDictionary<string, object?> MCM.Internal.IFluentSettings.ValuesSnapshot => _values;

    /// <summary>Re-seed declaration defaults, then load the current
    /// campaign's scoped JSON (pushes through refs via Set).</summary>
    private void ReloadForCurrentScope()
    {
        foreach (var kv in _props)
            _values[kv.Key] = kv.Value.Value;
        try { MCM.Internal.SettingsStorage.Load(this, Id); } catch { }
    }

    /// <summary>Consumer mods chain .BuildAsPerSave().Register(); idempotent.</summary>
    public void Register() => MCM.Internal.FluentSettingsRegistry.Register(this);

    /// <summary>Counterpart to Register(); no-op if not registered.</summary>
    public void Unregister() => MCM.Internal.FluentSettingsRegistry.Unregister(this);
}

}
