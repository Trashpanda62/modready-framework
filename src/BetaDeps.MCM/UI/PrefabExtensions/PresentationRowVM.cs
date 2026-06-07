// BetaDeps.MCM -- PresentationRowVM
//
// Sub-slice 4a of the "infinite scroll, one page per mod" rewrite.
//
// A thin wrapper ViewModel that holds EITHER a single SettingsPropertyVM
// (a real settings row) OR a header-only string (a group divider). The
// Mod Configuration prefab's ItemTemplate binds one of these per row in
// OptionsVMMixin.RowList, so the prefab no longer needs the Slot0..Slot19
// fixed-slot fan that the v0.5.x prefab currently surfaces.
//
// SURFACE: mirrors the existing Slot0_* binding set EXACTLY (same names,
// same types, same behaviour) so slice 4c can re-template the row XML
// with a one-for-one rename from "Slot0_X" to just "X". Type
// discrimination:
//   IsHeader  == true   ->  render the group-divider label only
//   IsProperty == true  ->  render the property row; IsBool / IsNumeric /
//                            IsText / IsDropdown / IsButton gates select
//                            the widget variant inside the ItemTemplate
//
// All mutating setters and Execute methods forward to the inner
// SettingsPropertyVM and re-fire OnPropertyChanged on the affected fields
// so the per-row widget refreshes.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Globalization;

using Bannerlord.UIExtenderEx.Attributes;

using BetaDeps.Foundation;

using MCM.UI.GUI.ViewModels;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

public sealed class PresentationRowVM : ViewModel
{
    private const string Tag = "PresentationRowVM";

    private readonly SettingsPropertyVM? _prop;
    private readonly string _headerText;
    private readonly bool _isExpanded = true;       // header rows: collapse state
    private readonly Action? _onToggleCollapse;     // header rows: toggle callback
    private readonly int  _indent;                  // Slice 8: nesting depth (0=top/parent, 1=child sub-group)

    /// <summary>Build a real property row.</summary>
    public PresentationRowVM(SettingsPropertyVM prop, int indent = 0)
    {
        _prop = prop ?? throw new ArgumentNullException(nameof(prop));
        _headerText = string.Empty;
        _indent = indent;
    }

    /// <summary>Build a collapsible group-divider header row (Phase 2.3).</summary>
    public PresentationRowVM(string headerText, bool isExpanded, Action? onToggleCollapse, int indent = 0)
    {
        _prop = null;
        _headerText = headerText ?? string.Empty;
        _isExpanded = isExpanded;
        _onToggleCollapse = onToggleCollapse;
        _indent = indent;
    }

    // Slice 8: left-indent in pixels for nested sub-groups (28px per level).
    [DataSourceProperty] public int IndentPixels => _indent * 28;

    /// <summary>Build a plain (always-expanded) group-divider header row.</summary>
    public PresentationRowVM(string headerText) : this(headerText, true, null) { }

    // ---------- Collapsible group header (Phase 2.3) ----------
    [DataSourceProperty] public bool   IsExpanded  => _isExpanded;
    [DataSourceProperty] public bool   IsCollapsed => !_isExpanded;
    // Slice 3: chevrons are now native sprite BrushWidgets (BetaDeps.Chevron.*)
    // toggled by @IsCollapsed/@IsExpanded; ChevronText kept as a fallback.
    // Solid triangles: down = expanded, right = collapsed (matches target).
    // Down triangle = expanded; right angle = collapsed. The game font renders
    // ▼ and ">" but NOT ▶ (it showed as a missing-glyph box), so use ">".
    [DataSourceProperty] public string ChevronText => _isExpanded ? "▼" : ">";

