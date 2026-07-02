// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SettingsPropertyVM is the per-row VM in the property grid. The concrete
// type-specific behavior (bool checkbox, int slider, float slider, dropdown,
// text input, button) is selected by the TypeKind property; the XML prefab
// uses an IsBool / IsInteger / IsFloating / IsDropdown / IsText / IsButton
// switch to render the correct widget set.

using System;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;

using ModReady.Foundation;

using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Internal;

using TaleWorlds.Library;

namespace MCM.UI.GUI.ViewModels;

public class SettingsPropertyVM : ViewModel
{
    // ---- CREST-compat surface --------------------------------------
    /// <summary>
    /// Optional hook invoked by the MCM UI before rendering each property
    /// row. CREST sets this from its CrestSettings static ctor to hide
    /// log-filter sub-toggles when the parent scope dropdown is not "None".
    /// Returns false to hide the row.
    /// </summary>
    public static Func<string, object, bool>? IsPropertyVisibleHook { get; set; }
    // ---- end CREST-compat ------------------------------------------

    private readonly BaseSettings _owner;
    private readonly PropertyInfo? _property;
    private readonly SettingPropertyAttribute? _attribute;

    // S3: synthetic-property delegates (subsystem toggles, etc.) — both set
    // together and checked first in LiveGet / WriteBack so they short-circuit
    // before any PropertyInfo / fluent lookup.
    private readonly Func<object?>? _readFunc;
    private readonly Action<object?>? _writeAction;
    // Stable sort order for synthetic properties (attribute/fluent paths use
    // the attribute's or builder's own Order value).
    private readonly int _syntheticOrder;

    // Fluent-builder backing (only set when this VM was constructed from a
    // FluentGlobalSettings + FluentProperty instead of reflection+attribute).
    // When _fluentProp != null, reads/writes go through MCM.Abstractions.
    // Base.Global.FluentGlobalSettings.Get/Set instead of PropertyInfo.
    private readonly MCM.Internal.IFluentSettings? _fluentOwner; // 2.3/H6: any fluent scope
    private readonly MCM.Abstractions.FluentBuilder.FluentProperty? _fluentProp;

    private string _displayName = string.Empty;
    private string _hintText = string.Empty;
    private bool _isVisible = true;
    private bool _boolValue;
    private int _intValue;
    private float _floatValue;
    private string _textValue = string.Empty;
    private string _valueFormat = "0";
    private double _minValue;
    private double _maxValue = 100;

    public int Order => _attribute?.Order ?? _fluentProp?.Order ?? _syntheticOrder;
    public string Name => _property?.Name ?? _fluentProp?.Id ?? string.Empty;
    public string TypeKind { get; }

    // (Header-mode CreateHeader/IsHeader/HeaderText removed in Phase 6 -- the
    // only caller was the retired fixed-slot fan; the live RowList renders
    // group headers via PresentationRowVM, not SettingsPropertyVM.)

