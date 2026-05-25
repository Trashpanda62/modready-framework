// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
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
// The returned object inherits from BasePerSaveSettings so the engine
// treats it like any per-save settings class (values travel with the
// save file, Reset() on campaign-end).
//
// Implementation-wise this is a thin wrapper around the same builder
// state FluentGlobalSettings uses — the per-save scope difference is
// effectively a label until v0.8 wires actual save-file persistence.
// What MATTERS for consumer-mod compat is that the type exists with the
// exact name `MCM.Abstractions.Base.PerSave.FluentPerSaveSettings`, so
// the CLR's type-load step succeeds when ButterEquipped is JIT'd.

using System;
using System.Collections.Generic;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.PerSave
{

public sealed class FluentPerSaveSettings : BasePerSaveSettings
{
    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public override string Id { get; }
    public override string DisplayName { get; }

    internal FluentPerSaveSettings(SettingsBuilderImpl builder)
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
}

}
