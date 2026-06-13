// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// BaseSettingsBuilder is the concrete fluent builder. Constructs an in-memory
// FluentGlobalSettings instance (an internal BaseSettings subclass) and
// registers it with the SettingsRegistry.

using System;
using System.Collections.Generic;

using BetaDeps.Foundation;

// Per-type property builder interfaces moved into the nested Models namespace
// (see ISettingsBuilder.cs); pull them in here so PropBuilderBase and the
// typed subclasses can implement them by short name.
using MCM.Abstractions.FluentBuilder.Models;

namespace MCM.Abstractions.FluentBuilder;

public static class BaseSettingsBuilder
{
    /// <summary>Entry point for the fluent builder API.</summary>
    public static ISettingsBuilder Create(string id, string displayName)
        => new SettingsBuilderImpl(id, displayName);
}

internal sealed class SettingsBuilderImpl : ISettingsBuilder
{
    public string Id { get; }
    public string DisplayName { get; }

    internal readonly List<PropertyGroupBuilderImpl> _groups = new();

    public SettingsBuilderImpl(string id, string displayName)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? id;
    }

    public ISettingsBuilder CreateGroup(string groupName, Action<ISettingsPropertyGroupBuilder> action)
    {
        var g = new PropertyGroupBuilderImpl(groupName);
        action?.Invoke(g);
        _groups.Add(g);
        return this;
    }

        /// <summary>No-op SetFormat for API compatibility. We always store as JSON.</summary>
    public ISettingsBuilder SetFormat(string format) => this;

    /// <summary>No-op SetFolderName for API compatibility.</summary>
    public ISettingsBuilder SetFolderName(string folderName) => this;

    /// <summary>No-op SetSubFolder for API compatibility.</summary>
    public ISettingsBuilder SetSubFolder(string subFolder) => this;

    /// <summary>No-op SetSubGroupDelimiter for API compatibility.</summary>
    public ISettingsBuilder SetSubGroupDelimiter(string delimiter) => this;

    // Presets declared on this builder. Stored so a future UI pass can wire
    // a "Presets" dropdown into the fluent panel. For now this is enough to
    // unblock Retinues and any other consumer mod that fluent-declares
    // presets — the call no longer throws MissingMethodException and the
    // preset definitions are captured for later use.
    internal readonly List<SettingsPresetBuilderImpl> _presets = new();

    public ISettingsBuilder CreatePreset(string id, string name, Action<ISettingsPresetBuilder> presetBuilder)
    {
        var p = new SettingsPresetBuilderImpl();
        p.SetId(id ?? string.Empty);
        p.SetName(name ?? id ?? string.Empty);
        try { presetBuilder?.Invoke(p); }
        catch (Exception ex) { DiagLog.LogCaught("FluentBuilder", $"CreatePreset({id}) configure", ex); }
        _presets.Add(p);
        DiagLog.Log("FluentBuilder", $"preset '{p.Id}' declared on settings '{Id}' with {p.Values.Count} value(s)");
        return this;
    }

    public MCM.Abstractions.Base.Global.FluentGlobalSettings BuildAsGlobal()
    {
        MCM.Abstractions.Base.Global.FluentGlobalSettings? settings = null;
        try
        {
            settings = new MCM.Abstractions.Base.Global.FluentGlobalSettings(this);
            MCM.Internal.FluentSettingsRegistry.Register(settings);
            DiagLog.Log("FluentBuilder", $"registered fluent settings '{Id}' with {_groups.Count} group(s)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("FluentBuilder", $"BuildAsGlobal({Id})", ex);
        }
        return settings!;
    }

    public MCM.Abstractions.Base.PerSave.FluentPerSaveSettings BuildAsPerSave()
    {
        // ButterEquipped v1.3.13+ binds to this. Behaviour matches
        // BuildAsGlobal but produces the per-save-scope class so consumer
        // mods that branch on settings scope get the right type back.
        MCM.Abstractions.Base.PerSave.FluentPerSaveSettings? settings = null;
        try
        {
            settings = new MCM.Abstractions.Base.PerSave.FluentPerSaveSettings(this);
            // Phase 2.3 / H6: per-save fluent settings now register like
            // global ones -- persisted (scoped path, H5) and rendered.
            MCM.Internal.FluentSettingsRegistry.Register(settings);
            DiagLog.Log("FluentBuilder", $"BuildAsPerSave: registered per-save settings '{Id}' with {_groups.Count} group(s)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("FluentBuilder", $"BuildAsPerSave({Id})", ex);
        }
        return settings!;
    }

    public MCM.Abstractions.Base.PerCampaign.FluentPerCampaignSettings BuildAsPerCampaign()
    {
        // v0.7.5 ship-blocker: XorberaxLegacy calls this during new-campaign
        // init. Without the method existing, the JIT throws MissingMethodException,
        // which our MCM reset-on-failure flow turns into a save/load feedback
        // loop and CTDs the game. Implementation mirrors BuildAsPerSave with
        // a per-campaign scope type.
        MCM.Abstractions.Base.PerCampaign.FluentPerCampaignSettings? settings = null;
        try
        {
            settings = new MCM.Abstractions.Base.PerCampaign.FluentPerCampaignSettings(this);
            // Phase 2.3 / H6: see BuildAsPerSave note.
            MCM.Internal.FluentSettingsRegistry.Register(settings);
            DiagLog.Log("FluentBuilder", $"BuildAsPerCampaign: registered per-campaign settings '{Id}' with {_groups.Count} group(s)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("FluentBuilder", $"BuildAsPerCampaign({Id})", ex);
        }
        return settings!;
    }
}

internal sealed class PropertyGroupBuilderImpl : ISettingsPropertyGroupBuilder
{
    public string Name { get; }
    public int GroupOrder { get; private set; }
    internal readonly List<FluentProperty> _properties = new();

    public PropertyGroupBuilderImpl(string name) { Name = name ?? string.Empty; }

    /// <summary>
    /// No-op for now (UI doesn't surface group ordering yet), but we capture
    /// the order so a future UI pass can sort groups before rendering.
    /// </summary>
    public ISettingsPropertyGroupBuilder SetGroupOrder(int order)
    {
        GroupOrder = order;
        return this;
    }

    public ISettingsPropertyGroupBuilder AddBool(string id, string displayName, bool defaultValue, Action<ISettingsPropertyBoolBuilder>? configure = null)
    {
        var b = new BoolPropBuilder(id, displayName, defaultValue);
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddInteger(string id, string displayName, int min, int max, int defaultValue, Action<ISettingsPropertyIntegerBuilder>? configure = null)
    {
        var b = new IntPropBuilder(id, displayName, min, max, defaultValue);
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddFloatingInteger(string id, string displayName, float min, float max, float defaultValue, Action<ISettingsPropertyFloatingIntegerBuilder>? configure = null)
    {
        var b = new FloatPropBuilder(id, displayName, min, max, defaultValue);
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddText(string id, string displayName, string defaultValue, Action<ISettingsPropertyTextBuilder>? configure = null)
    {
        var b = new TextPropBuilder(id, displayName, defaultValue);
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    // IRef-based overloads: read the current ref value as the initial default
    // and stash the ref on the FluentProperty. Since Phase 2.2 / B2
    // (2026-06-10), FluentGlobalSettings.Set/Get round-trip through the ref:
    // edits in the panel and values loaded from JSON reach the consumer
    // mod's own state, and reads show the mod's live value. Upstream MCMv5's
    // fluent API is IRef-exclusive, so this IS the primary data path for
    // fluent consumers (Diplomacy, RTSCamera, BEW...).
    // Defensive bool coercion for the IRef overloads. IRef.Value is object?, so a
    // consumer can legally bind a non-bool ref (ProxyRef<int>, a string-valued
    // PropertyRef, etc.) to a bool-shaped Add*; a hard `(bool)` unbox would throw
    // InvalidCastException straight into that mod's OnSubModuleLoad (CreateGroup
    // invokes the lambda with no try/catch). Mirror the AddInteger/AddFloatingInteger
    // (v0.6) and AddText hardening so a quirky ref degrades to a logged default.
    private static bool CoerceBool(string id, MCM.Common.IRef? @ref)
    {
        try
        {
            var v = @ref?.Value;
            if (v is bool b) return b;
            if (v == null) return false;
            return Convert.ToBoolean(v);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("MCM.SettingsBuilder", $"AddBool({id}) ref bool coercion -> false", ex);
            return false;
        }
    }

    public ISettingsPropertyGroupBuilder AddBool(string id, string displayName, MCM.Common.IRef @ref, Action<ISettingsPropertyBoolBuilder>? configure = null)
    {
        var b = new BoolPropBuilder(id, displayName, CoerceBool(id, @ref));
        b.Build().Ref = @ref;
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddInteger(string id, string displayName, int min, int max, MCM.Common.IRef @ref, Action<ISettingsPropertyIntegerBuilder>? configure = null)
    {
        int seed = 0;
        // v0.6 fix: previously this silently fell back to 0 on conversion
        // failure, hiding builder seeds with unexpected ref types. Log the
        // failure so silent zero defaults are diagnosable.
        try { seed = Convert.ToInt32(@ref?.Value ?? 0); }
        catch (Exception ex) { DiagLog.LogCaught("MCM.SettingsBuilder", $"AddInteger({id}) seed conversion -> 0", ex); }
        var b = new IntPropBuilder(id, displayName, min, max, seed);
        b.Build().Ref = @ref;
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddFloatingInteger(string id, string displayName, float min, float max, MCM.Common.IRef @ref, Action<ISettingsPropertyFloatingIntegerBuilder>? configure = null)
    {
        float seed = 0f;
        // v0.6 fix: same as AddInteger -- log conversion failures rather than
        // silently seeding zero.
        try { seed = Convert.ToSingle(@ref?.Value ?? 0f); }
        catch (Exception ex) { DiagLog.LogCaught("MCM.SettingsBuilder", $"AddFloatingInteger({id}) seed conversion -> 0", ex); }
        var b = new FloatPropBuilder(id, displayName, min, max, seed);
        b.Build().Ref = @ref;
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    public ISettingsPropertyGroupBuilder AddText(string id, string displayName, MCM.Common.IRef @ref, Action<ISettingsPropertyTextBuilder>? configure = null)
    {
        var b = new TextPropBuilder(id, displayName, (@ref?.Value as string) ?? string.Empty);
        b.Build().Ref = @ref;
        configure?.Invoke(b);
        _properties.Add(b.Build());
        return this;
    }

    // Phase 2.3 / finding H6: the two AddX members the group builder was
    // missing vs upstream (signatures verified against
    // Aragas/Bannerlord.MBOptionScreen, 2026-06-10). Both ride the B2 IRef
    // write-through pipeline like every other ref-bound property.

    public ISettingsPropertyGroupBuilder AddDropdown(string id, string displayName, int selectedIndex, MCM.Common.IRef @ref, Action<ISettingsPropertyDropdownBuilder>? configure = null)
    {
        var b = new DropdownPropBuilder(id, displayName);
        var prop = b.Build();
        prop.Ref = @ref;
        // The ref wraps the consumer's Dropdown<T>/DropdownDefault<T>
        // instance; the dropdown object itself is the property value (the
        // existing attribute-path UI + DropdownConverter persistence both
        // key off that shape). Apply the initial selection upstream-style.
        try
        {
            prop.Value = @ref?.Value;
            if (prop.Value != null && selectedIndex >= 0)
            {
                var idxProp = prop.Value.GetType().GetProperty("SelectedIndex",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                idxProp?.SetValue(prop.Value, selectedIndex);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("MCM.SettingsBuilder", $"AddDropdown({id}) init", ex);
        }
        configure?.Invoke(b);
        _properties.Add(prop);
        return this;
    }

    public ISettingsPropertyGroupBuilder AddToggle(string id, string displayName, MCM.Common.IRef @ref, Action<ISettingsPropertyToggleBuilder>? configure = null)
    {
        // Upstream renders a toggle as the group's on/off switch. We render
        // it as a bool row in the group (the group-toggle visual is a UI
        // nicety; the DATA contract -- bool round-tripped through the
        // consumer's IRef -- is what mods depend on).
        var b = new TogglePropBuilder(id, displayName, CoerceBool(id, @ref));
        var prop = b.Build();
        prop.Ref = @ref;
        configure?.Invoke(b);
        _properties.Add(prop);
        return this;
    }

    /// <summary>
    /// Add a clickable button row. The IRef wraps the click handler (the
    /// consumer mod typically passes a ProxyRef&lt;Action&gt;). When the user
    /// clicks the button in the MCM panel, the SettingsPropertyVM's
    /// InvokeAction() reaches in via FluentProperty.ClickAction and runs it.
    /// </summary>
    public ISettingsPropertyGroupBuilder AddButton(string id, string displayName, MCM.Common.IRef @ref, string content, Action<ISettingsPropertyButtonBuilder>? configure = null)
    {
        var b = new ButtonPropBuilder(id, displayName, content);
        var prop = b.Build();
        prop.Ref = @ref;
        // Snapshot the current ref value as the click handler. We resolve it
        // once at AddButton-time so the consumer mod doesn't need IRef.Value
        // to remain valid forever (most callers pass `new ProxyRef<Action>(...)`
        // inline). If the ref's value isn't an Action, ClickAction stays null
        // and InvokeAction is a no-op — same defensive behavior as the
        // attribute path.
        try { prop.ClickAction = @ref?.Value as System.Action; }
        catch (Exception ex) { DiagLog.LogCaught("MCM.SettingsBuilder", $"AddButton({id}) ref resolve", ex); }
        configure?.Invoke(b);
        _properties.Add(prop);
        return this;
    }
}

internal sealed class FluentProperty
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TypeKind { get; set; } = "";   // "bool" | "int" | "float" | "text"
    public object? Value { get; set; }
    public int Order { get; set; }
    public bool RequireRestart { get; set; }
    public string HintText { get; set; } = "";
    public double Min { get; set; }
    public double Max { get; set; }
    // printf-style value-format string for int/float sliders. Adjustable
    // Leveling and friends set this via ISettingsPropertyIntegerBuilder.
    // AddValueFormat / ISettingsPropertyFloatingIntegerBuilder.AddValueFormat;
    // we stash it here so future renderers can honour it, but the current
    // UI ignores it (sliders render the raw number).
    public string ValueFormat { get; set; } = "";

    /// <summary>
    /// Optional binding ref. Set by the IRef-based AddX(...) overloads on
    /// ISettingsPropertyGroupBuilder. Since Phase 2.2 / B2:
    /// FluentGlobalSettings.Get reads the ref's live value and Set writes
    /// through it (with type coercion), so the consumer mod's state and the
    /// MCM panel/JSON stay in sync.
    /// </summary>
    public MCM.Common.IRef? Ref { get; set; }

    /// <summary>
    /// For button-type fluent properties (TypeKind == "button"): the delegate
    /// invoked when the user clicks the button in the MCM panel. Resolved
    /// once at AddButton-time from the IRef the consumer passed in.
    /// </summary>
    public System.Action? ClickAction { get; set; }

    /// <summary>
    /// For button-type fluent properties (TypeKind == "button"): the label
    /// rendered ON the button itself (e.g. "Reset"). Mirror of the `content`
    /// argument BUTR MCM's AddButton takes.
    /// </summary>
    public string ButtonContent { get; set; } = string.Empty;
}

// Per-type builders --------------------------------------------------
//
// PropBuilderBase<TSelf> implements the GENERIC ISettingsPropertyBuilder<TSelf>
// interface (in the parent FluentBuilder namespace, NOT Models). Each typed
// builder passes itself as TSelf so SetHintText("...") returns the typed
// builder for chaining and the IL signature matches what BEW emits:
//   callvirt instance !!0 MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder`1::SetHintText(string)
// Anchoring this to the non-generic Models.ISettingsPropertyBuilder triggers
// EntryPointNotFoundException at consumer-mod JIT time.

internal abstract class PropBuilderBase<TSelf> :
    MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<TSelf>,
    MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder,                  // v0.7.5: parent-namespace non-generic
    MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder
    where TSelf : class
{
    protected readonly FluentProperty _prop = new();

    protected PropBuilderBase(string id, string displayName)
    {
        _prop.Id = id;
        _prop.DisplayName = displayName;
    }

    public TSelf SetOrder(int order)              { _prop.Order = order; return (TSelf)(object)this; }
    public TSelf SetRequireRestart(bool require)  { _prop.RequireRestart = require; return (TSelf)(object)this; }
    public TSelf SetHintText(string hintText)     { _prop.HintText = hintText ?? string.Empty; return (TSelf)(object)this; }

    // v0.7.5 SHIP-BLOCKER fix: explicit parent-namespace non-generic
    // ISettingsPropertyBuilder impls. XorberaxLegacy + AdjustableLeveling's
    // IL routes Set* / AddValueFormat through this interface, and the v0.7.4
    // shim returned the Models nested variant -- triggered MissingMethodException
    // -> SettingsStorage reset cycle -> CTD on new-campaign init.
    MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder.SetOrder(int order)
        { SetOrder(order); return this; }
    MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder.SetRequireRestart(bool require)
        { SetRequireRestart(require); return this; }
    MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder.SetHintText(string hintText)
        { SetHintText(hintText); return this; }

    // Explicit Models.ISettingsPropertyBuilder impls (kept for back-compat
    // with internal call sites that take that nested-namespace reference).
    MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder.SetOrder(int order)
        { SetOrder(order); return this; }
    MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder.SetRequireRestart(bool require)
        { SetRequireRestart(require); return this; }
    MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder
        MCM.Abstractions.FluentBuilder.Models.ISettingsPropertyBuilder.SetHintText(string hintText)
        { SetHintText(hintText); return this; }

    public FluentProperty Build() => _prop;
}

internal sealed class BoolPropBuilder : PropBuilderBase<ISettingsPropertyBoolBuilder>, ISettingsPropertyBoolBuilder
{
    public BoolPropBuilder(string id, string displayName, bool defaultValue) : base(id, displayName)
    {
        _prop.TypeKind = "bool";
        _prop.Value = defaultValue;
    }
}

internal sealed class IntPropBuilder : PropBuilderBase<ISettingsPropertyIntegerBuilder>, ISettingsPropertyIntegerBuilder
{
    public IntPropBuilder(string id, string displayName, int min, int max, int defaultValue) : base(id, displayName)
    {
        _prop.TypeKind = "int";
        _prop.Min = min;
        _prop.Max = max;
        _prop.Value = defaultValue;
    }

    // Adjustable Leveling + XorberaxLegacy v1.x call this with format strings
    // like "{0}" or "Tier {0}". v0.7.5: returns the PARENT-namespace
    // ISettingsPropertyBuilder to match the consumer mod's IL signature
    // (returning Models.ISettingsPropertyBuilder threw MissingMethodException
    // -> SettingsStorage save/reload cycle -> CTD).
    public MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder AddValueFormat(string valueFormat)
    {
        _prop.ValueFormat = valueFormat;
        return this;
    }
}

internal sealed class FloatPropBuilder : PropBuilderBase<ISettingsPropertyFloatingIntegerBuilder>, ISettingsPropertyFloatingIntegerBuilder
{
    public FloatPropBuilder(string id, string displayName, float min, float max, float defaultValue) : base(id, displayName)
    {
        _prop.TypeKind = "float";
        _prop.Min = min;
        _prop.Max = max;
        _prop.Value = defaultValue;
    }

    public MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder AddValueFormat(string valueFormat)
    {
        _prop.ValueFormat = valueFormat;
        return this;
    }
}

internal sealed class TextPropBuilder : PropBuilderBase<ISettingsPropertyTextBuilder>, ISettingsPropertyTextBuilder
{
    public TextPropBuilder(string id, string displayName, string defaultValue) : base(id, displayName)
    {
        _prop.TypeKind = "text";
        _prop.Value = defaultValue ?? string.Empty;
    }
}

// Phase 2.3 / H6: typed builders for the new AddDropdown/AddToggle members.
internal sealed class DropdownPropBuilder : PropBuilderBase<ISettingsPropertyDropdownBuilder>, ISettingsPropertyDropdownBuilder
{
    public DropdownPropBuilder(string id, string displayName) : base(id, displayName)
    {
        _prop.TypeKind = "dropdown";
    }
}

internal sealed class TogglePropBuilder : PropBuilderBase<ISettingsPropertyToggleBuilder>, ISettingsPropertyToggleBuilder
{
    public TogglePropBuilder(string id, string displayName, bool defaultValue) : base(id, displayName)
    {
        _prop.TypeKind = "bool"; // rendered as a bool row; see AddToggle note
        _prop.Value = defaultValue;
    }
}

/// <summary>
/// Backing builder for button-type fluent properties. Implements the
/// <see cref="ISettingsPropertyButtonBuilder"/> interface BEW and other
/// MCMv5 fluent consumers configure via the AddButton(...) lambda.
/// </summary>
internal sealed class ButtonPropBuilder : PropBuilderBase<ISettingsPropertyButtonBuilder>, ISettingsPropertyButtonBuilder
{
    public ButtonPropBuilder(string id, string displayName, string content) : base(id, displayName)
    {
        _prop.TypeKind = "button";
        // Strip TaleWorlds localization tokens like "{=InstallDnspyButton}Install now"
        // → "Install now". BEW passes its labels through the localization
        // system, and we ignore the key portion and use the fallback text.
        _prop.ButtonContent = MCM.Internal.TextHelper.StripLocalizationKeys(content ?? string.Empty);
        // Buttons have no "value" in the usual sense — the click handler is
        // stored on FluentProperty.ClickAction by AddButton's caller.
        _prop.Value = null;
    }
}
