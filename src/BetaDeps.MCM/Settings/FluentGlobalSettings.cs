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
using System.Globalization;

using BetaDeps.Foundation;

using MCM.Abstractions;
using MCM.Abstractions.FluentBuilder;

namespace MCM.Abstractions.Base.Global
{

public sealed class FluentGlobalSettings : BaseSettings, MCM.Internal.IFluentSettings
{
    private const string Tag = "MCM.FluentGlobalSettings";

    private readonly SettingsBuilderImpl _builder;
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    // Phase 2.2 / finding B2: id -> declared property, so Set/Get can reach
    // the consumer mod's IRef binding instead of stopping at the dictionary.
    private readonly Dictionary<string, FluentProperty> _props = new(StringComparer.Ordinal);

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
                _props[p.Id] = p;
            }
        }
    }

    // Phase 2.2 / finding B2 (2026-06-10 review): upstream MCMv5's fluent
    // builder is IRef-based EXCLUSIVELY -- consumer mods (Diplomacy,
    // RTSCamera, ImprovedGarrisons, BEW...) hand us ProxyRef/PropertyRef
    // bindings into their own state, and expect every read/write to flow
    // through them. The old implementation captured the refs at declaration
    // time and never touched them again: the UI showed values, edits
    // persisted to JSON, but the MOD ran on its compiled defaults forever.
    // Now:
    //   Get<T>  -> reads the ref's LIVE value when a ref is bound,
    //   Set     -> writes through to ref.Value (coerced to the ref's exact
    //              Type -- ProxyRef<T> hard-casts, so a boxed long where the
    //              mod expects int would throw and be dropped),
    //   Load    -> already routes through Set, so on-disk JSON reaches the
    //              mod's state at startup too.

    public T? Get<T>(string id)
    {
        object? v;
        if (_props.TryGetValue(id, out var prop) && prop.Ref != null)
        {
            try { v = prop.Ref.Value; }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"Get({Id}.{id}) ref read", ex);
                _values.TryGetValue(id, out v);
            }
        }
        else
        {
            if (!_values.TryGetValue(id, out v)) return default;
        }
        if (v == null) return default;
        if (v is T typed) return typed;
        try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); }
        catch { return default; }
    }

    public void Set(string id, object? value)
    {
        _values[id] = value;

        // B2 write-through: push into the consumer mod's binding.
        _props.TryGetValue(id, out var prop);
        MCM.Internal.FluentRefs.WriteThrough(Tag, Id, prop, value);

        OnPropertyChanged(id);
    }

    /// <summary>IFluentSettings: snapshot for serialization (Phase 2.3).</summary>
    IReadOnlyDictionary<string, object?> MCM.Internal.IFluentSettings.ValuesSnapshot => _values;

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

    /// <summary>
    /// v0.7.5 ship-blocker: counterpart to Register(). XorberaxLegacy (and
    /// other mods built against newer BUTR MCM revisions) call this during
    /// teardown / settings reset. Without the method, MissingMethodException
    /// at the call site fed the save/load feedback loop. Safe to call
    /// multiple times; no-ops if we weren't registered.
    /// </summary>
    public void Unregister()
    {
        MCM.Internal.FluentSettingsRegistry.Unregister(this);
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
    // Phase 2.3 / H6: element type widened from FluentGlobalSettings to
    // BaseSettings so per-save and per-campaign fluent instances register,
    // persist, and render too. (They used to be built and then dropped on
    // the floor: never loaded, never saved, never shown.)
    private static readonly List<MCM.Abstractions.BaseSettings> _all = new();
    private static readonly object _gate = new();

    public static void Register(MCM.Abstractions.BaseSettings settings)
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
        // (SettingsStorage routes per-save/per-campaign instances to their
        // scoped paths -- Phase 2.4 / H5.)
        try { SettingsStorage.Load(settings, settings.Id); } catch { }
    }

    /// <summary>
    /// v0.7.5: counterpart to Register(). Safe to call with a settings
    /// instance that was never registered (no-op in that case).
    /// </summary>
    public static void Unregister(MCM.Abstractions.BaseSettings settings)
    {
        if (settings == null) return;
        lock (_gate) { _all.Remove(settings); }
    }

    public static IReadOnlyList<MCM.Abstractions.BaseSettings> All
    {
        get { lock (_gate) { return _all.ToArray(); } }
    }
}

}