    [DataSourceMethod]
    public void ExecuteToggleCollapse()
    {
        try { _onToggleCollapse?.Invoke(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteToggleCollapse", ex); }
    }

    // ---------- Visibility + type discriminator + header text ----------

    [DataSourceProperty] public bool   IsVisible   => _prop != null || !string.IsNullOrEmpty(_headerText);
    [DataSourceProperty] public bool   IsHeader    => _prop == null && !string.IsNullOrEmpty(_headerText);
    [DataSourceProperty] public bool   IsProperty  => _prop != null;
    [DataSourceProperty] public string GroupHeader => _headerText;

    // ---------- Property surface forwards ----------

    [DataSourceProperty] public string DisplayName  => _prop?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string HintText     => _prop?.HintText ?? string.Empty;

    [DataSourceProperty] public bool   IsBool       => _prop?.IsBool ?? false;
    [DataSourceProperty] public bool   IsInteger    => _prop?.IsInteger ?? false;
    [DataSourceProperty] public bool   IsFloating   => _prop?.IsFloating ?? false;
    [DataSourceProperty] public bool   IsNumeric    => _prop != null && (_prop.IsInteger || _prop.IsFloating);
    [DataSourceProperty] public bool   IsText       => _prop?.IsText ?? false;
    [DataSourceProperty] public bool   IsButton     => _prop?.IsButton ?? false;
    [DataSourceProperty] public bool   IsDropdown   => _prop?.IsDropdown ?? false;
    [DataSourceProperty] public string DropdownText => _prop?.DropdownDisplayText ?? string.Empty;

    [DataSourceProperty] public float  MinValue     => (float)(_prop?.MinValue ?? 0);
    [DataSourceProperty] public float  MaxValue     => SafeMaxFloat(_prop);

    // Slider plumbing for the {RowList} ItemTemplate. Vanilla OptionItem.xml
    // binds IsDiscrete / DiscreteIncrementInterval / UpdateValueContinuously on
    // its per-option SliderWidget; the slot UI bound the equivalents via mixin
    // statics (SliderIncrementOne / SliderUpdateFalse). Since the ItemTemplate's
    // data context IS this row VM, expose them here so the slider binds locally.
    // UpdateContinuously=false matches vanilla game-options sliders (value
    // commits on handle release, not every drag tick -- this is also the
    // attribute whose absence used to crash the old slot-fan slider stack).
    // DiscreteIncrementInterval is INT on the native SliderWidget side -- binding
    // a float here throws "System.Single cannot be converted to System.Int32"
    // deep in GauntletView.RefreshBinding and takes down the whole Options
    // screen (the slot mixin learned this; see SliderIncrementOne).
    [DataSourceProperty] public bool IsDiscrete                => _prop?.IsInteger ?? false;
    [DataSourceProperty] public int  DiscreteIncrementInterval => 1;
    [DataSourceProperty] public bool UpdateContinuously        => false;

    [DataSourceProperty]
    public bool BoolValue
    {
        get => _prop?.BoolValue ?? false;
        set
        {
            if (_prop == null) return;
            if (_prop.BoolValue == value) return;
            _prop.BoolValue = value;
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(BoolText));
        }
    }
    [DataSourceProperty] public string BoolText   => (_prop?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string ButtonText => _prop?.ButtonContentText ?? "Run";

    [DataSourceProperty]
    public float IntValue
    {
        get => (float)(_prop?.IntValue ?? 0);
        set
        {
            if (_prop == null) return;
            _prop.IntValue = (int)value;
            OnPropertyChanged(nameof(IntValue));
            OnPropertyChanged(nameof(FloatValue));
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(EditableValueText));
        }
    }
    [DataSourceProperty]
    public float FloatValue
    {
        // Unified-binding dispatch: returns IntValue (cast to float) for int
        // settings, FloatValue for float settings. One slider widget handles both.
        get
        {
            var p = _prop;
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _prop;
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            OnPropertyChanged(nameof(FloatValue));
            OnPropertyChanged(nameof(IntValue));
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(EditableValueText));
        }
    }
    [DataSourceProperty]
    public string ValueText
    {
        get
        {
            var p = _prop;
            if (p == null) return string.Empty;
            try
            {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            }
            catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string EditableValueText
    {
        get => ValueText;
        set { SetFromEditableText(value); }
    }
    [DataSourceProperty]
    public string TextValue
    {
        get => _prop?.TextValue ?? string.Empty;
        set
        {
            if (_prop == null) return;
            if (string.Equals(_prop.TextValue, value, StringComparison.Ordinal)) return;
            _prop.TextValue = value;
            OnPropertyChanged(nameof(TextValue));
        }
    }

    // ---------- Execute commands ----------

    [DataSourceMethod]
    public void ExecuteToggleBool()
    {
        if (_prop == null) return;
        try
        {
            _prop.BoolValue = !_prop.BoolValue;
            OnPropertyChanged(nameof(BoolValue));
            OnPropertyChanged(nameof(BoolText));
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteToggleBool", ex); }
    }

    [DataSourceMethod]
    public void ExecuteDropdownNext()
    {
        if (_prop == null) return;
        try
        {
            _prop.CycleDropdownNext();
            OnPropertyChanged(nameof(DropdownText));
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteDropdownNext", ex); }
    }

    [DataSourceMethod]
    public void ExecuteDropdownPrev()
    {
        if (_prop == null) return;
        try
        {
            _prop.CycleDropdownPrev();
            OnPropertyChanged(nameof(DropdownText));
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteDropdownPrev", ex); }
    }

    [DataSourceMethod]
    public void ExecuteAction()
    {
        if (_prop == null) return;
        try { _prop.ExecuteAction(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteAction", ex); }
    }

    // Hover drives the shared right-side description panel. SettingsPropertyVM
    // exposes static HoverCallback / HoverEndCallback which OptionsVMMixin already
    // subscribes to (routing into HoveredOptionName / HoveredHintText / Is
    // HintVisible). Firing them with our inner prop reuses that exact wiring --
    // no mixin reference or new plumbing needed. Header rows have no _prop, so
    // hovering a divider simply clears the hint.
    [DataSourceMethod]
    public void ExecuteHoverBegin()
    {
        try
        {
            if (_prop == null) { SettingsPropertyVM.HoverEndCallback?.Invoke(); return; }
            SettingsPropertyVM.HoverCallback?.Invoke(_prop);
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteHoverBegin", ex); }
    }

    [DataSourceMethod]
    public void ExecuteHoverEnd()
    {
        try { SettingsPropertyVM.HoverEndCallback?.Invoke(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteHoverEnd", ex); }
    }

    /// <summary>
    /// Re-fire property-changed on every value-bearing field. Used by
    /// OptionsVMMixin when something outside our row mutates the underlying
    /// SettingsPropertyVM (e.g. a preset Apply) so the row repaints.
    /// </summary>
    public void NotifyRefresh()
    {
        if (_prop == null)
        {
            OnPropertyChanged(nameof(GroupHeader));
            return;
        }
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(BoolText));
        OnPropertyChanged(nameof(IntValue));
        OnPropertyChanged(nameof(FloatValue));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(EditableValueText));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(DropdownText));
        OnPropertyChanged(nameof(ButtonText));
    }

    /// <summary>Underlying SettingsPropertyVM (null when IsHeader).</summary>
    public SettingsPropertyVM? Inner => _prop;

    // ---------- helpers ----------

    private static float SafeMaxFloat(SettingsPropertyVM? p)
    {
        if (p == null) return 1f;
        // SliderWidget divides (Max - Min); zero-range crashes native code.
        // Sanitise so Max - Min > 0 always.
        var min = (float)p.MinValue;
        var max = (float)p.MaxValue;
        return (max > min) ? max : min + 1f;
    }

    private void SetFromEditableText(string? typed)
    {
        var p = _prop;
        if (p == null) return;
        if (string.IsNullOrWhiteSpace(typed)) return;
        var s = typed!.Trim();
        try
        {
            if (p.IsInteger)
            {
                int iv;
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out iv))
                {
                    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                        iv = (int)Math.Round(fv);
                    else
                        return;
                }
                var minI = (int)p.MinValue;
                var maxI = (int)p.MaxValue;
                if (iv < minI) iv = minI;
                if (iv > maxI) iv = maxI;
                if (p.IntValue != iv)
                {
                    p.IntValue = iv;
                    OnPropertyChanged(nameof(FloatValue));
                    OnPropertyChanged(nameof(IntValue));
                    OnPropertyChanged(nameof(ValueText));
                    // Do NOT re-fire EditableValueText -- the user is mid-edit.
                }
            }
            else if (p.IsFloating)
            {
                if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv))
                    return;
                var minF = (float)p.MinValue;
                var maxF = SafeMaxFloat(p);
                if (fv < minF) fv = minF;
                if (fv > maxF) fv = maxF;
                if (Math.Abs(p.FloatValue - fv) > 0.0001f)
                {
                    p.FloatValue = fv;
                    OnPropertyChanged(nameof(FloatValue));
                    OnPropertyChanged(nameof(ValueText));
                }
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "SetFromEditableText", ex); }
    }
}