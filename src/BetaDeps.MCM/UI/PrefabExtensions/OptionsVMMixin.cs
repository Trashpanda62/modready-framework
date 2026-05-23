// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// OptionsVMMixin attaches to TaleWorlds OptionsVM and exposes scalar bindings
// for the Mod Config tab. 10 editable property slots per page (Slot0..Slot9);
// see SlotCount constant. Defensive try/catch around RefreshSlots so any
// binding-side failure logs rather than crashes the game.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Extensions;
using Bannerlord.UIExtenderEx.ViewModels;

using BetaDeps.Foundation;

using MCM.Internal;
using MCM.UI.GUI.ViewModels;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

[ViewModelMixin(
    HandleDerived = true,
    TargetTypeName = "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.OptionsVM")]
internal sealed partial class OptionsVMMixin : BaseViewModelMixin<ViewModel>
{
    private const string Tag = "OptionsVMMixin";
    private const int SlotCount = 20;

    private bool _modConfigVisible;
    private string _modConfigTitle = "Mod Configuration";
    private string _registeredModsList = string.Empty;
    private string _summaryText = string.Empty;

    private RegisteredSettings[] _registered = System.Array.Empty<RegisteredSettings>();
    // v1.0: filtered view of _registered. Driven by BetaDepsModSearchText.
    // When the search text is empty, _filteredRegistered is the same array
    // as _registered; otherwise it's a sub-array matching the search query.
    // SelectMod / ExecutePrevMod / ExecuteNextMod all index through this.
    private RegisteredSettings[] _filteredRegistered = System.Array.Empty<RegisteredSettings>();
    private string _modSearchText = string.Empty;
    private int _currentModIndex;
    private SettingsVM? _currentSettingsVM;
    private List<SettingsPropertyVM> _currentFlatProps = new();
    private int _currentPageIndex;
    private readonly SettingsPropertyVM?[] _slots = new SettingsPropertyVM?[SlotCount];
    // Parallel array: the group header text for each slot row. When a slot is
    // a "header-only" divider row, this holds the group name and _slotIsHeader
    // is true; the property row that follows has _slotIsHeader=false and
    // GroupHeader=string.Empty.
    private readonly string[] _slotGroupHeaders = new string[SlotCount];
    // Parallel array: true if this slot renders as a full-width section
    // header instead of a property row. The XML prefab uses this as the
    // IsVisible toggle to switch each slot between "section divider" mode
    // and "property row" mode.
    private readonly bool[] _slotIsHeader = new bool[SlotCount];

    // Flat "presentation list" that interleaves header markers with property
    // entries. Built in SelectMod -> RebuildPresentationList. Each entry is
    // either ("header text", null) or (string.Empty, SettingsPropertyVM).
    private List<(string header, SettingsPropertyVM? prop)> _presentation = new();

    private string _selectedModName = "(no mod)";
    private string _selectedModSummary = string.Empty;
    private string _selectedPageSummary = string.Empty;

    [DataSourceProperty] public bool   BetaDepsModConfigTabVisible           { get => _modConfigVisible; set { _modConfigVisible = value; } }
    [DataSourceProperty] public string BetaDepsModConfigTitle                { get => _modConfigTitle;   set { _modConfigTitle = value; } }
    [DataSourceProperty] public string BetaDepsModConfigRegisteredModsList   { get => _registeredModsList; set { _registeredModsList = value; } }
    [DataSourceProperty] public string BetaDepsModConfigSummary              { get => _summaryText; set { _summaryText = value; } }

