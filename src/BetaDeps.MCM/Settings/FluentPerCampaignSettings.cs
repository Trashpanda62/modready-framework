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
// Without this type, the CLR throws MissingMethodException at the
// JIT-bind site -> our SettingsStorage was catching the throw via the
// PropertyChanged reset path, which fed an infinite save/load loop that
// CTD'd the game on new-campaign creation.
//
// Implementation-wise this is a thin wrapper around the same builder
// state FluentGlobalSettings uses. The per-campaign scope label exists
// for type-load purposes; v0.8 may differentiate actual persistence
// behaviour, but for now the dictionary semantics are identical.

using System;
using System.Collections.Generic;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.PerCampaign
{

public sealed class FluentPerCampaignSettings : BasePerCampaignSettings
{
    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public override string Id { get; }
    public override string DisplayName { get; }

    internal FluentPerCampaignSettings(SettingsBuilderImpl builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        Id = builder.Id;
        DisplayName = builder.DisplayName;

        // Seed defaults from the builder, same pattern as FluentGlobalSettings.
        foreach (var g in builder._groups)
        {
            foreach (var p in g._properties)
            {
                if (!_values.ContainsKey(p.Id))
                    _values[p.Id] = p.Value;
            }
        }
    }

    public object? GetValue(string id) =>
        _values.TryGetValue(id, out var v) ? v : null;

    public void SetValue(string id, object? value) =>
        _values[id] = value;

    /// <summary>
    /// Consumer mods (XorberaxLegacy v1.x) chain
    ///     builder.BuildAsPerCampaign().Register();
    /// so this method has to exist with the exact name + void return.
    /// PerCampaign settings aren't held in FluentSettingsRegistry (which is
    /// global-only) so for now this is a no-op that just satisfies the
    /// JIT bind site; v0.8 will add per-campaign registration once we
    /// wire actual save-file persistence.
    /// </summary>
    public void Register()
    {
        // intentionally empty -- see XML doc above.
    }

    /// <summary>
    /// Mirror of FluentGlobalSettings.Unregister so consumer mods that
    /// call Unregister() during teardown don't trip MissingMethodException.
    /// </summary>
    public void Unregister()
    {
        // intentionally empty -- see Register() XML doc above.
    }
}

}
