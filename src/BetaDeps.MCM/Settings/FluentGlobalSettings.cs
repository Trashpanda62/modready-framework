// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FluentGlobalSettings is the runtime representation of settings built via
// BaseSettingsBuilder. It holds the property bag in-memory + persists to
// JSON like AttributeGlobalSettings, but the properties are dictionary-keyed
// instead of reflected from a class declaration.
//
// Public in MCM.Abstractions.Base.Global because consumer mods reference it
// as the return type of ISettingsBuilder.BuildAsGlobal() -- e.g. they chain
// `var instance = builder.BuildAsGlobal();` and pass `instance` to other
// MCM APIs. The internal alias in MCM.Internal is kept so existing callers
// inside our own code don't have to be rewritten.

using System;
using System.Collections.Generic;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.Global
{

public sealed class FluentGlobalSettings : BaseSettings
{
    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public override string Id { get; }
    public override string DisplayName { get; }

    internal FluentGlobalSettings(SettingsBuilderImpl builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        Id = builder.Id;
        DisplayName = builder.DisplayName;

        // Seed defaults from the builder.
        foreach (var g in builder._groups)
        {
            foreach (var p in g._properties)
            {
                _values[p.Id] = p.Value;
            }
        }
    }

    public T? Get<T>(string id)
    {
        if (!_values.TryGetValue(id, out var v) || v == null) return default;
        try { return (T)Convert.ChangeType(v, typeof(T)); }
        catch { return default; }
    }

    public void Set(string id, object? value)
    {
        _values[id] = value;
        OnPropertyChanged(id);
    }

    internal IReadOnlyDictionary<string, object?> Values => _values;

    /// <summary>
    /// Upstream BUTR FluentGlobalSettings exposes Register() so consumers can
    /// chain `builder.BuildAsGlobal().Register()` for symmetry with the rest
    /// of the fluent API. Our BuildAsGlobal already registers this instance
    /// via FluentSettingsRegistry, so Register() is a (safe, idempotent)
    /// re-register: it ensures the singleton list contains us exactly once.
    /// </summary>
    public void Register()
    {
        MCM.Internal.FluentSettingsRegistry.Register(this);
    }
}

} // namespace MCM.Abstractions.Base.Global


// FluentSettingsRegistry stays internal in MCM.Internal so existing callers
// (BUTRSettingsContainer, ModOptionsVM) keep compiling.
namespace MCM.Internal
{

using MCM.Abstractions.Base.Global;

internal static class FluentSettingsRegistry
{
    private static readonly List<FluentGlobalSettings> _all = new();
    private static readonly object _gate = new();

    public static void Register(FluentGlobalSettings settings)
    {
        if (settings == null) return;
        lock (_gate)
        {
            // Idempotent: consumer mods commonly chain
            // .BuildAsGlobal().Register() which double-registers. Only add
            // if not already present.
            if (!_all.Contains(settings)) _all.Add(settings);
        }

        // On first registration, load any existing on-disk JSON into the
        // fluent dictionary so user-customized values survive a game launch.
        // Attribute-based settings get this for free via their singleton
        // Instance accessor; fluent settings have no such hook.
        try { SettingsStorage.Load(settings, settings.Id); } catch { }
    }

    public static IReadOnlyList<FluentGlobalSettings> All
    {
        get { lock (_gate) { return _all.ToArray(); } }
    }
}

}