    /// <summary>
    /// v1.0: mod-list search/filter. EditableTextWidget in the Mod Config header
    /// binds two-way to this. When the user types, ApplyFilter() rebuilds
    /// _filteredRegistered to mods whose DisplayName / Id / SourceAssemblyName
    /// contain the query (case-insensitive substring). With 24+ mods registered,
    /// search saves a lot of Prev/Next-button cycling.
    /// </summary>
    [DataSourceProperty]
    public string BetaDepsModSearchText
    {
        get => _modSearchText;
        set
        {
            var trimmed = value ?? string.Empty;
            if (_modSearchText == trimmed) return;
            _modSearchText = trimmed;
            ApplyFilter();
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsModSearchText));
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsSearchClearVisible));
        }
    }

    /// <summary>
    /// v1.0: visibility of the Clear button next to the search field. Hidden
    /// when no filter is active so the row stays uncluttered.
    /// </summary>
    [DataSourceProperty]
    public bool BetaDepsSearchClearVisible => !string.IsNullOrEmpty(_modSearchText);

    [DataSourceProperty] public string SelectedModName     => _selectedModName;
    [DataSourceProperty] public string SelectedModSummary  => _selectedModSummary;
    [DataSourceProperty] public string SelectedPageSummary => _selectedPageSummary;

    // (v0.4.23 removed the _currentRows / CurrentRows NavigatableListPanel
    //  experiment — the current XML uses fixed Slot0..Slot9 + pagination
    //  rather than a scrollable list. v0.6 audit deleted the dead members.)

    [DataSourceProperty] public string HoveredOptionName => _hoveredOptionName;
    private string _hoveredOptionName = string.Empty;
    // ---- Slider constant bindings ------------------------------------
    // Vanilla OptionItem.xml binds SliderWidget.IsDiscrete, DiscreteIncrementInterval,
    // and UpdateValueContinuously to data-source properties (@IsDiscrete etc.).
    // Using literal values for these attributes in our slot rows crashes the
    // native SliderWidget initialization. These four properties expose
    // constants the slider XML can bind to.
    [DataSourceProperty] public bool SliderIsDiscreteTrue  => true;
    [DataSourceProperty] public bool SliderIsDiscreteFalse => false;
    // DiscreteIncrementInterval is INT on the native side. Returning a float
    // caused ArgumentException ("System.Single cannot be converted to
    // System.Int32") deep in GauntletView.RefreshBinding.
    [DataSourceProperty] public int  SliderIncrementOne    => 1;
    [DataSourceProperty] public bool SliderUpdateFalse     => false;

    // SliderWidget native code divides by (Max-Min). If a consumer mod has
    // MinValue == MaxValue, or the slot is empty (both default to 0), the
    // native slider divides by zero and the whole Options screen crashes
    // before tabs/buttons even render. Always return Min + 1 minimum.
    private static float SafeMaxFloat(MCM.UI.GUI.ViewModels.SettingsPropertyVM? p)
    {
        if (p == null) return 100f;
        var min = (float)p.MinValue;
        var max = (float)p.MaxValue;
        return max > min ? max : min + 1f;
    }

    // ---- Per-slot data source for MCMOptionRow prefab instances ------
    // The v0.4.4 approach (per user direction): each slot row is rendered
    // by a separate prefab instance (MCMOptionRow.xml) that takes a
    // DataSource. The prefab body references @DisplayName / @BoolText /
    // @MinValue / @MaxValue / @IntValue / @IsBool / @IsInteger / etc.
    // directly (no Slot{N}_ prefix), giving each SliderWidget its own
    // prefab scope -- the same way vanilla OptionItem.xml works.
    //
    // SlotN_VM returns the underlying SettingsPropertyVM if the slot is
    // populated, or the shared _emptyPlaceholderVM otherwise (binding
    // refresh on a null DataSource throws; a placeholder keeps all
    // bindings safely resolvable as defaults).
    private static readonly SettingsPropertyVM _emptyPlaceholderVM = MakeEmptyPlaceholderVM();
    private static SettingsPropertyVM MakeEmptyPlaceholderVM()
    {
        // Invisible header VM. Bindings on MCMOptionRow that read IsVisible
        // see false and the whole row collapses; bindings that read other
        // properties (DisplayName, MinValue, etc.) see safe defaults so
        // SliderWidget construction doesn't blow up on missing values.
        var vm = SettingsPropertyVM.CreateHeader(string.Empty);
        vm.IsVisible = false;
        return vm;
    }
    // Cached header VMs (one per slot). Re-using the same VM instance per
    // slot keeps the binding stable -- only the HeaderText property notifies
    // when the underlying group changes.
    private readonly SettingsPropertyVM[] _slotHeaderVMs =
        System.Linq.Enumerable.Range(0, SlotCount)
            .Select(_ => SettingsPropertyVM.CreateHeader(string.Empty))
            .ToArray();
    private SettingsPropertyVM SlotVM(int n)
    {
        if (n < 0 || n >= _slots.Length) return _emptyPlaceholderVM;
        var prop = _slots[n];
        if (prop != null) return prop;
        if (_slotIsHeader[n])
        {
            // Header slot. Reuse the cached header VM, just refresh its text.
            var hdr = _slotHeaderVMs[n];
            hdr.HeaderText = _slotGroupHeaders[n] ?? string.Empty;
            hdr.IsVisible = true;
            return hdr;
        }
        return _emptyPlaceholderVM;
    }
    [DataSourceProperty] public SettingsPropertyVM Slot0_VM => SlotVM(0);
    [DataSourceProperty] public SettingsPropertyVM Slot1_VM => SlotVM(1);
    [DataSourceProperty] public SettingsPropertyVM Slot2_VM => SlotVM(2);
    [DataSourceProperty] public SettingsPropertyVM Slot3_VM => SlotVM(3);
    [DataSourceProperty] public SettingsPropertyVM Slot4_VM => SlotVM(4);
    [DataSourceProperty] public SettingsPropertyVM Slot5_VM => SlotVM(5);
    [DataSourceProperty] public SettingsPropertyVM Slot6_VM => SlotVM(6);
    [DataSourceProperty] public SettingsPropertyVM Slot7_VM => SlotVM(7);
    [DataSourceProperty] public SettingsPropertyVM Slot8_VM => SlotVM(8);
    [DataSourceProperty] public SettingsPropertyVM Slot9_VM => SlotVM(9);
    [DataSourceProperty] public SettingsPropertyVM Slot10_VM => SlotVM(10);
    [DataSourceProperty] public SettingsPropertyVM Slot11_VM => SlotVM(11);
    [DataSourceProperty] public SettingsPropertyVM Slot12_VM => SlotVM(12);
    [DataSourceProperty] public SettingsPropertyVM Slot13_VM => SlotVM(13);
    [DataSourceProperty] public SettingsPropertyVM Slot14_VM => SlotVM(14);
    [DataSourceProperty] public SettingsPropertyVM Slot15_VM => SlotVM(15);
    [DataSourceProperty] public SettingsPropertyVM Slot16_VM => SlotVM(16);
    [DataSourceProperty] public SettingsPropertyVM Slot17_VM => SlotVM(17);
    [DataSourceProperty] public SettingsPropertyVM Slot18_VM => SlotVM(18);
    [DataSourceProperty] public SettingsPropertyVM Slot19_VM => SlotVM(19);
    [DataSourceProperty] public SettingsPropertyVM Slot20_VM => SlotVM(20);
    [DataSourceProperty] public SettingsPropertyVM Slot21_VM => SlotVM(21);
    [DataSourceProperty] public SettingsPropertyVM Slot22_VM => SlotVM(22);
    [DataSourceProperty] public SettingsPropertyVM Slot23_VM => SlotVM(23);
    [DataSourceProperty] public SettingsPropertyVM Slot24_VM => SlotVM(24);
    [DataSourceProperty] public SettingsPropertyVM Slot25_VM => SlotVM(25);
    [DataSourceProperty] public SettingsPropertyVM Slot26_VM => SlotVM(26);
    [DataSourceProperty] public SettingsPropertyVM Slot27_VM => SlotVM(27);
    [DataSourceProperty] public SettingsPropertyVM Slot28_VM => SlotVM(28);
    [DataSourceProperty] public SettingsPropertyVM Slot29_VM => SlotVM(29);
    [DataSourceProperty] public SettingsPropertyVM Slot30_VM => SlotVM(30);
    [DataSourceProperty] public SettingsPropertyVM Slot31_VM => SlotVM(31);
    [DataSourceProperty] public SettingsPropertyVM Slot32_VM => SlotVM(32);
    [DataSourceProperty] public SettingsPropertyVM Slot33_VM => SlotVM(33);
    [DataSourceProperty] public SettingsPropertyVM Slot34_VM => SlotVM(34);
    [DataSourceProperty] public SettingsPropertyVM Slot35_VM => SlotVM(35);
    [DataSourceProperty] public SettingsPropertyVM Slot36_VM => SlotVM(36);
    [DataSourceProperty] public SettingsPropertyVM Slot37_VM => SlotVM(37);
    [DataSourceProperty] public SettingsPropertyVM Slot38_VM => SlotVM(38);
    [DataSourceProperty] public SettingsPropertyVM Slot39_VM => SlotVM(39);
    [DataSourceProperty] public SettingsPropertyVM Slot40_VM => SlotVM(40);
    [DataSourceProperty] public SettingsPropertyVM Slot41_VM => SlotVM(41);
    [DataSourceProperty] public SettingsPropertyVM Slot42_VM => SlotVM(42);
    [DataSourceProperty] public SettingsPropertyVM Slot43_VM => SlotVM(43);
    [DataSourceProperty] public SettingsPropertyVM Slot44_VM => SlotVM(44);
    [DataSourceProperty] public SettingsPropertyVM Slot45_VM => SlotVM(45);
    [DataSourceProperty] public SettingsPropertyVM Slot46_VM => SlotVM(46);
    [DataSourceProperty] public SettingsPropertyVM Slot47_VM => SlotVM(47);
    [DataSourceProperty] public SettingsPropertyVM Slot48_VM => SlotVM(48);
    [DataSourceProperty] public SettingsPropertyVM Slot49_VM => SlotVM(49);
    [DataSourceProperty] public bool Slot10_IsActive => _slots[10] != null || _slotIsHeader[10];
    [DataSourceProperty] public bool Slot11_IsActive => _slots[11] != null || _slotIsHeader[11];
    [DataSourceProperty] public bool Slot12_IsActive => _slots[12] != null || _slotIsHeader[12];
    [DataSourceProperty] public bool Slot13_IsActive => _slots[13] != null || _slotIsHeader[13];
    [DataSourceProperty] public bool Slot14_IsActive => _slots[14] != null || _slotIsHeader[14];
    [DataSourceProperty] public bool Slot15_IsActive => _slots[15] != null || _slotIsHeader[15];
    [DataSourceProperty] public bool Slot16_IsActive => _slots[16] != null || _slotIsHeader[16];
    [DataSourceProperty] public bool Slot17_IsActive => _slots[17] != null || _slotIsHeader[17];
    [DataSourceProperty] public bool Slot18_IsActive => _slots[18] != null || _slotIsHeader[18];
    [DataSourceProperty] public bool Slot19_IsActive => _slots[19] != null || _slotIsHeader[19];
    [DataSourceProperty] public bool Slot20_IsActive => _slots[20] != null || _slotIsHeader[20];
    [DataSourceProperty] public bool Slot21_IsActive => _slots[21] != null || _slotIsHeader[21];
    [DataSourceProperty] public bool Slot22_IsActive => _slots[22] != null || _slotIsHeader[22];
    [DataSourceProperty] public bool Slot23_IsActive => _slots[23] != null || _slotIsHeader[23];
    [DataSourceProperty] public bool Slot24_IsActive => _slots[24] != null || _slotIsHeader[24];
    [DataSourceProperty] public bool Slot25_IsActive => _slots[25] != null || _slotIsHeader[25];
    [DataSourceProperty] public bool Slot26_IsActive => _slots[26] != null || _slotIsHeader[26];
    [DataSourceProperty] public bool Slot27_IsActive => _slots[27] != null || _slotIsHeader[27];
    [DataSourceProperty] public bool Slot28_IsActive => _slots[28] != null || _slotIsHeader[28];
    [DataSourceProperty] public bool Slot29_IsActive => _slots[29] != null || _slotIsHeader[29];
    [DataSourceProperty] public bool Slot30_IsActive => _slots[30] != null || _slotIsHeader[30];
    [DataSourceProperty] public bool Slot31_IsActive => _slots[31] != null || _slotIsHeader[31];
    [DataSourceProperty] public bool Slot32_IsActive => _slots[32] != null || _slotIsHeader[32];
    [DataSourceProperty] public bool Slot33_IsActive => _slots[33] != null || _slotIsHeader[33];
    [DataSourceProperty] public bool Slot34_IsActive => _slots[34] != null || _slotIsHeader[34];
    [DataSourceProperty] public bool Slot35_IsActive => _slots[35] != null || _slotIsHeader[35];
    [DataSourceProperty] public bool Slot36_IsActive => _slots[36] != null || _slotIsHeader[36];
    [DataSourceProperty] public bool Slot37_IsActive => _slots[37] != null || _slotIsHeader[37];
    [DataSourceProperty] public bool Slot38_IsActive => _slots[38] != null || _slotIsHeader[38];
    [DataSourceProperty] public bool Slot39_IsActive => _slots[39] != null || _slotIsHeader[39];
    [DataSourceProperty] public bool Slot40_IsActive => _slots[40] != null || _slotIsHeader[40];
    [DataSourceProperty] public bool Slot41_IsActive => _slots[41] != null || _slotIsHeader[41];
    [DataSourceProperty] public bool Slot42_IsActive => _slots[42] != null || _slotIsHeader[42];
    [DataSourceProperty] public bool Slot43_IsActive => _slots[43] != null || _slotIsHeader[43];
    [DataSourceProperty] public bool Slot44_IsActive => _slots[44] != null || _slotIsHeader[44];
    [DataSourceProperty] public bool Slot45_IsActive => _slots[45] != null || _slotIsHeader[45];
    [DataSourceProperty] public bool Slot46_IsActive => _slots[46] != null || _slotIsHeader[46];
    [DataSourceProperty] public bool Slot47_IsActive => _slots[47] != null || _slotIsHeader[47];
    [DataSourceProperty] public bool Slot48_IsActive => _slots[48] != null || _slotIsHeader[48];
    [DataSourceProperty] public bool Slot49_IsActive => _slots[49] != null || _slotIsHeader[49];
    // ---- end v0.4 polished prefab fields ----------------------------

    private string _hoveredHintText = string.Empty;
    [DataSourceProperty] public string HoveredHintText => _hoveredHintText;
    [DataSourceProperty] public bool   IsHintVisible   => !string.IsNullOrEmpty(_hoveredHintText);

    private void SetHoveredHint(int slot)
    {
        var ht = (slot >= 0 && slot < _slots.Length) ? (_slots[slot]?.HintText    ?? string.Empty) : string.Empty;
        var nm = (slot >= 0 && slot < _slots.Length) ? (_slots[slot]?.DisplayName ?? string.Empty) : string.Empty;
        var nameChanged = _hoveredOptionName != nm;
        var hintChanged = _hoveredHintText   != ht;
        if (!nameChanged && !hintChanged) return;
        _hoveredOptionName = nm;
        _hoveredHintText   = ht;
        if (nameChanged) ViewModel.NotifyPropertyChanged(nameof(HoveredOptionName));
        if (hintChanged) { ViewModel.NotifyPropertyChanged(nameof(HoveredHintText)); ViewModel.NotifyPropertyChanged(nameof(IsHintVisible)); }
    }
    private void ClearHoveredHint()
    {
        if (string.IsNullOrEmpty(_hoveredHintText) && string.IsNullOrEmpty(_hoveredOptionName)) return;
        _hoveredHintText = string.Empty;
        _hoveredOptionName = string.Empty;
        ViewModel.NotifyPropertyChanged(nameof(HoveredHintText));
        ViewModel.NotifyPropertyChanged(nameof(HoveredOptionName));
        ViewModel.NotifyPropertyChanged(nameof(IsHintVisible));
    }
    // Page indicator dots: up to 10 dots shown above the slot list. Each
    // PageN_Exists is true if the current mod has at least N+1 pages;
    // PageN_IsCurrent is true if N is the current page index. The dot widget
    // in the prefab uses Exists for IsVisible and IsCurrent for an active brush.
    private int _totalPages = 1;
    [DataSourceProperty] public bool Page0_Exists    => _totalPages > 0;
    [DataSourceProperty] public bool Page1_Exists    => _totalPages > 1;
    [DataSourceProperty] public bool Page2_Exists    => _totalPages > 2;
    [DataSourceProperty] public bool Page3_Exists    => _totalPages > 3;
    [DataSourceProperty] public bool Page4_Exists    => _totalPages > 4;
    [DataSourceProperty] public bool Page5_Exists    => _totalPages > 5;
    [DataSourceProperty] public bool Page6_Exists    => _totalPages > 6;
    [DataSourceProperty] public bool Page7_Exists    => _totalPages > 7;
    [DataSourceProperty] public bool Page8_Exists    => _totalPages > 8;
    [DataSourceProperty] public bool Page9_Exists    => _totalPages > 9;
    [DataSourceProperty] public bool Page0_IsCurrent => _currentPageIndex == 0;
    [DataSourceProperty] public bool Page1_IsCurrent => _currentPageIndex == 1;
    [DataSourceProperty] public bool Page2_IsCurrent => _currentPageIndex == 2;
    [DataSourceProperty] public bool Page3_IsCurrent => _currentPageIndex == 3;
    [DataSourceProperty] public bool Page4_IsCurrent => _currentPageIndex == 4;
    [DataSourceProperty] public bool Page5_IsCurrent => _currentPageIndex == 5;
    [DataSourceProperty] public bool Page6_IsCurrent => _currentPageIndex == 6;
    [DataSourceProperty] public bool Page7_IsCurrent => _currentPageIndex == 7;
    [DataSourceProperty] public bool Page8_IsCurrent => _currentPageIndex == 8;
    [DataSourceProperty] public bool Page9_IsCurrent => _currentPageIndex == 9;

    // ---- Slot 0 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot0_IsVisible    => _slots[0] != null || _slotIsHeader[0];
    [DataSourceProperty] public bool   Slot0_IsHeader    => _slotIsHeader[0];
    [DataSourceProperty] public bool   Slot0_IsProperty  => _slots[0] != null && !_slotIsHeader[0];
    [DataSourceProperty] public string Slot0_DisplayName  => _slots[0]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot0_GroupHeader => _slotGroupHeaders[0] ?? string.Empty;
    [DataSourceProperty] public string Slot0_HintText     => _slots[0]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot0_IsBool       => _slots[0]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot0_IsInteger    => _slots[0]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot0_IsFloating   => _slots[0]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot0_IsNumeric    => _slots[0] != null && (_slots[0]!.IsInteger || _slots[0]!.IsFloating);
    [DataSourceProperty] public bool   Slot0_IsText       => _slots[0]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot0_IsButton     => _slots[0]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot0_IsDropdown   => _slots[0]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot0_DropdownText => _slots[0]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot0_MinValue     => (float)(_slots[0]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot0_MaxValue     => SafeMaxFloat(_slots[0]);
    [DataSourceProperty]
    public bool Slot0_BoolValue
    {
        get => _slots[0]?.BoolValue ?? false;
        set { if (_slots[0] != null) { _slots[0]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot0_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot0_BoolText => (_slots[0]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot0_ButtonText => _slots[0]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot0_IntValue
    {
        get => (float)(_slots[0]?.IntValue ?? 0);
        set { if (_slots[0] != null) { _slots[0]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot0_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot0_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot0_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[0];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[0];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot0_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot0_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot0_ValueText
    {
        get
        {
            var p = _slots[0];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot0_TextValue
    {
        get => _slots[0]?.TextValue ?? string.Empty;
        set { if (_slots[0] != null) { _slots[0]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot0_TextValue));} }
    }

    // ---- Slot 1 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot1_IsVisible    => _slots[1] != null || _slotIsHeader[1];
    [DataSourceProperty] public bool   Slot1_IsHeader    => _slotIsHeader[1];
    [DataSourceProperty] public bool   Slot1_IsProperty  => _slots[1] != null && !_slotIsHeader[1];
    [DataSourceProperty] public string Slot1_DisplayName  => _slots[1]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot1_GroupHeader => _slotGroupHeaders[1] ?? string.Empty;
    [DataSourceProperty] public string Slot1_HintText     => _slots[1]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot1_IsBool       => _slots[1]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot1_IsInteger    => _slots[1]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot1_IsFloating   => _slots[1]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot1_IsNumeric    => _slots[1] != null && (_slots[1]!.IsInteger || _slots[1]!.IsFloating);
    [DataSourceProperty] public bool   Slot1_IsText       => _slots[1]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot1_IsButton     => _slots[1]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot1_IsDropdown   => _slots[1]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot1_DropdownText => _slots[1]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot1_MinValue     => (float)(_slots[1]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot1_MaxValue     => SafeMaxFloat(_slots[1]);
    [DataSourceProperty]
    public bool Slot1_BoolValue
    {
        get => _slots[1]?.BoolValue ?? false;
        set { if (_slots[1] != null) { _slots[1]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot1_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot1_BoolText => (_slots[1]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot1_ButtonText => _slots[1]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot1_IntValue
    {
        get => (float)(_slots[1]?.IntValue ?? 0);
        set { if (_slots[1] != null) { _slots[1]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot1_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot1_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot1_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[1];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[1];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot1_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot1_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot1_ValueText
    {
        get
        {
            var p = _slots[1];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot1_TextValue
    {
        get => _slots[1]?.TextValue ?? string.Empty;
        set { if (_slots[1] != null) { _slots[1]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot1_TextValue));} }
    }

    // ---- Slot 2 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot2_IsVisible    => _slots[2] != null || _slotIsHeader[2];
    [DataSourceProperty] public bool   Slot2_IsHeader    => _slotIsHeader[2];
    [DataSourceProperty] public bool   Slot2_IsProperty  => _slots[2] != null && !_slotIsHeader[2];
    [DataSourceProperty] public string Slot2_DisplayName  => _slots[2]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot2_GroupHeader => _slotGroupHeaders[2] ?? string.Empty;
    [DataSourceProperty] public string Slot2_HintText     => _slots[2]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot2_IsBool       => _slots[2]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot2_IsInteger    => _slots[2]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot2_IsFloating   => _slots[2]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot2_IsNumeric    => _slots[2] != null && (_slots[2]!.IsInteger || _slots[2]!.IsFloating);
    [DataSourceProperty] public bool   Slot2_IsText       => _slots[2]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot2_IsButton     => _slots[2]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot2_IsDropdown   => _slots[2]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot2_DropdownText => _slots[2]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot2_MinValue     => (float)(_slots[2]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot2_MaxValue     => SafeMaxFloat(_slots[2]);
    [DataSourceProperty]
    public bool Slot2_BoolValue
    {
        get => _slots[2]?.BoolValue ?? false;
        set { if (_slots[2] != null) { _slots[2]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot2_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot2_BoolText => (_slots[2]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot2_ButtonText => _slots[2]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot2_IntValue
    {
        get => (float)(_slots[2]?.IntValue ?? 0);
        set { if (_slots[2] != null) { _slots[2]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot2_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot2_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot2_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[2];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[2];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot2_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot2_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot2_ValueText
    {
        get
        {
            var p = _slots[2];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot2_TextValue
    {
        get => _slots[2]?.TextValue ?? string.Empty;
        set { if (_slots[2] != null) { _slots[2]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot2_TextValue));} }
    }

    // ---- Slot 3 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot3_IsVisible    => _slots[3] != null || _slotIsHeader[3];
    [DataSourceProperty] public bool   Slot3_IsHeader    => _slotIsHeader[3];
    [DataSourceProperty] public bool   Slot3_IsProperty  => _slots[3] != null && !_slotIsHeader[3];
    [DataSourceProperty] public string Slot3_DisplayName  => _slots[3]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot3_GroupHeader => _slotGroupHeaders[3] ?? string.Empty;
    [DataSourceProperty] public string Slot3_HintText     => _slots[3]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot3_IsBool       => _slots[3]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot3_IsInteger    => _slots[3]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot3_IsFloating   => _slots[3]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot3_IsNumeric    => _slots[3] != null && (_slots[3]!.IsInteger || _slots[3]!.IsFloating);
    [DataSourceProperty] public bool   Slot3_IsText       => _slots[3]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot3_IsButton     => _slots[3]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot3_IsDropdown   => _slots[3]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot3_DropdownText => _slots[3]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot3_MinValue     => (float)(_slots[3]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot3_MaxValue     => SafeMaxFloat(_slots[3]);
    [DataSourceProperty]
    public bool Slot3_BoolValue
    {
        get => _slots[3]?.BoolValue ?? false;
        set { if (_slots[3] != null) { _slots[3]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot3_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot3_BoolText => (_slots[3]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot3_ButtonText => _slots[3]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot3_IntValue
    {
        get => (float)(_slots[3]?.IntValue ?? 0);
        set { if (_slots[3] != null) { _slots[3]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot3_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot3_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot3_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[3];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[3];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot3_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot3_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot3_ValueText
    {
        get
        {
            var p = _slots[3];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot3_TextValue
    {
        get => _slots[3]?.TextValue ?? string.Empty;
        set { if (_slots[3] != null) { _slots[3]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot3_TextValue));} }
    }

    // ---- Slot 4 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot4_IsVisible    => _slots[4] != null || _slotIsHeader[4];
    [DataSourceProperty] public bool   Slot4_IsHeader    => _slotIsHeader[4];
    [DataSourceProperty] public bool   Slot4_IsProperty  => _slots[4] != null && !_slotIsHeader[4];
    [DataSourceProperty] public string Slot4_DisplayName  => _slots[4]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot4_GroupHeader => _slotGroupHeaders[4] ?? string.Empty;
    [DataSourceProperty] public string Slot4_HintText     => _slots[4]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot4_IsBool       => _slots[4]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot4_IsInteger    => _slots[4]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot4_IsFloating   => _slots[4]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot4_IsNumeric    => _slots[4] != null && (_slots[4]!.IsInteger || _slots[4]!.IsFloating);
    [DataSourceProperty] public bool   Slot4_IsText       => _slots[4]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot4_IsButton     => _slots[4]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot4_IsDropdown   => _slots[4]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot4_DropdownText => _slots[4]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot4_MinValue     => (float)(_slots[4]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot4_MaxValue     => SafeMaxFloat(_slots[4]);
    [DataSourceProperty]
    public bool Slot4_BoolValue
    {
        get => _slots[4]?.BoolValue ?? false;
        set { if (_slots[4] != null) { _slots[4]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot4_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot4_BoolText => (_slots[4]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot4_ButtonText => _slots[4]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot4_IntValue
    {
        get => (float)(_slots[4]?.IntValue ?? 0);
        set { if (_slots[4] != null) { _slots[4]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot4_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot4_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot4_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[4];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[4];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot4_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot4_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot4_ValueText
    {
        get
        {
            var p = _slots[4];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot4_TextValue
    {
        get => _slots[4]?.TextValue ?? string.Empty;
        set { if (_slots[4] != null) { _slots[4]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot4_TextValue));} }
    }

    // ---- Slot 5 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot5_IsVisible    => _slots[5] != null || _slotIsHeader[5];
    [DataSourceProperty] public bool   Slot5_IsHeader    => _slotIsHeader[5];
    [DataSourceProperty] public bool   Slot5_IsProperty  => _slots[5] != null && !_slotIsHeader[5];
    [DataSourceProperty] public string Slot5_DisplayName  => _slots[5]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot5_GroupHeader => _slotGroupHeaders[5] ?? string.Empty;
    [DataSourceProperty] public string Slot5_HintText     => _slots[5]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot5_IsBool       => _slots[5]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot5_IsInteger    => _slots[5]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot5_IsFloating   => _slots[5]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot5_IsNumeric    => _slots[5] != null && (_slots[5]!.IsInteger || _slots[5]!.IsFloating);
    [DataSourceProperty] public bool   Slot5_IsText       => _slots[5]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot5_IsButton     => _slots[5]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot5_IsDropdown   => _slots[5]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot5_DropdownText => _slots[5]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot5_MinValue     => (float)(_slots[5]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot5_MaxValue     => SafeMaxFloat(_slots[5]);
    [DataSourceProperty]
    public bool Slot5_BoolValue
    {
        get => _slots[5]?.BoolValue ?? false;
        set { if (_slots[5] != null) { _slots[5]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot5_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot5_BoolText => (_slots[5]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot5_ButtonText => _slots[5]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot5_IntValue
    {
        get => (float)(_slots[5]?.IntValue ?? 0);
        set { if (_slots[5] != null) { _slots[5]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot5_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot5_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot5_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[5];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[5];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot5_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot5_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot5_ValueText
    {
        get
        {
            var p = _slots[5];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot5_TextValue
    {
        get => _slots[5]?.TextValue ?? string.Empty;
        set { if (_slots[5] != null) { _slots[5]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot5_TextValue));} }
    }

    // ---- Slot 6 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot6_IsVisible    => _slots[6] != null || _slotIsHeader[6];
    [DataSourceProperty] public bool   Slot6_IsHeader    => _slotIsHeader[6];
    [DataSourceProperty] public bool   Slot6_IsProperty  => _slots[6] != null && !_slotIsHeader[6];
    [DataSourceProperty] public string Slot6_DisplayName  => _slots[6]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot6_GroupHeader => _slotGroupHeaders[6] ?? string.Empty;
    [DataSourceProperty] public string Slot6_HintText     => _slots[6]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot6_IsBool       => _slots[6]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot6_IsInteger    => _slots[6]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot6_IsFloating   => _slots[6]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot6_IsNumeric    => _slots[6] != null && (_slots[6]!.IsInteger || _slots[6]!.IsFloating);
    [DataSourceProperty] public bool   Slot6_IsText       => _slots[6]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot6_IsButton     => _slots[6]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot6_IsDropdown   => _slots[6]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot6_DropdownText => _slots[6]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot6_MinValue     => (float)(_slots[6]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot6_MaxValue     => SafeMaxFloat(_slots[6]);
    [DataSourceProperty]
    public bool Slot6_BoolValue
    {
        get => _slots[6]?.BoolValue ?? false;
        set { if (_slots[6] != null) { _slots[6]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot6_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot6_BoolText => (_slots[6]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot6_ButtonText => _slots[6]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot6_IntValue
    {
        get => (float)(_slots[6]?.IntValue ?? 0);
        set { if (_slots[6] != null) { _slots[6]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot6_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot6_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot6_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[6];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[6];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot6_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot6_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot6_ValueText
    {
        get
        {
            var p = _slots[6];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot6_TextValue
    {
        get => _slots[6]?.TextValue ?? string.Empty;
        set { if (_slots[6] != null) { _slots[6]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot6_TextValue));} }
    }

    // ---- Slot 7 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot7_IsVisible    => _slots[7] != null || _slotIsHeader[7];
    [DataSourceProperty] public bool   Slot7_IsHeader    => _slotIsHeader[7];
    [DataSourceProperty] public bool   Slot7_IsProperty  => _slots[7] != null && !_slotIsHeader[7];
    [DataSourceProperty] public string Slot7_DisplayName  => _slots[7]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot7_GroupHeader => _slotGroupHeaders[7] ?? string.Empty;
    [DataSourceProperty] public string Slot7_HintText     => _slots[7]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot7_IsBool       => _slots[7]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot7_IsInteger    => _slots[7]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot7_IsFloating   => _slots[7]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot7_IsNumeric    => _slots[7] != null && (_slots[7]!.IsInteger || _slots[7]!.IsFloating);
    [DataSourceProperty] public bool   Slot7_IsText       => _slots[7]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot7_IsButton     => _slots[7]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot7_IsDropdown   => _slots[7]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot7_DropdownText => _slots[7]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot7_MinValue     => (float)(_slots[7]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot7_MaxValue     => SafeMaxFloat(_slots[7]);
    [DataSourceProperty]
    public bool Slot7_BoolValue
    {
        get => _slots[7]?.BoolValue ?? false;
        set { if (_slots[7] != null) { _slots[7]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot7_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot7_BoolText => (_slots[7]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot7_ButtonText => _slots[7]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot7_IntValue
    {
        get => (float)(_slots[7]?.IntValue ?? 0);
        set { if (_slots[7] != null) { _slots[7]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot7_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot7_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot7_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[7];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[7];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot7_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot7_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot7_ValueText
    {
        get
        {
            var p = _slots[7];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot7_TextValue
    {
        get => _slots[7]?.TextValue ?? string.Empty;
        set { if (_slots[7] != null) { _slots[7]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot7_TextValue));} }
    }

    // ---- Slot 8 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot8_IsVisible    => _slots[8] != null || _slotIsHeader[8];
    [DataSourceProperty] public bool   Slot8_IsHeader    => _slotIsHeader[8];
    [DataSourceProperty] public bool   Slot8_IsProperty  => _slots[8] != null && !_slotIsHeader[8];
    [DataSourceProperty] public string Slot8_DisplayName  => _slots[8]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot8_GroupHeader => _slotGroupHeaders[8] ?? string.Empty;
    [DataSourceProperty] public string Slot8_HintText     => _slots[8]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot8_IsBool       => _slots[8]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot8_IsInteger    => _slots[8]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot8_IsFloating   => _slots[8]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot8_IsNumeric    => _slots[8] != null && (_slots[8]!.IsInteger || _slots[8]!.IsFloating);
    [DataSourceProperty] public bool   Slot8_IsText       => _slots[8]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot8_IsButton     => _slots[8]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot8_IsDropdown   => _slots[8]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot8_DropdownText => _slots[8]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot8_MinValue     => (float)(_slots[8]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot8_MaxValue     => SafeMaxFloat(_slots[8]);
    [DataSourceProperty]
    public bool Slot8_BoolValue
    {
        get => _slots[8]?.BoolValue ?? false;
        set { if (_slots[8] != null) { _slots[8]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot8_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot8_BoolText => (_slots[8]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot8_ButtonText => _slots[8]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot8_IntValue
    {
        get => (float)(_slots[8]?.IntValue ?? 0);
        set { if (_slots[8] != null) { _slots[8]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot8_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot8_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot8_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[8];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[8];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot8_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot8_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot8_ValueText
    {
        get
        {
            var p = _slots[8];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot8_TextValue
    {
        get => _slots[8]?.TextValue ?? string.Empty;
        set { if (_slots[8] != null) { _slots[8]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot8_TextValue));} }
    }

    // ---- Slot 9 ----------------------------------------------------
    [DataSourceProperty] public bool   Slot9_IsVisible    => _slots[9] != null || _slotIsHeader[9];
    [DataSourceProperty] public bool   Slot9_IsHeader    => _slotIsHeader[9];
    [DataSourceProperty] public bool   Slot9_IsProperty  => _slots[9] != null && !_slotIsHeader[9];
    [DataSourceProperty] public string Slot9_DisplayName  => _slots[9]?.DisplayName ?? string.Empty;
    [DataSourceProperty] public string Slot9_GroupHeader => _slotGroupHeaders[9] ?? string.Empty;
    [DataSourceProperty] public string Slot9_HintText     => _slots[9]?.HintText    ?? string.Empty;
    [DataSourceProperty] public bool   Slot9_IsBool       => _slots[9]?.IsBool      ?? false;
    [DataSourceProperty] public bool   Slot9_IsInteger    => _slots[9]?.IsInteger   ?? false;
    [DataSourceProperty] public bool   Slot9_IsFloating   => _slots[9]?.IsFloating  ?? false;
    [DataSourceProperty] public bool   Slot9_IsNumeric    => _slots[9] != null && (_slots[9]!.IsInteger || _slots[9]!.IsFloating);
    [DataSourceProperty] public bool   Slot9_IsText       => _slots[9]?.IsText      ?? false;
    [DataSourceProperty] public bool   Slot9_IsButton     => _slots[9]?.IsButton    ?? false;
    [DataSourceProperty] public bool   Slot9_IsDropdown   => _slots[9]?.IsDropdown  ?? false;
    [DataSourceProperty] public string Slot9_DropdownText => _slots[9]?.DropdownDisplayText ?? string.Empty;
    [DataSourceProperty] public float  Slot9_MinValue     => (float)(_slots[9]?.MinValue ?? 0);
    [DataSourceProperty] public float  Slot9_MaxValue     => SafeMaxFloat(_slots[9]);
    [DataSourceProperty]
    public bool Slot9_BoolValue
    {
        get => _slots[9]?.BoolValue ?? false;
        set { if (_slots[9] != null) { _slots[9]!.BoolValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot9_BoolValue));} }
    }
    [DataSourceProperty]
    public string Slot9_BoolText => (_slots[9]?.BoolValue ?? false) ? "ON" : "OFF";
    [DataSourceProperty]
    public string Slot9_ButtonText => _slots[9]?.ButtonContentText ?? "Run";
    [DataSourceProperty]
    public float Slot9_IntValue
    {
        get => (float)(_slots[9]?.IntValue ?? 0);
        set { if (_slots[9] != null) { _slots[9]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot9_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot9_ValueText));} }
    }
    [DataSourceProperty]
    public float Slot9_FloatValue
    {
        // v0.5.5 unified-binding dispatch: returns IntValue (cast to float) for
        // int settings, FloatValue for float settings. Single slider per slot
        // can now drive both numeric types, doubling slider coverage without
        // crossing the 6-per-page widget-construction ceiling.
        get
        {
            var p = _slots[9];
            if (p == null) return 0f;
            return p.IsInteger ? (float)p.IntValue : p.FloatValue;
        }
        set
        {
            var p = _slots[9];
            if (p == null) return;
            if (p.IsInteger) p.IntValue = (int)value;
            else if (p.IsFloating) p.FloatValue = value;
            ViewModel.NotifyPropertyChanged(nameof(Slot9_FloatValue));
            ViewModel.NotifyPropertyChanged(nameof(Slot9_ValueText));
        }
    }
    [DataSourceProperty]
    public string Slot9_ValueText
    {
        get
        {
            var p = _slots[9];
            if (p == null) return string.Empty;
            try {
                // Consumer mods sometimes put a TaleWorlds localization key in
                // ValueFormat (e.g. "{=xl_setting_format_int}0"). Treat any
                // format that starts with "{=" as bogus and fall back to a
                // type-appropriate default.
                var fmt = p.ValueFormat;
                if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
                if (p.IsInteger) return p.IntValue.ToString(fmt);
                if (p.IsFloating) return p.FloatValue.ToString(fmt);
            } catch { }
            return string.Empty;
        }
    }
    [DataSourceProperty]
    public string Slot9_TextValue
    {
        get => _slots[9]?.TextValue ?? string.Empty;
        set { if (_slots[9] != null) { _slots[9]!.TextValue = value; ViewModel.NotifyPropertyChanged(nameof(Slot9_TextValue));} }
    }

    public OptionsVMMixin(ViewModel vm) : base(vm)
    {
        try { SettingsRegistry.DiscoverAll(); }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "DiscoverAll", ex); }
        RebuildModList();
        // v1.0 (task #12): VM-side hover wiring. Slots 10-99 use the new
        // VM-binding template, which sets DataSource="{SlotN_VM}" on the row
        // and binds Command.HoverBegin/HoverEnd to the VM's ExecuteHoverBegin/
        // ExecuteHoverEnd. Those VM methods fire the static HoverCallback /
        // HoverEndCallback. Subscribing here routes the callback back into our
        // _hoveredHintText/_hoveredOptionName state so the footer hint panel
        // updates the same way it does for slots 0-9's per-slot Hover handlers.
        // Static-typed callbacks (single global subscriber across all mixin
        // instances) is fine here — only one OptionsVM is alive at a time.
        SettingsPropertyVM.HoverCallback = vm =>
        {
            try
            {
                if (vm == null) { ClearHoveredHint(); return; }
                var ht = vm.HintText ?? string.Empty;
                var nm = vm.DisplayName ?? string.Empty;
                var nameChanged = _hoveredOptionName != nm;
                var hintChanged = _hoveredHintText   != ht;
                if (!nameChanged && !hintChanged) return;
                _hoveredOptionName = nm;
                _hoveredHintText   = ht;
                if (nameChanged) ViewModel.NotifyPropertyChanged(nameof(HoveredOptionName));
                if (hintChanged)
                {
                    ViewModel.NotifyPropertyChanged(nameof(HoveredHintText));
                    ViewModel.NotifyPropertyChanged(nameof(IsHintVisible));
                }
            }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, "HoverCallback", ex); }
        };
        SettingsPropertyVM.HoverEndCallback = () =>
        {
            try { ClearHoveredHint(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, "HoverEndCallback", ex); }
        };
        SelectMod(0);
    }

    private void RebuildModList()
    {
        try
        {
            _registered = SettingsRegistry.All
                .OrderBy(r => r.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // v0.5.6 polish: annotate cryptic DisplayName strings with their
            // source folder. AIInfluence ships a secondary settings class with
            // DisplayName "BUG-FIX-0" -- without context, users can't tell what
            // mod that's from. If the DisplayName doesn't already contain the
            // source assembly name (case-insensitive, ignoring spaces), prefix
            // "<AssemblyName> — <DisplayName>" so users see "AIInfluence —
            // BUG-FIX-0". Skipped when the DisplayName is empty (no useful
            // string to prefix) or when the assembly name is generic / matches
            // already.
            try
            {
                foreach (var r in _registered)
                {
                    var dn = r.DisplayName ?? string.Empty;
                    var asm = r.SourceAssemblyName;
                    if (string.IsNullOrEmpty(dn) || string.IsNullOrEmpty(asm)) continue;
                    // Skip if the DisplayName already references the assembly
                    // (substring, case- and space-insensitive). Avoids
                    // redundancy like "AIInfluence — AIInfluence Settings".
                    var dnSquished = dn.Replace(" ", "").Replace(".", "");
                    var asmSquished = asm.Replace(" ", "").Replace(".", "");
                    if (dnSquished.IndexOf(asmSquished, System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (asmSquished.IndexOf(dnSquished, System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    r.DisplayName = asm + " — " + dn;
                }
            }
            catch (System.Exception fex) { DiagLog.LogCaught(Tag, "RebuildModList/prefix", fex); }

            // v0.5.6 polish: disambiguate registered settings that share a
            // DisplayName. When two settings have the same DisplayName (e.g.
            // RaiseYourTorch_MCM and RaiseYourTorchWithRYB_MCM both registering
            // as "Raise your Torch"), the cycler can't tell them apart. Append
            // a short hint derived from the registry Id to whichever copies
            // are duplicates so the user can pick the right one.
            try
            {
                var byName = _registered
                    .GroupBy(r => r.DisplayName ?? string.Empty, System.StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .ToArray();
                foreach (var g in byName)
                {
                    foreach (var dup in g)
                    {
                        // Derive a hint from the Id, e.g. RaiseYourTorchWithRYB_MCM -> "WithRYB"
                        var hint = ExtractIdHint(dup.Id, dup.DisplayName ?? string.Empty);
                        if (!string.IsNullOrEmpty(hint))
                        {
                            dup.DisplayName = (dup.DisplayName ?? string.Empty) + " (" + hint + ")";
                        }
                    }
                }
            }
            catch (System.Exception dex) { DiagLog.LogCaught(Tag, "RebuildModList/disambiguate", dex); }

            _summaryText = _registered.Length == 0
                ? "No mod settings discovered yet."
                : $"{_registered.Length} mod(s) registered. Use the Prev/Next buttons to cycle.";
            DiagLog.Log(Tag, $"RebuildModList: surfaced {_registered.Length} mod(s)");
            _filteredRegistered = _registered;
            ApplyFilter();
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "RebuildModList", ex);
            _summaryText = "(error reading SettingsRegistry; see runtime.log)";
        }
    }

    /// <summary>
    /// v1.0: recompute _filteredRegistered based on _modSearchText. Called by
    /// the BetaDepsModSearchText setter and by RebuildModList. Always resets
    /// the cycler to the first filtered result.
    /// </summary>
    private void ApplyFilter()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_modSearchText))
            {
                _filteredRegistered = _registered;
            }
            else
            {
                var q = _modSearchText.Trim();
                _filteredRegistered = _registered
                    .Where(r => (r.DisplayName ?? string.Empty).IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.Id ?? string.Empty).IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0
                             || (r.SourceAssemblyName ?? string.Empty).IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
            }
            // Jump to first match so the user sees a real mod immediately after filtering.
            _currentModIndex = 0;
            SelectMod(0);
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ApplyFilter", ex);
        }
    }

    // v0.5.6: helper used by disambiguation. Strip the common prefix/suffix
    // from a settings Id so what remains usefully distinguishes between two
    // copies. Returns empty if no useful hint can be derived.
    private static string ExtractIdHint(string id, string displayName)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        // Strip trailing _MCM, _Settings, _v1 etc.
        var clean = System.Text.RegularExpressions.Regex.Replace(id, @"_?(MCM|Settings|v\d+(\.\d+)*)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Strip everything that matches the DisplayName (case-insensitive, ignoring spaces)
        var dnNoSpaces = (displayName ?? string.Empty).Replace(" ", "");
        if (!string.IsNullOrEmpty(dnNoSpaces))
        {
            clean = System.Text.RegularExpressions.Regex.Replace(clean, System.Text.RegularExpressions.Regex.Escape(dnNoSpaces), "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        // Remove non-alphanumeric leading/trailing chars
        clean = clean.Trim('_', '-', ' ', '.');
        return clean;
    }

    private void SelectMod(int newIndex)
    {
        try
        {
            // v1.0: SelectMod operates on the FILTERED view, not the full list.
            // When the user searches, only matching mods can be cycled to.
            // When the search is empty, _filteredRegistered IS _registered.
            if (_filteredRegistered.Length == 0)
            {
                _currentModIndex = 0;
                _currentSettingsVM = null;
                _currentFlatProps = new List<SettingsPropertyVM>();
                _selectedModName = string.IsNullOrEmpty(_modSearchText) ? "(no mods)" : "(no matches)";
                _selectedModSummary = string.IsNullOrEmpty(_modSearchText)
                    ? "0 of 0"
                    : $"0 of 0 matching \"{_modSearchText}\"";
                _currentPageIndex = 0;
                RefreshSlots();
                NotifyHeader();
                return;
            }

            if (newIndex < 0) newIndex = _filteredRegistered.Length - 1;
            if (newIndex >= _filteredRegistered.Length) newIndex = 0;
            _currentModIndex = newIndex;

            var entry = _filteredRegistered[_currentModIndex];
            try { _currentSettingsVM = new SettingsVM(entry.Instance); }
            catch (System.Exception ex)
            {
                DiagLog.LogCaught(Tag, $"SettingsVM ctor for {entry.Id}", ex);
                _currentSettingsVM = null;
            }

            _currentFlatProps = new List<SettingsPropertyVM>();
            _presentation = new List<(string, SettingsPropertyVM?)>();
            if (_currentSettingsVM != null)
            {
                try
                {
                    string lastGroup = null!;
                    foreach (var g in _currentSettingsVM.SettingPropertyGroups)
                    {
                        var groupName = MCM.Internal.TextHelper.StripLocalizationKeys(g.GroupName ?? string.Empty);
                        // Emit a header-only divider row when the group changes
                        // (and the new group has a non-empty name).
                        if (!string.IsNullOrEmpty(groupName) &&
                            !string.Equals(groupName, lastGroup, System.StringComparison.Ordinal))
                        {
                            _presentation.Add((groupName, null));
                            lastGroup = groupName;
                        }
                        foreach (var p in g.SettingProperties)
                        {
                            _currentFlatProps.Add(p);
                            _presentation.Add((string.Empty, p));
                        }
                    }
                }
                catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"SelectMod/flatten {entry.Id}", ex); }
            }

            _selectedModName = MCM.Internal.TextHelper.StripLocalizationKeys(entry.DisplayName);
            // v1.0: show the position within the filtered view, plus a
            // "(of N total)" hint when a filter is active so the user can
            // tell how aggressively the search narrowed things.
            _selectedModSummary = string.IsNullOrEmpty(_modSearchText)
                ? $"Mod {_currentModIndex + 1} of {_filteredRegistered.Length}"
                : $"Mod {_currentModIndex + 1} of {_filteredRegistered.Length}  (filtered from {_registered.Length})";
            _currentPageIndex = 0;
            RefreshSlots();
            NotifyHeader();
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "SelectMod", ex); }
    }

    private void RefreshSlots()
    {
        try
        {
            int total = _presentation.Count;
            // v0.5.9 (task #13 perf): soft pagination. With SlotCount=20 most
            // mods fit on one page (no Prev/Next Page UI), but ROT-class mods
            // with 200+ properties get sliced. The 50-row prefab was too heavy
            // for Gauntlet's binding refresh — 1-10s mod switches. 20 rows
            // should be much snappier.
            int pages = total == 0 ? 1 : (total + SlotCount - 1) / SlotCount;
            _totalPages = pages;
            if (_currentPageIndex < 0) _currentPageIndex = pages - 1;
            if (_currentPageIndex >= pages) _currentPageIndex = 0;

            _selectedPageSummary = pages > 1
                ? $"Page {_currentPageIndex + 1} of {pages}"
                : string.Empty;

            int start = _currentPageIndex * SlotCount;
            for (int i = 0; i < SlotCount; i++)
            {
                int idx = start + i;
                if (idx >= 0 && idx < total)
                {
                    var entry = _presentation[idx];
                    _slots[i] = entry.prop;
                    _slotGroupHeaders[i] = entry.header ?? string.Empty;
                    _slotIsHeader[i] = entry.prop == null && !string.IsNullOrEmpty(entry.header);
                }
                else
                {
                    _slots[i] = null;
                    _slotGroupHeaders[i] = string.Empty;
                    _slotIsHeader[i] = false;
                }
            }

            NotifyAllSlots();
            ViewModel.NotifyPropertyChanged(nameof(SelectedPageSummary));
            ViewModel.NotifyPropertyChanged(nameof(PaginationVisible));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "RefreshSlots", ex); }
    }

    /// <summary>
    /// v0.5.9 (task #13 perf): Prev/Next Page button visibility binding.
    /// Only true when the current mod has more properties than one page can
    /// show, so single-page mods don't show pagination UI clutter.
    /// </summary>
    [DataSourceProperty] public bool PaginationVisible => _totalPages > 1;

    private void NotifyHeader()
    {
        try
        {
            ViewModel.NotifyPropertyChanged(nameof(SelectedModName));
            ViewModel.NotifyPropertyChanged(nameof(SelectedModSummary));
            ViewModel.NotifyPropertyChanged(nameof(SelectedPageSummary));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "NotifyHeader", ex); }
    }

    /// <summary>
    /// v1.0 (task #13, perf): TYPE-AWARE notifications. Previously fired
    /// 22 PropertyChanged events per slot regardless of type (bool, int,
    /// float, text, button, dropdown) — most were no-ops because the
    /// matching widgets were hidden via IsVisible. Each PropertyChanged
    /// causes Gauntlet to walk the widget tree looking for subscribers,
    /// so cutting event count is the highest-leverage perf optimization
    /// without changing the prefab structure. Now fires:
    ///   - ALWAYS for active slots: IsVisible, IsHeader, IsProperty,
    ///     DisplayName, GroupHeader, HintText, plus all the type-FLAGS
    ///     (IsBool/IsInteger/IsFloating/IsNumeric/IsText/IsButton/IsDropdown)
    ///     because the widget's own IsVisible binding reads them.
    ///   - CONDITIONALLY for active slots: only the value properties that
    ///     match the slot's TypeKind. Bool slots get BoolValue/BoolText.
    ///     Numerics get IntValue/FloatValue/ValueText/MinValue/MaxValue.
    ///     etc.
    ///   - For inactive slots: only IsVisible (so the row hides).
    /// Net effect: ~75% fewer PropertyChanged events on a typical mod
    /// switch, which should cut the mod-switch lag proportionally.
    /// </summary>
    private void NotifyAllSlots()
    {
        try
        {
            // Properties that always need notifying for active slots —
            // these drive the row's visibility / label, independent of type.
            string[] alwaysSuffixes =
            {
                "_IsVisible", "_IsHeader", "_IsProperty",
                "_DisplayName", "_GroupHeader", "_HintText",
                "_IsBool", "_IsInteger", "_IsFloating", "_IsNumeric",
                "_IsText", "_IsButton", "_IsDropdown",
            };
            // Type-conditional value properties.
            string[] boolSuffixes     = { "_BoolValue", "_BoolText" };
            string[] numericSuffixes  = { "_MinValue", "_MaxValue", "_IntValue", "_FloatValue", "_ValueText" };
            string[] textSuffixes     = { "_TextValue" };
            string[] buttonSuffixes   = { "_ButtonText" };
            string[] dropdownSuffixes = { "_DropdownText" };

            for (int n = 0; n < SlotCount; n++)
            {
                bool active = _slots[n] != null || _slotIsHeader[n];
                string slot = "Slot" + n;

                if (!active)
                {
                    // Inactive slot — collapse via IsVisible only.
                    ViewModel.NotifyPropertyChanged(slot + "_IsVisible");
                    continue;
                }

                foreach (var suf in alwaysSuffixes)
                    ViewModel.NotifyPropertyChanged(slot + suf);

                // Skip the type-conditional notifications for header rows
                // (they have no SettingsPropertyVM, just a header string).
                var prop = _slots[n];
                if (prop == null) continue;

                string[] valueSuffixes;
                switch (prop.TypeKind)
                {
                    case "bool":     valueSuffixes = boolSuffixes;     break;
                    case "int":
                    case "float":    valueSuffixes = numericSuffixes;  break;
                    case "text":     valueSuffixes = textSuffixes;     break;
                    case "button":   valueSuffixes = buttonSuffixes;   break;
                    case "dropdown": valueSuffixes = dropdownSuffixes; break;
                    default:         valueSuffixes = System.Array.Empty<string>(); break;
                }
                foreach (var suf in valueSuffixes)
                    ViewModel.NotifyPropertyChanged(slot + suf);
            }
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "NotifyAllSlots", ex); }
    }

    [DataSourceMethod] public void ExecuteOpenModConfig()
    {
        BetaDepsModConfigTabVisible = !BetaDepsModConfigTabVisible;
        try { SettingsRegistry.DiscoverAll(); } catch { }
        RebuildModList();
        SettingsPropertyVM.HoverCallback = OnRowHover;
        SelectMod(_currentModIndex);
    }

    private void OnRowHover(SettingsPropertyVM row)
    {
        if (row == null) return;
        var ht = row.HintText ?? string.Empty;
        var on = row.DisplayName ?? string.Empty;
        if (_hoveredHintText == ht && _hoveredOptionName == on) return;
        _hoveredHintText = ht;
        _hoveredOptionName = on;
        ViewModel.NotifyPropertyChanged(nameof(HoveredHintText));
        ViewModel.NotifyPropertyChanged(nameof(IsHintVisible));
        ViewModel.NotifyPropertyChanged(nameof(HoveredOptionName));
    }

    [DataSourceMethod] public void ExecuteSlot0Hover()    { SetHoveredHint(0); }
    [DataSourceMethod] public void ExecuteSlot0HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot1Hover()    { SetHoveredHint(1); }
    [DataSourceMethod] public void ExecuteSlot1HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot2Hover()    { SetHoveredHint(2); }
    [DataSourceMethod] public void ExecuteSlot2HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot3Hover()    { SetHoveredHint(3); }
    [DataSourceMethod] public void ExecuteSlot3HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot4Hover()    { SetHoveredHint(4); }
    [DataSourceMethod] public void ExecuteSlot4HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot5Hover()    { SetHoveredHint(5); }
    [DataSourceMethod] public void ExecuteSlot5HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot6Hover()    { SetHoveredHint(6); }
    [DataSourceMethod] public void ExecuteSlot6HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot7Hover()    { SetHoveredHint(7); }
    [DataSourceMethod] public void ExecuteSlot7HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot8Hover()    { SetHoveredHint(8); }
    [DataSourceMethod] public void ExecuteSlot8HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot9Hover()    { SetHoveredHint(9); }
    [DataSourceMethod] public void ExecuteSlot9HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot10Hover()    { SetHoveredHint(10); }
    [DataSourceMethod] public void ExecuteSlot10HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot11Hover()    { SetHoveredHint(11); }
    [DataSourceMethod] public void ExecuteSlot11HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot12Hover()    { SetHoveredHint(12); }
    [DataSourceMethod] public void ExecuteSlot12HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot13Hover()    { SetHoveredHint(13); }
    [DataSourceMethod] public void ExecuteSlot13HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot14Hover()    { SetHoveredHint(14); }
    [DataSourceMethod] public void ExecuteSlot14HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot15Hover()    { SetHoveredHint(15); }
    [DataSourceMethod] public void ExecuteSlot15HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot16Hover()    { SetHoveredHint(16); }
    [DataSourceMethod] public void ExecuteSlot16HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot17Hover()    { SetHoveredHint(17); }
    [DataSourceMethod] public void ExecuteSlot17HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot18Hover()    { SetHoveredHint(18); }
    [DataSourceMethod] public void ExecuteSlot18HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot19Hover()    { SetHoveredHint(19); }
    [DataSourceMethod] public void ExecuteSlot19HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot20Hover()    { SetHoveredHint(20); }
    [DataSourceMethod] public void ExecuteSlot20HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot21Hover()    { SetHoveredHint(21); }
    [DataSourceMethod] public void ExecuteSlot21HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot22Hover()    { SetHoveredHint(22); }
    [DataSourceMethod] public void ExecuteSlot22HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot23Hover()    { SetHoveredHint(23); }
    [DataSourceMethod] public void ExecuteSlot23HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot24Hover()    { SetHoveredHint(24); }
    [DataSourceMethod] public void ExecuteSlot24HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot25Hover()    { SetHoveredHint(25); }
    [DataSourceMethod] public void ExecuteSlot25HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot26Hover()    { SetHoveredHint(26); }
    [DataSourceMethod] public void ExecuteSlot26HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot27Hover()    { SetHoveredHint(27); }
    [DataSourceMethod] public void ExecuteSlot27HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot28Hover()    { SetHoveredHint(28); }
    [DataSourceMethod] public void ExecuteSlot28HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot29Hover()    { SetHoveredHint(29); }
    [DataSourceMethod] public void ExecuteSlot29HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot30Hover()    { SetHoveredHint(30); }
    [DataSourceMethod] public void ExecuteSlot30HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot31Hover()    { SetHoveredHint(31); }
    [DataSourceMethod] public void ExecuteSlot31HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot32Hover()    { SetHoveredHint(32); }
    [DataSourceMethod] public void ExecuteSlot32HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot33Hover()    { SetHoveredHint(33); }
    [DataSourceMethod] public void ExecuteSlot33HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot34Hover()    { SetHoveredHint(34); }
    [DataSourceMethod] public void ExecuteSlot34HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot35Hover()    { SetHoveredHint(35); }
    [DataSourceMethod] public void ExecuteSlot35HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot36Hover()    { SetHoveredHint(36); }
    [DataSourceMethod] public void ExecuteSlot36HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot37Hover()    { SetHoveredHint(37); }
    [DataSourceMethod] public void ExecuteSlot37HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot38Hover()    { SetHoveredHint(38); }
    [DataSourceMethod] public void ExecuteSlot38HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot39Hover()    { SetHoveredHint(39); }
    [DataSourceMethod] public void ExecuteSlot39HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot40Hover()    { SetHoveredHint(40); }
    [DataSourceMethod] public void ExecuteSlot40HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot41Hover()    { SetHoveredHint(41); }
    [DataSourceMethod] public void ExecuteSlot41HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot42Hover()    { SetHoveredHint(42); }
    [DataSourceMethod] public void ExecuteSlot42HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot43Hover()    { SetHoveredHint(43); }
    [DataSourceMethod] public void ExecuteSlot43HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot44Hover()    { SetHoveredHint(44); }
    [DataSourceMethod] public void ExecuteSlot44HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot45Hover()    { SetHoveredHint(45); }
    [DataSourceMethod] public void ExecuteSlot45HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot46Hover()    { SetHoveredHint(46); }
    [DataSourceMethod] public void ExecuteSlot46HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot47Hover()    { SetHoveredHint(47); }
    [DataSourceMethod] public void ExecuteSlot47HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot48Hover()    { SetHoveredHint(48); }
    [DataSourceMethod] public void ExecuteSlot48HoverEnd() { ClearHoveredHint(); }
    [DataSourceMethod] public void ExecuteSlot49Hover()    { SetHoveredHint(49); }
    [DataSourceMethod] public void ExecuteSlot49HoverEnd() { ClearHoveredHint(); }
    // ---- Per-slot dropdown cycle handlers --------------------------
    // Each slot row in the prefab gets a "[ < ] [ value ] [ > ]" cycle
    // control whose buttons bind to these Execute methods. Gauntlet's
    // Command.Click can't pass parameters, so we need one pair per slot.
    private void CycleSlot(int slot, int dir)
    {
        var p = (slot >= 0 && slot < _slots.Length) ? _slots[slot] : null;
        if (p == null) return;
        try
        {
            if (dir > 0) p.CycleDropdownNext(); else p.CycleDropdownPrev();
            switch (slot)
            {
                case 0: ViewModel.NotifyPropertyChanged(nameof(Slot0_DropdownText)); break;
                case 1: ViewModel.NotifyPropertyChanged(nameof(Slot1_DropdownText)); break;
                case 2: ViewModel.NotifyPropertyChanged(nameof(Slot2_DropdownText)); break;
                case 3: ViewModel.NotifyPropertyChanged(nameof(Slot3_DropdownText)); break;
                case 4: ViewModel.NotifyPropertyChanged(nameof(Slot4_DropdownText)); break;
                case 5: ViewModel.NotifyPropertyChanged(nameof(Slot5_DropdownText)); break;
                case 6: ViewModel.NotifyPropertyChanged(nameof(Slot6_DropdownText)); break;
                case 7: ViewModel.NotifyPropertyChanged(nameof(Slot7_DropdownText)); break;
                case 8: ViewModel.NotifyPropertyChanged(nameof(Slot8_DropdownText)); break;
                case 9: ViewModel.NotifyPropertyChanged(nameof(Slot9_DropdownText)); break;
            }
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"CycleSlot({slot},{dir})", ex); }
    }
    [DataSourceMethod] public void ExecuteSlot0ToggleBool() { if (_slots[0] != null) { _slots[0]!.BoolValue = !_slots[0]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot0_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot0_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot1ToggleBool() { if (_slots[1] != null) { _slots[1]!.BoolValue = !_slots[1]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot1_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot1_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot2ToggleBool() { if (_slots[2] != null) { _slots[2]!.BoolValue = !_slots[2]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot2_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot2_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot3ToggleBool() { if (_slots[3] != null) { _slots[3]!.BoolValue = !_slots[3]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot3_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot3_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot4ToggleBool() { if (_slots[4] != null) { _slots[4]!.BoolValue = !_slots[4]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot4_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot4_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot5ToggleBool() { if (_slots[5] != null) { _slots[5]!.BoolValue = !_slots[5]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot5_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot5_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot6ToggleBool() { if (_slots[6] != null) { _slots[6]!.BoolValue = !_slots[6]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot6_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot6_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot7ToggleBool() { if (_slots[7] != null) { _slots[7]!.BoolValue = !_slots[7]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot7_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot7_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot8ToggleBool() { if (_slots[8] != null) { _slots[8]!.BoolValue = !_slots[8]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot8_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot8_BoolText));} }
    [DataSourceMethod] public void ExecuteSlot9ToggleBool() { if (_slots[9] != null) { _slots[9]!.BoolValue = !_slots[9]!.BoolValue; ViewModel.NotifyPropertyChanged(nameof(Slot9_BoolValue)); ViewModel.NotifyPropertyChanged(nameof(Slot9_BoolText));} }
    // v0.5.6: per-slot action-button click handlers. Invokes the underlying
    // Action delegate via SettingsPropertyVM.InvokeAction. Used by IsButton
    // settings (e.g. AIInfluence's 'Join Discord' link, 'Support on Boosty').
    [DataSourceMethod] public void ExecuteSlot0ActionButton() { try { _slots[0]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot0ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot1ActionButton() { try { _slots[1]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot1ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot2ActionButton() { try { _slots[2]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot2ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot3ActionButton() { try { _slots[3]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot3ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot4ActionButton() { try { _slots[4]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot4ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot5ActionButton() { try { _slots[5]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot5ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot6ActionButton() { try { _slots[6]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot6ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot7ActionButton() { try { _slots[7]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot7ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot8ActionButton() { try { _slots[8]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot8ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot9ActionButton() { try { _slots[9]?.InvokeAction(); } catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteSlot9ActionButton", ex); } }
    [DataSourceMethod] public void ExecuteSlot0DropdownNext() => CycleSlot(0, +1);
    [DataSourceMethod] public void ExecuteSlot0DropdownPrev() => CycleSlot(0, -1);
    [DataSourceMethod] public void ExecuteSlot1DropdownNext() => CycleSlot(1, +1);
    [DataSourceMethod] public void ExecuteSlot1DropdownPrev() => CycleSlot(1, -1);
    [DataSourceMethod] public void ExecuteSlot2DropdownNext() => CycleSlot(2, +1);
    [DataSourceMethod] public void ExecuteSlot2DropdownPrev() => CycleSlot(2, -1);
    [DataSourceMethod] public void ExecuteSlot3DropdownNext() => CycleSlot(3, +1);
    [DataSourceMethod] public void ExecuteSlot3DropdownPrev() => CycleSlot(3, -1);
    [DataSourceMethod] public void ExecuteSlot4DropdownNext() => CycleSlot(4, +1);
    [DataSourceMethod] public void ExecuteSlot4DropdownPrev() => CycleSlot(4, -1);
    [DataSourceMethod] public void ExecuteSlot5DropdownNext() => CycleSlot(5, +1);
    [DataSourceMethod] public void ExecuteSlot5DropdownPrev() => CycleSlot(5, -1);
    [DataSourceMethod] public void ExecuteSlot6DropdownNext() => CycleSlot(6, +1);
    [DataSourceMethod] public void ExecuteSlot6DropdownPrev() => CycleSlot(6, -1);
    [DataSourceMethod] public void ExecuteSlot7DropdownNext() => CycleSlot(7, +1);
    [DataSourceMethod] public void ExecuteSlot7DropdownPrev() => CycleSlot(7, -1);
    [DataSourceMethod] public void ExecuteSlot8DropdownNext() => CycleSlot(8, +1);
    [DataSourceMethod] public void ExecuteSlot8DropdownPrev() => CycleSlot(8, -1);
    [DataSourceMethod] public void ExecuteSlot9DropdownNext() => CycleSlot(9, +1);
    [DataSourceMethod] public void ExecuteSlot9DropdownPrev() => CycleSlot(9, -1);

    [DataSourceMethod] public void ExecuteNextMod()
    {
        DiagLog.Log(Tag, $"ExecuteNextMod (cur={_currentModIndex})");
        SelectMod(_currentModIndex + 1);
    }
    [DataSourceMethod] public void ExecutePrevMod()
    {
        DiagLog.Log(Tag, $"ExecutePrevMod (cur={_currentModIndex})");
        SelectMod(_currentModIndex - 1);
    }

    /// <summary>
    /// v1.0: reset the active mod-list filter back to showing all mods. Bound
    /// to the Clear button next to the inline search field. The Q/E tab-switch
    /// suppression while typing is implemented separately by TabSwitchGuardPatch.
    /// </summary>
    [DataSourceMethod]
    public void ExecuteClearSearch()
    {
        try
        {
            DiagLog.Log(Tag, $"ExecuteClearSearch (was=\"{_modSearchText}\")");
            BetaDepsModSearchText = string.Empty;
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteClearSearch", ex);
        }
    }
    [DataSourceMethod] public void ExecuteNextPage()
    {
        DiagLog.Log(Tag, $"ExecuteNextPage (cur={_currentPageIndex})");
        _currentPageIndex++;
        RefreshSlots();
    }
    [DataSourceMethod] public void ExecutePrevPage()
    {
        DiagLog.Log(Tag, $"ExecutePrevPage (cur={_currentPageIndex})");
        _currentPageIndex--;
        RefreshSlots();
    }

    /// <summary>
    /// Reset every property of the currently-selected settings class to the
    /// value the consumer mod's class definition initializes it to. We
    /// construct a fresh instance of the same settings type (which executes
    /// the class's field initializers and parameterless ctor), then copy each
    /// [SettingPropertyX]-decorated property from the fresh instance into the
    /// live singleton. The result is persisted via the same save path used
    /// when the user clicks Done on the Options panel.
    /// </summary>

    // SaveCurrent was the v0.3.2-and-earlier per-keystroke persist. Removed in
    // v0.3.3: all in-memory edits are now committed to disk by SaveOnDonePatch's
    // Done postfix, and discarded by its Cancel postfix (which reloads each
    // settings instance from disk). This is the contract every consumer mod
    // expects from MCM's stock Options screen.

    [DataSourceMethod] public void ExecuteResetDefaults()
    {
        var current = _currentSettingsVM?.Settings;
        if (current == null)
        {
            DiagLog.Log(Tag, "ExecuteResetDefaults: no settings selected; ignored");
            return;
        }
        try
        {
            var t = current.GetType();
            object? fresh;
            try { fresh = System.Activator.CreateInstance(t); }
            catch (System.Exception ex)
            {
                DiagLog.LogCaught(Tag, $"ExecuteResetDefaults/CreateInstance({t.FullName})", ex);
                return;
            }
            if (fresh == null) return;

            int copied = 0;
            foreach (var p in t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (p.GetCustomAttributes(inherit: true).OfType<MCM.Abstractions.Attributes.SettingPropertyAttribute>().FirstOrDefault() == null) continue;
                if (!p.CanWrite) continue;
                try
                {
                    var defaultVal = p.GetValue(fresh);
                    p.SetValue(current, defaultVal);
                    copied++;
                }
                catch (System.Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"ExecuteResetDefaults/Set({t.Name}.{p.Name})", ex);
                }
            }

            // No inline Save — v0.3.3 buffers all edits until the user clicks
            // Done on the Options screen. Reset is now a *staged* operation:
            // Cancel will reload from disk and undo the reset; Done will
            // persist the defaults the same way it persists any other edit.

            DiagLog.Log(Tag, $"ExecuteResetDefaults: reset {copied} property(ies) on {current.Id} (in-memory; persisted on Done)");
            RefreshSlots();
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteResetDefaults", ex);
        }
    }

    // ---- Self-test button -----------------------------------------------
    //
    // Visible during BetaDeps development for one-click smoke-testing of
    // every consumer mod's settings. Hidden from non-dev builds by default;
    // the gate below currently always returns true so the button is always
    // shown. Flip to false (or wire to a build constant) before shipping
    // v1.0 if you want to hide it from end users.

    private bool _selfTestRunning;
    // v0.5.x: button text is dynamic so we can flip the label to "Running..."
    // while the harness is in flight. Field initialized to "Run Self-Test"
    // matches the prefab's @SelfTestButtonText binding default.
    private string _selfTestButtonText = "Run Self-Test";
    [DataSourceProperty] public string SelfTestButtonText => _selfTestButtonText;

    /// <summary>
    /// Run the McmSelfTest harness across every registered settings instance.
    /// Wired to the "Run Self-Test" button on the Mod Config tab. Concurrency-
    /// guarded with _selfTestRunning so a double-click only runs the test
    /// once. The full pass/fail report is written to runtime.log by McmSelfTest
    /// itself; this method just kicks it off and flips the button label.
    /// </summary>
    [DataSourceMethod]
    public void ExecuteRunSelfTest()
    {
        if (_selfTestRunning)
        {
            DiagLog.Log(Tag, "ExecuteRunSelfTest: already running; ignored");
            return;
        }
        // v0.5.9 (post-Nexus comment): show a confirmation dialog before
        // running. Self-Test mutates each property, saves to disk, and
        // restores from a timestamped backup. If the restore step is
        // interrupted (game crash, alt-tab kill, etc.) the user's live
        // settings could be left in a test-mutated state. The Nexus
        // commenter who reported "I cannot load the saved values of the
        // mode due to the AI Influence Settings PASS" was likely a victim
        // of this — they ran the test, settings got mutated, and they
        // didn't know about the backup folder. Confirming up-front + telling
        // them about the backup path in the result popup makes both the
        // risk and the recovery path explicit.
        try
        {
            var confirm = new TaleWorlds.Library.InquiryData(
                titleText: "Run BetaDeps Self-Test?",
                text: "The Self-Test mutates every registered mod's settings, writes them to disk, reloads them, and restores from a timestamped backup at the end. Your settings should be unchanged when it finishes.\n\nBefore running, your current JSON files are copied to:\nDocuments\\Mount and Blade II Bannerlord\\Configs\\ModSettings\\Global\\SelfTestBackup-<timestamp>\\\n\nIf anything looks wrong afterwards, restore from that folder.\n\nProceed?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Run Self-Test",
                negativeText: "Cancel",
                affirmativeAction: () => RunSelfTestConfirmed(),
                negativeAction: () => { });
            TaleWorlds.Library.InformationManager.ShowInquiry(confirm, pauseGameActiveState: true);
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteRunSelfTest/confirm", ex);
        }
    }

    /// <summary>
    /// "Send to GitHub" button. Opens the BetaDeps GitHub issues page in
    /// the user's default browser, with the selftest.log path reminder so
    /// they can attach it to the issue. Light-touch by design: GitHub
    /// new-issue URL has a length cap on pre-filled body content, and most
    /// users prefer to write their own description anyway. The path-to-file
    /// reminder is the value we add.
    /// </summary>
    [DataSourceMethod]
    public void ExecuteSendToGitHub()
    {
        // First log line lets us verify (via runtime.log) that the click
        // actually reached this method. If we click in-game and don't see
        // this line, the XML binding is wrong; if we see it but no browser
        // opens, Process.Start is the issue.
        DiagLog.Log(Tag, "ExecuteSendToGitHub: click received");
        try
        {
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rtPath) ?? "(unknown)";
            var selftest = System.IO.Path.Combine(dir, "selftest.log");
            var runtime  = System.IO.Path.Combine(dir, "runtime.log");

            var prompt = new TaleWorlds.Library.InquiryData(
                titleText: "Send a crash report to BetaDeps",
                text: "This will open the BetaDeps GitHub issues page in your browser. Before clicking \"New Issue\" there:\n\n" +
                      "1. Click \"Run Self-Test\" first if you haven't already this session, so the report is fresh.\n" +
                      $"2. Attach BOTH of these files to the issue (drag-and-drop works):\n   • {RedactUserPath(selftest)}\n   • {RedactUserPath(runtime)}\n" +
                      "3. Describe what you were doing when the crash happened.\n\n" +
                      "Open GitHub now?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Open GitHub",
                negativeText: "Cancel",
                affirmativeAction: () => OpenGitHubIssueUrl(),
                negativeAction: () => { });
            TaleWorlds.Library.InformationManager.ShowInquiry(prompt, pauseGameActiveState: true);
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteSendToGitHub", ex);
        }
    }

    private void OpenGitHubIssueUrl()
    {
        try
        {
            // Build a pre-populated issue URL: ?title=...&body=...
            // GitHub caps body length at roughly 8KB before the URL gets
            // rejected. Selftest.log alone can be 30KB+, so we don't try
            // to inline the full file. Instead the body contains the
            // useful summary the user needs to triage (BetaDeps version,
            // last-good count, auto-disabled mods, top of selftest report)
            // plus instructions for the user to drag-drop the full log
            // files as attachments.

            var titleText = $"BetaDeps Self-Test report  {System.DateTime.Now:yyyy-MM-dd}";
            var body = BuildGitHubIssueBody();

            // Trim body if it would exceed GitHub's URL cap. 7000 chars
            // leaves headroom for URL encoding (each char becomes 1-3
            // bytes after escape) plus the title and template params.
            const int maxBody = 7000;
            if (body.Length > maxBody)
            {
                body = body.Substring(0, maxBody) +
                       "\n\n[... truncated for URL length; attach full files for the rest ...]";
            }

            var url = "https://github.com/Trashpanda62/Betadeps/issues/new"
                    + "?title=" + System.Uri.EscapeDataString(titleText)
                    + "&body=" + System.Uri.EscapeDataString(body);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(psi);
            DiagLog.Log(Tag, $"ExecuteSendToGitHub: opened pre-filled GitHub URL ({url.Length} chars)");
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "OpenGitHubIssueUrl", ex);
        }
    }

    /// <summary>
    /// Constructs the issue body for the "Send to GitHub" pre-fill. Includes
    /// BetaDeps version, the Bannerlord version, the auto-disable diagnostics
    /// (which mods loaded clean, which got disabled and why), and a head-of-
    /// file snippet from selftest.log if one exists. Paths are redacted via
    /// RedactUserPath so users don't accidentally leak their Windows username
    /// when the issue page renders.
    /// </summary>
    private static string BuildGitHubIssueBody()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("### What happened");
        sb.AppendLine("(replace this line with a short description of what you were doing when the crash / issue occurred)");
        sb.AppendLine();
        sb.AppendLine("### Environment");
        try
        {
            sb.AppendLine($"- Branch:    {BetaDeps.Foundation.VersionProbe.Branch} (Bannerlord v{BetaDeps.Foundation.VersionProbe.Major}.{BetaDeps.Foundation.VersionProbe.Minor})");
            var asmName = typeof(MCMSubModule).Assembly.GetName();
            sb.AppendLine($"- BetaDeps:  v{asmName.Version}");
        }
        catch { }
        sb.AppendLine();
        sb.AppendLine("### Logs");
        sb.AppendLine("Please drag-drop these two files from your install into the GitHub issue (they auto-attach):");
        try
        {
            var rt = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            sb.AppendLine($"- runtime.log  (`{RedactUserPath(rt)}`)");
            if (!string.IsNullOrEmpty(dir))
                sb.AppendLine($"- selftest.log (`{RedactUserPath(System.IO.Path.Combine(dir, "selftest.log"))}`)");
        }
        catch { }
        sb.AppendLine();

        // Auto-disable diagnostics section: this is the most actionable
        // piece for someone debugging the user's crash.
        sb.AppendLine("### BetaDeps runtime detection state");
        try
        {
            var rt = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            if (!string.IsNullOrEmpty(dir))
            {
                var lastGoodPath = System.IO.Path.Combine(dir, "last-good-modlist.txt");
                if (System.IO.File.Exists(lastGoodPath))
                {
                    var lines = System.IO.File.ReadAllLines(lastGoodPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#")).ToList();
                    sb.AppendLine($"**Loaded cleanly last session ({lines.Count} mod(s))**: " +
                                  string.Join(", ", lines));
                }
                else
                {
                    sb.AppendLine("**Loaded cleanly last session**: no baseline file yet (first run or never reached main menu)");
                }
                sb.AppendLine();

                var disabledPath = System.IO.Path.Combine(dir, "betadeps-disabled-mods.log");
                if (System.IO.File.Exists(disabledPath))
                {
                    var lines = System.IO.File.ReadAllLines(disabledPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    int take = System.Math.Min(10, lines.Count);
                    sb.AppendLine($"**Auto-disable history (last {take} of {lines.Count})**:");
                    sb.AppendLine("```");
                    foreach (var l in lines.Skip(System.Math.Max(0, lines.Count - take)))
                        sb.AppendLine(l);
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine("**Auto-disable history**: empty (no mods have been auto-disabled)");
                }
            }
        }
        catch (System.Exception ex) { sb.AppendLine($"(error gathering diagnostics: {ex.Message})"); }
        sb.AppendLine();

        // Top of selftest.log so reviewer sees the headline pass/fail
        // numbers without having to download the attachment.
        sb.AppendLine("### Self-Test headline");
        try
        {
            var rt = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            var selftestPath = string.IsNullOrEmpty(dir) ? null : System.IO.Path.Combine(dir, "selftest.log");
            if (!string.IsNullOrEmpty(selftestPath) && System.IO.File.Exists(selftestPath))
            {
                var head = System.IO.File.ReadAllLines(selftestPath).Take(15);
                sb.AppendLine("```");
                foreach (var l in head) sb.AppendLine(l);
                sb.AppendLine("```");
            }
            else
            {
                sb.AppendLine("_No selftest.log on disk this session. Click \"Run Self-Test\" in Mod Config before submitting._");
            }
        }
        catch { }

        return sb.ToString();
    }

    /// <summary>
    /// Replace the per-user portion of a Windows path (C:\Users\&lt;name&gt;\OneDrive\Documents\...
    /// or similar) with a placeholder, so users who screenshot or paste this
    /// dialog into a public GitHub issue don't accidentally leak their
    /// Windows username / OneDrive folder structure. Everything after the
    /// well-known "Documents\Mount and Blade II Bannerlord\..." stays intact
    /// so the user can still find the folder.
    /// </summary>
    private static string RedactUserPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        try
        {
            // Common patterns: C:\Users\Foo\OneDrive\Documents\..., C:\Users\Foo\Documents\...
            // Replace everything up to and including the username segment.
            var lower = path.Replace('/', '\\').ToLowerInvariant();
            var anchor = "\\documents\\";
            var idx = lower.IndexOf(anchor, System.StringComparison.Ordinal);
            if (idx >= 0)
            {
                // Keep the "...\Documents\" tail intact, redact everything before it.
                return "<Documents>" + path.Substring(idx + anchor.Length - 1);
            }
        }
        catch { }
        return path;
    }

    private void RunSelfTestConfirmed()
    {
        if (_selfTestRunning) return;
        _selfTestRunning = true;
        _selfTestButtonText = "Running...";
        ViewModel.NotifyPropertyChanged(nameof(SelfTestButtonText));
        try
        {
            DiagLog.Log(Tag, "ExecuteRunSelfTest: starting McmSelfTest.RunAll()");
            var report = McmSelfTest.RunAll();
            int passed = 0, failed = 0, quirks = 0;
            var failedModNames = new System.Collections.Generic.List<string>();
            var quirkModNames = new System.Collections.Generic.List<string>();
            foreach (var m in report.Mods)
            {
                // Known consumer-mod quirks (e.g. DismembermentPlus's
                // DismembermentRealism setter intentionally clobbering
                // DismembermentChance as a "realism preset" feature) aren't
                // BetaDeps bugs and shouldn't count as failures.
                bool isQuirk = McmSelfTest.Report.KnownConsumerQuirks.ContainsKey(m.ModId ?? string.Empty);
                if (isQuirk)
                {
                    quirks++;
                    quirkModNames.Add(m.ModDisplayName ?? m.ModId ?? string.Empty);
                    continue;
                }
                if (m.FatalError != null)
                {
                    failed++;
                    failedModNames.Add($"{m.ModDisplayName ?? m.ModId} (FATAL: {m.FatalError})");
                    continue;
                }
                if (m.DonePassed && m.CancelPassed &&
                    m.Properties.TrueForAll(p => p.RoundTripPassed))
                {
                    passed++;
                }
                else
                {
                    failed++;
                    var reasons = new System.Collections.Generic.List<string>();
                    if (!m.DonePassed) reasons.Add("Done");
                    if (!m.CancelPassed) reasons.Add("Cancel");
                    int badProps = m.Properties.Count(p => !p.RoundTripPassed);
                    if (badProps > 0) reasons.Add($"{badProps} prop(s)");
                    failedModNames.Add($"{m.ModDisplayName ?? m.ModId} ({string.Join(", ", reasons)})");
                }
            }
            DiagLog.Log(Tag, $"ExecuteRunSelfTest: {passed} passed, {failed} failed, {quirks} quirk(s) skipped (of {report.Mods.Count} mods)");

            // v1.0: in-game popup so the user can see the result without
            // tabbing out to runtime.log.
            try
            {
                var headline = failed == 0
                    ? $"All {passed} mod(s) passed!"
                    : $"{passed} passed · {failed} failed";
                if (quirks > 0) headline += $" · {quirks} known quirk(s)";

                var sb = new System.Text.StringBuilder();
                if (failed == 0)
                {
                    sb.Append($"BetaDeps round-tripped every [SettingProperty*] declaration across {passed} mod(s) without a single mismatch. Done and Cancel semantics both confirmed.");
                }
                else
                {
                    var capped = failedModNames.GetRange(0, System.Math.Min(20, failedModNames.Count));
                    sb.Append("Failing mod(s):\n");
                    sb.Append(string.Join("\n", capped));
                    if (failedModNames.Count > 20)
                        sb.Append($"\n…and {failedModNames.Count - 20} more (see runtime.log)");
                }
                if (quirks > 0)
                {
                    sb.Append("\n\nKnown consumer-mod quirks (not BetaDeps bugs):\n");
                    sb.Append(string.Join("\n", quirkModNames));
                }

                // v0.5.9 (post-Nexus comment): tell the user where their
                // pre-test JSON backup lives. v0.6: redact the per-user
                // portion of the path so users who screenshot or paste this
                // dialog (e.g. into a public GitHub issue) don't leak their
                // Windows username / OneDrive folder structure. The user
                // can still find the folder because everything from
                // "Documents\Mount and Blade II Bannerlord\..." is the
                // stable, well-known part of the path.
                if (!string.IsNullOrEmpty(report.BackupDir))
                {
                    sb.Append("\n\nPre-test settings backed up to:\n");
                    sb.Append(RedactUserPath(report.BackupDir));
                    sb.Append("\n(Copy those .json files back over the live ones if any setting looks wrong.)");
                }
                string body = sb.ToString();

                var inquiry = new TaleWorlds.Library.InquiryData(
                    titleText: $"BetaDeps Self-Test — {headline}",
                    text: body,
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: false,
                    affirmativeText: "OK",
                    negativeText: string.Empty,
                    affirmativeAction: () => { },
                    negativeAction: () => { });
                TaleWorlds.Library.InformationManager.ShowInquiry(inquiry, pauseGameActiveState: true);
            }
            catch (System.Exception inqEx)
            {
                DiagLog.LogCaught(Tag, "ExecuteRunSelfTest/inquiry", inqEx);
            }
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteRunSelfTest", ex);
        }
        finally
        {
            _selfTestRunning = false;
            _selfTestButtonText = "Run Self-Test";
            ViewModel.NotifyPropertyChanged(nameof(SelfTestButtonText));
        }
    }
}
