// AUTO-GENERATED. Do not edit by hand — regenerate via the Python block
// in scripts/Build-Phase1.ps1 (or the bash inline that produced this file).
// Slot{N}_X data-source accessors + Execute methods for slots 10..249, used
// by the Mod Config tab's unpaginated slot list (BuildSlotRow in
// OptionsPrefabExtensions.cs). Slots 0-9 keep their hand-rolled equivalents
// in the main OptionsVMMixin.cs file because they support the slider widget
// path; slots 10+ get text-only fallback for numerics.

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Extensions;  // NotifyPropertyChanged extension method
using Bannerlord.UIExtenderEx.ViewModels;
using BetaDeps.Foundation;

using TaleWorlds.Library;  // DataSourceProperty/DataSourceMethod live here

namespace MCM.UI.PrefabExtensions;

internal sealed partial class OptionsVMMixin
{

    // ---- Slot 10 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot10_IsVisible    => _slots[10] != null || _slotIsHeader[10];
    [DataSourceProperty] public bool   Slot10_IsHeader    => _slotIsHeader[10];
    [DataSourceProperty] public bool   Slot10_IsProperty  => _slots[10] != null && !_slotIsHeader[10];
    [DataSourceProperty] public string Slot10_DisplayName  => _slots[10]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot10_GroupHeader => _slotGroupHeaders[10] ?? string.Empty;
    [DataSourceProperty] public string Slot10_HintText     => _slots[10]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot10_IsBool       => _slots[10]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot10_IsInteger    => _slots[10]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot10_IsFloating   => _slots[10]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot10_IsNumeric    => _slots[10] != null && (_slots[10]!.IsInteger || _slots[10]!.IsFloating);
    [DataSourceProperty] public bool   Slot10_IsText       => _slots[10]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot10_IsButton     => _slots[10]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot10_IsDropdown   => _slots[10]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot10_DropdownText => _slots[10]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot10_MinValue     => (float)(_slots[10]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot10_MaxValue     => SafeMaxFloat(_slots[10]);
    [DataSourceProperty]
    public bool Slot10_BoolValue
    {
        get => _slots[10]?.BoolValue ?? false;
        set { if (_slots[10] != null) { _slots[10]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot10_BoolValue));} }
    }
    [DataSourceProperty] public string Slot10_BoolText => (_slots[10]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot10_ButtonText => _slots[10]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot10_IntValue
    {
        get => (float)(_slots[10]?.IntValue ?? 0);
        set { if (_slots[10] != null) { _slots[10]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot10_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot10_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot10_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot10_FloatValue
    {
        get { var p = _slots[10]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[10]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot10_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot10_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot10_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot10_ValueText
    {
        get
        {
            var p = _slots[10];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot10_TextValue
    {
        get => _slots[10]?.TextValue ?? string.Empty;
        set { if (_slots[10] != null) { _slots[10]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot10_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot10ToggleBool() { if (_slots[10] != null) { _slots[10]!.BoolValue = !_slots[10]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot10_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot10_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot10ActionButton() { try { _slots[10]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot10ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot10DropdownNext() => CycleSlot(10, +1);
    [DataSourceMethod] public void ExecuteSlot10DropdownPrev() => CycleSlot(10, -1);

    // ---- Slot 11 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot11_IsVisible    => _slots[11] != null || _slotIsHeader[11];
    [DataSourceProperty] public bool   Slot11_IsHeader    => _slotIsHeader[11];
    [DataSourceProperty] public bool   Slot11_IsProperty  => _slots[11] != null && !_slotIsHeader[11];
    [DataSourceProperty] public string Slot11_DisplayName  => _slots[11]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot11_GroupHeader => _slotGroupHeaders[11] ?? string.Empty;
    [DataSourceProperty] public string Slot11_HintText     => _slots[11]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot11_IsBool       => _slots[11]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot11_IsInteger    => _slots[11]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot11_IsFloating   => _slots[11]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot11_IsNumeric    => _slots[11] != null && (_slots[11]!.IsInteger || _slots[11]!.IsFloating);
    [DataSourceProperty] public bool   Slot11_IsText       => _slots[11]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot11_IsButton     => _slots[11]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot11_IsDropdown   => _slots[11]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot11_DropdownText => _slots[11]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot11_MinValue     => (float)(_slots[11]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot11_MaxValue     => SafeMaxFloat(_slots[11]);
    [DataSourceProperty]
    public bool Slot11_BoolValue
    {
        get => _slots[11]?.BoolValue ?? false;
        set { if (_slots[11] != null) { _slots[11]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot11_BoolValue));} }
    }
    [DataSourceProperty] public string Slot11_BoolText => (_slots[11]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot11_ButtonText => _slots[11]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot11_IntValue
    {
        get => (float)(_slots[11]?.IntValue ?? 0);
        set { if (_slots[11] != null) { _slots[11]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot11_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot11_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot11_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot11_FloatValue
    {
        get { var p = _slots[11]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[11]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot11_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot11_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot11_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot11_ValueText
    {
        get
        {
            var p = _slots[11];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot11_TextValue
    {
        get => _slots[11]?.TextValue ?? string.Empty;
        set { if (_slots[11] != null) { _slots[11]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot11_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot11ToggleBool() { if (_slots[11] != null) { _slots[11]!.BoolValue = !_slots[11]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot11_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot11_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot11ActionButton() { try { _slots[11]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot11ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot11DropdownNext() => CycleSlot(11, +1);
    [DataSourceMethod] public void ExecuteSlot11DropdownPrev() => CycleSlot(11, -1);

    // ---- Slot 12 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot12_IsVisible    => _slots[12] != null || _slotIsHeader[12];
    [DataSourceProperty] public bool   Slot12_IsHeader    => _slotIsHeader[12];
    [DataSourceProperty] public bool   Slot12_IsProperty  => _slots[12] != null && !_slotIsHeader[12];
    [DataSourceProperty] public string Slot12_DisplayName  => _slots[12]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot12_GroupHeader => _slotGroupHeaders[12] ?? string.Empty;
    [DataSourceProperty] public string Slot12_HintText     => _slots[12]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot12_IsBool       => _slots[12]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot12_IsInteger    => _slots[12]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot12_IsFloating   => _slots[12]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot12_IsNumeric    => _slots[12] != null && (_slots[12]!.IsInteger || _slots[12]!.IsFloating);
    [DataSourceProperty] public bool   Slot12_IsText       => _slots[12]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot12_IsButton     => _slots[12]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot12_IsDropdown   => _slots[12]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot12_DropdownText => _slots[12]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot12_MinValue     => (float)(_slots[12]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot12_MaxValue     => SafeMaxFloat(_slots[12]);
    [DataSourceProperty]
    public bool Slot12_BoolValue
    {
        get => _slots[12]?.BoolValue ?? false;
        set { if (_slots[12] != null) { _slots[12]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot12_BoolValue));} }
    }
    [DataSourceProperty] public string Slot12_BoolText => (_slots[12]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot12_ButtonText => _slots[12]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot12_IntValue
    {
        get => (float)(_slots[12]?.IntValue ?? 0);
        set { if (_slots[12] != null) { _slots[12]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot12_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot12_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot12_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot12_FloatValue
    {
        get { var p = _slots[12]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[12]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot12_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot12_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot12_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot12_ValueText
    {
        get
        {
            var p = _slots[12];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot12_TextValue
    {
        get => _slots[12]?.TextValue ?? string.Empty;
        set { if (_slots[12] != null) { _slots[12]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot12_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot12ToggleBool() { if (_slots[12] != null) { _slots[12]!.BoolValue = !_slots[12]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot12_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot12_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot12ActionButton() { try { _slots[12]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot12ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot12DropdownNext() => CycleSlot(12, +1);
    [DataSourceMethod] public void ExecuteSlot12DropdownPrev() => CycleSlot(12, -1);

    // ---- Slot 13 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot13_IsVisible    => _slots[13] != null || _slotIsHeader[13];
    [DataSourceProperty] public bool   Slot13_IsHeader    => _slotIsHeader[13];
    [DataSourceProperty] public bool   Slot13_IsProperty  => _slots[13] != null && !_slotIsHeader[13];
    [DataSourceProperty] public string Slot13_DisplayName  => _slots[13]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot13_GroupHeader => _slotGroupHeaders[13] ?? string.Empty;
    [DataSourceProperty] public string Slot13_HintText     => _slots[13]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot13_IsBool       => _slots[13]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot13_IsInteger    => _slots[13]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot13_IsFloating   => _slots[13]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot13_IsNumeric    => _slots[13] != null && (_slots[13]!.IsInteger || _slots[13]!.IsFloating);
    [DataSourceProperty] public bool   Slot13_IsText       => _slots[13]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot13_IsButton     => _slots[13]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot13_IsDropdown   => _slots[13]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot13_DropdownText => _slots[13]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot13_MinValue     => (float)(_slots[13]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot13_MaxValue     => SafeMaxFloat(_slots[13]);
    [DataSourceProperty]
    public bool Slot13_BoolValue
    {
        get => _slots[13]?.BoolValue ?? false;
        set { if (_slots[13] != null) { _slots[13]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot13_BoolValue));} }
    }
    [DataSourceProperty] public string Slot13_BoolText => (_slots[13]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot13_ButtonText => _slots[13]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot13_IntValue
    {
        get => (float)(_slots[13]?.IntValue ?? 0);
        set { if (_slots[13] != null) { _slots[13]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot13_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot13_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot13_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot13_FloatValue
    {
        get { var p = _slots[13]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[13]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot13_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot13_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot13_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot13_ValueText
    {
        get
        {
            var p = _slots[13];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot13_TextValue
    {
        get => _slots[13]?.TextValue ?? string.Empty;
        set { if (_slots[13] != null) { _slots[13]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot13_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot13ToggleBool() { if (_slots[13] != null) { _slots[13]!.BoolValue = !_slots[13]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot13_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot13_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot13ActionButton() { try { _slots[13]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot13ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot13DropdownNext() => CycleSlot(13, +1);
    [DataSourceMethod] public void ExecuteSlot13DropdownPrev() => CycleSlot(13, -1);

    // ---- Slot 14 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot14_IsVisible    => _slots[14] != null || _slotIsHeader[14];
    [DataSourceProperty] public bool   Slot14_IsHeader    => _slotIsHeader[14];
    [DataSourceProperty] public bool   Slot14_IsProperty  => _slots[14] != null && !_slotIsHeader[14];
    [DataSourceProperty] public string Slot14_DisplayName  => _slots[14]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot14_GroupHeader => _slotGroupHeaders[14] ?? string.Empty;
    [DataSourceProperty] public string Slot14_HintText     => _slots[14]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot14_IsBool       => _slots[14]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot14_IsInteger    => _slots[14]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot14_IsFloating   => _slots[14]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot14_IsNumeric    => _slots[14] != null && (_slots[14]!.IsInteger || _slots[14]!.IsFloating);
    [DataSourceProperty] public bool   Slot14_IsText       => _slots[14]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot14_IsButton     => _slots[14]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot14_IsDropdown   => _slots[14]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot14_DropdownText => _slots[14]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot14_MinValue     => (float)(_slots[14]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot14_MaxValue     => SafeMaxFloat(_slots[14]);
    [DataSourceProperty]
    public bool Slot14_BoolValue
    {
        get => _slots[14]?.BoolValue ?? false;
        set { if (_slots[14] != null) { _slots[14]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot14_BoolValue));} }
    }
    [DataSourceProperty] public string Slot14_BoolText => (_slots[14]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot14_ButtonText => _slots[14]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot14_IntValue
    {
        get => (float)(_slots[14]?.IntValue ?? 0);
        set { if (_slots[14] != null) { _slots[14]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot14_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot14_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot14_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot14_FloatValue
    {
        get { var p = _slots[14]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[14]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot14_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot14_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot14_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot14_ValueText
    {
        get
        {
            var p = _slots[14];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot14_TextValue
    {
        get => _slots[14]?.TextValue ?? string.Empty;
        set { if (_slots[14] != null) { _slots[14]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot14_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot14ToggleBool() { if (_slots[14] != null) { _slots[14]!.BoolValue = !_slots[14]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot14_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot14_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot14ActionButton() { try { _slots[14]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot14ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot14DropdownNext() => CycleSlot(14, +1);
    [DataSourceMethod] public void ExecuteSlot14DropdownPrev() => CycleSlot(14, -1);

    // ---- Slot 15 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot15_IsVisible    => _slots[15] != null || _slotIsHeader[15];
    [DataSourceProperty] public bool   Slot15_IsHeader    => _slotIsHeader[15];
    [DataSourceProperty] public bool   Slot15_IsProperty  => _slots[15] != null && !_slotIsHeader[15];
    [DataSourceProperty] public string Slot15_DisplayName  => _slots[15]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot15_GroupHeader => _slotGroupHeaders[15] ?? string.Empty;
    [DataSourceProperty] public string Slot15_HintText     => _slots[15]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot15_IsBool       => _slots[15]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot15_IsInteger    => _slots[15]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot15_IsFloating   => _slots[15]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot15_IsNumeric    => _slots[15] != null && (_slots[15]!.IsInteger || _slots[15]!.IsFloating);
    [DataSourceProperty] public bool   Slot15_IsText       => _slots[15]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot15_IsButton     => _slots[15]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot15_IsDropdown   => _slots[15]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot15_DropdownText => _slots[15]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot15_MinValue     => (float)(_slots[15]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot15_MaxValue     => SafeMaxFloat(_slots[15]);
    [DataSourceProperty]
    public bool Slot15_BoolValue
    {
        get => _slots[15]?.BoolValue ?? false;
        set { if (_slots[15] != null) { _slots[15]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot15_BoolValue));} }
    }
    [DataSourceProperty] public string Slot15_BoolText => (_slots[15]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot15_ButtonText => _slots[15]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot15_IntValue
    {
        get => (float)(_slots[15]?.IntValue ?? 0);
        set { if (_slots[15] != null) { _slots[15]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot15_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot15_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot15_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot15_FloatValue
    {
        get { var p = _slots[15]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[15]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot15_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot15_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot15_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot15_ValueText
    {
        get
        {
            var p = _slots[15];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot15_TextValue
    {
        get => _slots[15]?.TextValue ?? string.Empty;
        set { if (_slots[15] != null) { _slots[15]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot15_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot15ToggleBool() { if (_slots[15] != null) { _slots[15]!.BoolValue = !_slots[15]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot15_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot15_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot15ActionButton() { try { _slots[15]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot15ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot15DropdownNext() => CycleSlot(15, +1);
    [DataSourceMethod] public void ExecuteSlot15DropdownPrev() => CycleSlot(15, -1);

    // ---- Slot 16 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot16_IsVisible    => _slots[16] != null || _slotIsHeader[16];
    [DataSourceProperty] public bool   Slot16_IsHeader    => _slotIsHeader[16];
    [DataSourceProperty] public bool   Slot16_IsProperty  => _slots[16] != null && !_slotIsHeader[16];
    [DataSourceProperty] public string Slot16_DisplayName  => _slots[16]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot16_GroupHeader => _slotGroupHeaders[16] ?? string.Empty;
    [DataSourceProperty] public string Slot16_HintText     => _slots[16]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot16_IsBool       => _slots[16]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot16_IsInteger    => _slots[16]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot16_IsFloating   => _slots[16]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot16_IsNumeric    => _slots[16] != null && (_slots[16]!.IsInteger || _slots[16]!.IsFloating);
    [DataSourceProperty] public bool   Slot16_IsText       => _slots[16]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot16_IsButton     => _slots[16]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot16_IsDropdown   => _slots[16]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot16_DropdownText => _slots[16]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot16_MinValue     => (float)(_slots[16]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot16_MaxValue     => SafeMaxFloat(_slots[16]);
    [DataSourceProperty]
    public bool Slot16_BoolValue
    {
        get => _slots[16]?.BoolValue ?? false;
        set { if (_slots[16] != null) { _slots[16]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot16_BoolValue));} }
    }
    [DataSourceProperty] public string Slot16_BoolText => (_slots[16]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot16_ButtonText => _slots[16]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot16_IntValue
    {
        get => (float)(_slots[16]?.IntValue ?? 0);
        set { if (_slots[16] != null) { _slots[16]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot16_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot16_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot16_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot16_FloatValue
    {
        get { var p = _slots[16]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[16]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot16_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot16_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot16_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot16_ValueText
    {
        get
        {
            var p = _slots[16];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot16_TextValue
    {
        get => _slots[16]?.TextValue ?? string.Empty;
        set { if (_slots[16] != null) { _slots[16]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot16_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot16ToggleBool() { if (_slots[16] != null) { _slots[16]!.BoolValue = !_slots[16]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot16_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot16_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot16ActionButton() { try { _slots[16]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot16ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot16DropdownNext() => CycleSlot(16, +1);
    [DataSourceMethod] public void ExecuteSlot16DropdownPrev() => CycleSlot(16, -1);

    // ---- Slot 17 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot17_IsVisible    => _slots[17] != null || _slotIsHeader[17];
    [DataSourceProperty] public bool   Slot17_IsHeader    => _slotIsHeader[17];
    [DataSourceProperty] public bool   Slot17_IsProperty  => _slots[17] != null && !_slotIsHeader[17];
    [DataSourceProperty] public string Slot17_DisplayName  => _slots[17]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot17_GroupHeader => _slotGroupHeaders[17] ?? string.Empty;
    [DataSourceProperty] public string Slot17_HintText     => _slots[17]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot17_IsBool       => _slots[17]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot17_IsInteger    => _slots[17]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot17_IsFloating   => _slots[17]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot17_IsNumeric    => _slots[17] != null && (_slots[17]!.IsInteger || _slots[17]!.IsFloating);
    [DataSourceProperty] public bool   Slot17_IsText       => _slots[17]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot17_IsButton     => _slots[17]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot17_IsDropdown   => _slots[17]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot17_DropdownText => _slots[17]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot17_MinValue     => (float)(_slots[17]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot17_MaxValue     => SafeMaxFloat(_slots[17]);
    [DataSourceProperty]
    public bool Slot17_BoolValue
    {
        get => _slots[17]?.BoolValue ?? false;
        set { if (_slots[17] != null) { _slots[17]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot17_BoolValue));} }
    }
    [DataSourceProperty] public string Slot17_BoolText => (_slots[17]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot17_ButtonText => _slots[17]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot17_IntValue
    {
        get => (float)(_slots[17]?.IntValue ?? 0);
        set { if (_slots[17] != null) { _slots[17]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot17_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot17_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot17_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot17_FloatValue
    {
        get { var p = _slots[17]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[17]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot17_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot17_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot17_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot17_ValueText
    {
        get
        {
            var p = _slots[17];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot17_TextValue
    {
        get => _slots[17]?.TextValue ?? string.Empty;
        set { if (_slots[17] != null) { _slots[17]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot17_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot17ToggleBool() { if (_slots[17] != null) { _slots[17]!.BoolValue = !_slots[17]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot17_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot17_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot17ActionButton() { try { _slots[17]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot17ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot17DropdownNext() => CycleSlot(17, +1);
    [DataSourceMethod] public void ExecuteSlot17DropdownPrev() => CycleSlot(17, -1);

    // ---- Slot 18 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot18_IsVisible    => _slots[18] != null || _slotIsHeader[18];
    [DataSourceProperty] public bool   Slot18_IsHeader    => _slotIsHeader[18];
    [DataSourceProperty] public bool   Slot18_IsProperty  => _slots[18] != null && !_slotIsHeader[18];
    [DataSourceProperty] public string Slot18_DisplayName  => _slots[18]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot18_GroupHeader => _slotGroupHeaders[18] ?? string.Empty;
    [DataSourceProperty] public string Slot18_HintText     => _slots[18]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot18_IsBool       => _slots[18]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot18_IsInteger    => _slots[18]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot18_IsFloating   => _slots[18]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot18_IsNumeric    => _slots[18] != null && (_slots[18]!.IsInteger || _slots[18]!.IsFloating);
    [DataSourceProperty] public bool   Slot18_IsText       => _slots[18]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot18_IsButton     => _slots[18]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot18_IsDropdown   => _slots[18]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot18_DropdownText => _slots[18]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot18_MinValue     => (float)(_slots[18]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot18_MaxValue     => SafeMaxFloat(_slots[18]);
    [DataSourceProperty]
    public bool Slot18_BoolValue
    {
        get => _slots[18]?.BoolValue ?? false;
        set { if (_slots[18] != null) { _slots[18]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot18_BoolValue));} }
    }
    [DataSourceProperty] public string Slot18_BoolText => (_slots[18]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot18_ButtonText => _slots[18]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot18_IntValue
    {
        get => (float)(_slots[18]?.IntValue ?? 0);
        set { if (_slots[18] != null) { _slots[18]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot18_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot18_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot18_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot18_FloatValue
    {
        get { var p = _slots[18]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[18]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot18_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot18_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot18_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot18_ValueText
    {
        get
        {
            var p = _slots[18];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot18_TextValue
    {
        get => _slots[18]?.TextValue ?? string.Empty;
        set { if (_slots[18] != null) { _slots[18]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot18_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot18ToggleBool() { if (_slots[18] != null) { _slots[18]!.BoolValue = !_slots[18]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot18_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot18_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot18ActionButton() { try { _slots[18]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot18ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot18DropdownNext() => CycleSlot(18, +1);
    [DataSourceMethod] public void ExecuteSlot18DropdownPrev() => CycleSlot(18, -1);

    // ---- Slot 19 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot19_IsVisible    => _slots[19] != null || _slotIsHeader[19];
    [DataSourceProperty] public bool   Slot19_IsHeader    => _slotIsHeader[19];
    [DataSourceProperty] public bool   Slot19_IsProperty  => _slots[19] != null && !_slotIsHeader[19];
    [DataSourceProperty] public string Slot19_DisplayName  => _slots[19]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot19_GroupHeader => _slotGroupHeaders[19] ?? string.Empty;
    [DataSourceProperty] public string Slot19_HintText     => _slots[19]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot19_IsBool       => _slots[19]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot19_IsInteger    => _slots[19]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot19_IsFloating   => _slots[19]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot19_IsNumeric    => _slots[19] != null && (_slots[19]!.IsInteger || _slots[19]!.IsFloating);
    [DataSourceProperty] public bool   Slot19_IsText       => _slots[19]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot19_IsButton     => _slots[19]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot19_IsDropdown   => _slots[19]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot19_DropdownText => _slots[19]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot19_MinValue     => (float)(_slots[19]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot19_MaxValue     => SafeMaxFloat(_slots[19]);
    [DataSourceProperty]
    public bool Slot19_BoolValue
    {
        get => _slots[19]?.BoolValue ?? false;
        set { if (_slots[19] != null) { _slots[19]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot19_BoolValue));} }
    }
    [DataSourceProperty] public string Slot19_BoolText => (_slots[19]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty] public string Slot19_ButtonText => _slots[19]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot19_IntValue
    {
        get => (float)(_slots[19]?.IntValue ?? 0);
        set { if (_slots[19] != null) { _slots[19]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot19_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot19_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot19_EditableValueText));} }
    }
    [DataSourceProperty]
    public float Slot19_FloatValue
    {
        get { var p = _slots[19]; if (p == null) return 0f; return p.IsInteger ? (float)p.IntValue : p.FloatValue; }
        set { var p = _slots[19]; if (p == null) return; if (p.IsInteger) p.IntValue = (int)value; else if (p.IsFloating) p.FloatValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot19_FloatValue)); ViewModel.NotifyPropertyChanged(nameof(Slot19_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot19_EditableValueText)); }
    }
    [DataSourceProperty]
    public string Slot19_ValueText
    {
        get
        {
            var p = _slots[19];
            if (p == null) return string.Empty;
            try {
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot19_TextValue
    {
        get => _slots[19]?.TextValue ?? string.Empty;
        set { if (_slots[19] != null) { _slots[19]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot19_TextValue));} }
    }

    [DataSourceMethod] public void ExecuteSlot19ToggleBool() { if (_slots[19] != null) { _slots[19]!.BoolValue = !_slots[19]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot19_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot19_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot19ActionButton() { try { _slots[19]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot19ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot19DropdownNext() => CycleSlot(19, +1);
    [DataSourceMethod] public void ExecuteSlot19DropdownPrev() => CycleSlot(19, -1);

    // v0.7.6 click-to-edit: editable numeric value bindings for slots 10-19.
    // Pairs with the EditableTextWidget in BuildUnifiedSliderBlock. Slots 0-9
    // live in OptionsVMMixin.cs.
    [DataSourceProperty] public string Slot10_EditableValueText { get => FormatSlotValueText(10); set { SetSlotFromEditableText(10, value); } }
    [DataSourceProperty] public string Slot11_EditableValueText { get => FormatSlotValueText(11); set { SetSlotFromEditableText(11, value); } }
    [DataSourceProperty] public string Slot12_EditableValueText { get => FormatSlotValueText(12); set { SetSlotFromEditableText(12, value); } }
    [DataSourceProperty] public string Slot13_EditableValueText { get => FormatSlotValueText(13); set { SetSlotFromEditableText(13, value); } }
    [DataSourceProperty] public string Slot14_EditableValueText { get => FormatSlotValueText(14); set { SetSlotFromEditableText(14, value); } }
    [DataSourceProperty] public string Slot15_EditableValueText { get => FormatSlotValueText(15); set { SetSlotFromEditableText(15, value); } }
    [DataSourceProperty] public string Slot16_EditableValueText { get => FormatSlotValueText(16); set { SetSlotFromEditableText(16, value); } }
    [DataSourceProperty] public string Slot17_EditableValueText { get => FormatSlotValueText(17); set { SetSlotFromEditableText(17, value); } }
    [DataSourceProperty] public string Slot18_EditableValueText { get => FormatSlotValueText(18); set { SetSlotFromEditableText(18, value); } }
    [DataSourceProperty] public string Slot19_EditableValueText { get => FormatSlotValueText(19); set { SetSlotFromEditableText(19, value); } }
}
