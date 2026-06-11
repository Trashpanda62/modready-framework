// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FluentPerCampaignSettings is the per-campaign-scope counterpart to
// FluentGlobalSettings and FluentPerSaveSettings. Scope distinction (per
// upstream BUTR MCM convention):
//   - Global       -- one settings instance shared across all campaigns
//   - PerCampaign  -- one instance per active campaign (survives saves
//                     within that campaign, lost when a new campaign starts)
//   - PerSave      -- one instance per save file (resets on new campaign)
//
// XorberaxLegacy v1.x and other mods built against newer BUTR MCM revisions
// call ISettingsBuilder.BuildAsPerCampaign() during new-campaign init.
//
// Phase 2.3/2.4 (findings H6 + H5, 2026-06-10 review): upgraded from a
// type-load shim to the full pipeline -- IFluentSettings + FluentRefs IRef
// write-through, registered for persistence + panel rendering, stored at
// SettingsStorage's per-campaign scoped path
// (Configs\ModSettings\PerCampaign\<campaignId>\<Id>.json), and reset on
// campaign change via ScopedSettingsTracker.

using System;
using System.Collections.Generic;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.PerCampaign
{

public sealed class FluentPerCampaignSettings : BasePerCampaignSettings, MCM.Internal.IFluentSettings
{
    private const string Tag = "MCM.FluentPerCampaignSettings";

    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FluentProperty> _props = new(StringComparer.Ordinal);
    // Per-property compiled default, snapshotted in the ctor before Load runs.
    private readonly Dictionary<string, object?> _defaults = new(StringComparer.Ordinal);

    public override string Id { get; }
    public override string DisplayName { get; }

    internal FluentPerCampaignSettings(SettingsBuilderImpl builder)
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

        MCM.Internal.ScopedSettingsTracker.Register($"fluent-percampaign:{Id}", ReloadForCurrentScope);
    }

    public T? Get<T>(string id)
    {
        object? v;
        if (_props.TryGetValue(id, out var prop) && prop.Ref != null)
        {
            try { v = prop.Ref.Value; }
            catch (Exception ex)
            {
                BetaDeps.Foundation.DiagLog.LogCaught(Tag, $"Get({Id}.{id}) ref read", ex);
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
            catch (Exception ex) { BetaDeps.Foundation.DiagLog.LogCaught(Tag, $"ResetToDefaults({Id}.{kv.Key})", ex); }
        }
        return n;
    }

    // Legacy accessors kept for any existing internal callers.
    public object? GetValue(string id) => _values.TryGetValue(id, out var v) ? v : null;
    public void SetValue(string id, object? value) => Set(id, value);

    IReadOnlyDictionary<string, object?> MCM.Internal.IFluentSettings.ValuesSnapshot => _values;

    private void ReloadForCurrentScope()
    {
        foreach (var kv in _props)
            _values[kv.Key] = kv.Value.Value;
        try { MCM.Internal.SettingsStorage.Load(this, Id); } catch { }
    }

    /// <summary>
    /// Consumer mods (XorberaxLegacy v1.x) chain
    ///     builder.BuildAsPerCampaign().Register();
    /// Phase 2.3: now actually registers (used to be an intentional no-op
    /// when the registry was global-only). Idempotent.
    /// </summary>
    public void Register() => MCM.Internal.FluentSettingsRegistry.Register(this);

    /// <summary>Counterpart to Register(); no-op if not registered.</summary>
    public void Unregister() => MCM.Internal.FluentSettingsRegistry.Unregister(this);
}

}