    [DataSourceProperty]
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChangedWithValue(value, nameof(DisplayName)); } }
    [DataSourceProperty]
    public string HintText    { get => _hintText;    set { _hintText = value;    OnPropertyChangedWithValue(value, nameof(HintText));    } }
    [DataSourceProperty]
    public bool IsVisible     { get => _isVisible;   set { _isVisible = value;   OnPropertyChangedWithValue(value, nameof(IsVisible));   } }

    // Per-type display flags consumed by the XML prefab via a switch:
    [DataSourceProperty] public bool IsBool     => TypeKind == "bool";
    [DataSourceProperty] public bool IsInteger  => TypeKind == "int";
    [DataSourceProperty] public bool IsFloating => TypeKind == "float";
    [DataSourceProperty] public bool IsDropdown => TypeKind == "dropdown";
    [DataSourceProperty] public bool IsText     => TypeKind == "text";
    [DataSourceProperty] public bool IsButton   => TypeKind == "button";
    // v1.0 (task #12, unpaginated): IsNumeric mirrors the per-slot
    // mixin binding so VM-context slot templates (used for slots 10-99 in
    // the scrollable mod page) can show the same text-only ValueText
    // fallback the per-slot mixin path uses outside the first 6 slider slots.
    [DataSourceProperty] public bool IsNumeric  => IsInteger || IsFloating;

    /// <summary>
    /// Text displayed on the button face (e.g. "Reset", "Open Config"). For
    /// attribute-style buttons this comes from [SettingPropertyButton(Content=...)];
    /// for fluent-style buttons it comes from the AddButton(...) `content`
    /// argument and is stored on FluentProperty.ButtonContent. Falls back to
    /// "Run" if neither source supplies a label, matching v0.5.6's default.
    /// Both sources are passed through TextHelper.StripLocalizationKeys so
    /// raw "{=Key}Fallback" tokens (used by BEW and AIInfluence among others)
    /// don't bleed through to the UI; fluent sources are also pre-stripped at
    /// ButtonPropBuilder ctor time so this strip is cheap-redundant in that
    /// path but is the only line of defense for attribute-style buttons.
    /// </summary>
    [DataSourceProperty]
    public string ButtonContentText
    {
        get
        {
            if (TypeKind != "button") return string.Empty;
            // Fluent button — read content set at AddButton time.
            if (_fluentProp != null && !string.IsNullOrEmpty(_fluentProp.ButtonContent))
                return MCM.Internal.TextHelper.StripLocalizationKeys(_fluentProp.ButtonContent);
            // Attribute button — read [SettingPropertyButton(Content="...")].
            if (_attribute is SettingPropertyButtonAttribute bAttr && !string.IsNullOrEmpty(bAttr.Content))
                return MCM.Internal.TextHelper.StripLocalizationKeys(bAttr.Content);
            // No explicit button Content: fall back to the row's display name
            // (a meaningful label like "Reset Mod" / "Master Strikes") instead of
            // the literal "Run". Several foreign-MCM mods (Cinematic Combat, Lively
            // Animations, Transmog) use the one-arg [SettingPropertyButton(name)]
            // form and leave Content unset, which previously surfaced as "Run".
            return string.IsNullOrEmpty(_displayName) ? "Done" : _displayName;
        }
    }

    [DataSourceProperty]
    public bool BoolValue
    {
        get { try { return LiveGet() is bool b ? b : _boolValue; } catch { return _boolValue; } }
        set { _boolValue = value; OnPropertyChangedWithValue(value, nameof(BoolValue)); WriteBack(value); }
    }

    [DataSourceProperty]
    public int IntValue
    {
        get { try { return LiveGet() is int i ? i : _intValue; } catch { return _intValue; } }
        set { _intValue = value; OnPropertyChangedWithValue(value, nameof(IntValue)); WriteBack(value); }
    }

    [DataSourceProperty]
    public float FloatValue
    {
        get { try { return LiveGet() is float f ? f : _floatValue; } catch { return _floatValue; } }
        set { _floatValue = value; OnPropertyChangedWithValue(value, nameof(FloatValue)); WriteBack(value); }
    }

    [DataSourceProperty]
    public string TextValue
    {
        // Strip "{=Key}Fallback" tokens on read so consumer mods that store
        // their setting values pre-localized (some Russian-localized mods,
        // ChatAi's preset names, etc.) don't bleed raw tokens into the UI.
        // Construction-time strip in the ctor only covers static names/hints;
        // dynamic value reads need their own pass.
        get
        {
            try { return MCM.Internal.TextHelper.StripLocalizationKeys(LiveGet() as string ?? _textValue); }
            catch { return MCM.Internal.TextHelper.StripLocalizationKeys(_textValue); }
        }
        set { _textValue = value; OnPropertyChangedWithValue(value, nameof(TextValue)); WriteBack(value); }
    }

    /// <summary>Read the current value from whichever backing store this VM
    /// uses — PropertyInfo for attribute-based settings, fluent dictionary
    /// for FluentGlobalSettings, or delegate for synthetic S3 properties.
    /// Returns null on any failure.</summary>
    private object? LiveGet()
    {
        // S3: synthetic property (subsystem toggles, etc.)
        if (_readFunc != null) return _readFunc();

        if (_fluentProp != null && _fluentOwner != null)
        {
            // The fluent dictionary stores by id; Get<object> returns the raw
            // boxed value with no Convert.ChangeType applied.
            return _fluentOwner.Get<object>(_fluentProp.Id);
        }
        if (_property != null)
        {
            // S1: foreign-settings adapter -- reflect over the wrapped instance
            // instead of the adapter shell (which has no [SettingProperty*] members).
            var reflTarget = _owner is MCM.Internal.SettingsRegistry.ForeignSettingsAdapter fa
                ? fa.Wrapped
                : (object)_owner;
            return _property.GetValue(reflTarget);
        }
        return null;
    }

    [DataSourceProperty] public string ValueFormat => _valueFormat;
    [DataSourceProperty] public double MinValue    => _minValue;
    [DataSourceProperty] public double MaxValue    => _maxValue;

    // (The v0.4.4 MCMOptionRow slider bindings -- MinValueAsFloat/
    // MaxValueAsFloat/IntValueAsFloat + the IsDiscrete/Increment/Update slider
    // constants -- were removed in Phase 6 with MCMOptionRow.xml; the live
    // RowList slider binds PresentationRowVM's equivalents.)

    // ---- Dropdown surface ------------------------------------------
    // Dropdown<T> / DropdownDefault<T> instances are read by reflection on
    // SelectedIndex / Count / this[int] so we don't have to know T at compile
    // time. The dropdown object is the property's current value.
    /// <summary>Number of choices on the bound dropdown (0 if none).</summary>
    [DataSourceProperty]
    public int DropdownItemCount
    {
        get
        {
            if (TypeKind != "dropdown") return 0;
            var d = SafeGet();
            return GetDropdownCount(d);
        }
    }

    /// <summary>
    /// String shown on the dropdown cycle button -- the currently-selected
    /// item's ToString(), or "(empty)" if the dropdown has no items.
    /// </summary>
    [DataSourceProperty]
    public string DropdownDisplayText
    {
        get
        {
            if (TypeKind != "dropdown") return string.Empty;
            var d = SafeGet();
            return GetDropdownDisplayText(d);
        }
    }

    /// <summary>Currently-selected index (-1 if empty).</summary>
    [DataSourceProperty]
    public int DropdownSelectedIndex
    {
        get
        {
            if (TypeKind != "dropdown") return -1;
            var d = SafeGet();
            return GetDropdownSelectedIndex(d);
        }
        set
        {
            if (TypeKind != "dropdown") return;
            var d = SafeGet();
            SetDropdownSelectedIndex(d, value);
            OnPropertyChangedWithValue(value, nameof(DropdownSelectedIndex));
            OnPropertyChanged(nameof(DropdownDisplayText));
        }
    }

    /// <summary>Advance the dropdown selection by +1 (wraps around).</summary>
    public void CycleDropdownNext()
    {
        if (TypeKind != "dropdown") return;
        var d = SafeGet();
        var count = GetDropdownCount(d);
        if (count <= 0) return;
        var idx = GetDropdownSelectedIndex(d);
        DropdownSelectedIndex = (idx + 1) % count;
    }

    /// <summary>Advance the dropdown selection by -1 (wraps around).</summary>
    public void CycleDropdownPrev()
    {
        if (TypeKind != "dropdown") return;
        var d = SafeGet();
        var count = GetDropdownCount(d);
        if (count <= 0) return;
        var idx = GetDropdownSelectedIndex(d);
        DropdownSelectedIndex = (idx - 1 + count) % count;
    }

    /// <summary>
    /// v0.5.6: invoke the underlying Action delegate for a button-type property.
    /// Used by per-slot ExecuteSlot{n}ActionButton handlers in OptionsVMMixin.
    /// No-op if this property isn't a button-type or the property has no value.
    /// </summary>
    public void InvokeAction()
    {
        if (TypeKind != "button") return;
        try
        {
            // Attribute-style button: the property getter returns an Action.
            if (_property != null)
            {
                var val = _property.GetValue(_owner);
                (val as System.Action)?.Invoke();
                return;
            }

            // Fluent-style button (v1.0, BEW MCMv5 shim): the click handler
            // was resolved at AddButton(...) time and stashed on the
            // FluentProperty. If the consumer passed a stale IRef, the
            // delegate may now point at a finalized object — try/catch
            // around the invoke keeps the UI responsive.
            _fluentProp?.ClickAction?.Invoke();
        }
        catch { /* mod author's action threw — swallow to keep UI responsive */ }
    }

    // ---- ItemTemplate Command.Click targets ------------------------
    // The v0.4 polished prefab uses NavigatableListPanel + ItemTemplate,
    // where each row's buttons bind Command.Click directly to a method
    // on this row VM (instead of calling back to the mixin via per-slot
    // ExecuteSlot{N}DropdownNext methods). These are [DataSourceMethod]-
    // attributed wrappers around the CycleDropdownPrev/Next / bool toggle
    // logic above.

    [DataSourceMethod]
    public void ExecuteDropdownNext() => CycleDropdownNext();

    [DataSourceMethod]
    public void ExecuteDropdownPrev() => CycleDropdownPrev();

    /// <summary>
    /// v1.0 (task #12): VM-side handler so the new unpaginated slot template
    /// can bind `Command.Click="ExecuteAction"` directly on the row VM (via
    /// `DataSource="{SlotN_VM}"`) instead of needing per-slot
    /// ExecuteSlot{N}ActionButton methods on the mixin. Internally invokes the
    /// same path as InvokeAction.
    /// </summary>
    [DataSourceMethod]
    public void ExecuteAction() => InvokeAction();

    [DataSourceMethod]
    public void ExecuteToggleBool()
    {
        if (TypeKind != "bool") return;
        BoolValue = !BoolValue;
        OnPropertyChanged(nameof(BoolText));
    }

    [DataSourceMethod]
    public void ExecuteHoverBegin()
    {
        HoverCallback?.Invoke(this);
    }

    [DataSourceMethod]
    public void ExecuteHoverEnd()
    {
        HoverEndCallback?.Invoke();
    }

    [DataSourceProperty]
    public SettingsPropertyVM Hint => this;

    [DataSourceMethod]
    public void ExecuteBeginHint() => HoverCallback?.Invoke(this);

    [DataSourceMethod]
    public void ExecuteEndHint() => HoverEndCallback?.Invoke();

    public static System.Action<SettingsPropertyVM>? HoverCallback { get; set; }
    public static System.Action? HoverEndCallback { get; set; }

    /// <summary>Convenience text for bool button label.</summary>
    [DataSourceProperty]
    public string BoolText
    {
        get
        {
            if (TypeKind != "bool") return string.Empty;
            try { return (LiveGet() is bool b ? b : _boolValue) ? "ON" : "OFF"; }
            catch { return _boolValue ? "ON" : "OFF"; }
        }
    }

    /// <summary>Convenience text for slider value readout.</summary>
    [DataSourceProperty]
    public string ValueText
    {
        get
        {
            try
            {
                var v = LiveGet();
                if (TypeKind == "int" && v is int i) return i.ToString(_valueFormat);
                if (TypeKind == "float" && v is float f) return f.ToString(_valueFormat);
            }
            catch { }
            return string.Empty;
        }
    }

    /// <summary>Text shown on the dropdown cycle button.</summary>
    [DataSourceProperty]
    public string DropdownText => DropdownDisplayText;
    // ---- end ItemTemplate command targets --------------------------

    private object? SafeGet()
    {
        try { return LiveGet(); }
        catch { return null; }
    }

    private static int GetDropdownCount(object? d)
    {
        if (d == null) return 0;
        try
        {
            var p = d.GetType().GetProperty("Count");
            if (p != null) return (int)(p.GetValue(d) ?? 0);
        }
        catch { }
        return 0;
    }

    private static int GetDropdownSelectedIndex(object? d)
    {
        if (d == null) return -1;
        try
        {
            var p = d.GetType().GetProperty("SelectedIndex");
            if (p != null) return (int)(p.GetValue(d) ?? -1);
        }
        catch { }
        return -1;
    }

    private static void SetDropdownSelectedIndex(object? d, int newIdx)
    {
        if (d == null) return;
        try
        {
            var p = d.GetType().GetProperty("SelectedIndex");
            if (p != null && p.CanWrite) p.SetValue(d, newIdx);
        }
        catch { }
    }

    private static string GetDropdownDisplayText(object? d)
    {
        if (d == null) return string.Empty;
        try
        {
            // Strip "{=Key}Fallback" tokens — some dropdown option ToString()
            // overrides return localization-wrapped strings (ChatAi's preset
            // names, e.g.). Apply the same defense we use for DisplayName /
            // HintText / ButtonContent so no consumer-mod string ever bleeds
            // through to the UI with raw tokens.
            var pSel = d.GetType().GetProperty("SelectedValue");
            if (pSel != null)
            {
                var v = pSel.GetValue(d);
                return MCM.Internal.TextHelper.StripLocalizationKeys(v?.ToString() ?? "(empty)");
            }
            return MCM.Internal.TextHelper.StripLocalizationKeys(d.ToString() ?? "(empty)");
        }
        catch { return "(empty)"; }
    }
    // ---- end dropdown surface --------------------------------------

    public SettingsPropertyVM(BaseSettings owner, PropertyInfo property, SettingPropertyAttribute attribute, string typeKind)
    {
        _owner = owner;
        _property = property;
        _attribute = attribute;
        TypeKind = typeKind;
        // v1.0 (task #13 perf): write to backing fields directly during
        // construction instead of going through the property setters. The
        // setters call OnPropertyChangedWithValue which fires PropertyChanged
        // — useless during ctor (no widget is bound yet) but each event still
        // walks UIExtenderEx's binding-patch reflection chain. For ROT (237
        // properties × 2 spurious events each = 474 wasted events on every
        // mod switch), this is meaningful.
        _displayName = TextHelper.StripLocalizationKeys(attribute.DisplayName ?? property.Name);
        _hintText = TextHelper.StripLocalizationKeys(attribute.HintText ?? string.Empty);
        ReadFromProperty();
    }

    /// <summary>Fluent-settings constructor. Carries no PropertyInfo /
    /// SettingPropertyAttribute — instead reads metadata from the
    /// FluentProperty and reads/writes through the FluentGlobalSettings
    /// dictionary by property id. Used for mods that built their settings
    /// via ISettingsBuilder rather than [SettingPropertyX] attributes
    /// (Diplomacy, ImprovedGarrisons, RTSCamera, BetterSmithingContinued).
    ///
    /// Internal because FluentProperty is internal — exposing this
    /// constructor publicly would violate accessibility rules.</summary>
    internal SettingsPropertyVM(
        MCM.Abstractions.BaseSettings owner, // 2.3/H6: any fluent scope (all implement IFluentSettings)
        MCM.Abstractions.FluentBuilder.FluentProperty fp)
    {
        _owner = owner;
        _fluentOwner = owner as MCM.Internal.IFluentSettings;
        _fluentProp = fp;
        TypeKind = fp.TypeKind;
        // Same perf pattern as the attribute ctor — bypass the setters during
        // construction so no spurious PropertyChanged events fire.
        _displayName = TextHelper.StripLocalizationKeys(fp.DisplayName ?? fp.Id ?? string.Empty);
        _hintText = TextHelper.StripLocalizationKeys(fp.HintText ?? string.Empty);
        _minValue = fp.Min;
        _maxValue = fp.Max;
        // No ValueFormat on FluentProperty (the builder doesn't expose one);
        // fall back to a sensible default per type.
        _valueFormat = fp.TypeKind switch { "int" => "0", "float" => "0.00", _ => "0" };
        ReadFromProperty();
    }

    /// <summary>
    /// S3: synthetic bool property backed by delegate pair instead of a PropertyInfo.
    /// Used for the ButterLib SubSystem toggles page where each row represents an
    /// ISubSystem rather than a C# property.
    /// </summary>
    internal SettingsPropertyVM(BaseSettings owner, string displayName, string hintText,
        Func<object?> readFunc, Action<object?> writeAction, int order = 0)
    {
        _owner         = owner;
        _readFunc      = readFunc;
        _writeAction   = writeAction;
        _syntheticOrder = order;
        TypeKind       = "bool";
        _displayName   = displayName;
        _hintText      = hintText;
        // Snapshot initial state without going through the setter (no binding attached yet).
        _boolValue     = readFunc() is bool b && b;
    }

    /// <summary>Factory that selects the right variant based on the attribute type.</summary>
    public static SettingsPropertyVM Create(BaseSettings owner, PropertyInfo property, SettingPropertyAttribute attribute)
    {
        // V2-style attributes carry the widget type in their subtype.
        switch (attribute)
        {
            case SettingPropertyBoolAttribute:
                // Trust the property TYPE over the attribute: MCM's v1->v2 adaptation
                // can report a Bool attribute for a Dropdown<T> property, which would
                // render it as a checkbox and throw coercing Boolean -> Dropdown<T> on
                // write. A Dropdown property is always a dropdown regardless of attribute.
                if (IsDropdownType(property.PropertyType))
                    return new SettingsPropertyVM(owner, property, attribute, "dropdown");
                if (property.PropertyType != typeof(bool))
                    DiagLog.Log("SettingsPropertyVM", $"Create: {owner.Id}.{property.Name} Bool-attr on non-bool propType={property.PropertyType.FullName} -> bool");
                return new SettingsPropertyVM(owner, property, attribute, "bool");
            case SettingPropertyIntegerAttribute i:
                return new SettingsPropertyVM(owner, property, attribute, "int") { _minValue = i.MinValue, _maxValue = i.MaxValue, _valueFormat = i.ValueFormat };
            case SettingPropertyFloatingIntegerAttribute f:
                return new SettingsPropertyVM(owner, property, attribute, "float") { _minValue = f.MinValue, _maxValue = f.MaxValue, _valueFormat = f.ValueFormat };
            case SettingPropertyDropdownAttribute:
                return new SettingsPropertyVM(owner, property, attribute, "dropdown");
            case SettingPropertyTextAttribute:
                return new SettingsPropertyVM(owner, property, attribute, "text");
            case SettingPropertyButtonAttribute:
                return new SettingsPropertyVM(owner, property, attribute, "button");
        }

        // V1-style attribute (MCM.Abstractions.Attributes.v1.SettingPropertyAttribute):
        // one generic attribute used for all numeric/bool/text properties.
        if (attribute is MCM.Abstractions.Attributes.v1.SettingPropertyAttribute v1)
        {
            var pt = property.PropertyType;
            // MCM v1 used ONE generic [SettingProperty] for every widget, so a v1
            // dropdown is that attribute on a Dropdown<T>/DropdownDefault<T> property.
            // Check it FIRST -- Dropdown<string> is not typeof(string), so without this
            // it fell through to the "bool" fallback below and rendered as a checkbox
            // (and the UI-layer WriteBack tried to coerce Boolean -> Dropdown<T>, throwing).
            if (IsDropdownType(pt))
                return new SettingsPropertyVM(owner, property, attribute, "dropdown");
            if (pt == typeof(bool))
                return new SettingsPropertyVM(owner, property, attribute, "bool");
            if (pt == typeof(int) || pt == typeof(long) || pt == typeof(short) || pt == typeof(byte))
                return new SettingsPropertyVM(owner, property, attribute, "int")  { _minValue = (double)v1.MinValue, _maxValue = (double)v1.MaxValue, _valueFormat = v1.ValueFormat };
            if (pt == typeof(float) || pt == typeof(double) || pt == typeof(decimal))
                return new SettingsPropertyVM(owner, property, attribute, "float") { _minValue = (double)v1.MinValue, _maxValue = (double)v1.MaxValue, _valueFormat = v1.ValueFormat };
            if (pt == typeof(string))
                return new SettingsPropertyVM(owner, property, attribute, "text");
        }

        // Fallback for unknown attribute shapes: a Dropdown property must still be a
        // dropdown (never mis-typed as bool), otherwise default to bool.
        if (IsDropdownType(property.PropertyType))
            return new SettingsPropertyVM(owner, property, attribute, "dropdown");
        // Diagnostic: anything non-bool that still defaults to "bool" is a mis-classify
        // waiting to happen (the UI-layer WriteBack will throw coercing bool -> the real
        // type). Log the exact attribute + property type so it is traceable.
        if (property.PropertyType != typeof(bool))
            DiagLog.Log("SettingsPropertyVM", $"Create: {owner.Id}.{property.Name} defaulting to bool -- attr={attribute.GetType().FullName}, propType={property.PropertyType.FullName}");
        return new SettingsPropertyVM(owner, property, attribute, "bool");
    }

    /// <summary>True if the type is (or derives from) Dropdown&lt;T&gt; / DropdownDefault&lt;T&gt;.
    /// Matched by generic-type FullName rather than typeof() equality: in Bannerlord's
    /// multi-MCM environment a consumer mod's Dropdown&lt;T&gt; can come from a different
    /// MCM assembly than the one this code was compiled against, so a typeof() compare
    /// (assembly-sensitive) silently missed and the dropdown was typed "bool". Mirrors the
    /// intent of MCM.Common.DropdownConverter.CanConvert.</summary>
    private static bool IsDropdownType(Type? t)
    {
        while (t != null && t != typeof(object))
        {
            if (t.IsGenericType)
            {
                var n = t.GetGenericTypeDefinition().FullName;
                if (n == "MCM.Common.Dropdown`1" || n == "MCM.Common.DropdownDefault`1") return true;
            }
            t = t.BaseType;
        }
        return false;
    }

    private void ReadFromProperty()
    {
        try
        {
            var v = LiveGet();
            switch (TypeKind)
            {
                case "bool":  _boolValue  = v is bool b ? b : false;       break;
                case "int":   _intValue   = ConvertToInt(v);                break;
                case "float": _floatValue = ConvertToFloat(v);              break;
                case "text":  _textValue  = v as string ?? string.Empty;    break;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("SettingsPropertyVM", $"ReadFromProperty({_owner.Id}.{Name})", ex);
        }
    }

    private static int ConvertToInt(object? v) => v switch
    {
        int i => i,
        long l => (int)l,
        // Round (not truncate) to match the other int paths: PresentationRowVM
        // uses (int)Math.Round, FluentSupport Convert.ToInt64, McmSelfTest
        // Math.Round -- truncation here made the same setting read 2 vs 3.
        float f => (int)System.Math.Round(f),
        double d => (int)System.Math.Round(d),
        _ => 0,
    };

    private static float ConvertToFloat(object? v) => v switch
    {
        float f => f,
        int i => (float)i,
        double d => (float)d,
        long l => (float)l,
        _ => 0f,
    };

    private void WriteBack(object value)
    {
        // S3: synthetic property -- delegate handles the write entirely.
        if (_writeAction != null)
        {
            try { _writeAction(value); }
            catch (Exception ex) { DiagLog.LogCaught("SettingsPropertyVM", $"WriteBack(synthetic {_displayName})", ex); }
            return;
        }

        // Fluent path — write into the FluentGlobalSettings dictionary by id.
        if (_fluentProp != null && _fluentOwner != null)
        {
            try { _fluentOwner.Set(_fluentProp.Id, value); }
            catch (Exception ex)
            {
                DiagLog.LogCaught("SettingsPropertyVM", $"WriteBack(fluent {_owner.Id}.{_fluentProp.Id})", ex);
            }
            return;
        }

        // Reflection path. Several defensive guards before touching the property.
        if (_property == null || !_property.CanWrite) return;

        // S1: foreign-settings adapter -- reflect over the wrapped instance.
        var reflTarget = _owner is MCM.Internal.SettingsRegistry.ForeignSettingsAdapter fa
            ? fa.Wrapped
            : (object)_owner;

        var targetType = _property.PropertyType;

        // 1. Skip delegate-typed properties (Action, Func, EventHandler). The
        //    Gauntlet data-binding system rebinds ALL slot value channels
        //    (IntValue, FloatValue, BoolValue, TextValue) every time the
        //    panel populates a new mod -- including for Action-typed
        //    button properties which only care about their Click event.
        //    Writing an int/float to an Action throws ArgumentException
        //    and floods the log with cascade errors that can destabilize
        //    the UI. Silently no-op instead.
        if (typeof(Delegate).IsAssignableFrom(targetType)) return;

        // 2. Skip if the value's type already matches what the property
        //    expects -- common case, fast path.
        if (value != null && targetType.IsInstanceOfType(value))
        {
            try { _property.SetValue(reflTarget, value); }
            catch (Exception ex)
            {
                DiagLog.LogCaught("SettingsPropertyVM", $"WriteBack({_owner.Id}.{_property.Name})", ex);
            }
            return;
        }

        // 3. Try to coerce. Some consumer mods declare a property as int
        //    but annotate it [SettingPropertyFloatingInteger] (XorberaxLegacy
        //    LoanTerm / RenownLossForUnpaidLoan / etc.). Our UI binds them
        //    as floats, the user moves a slider, and we'd call SetValue
        //    with a float on an int-typed property -- ArgumentException.
        //    Convert.ChangeType handles the standard numeric coercions.
        try
        {
            var coerced = value == null ? null : Convert.ChangeType(value, targetType);
            _property.SetValue(reflTarget, coerced);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught("SettingsPropertyVM", $"WriteBack({_owner.Id}.{_property.Name}) coerce {value?.GetType().Name ?? "null"} -> {targetType.Name}", ex);
        }
    }

    /// <summary>Run the CREST-style visibility hook against this property.</summary>
    public void RefreshVisibility(BaseSettings owner)
    {
        var hook = IsPropertyVisibleHook;
        if (hook == null) { IsVisible = true; return; }
        try { IsVisible = hook(DisplayName, owner); }
        catch { IsVisible = true; }
    }
}
