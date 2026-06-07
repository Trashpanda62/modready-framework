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

    // Multiple settings registered by the SAME mod (assembly) are consolidated to
    // one sidebar entry; the non-primary ones are stashed here keyed by the primary
    // and merged into its presentation on select (e.g. AIInfluence's "BUG-FIX-0").
    private readonly System.Collections.Generic.Dictionary<RegisteredSettings, System.Collections.Generic.List<RegisteredSettings>> _extraSettings = new();
    // Settings whose DisplayName got the "<Assembly> — " prefix (their own name did
    // NOT already reference the mod) -- used to pick the primary per assembly.
    private readonly System.Collections.Generic.HashSet<RegisteredSettings> _prefixedSettings = new();

    // ----- Preset cycle-row state (v0.8.2 Suberfudge feature) -----
    // _presetCycle is a synthetic walk over: index 0 = "(Current settings)" sentinel,
    // 1..N = saved preset names for the current mod, last = "(Save current as new...)"
    // sentinel. ExecutePresetCycleNext/Prev rotate _presetCycleIndex; ExecutePresetApply
    // dispatches based on the kind. Rebuilt on every SelectMod.
    private string[] _presetCycle = new[] { "(Current settings)" };
    private int _presetCycleIndex;
    private const string PRESET_SENTINEL_CURRENT = "(Current settings)";
    private const string PRESET_SENTINEL_SAVE    = "(Save current as new...)";

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

    // v0.8.2 slice 4b: dynamic row list for the infinite-scroll ItemTemplate.
    // Parallel to the legacy fixed-slot fan (_slots[0..19]); the new prefab in
    // slice 4c binds to @RowList instead of @Slot0..@Slot19. Built in SelectMod
    // from the same _presentation source the slot fan uses, so both stay in
    // sync until slice 4d deletes the slot fan.
    private MBBindingList<PresentationRowVM> _rowList = new();
    [DataSourceProperty]
    public MBBindingList<PresentationRowVM> RowList => _rowList;

    // Phase 2.1: left "Mods" sidebar. One ModRowVM per entry in the current
    // filtered mod list; IsSelected marks the active mod. Rebuilt by
    // RebuildModListRows() on filter changes; SelectMod() flips the IsSelected
    // flags. Replaces the Prev/Next mod cycler. Bound via {ModList} + an
    // ItemTemplate, the same mixin-MBBindingList path RowList uses.
    private MBBindingList<ModRowVM> _modList = new();
    [DataSourceProperty]
    public MBBindingList<ModRowVM> ModList => _modList;

    private string _selectedModName = "(no mod)";
    private string _selectedModSummary = string.Empty;
    private string _selectedPageSummary = string.Empty;

    [DataSourceProperty] public bool   BetaDepsModConfigTabVisible           { get => _modConfigVisible; set { _modConfigVisible = value; } }

    /// <summary>
    /// Title shown at the top of the Mod Config page. Polish (post-v0.8):
    /// when one or more mods have registered settings, the title appends
    /// "  ·  N mods" so users can see at a glance how many mods registered
    /// without having to cycle through the Prev/Next buttons. Empty modlist
    /// still shows just "Mod Configuration" — the empty-state hint below
    /// already explains there's nothing to see.
    ///
    /// RebuildModList notifies this property after _registered changes so
    /// the count refreshes when mods register late (e.g. fluent-builder
    /// settings created in a consumer mod's OnSubModuleLoad).
    /// </summary>
    [DataSourceProperty]
    public string BetaDepsModConfigTitle
    {
        get
        {
            var count = _registered?.Length ?? 0;
            if (count <= 0) return _modConfigTitle;
            return $"{_modConfigTitle}  ·  {count} mod{(count == 1 ? "" : "s")}";
        }
        set { _modConfigTitle = value; }
    }
    [DataSourceProperty] public string BetaDepsModConfigRegisteredModsList   { get => _registeredModsList; set { _registeredModsList = value; } }
    [DataSourceProperty] public string BetaDepsModConfigSummary              { get => _summaryText; set { _summaryText = value; } }

    // v0.7.6 visual polish:
    //   * empty-state visibility: when no mods registered, show a hint message
    //     instead of an empty settings panel.
    //   * toggle-button labels: instead of "Toggle X", show "X: ON" or "X: OFF"
    //     so users can read current state without clicking. Backed by flag files.
    [DataSourceProperty] public bool BetaDepsModConfigHasMods   => _registered != null && _registered.Length > 0;
    [DataSourceProperty] public bool BetaDepsModConfigIsEmpty   => !BetaDepsModConfigHasMods;

    private static string? ResolveBetaDepsDir()
    {
        try
        {
            var ownPath = typeof(OptionsVMMixin).Assembly.Location;
            if (string.IsNullOrEmpty(ownPath)) return null;
            var binDir = System.IO.Path.GetDirectoryName(ownPath);
            var betaDepsDir = System.IO.Path.GetDirectoryName(binDir);   // BetaDeps\bin\.. -> BetaDeps\
            return betaDepsDir;
        }
        catch { return null; }
    }




    /// <summary>
    /// Re-notify every toggle-button label + empty-state binding so the
    /// in-game text refreshes immediately after the user clicks one of
    /// the toggle buttons (or after RebuildModList changes _registered).
    /// </summary>
    private void NotifyVisualState()
    {
        try
        {
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsModConfigHasMods));
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsModConfigIsEmpty));
            // Polish (post-v0.8): the title contains the registered-mod count
            // dynamically. Refresh after RebuildModList changes _registered so
            // late-registering fluent settings update the count in real time.
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsModConfigTitle));
            // RebuildModList clears _summaryText (the old "N mod(s) registered"
            // line was redundant with the title's count). Notify so the
            // RichTextWidget bound to BetaDepsModConfigSummary collapses
            // in-place after the polish change.
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsModConfigSummary));
        }
        catch { }
    }

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
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsSearchPlaceholderVisible));
        }
    }

    /// <summary>
    /// v1.0: visibility of the Clear button next to the search field. Hidden
    /// when no filter is active so the row stays uncluttered.
    /// </summary>
    [DataSourceProperty]
    public bool BetaDepsSearchClearVisible => !string.IsNullOrEmpty(_modSearchText);

    /// <summary>
    /// Polish (post-v0.8): inverse of SearchClearVisible. The prefab uses this
    /// to render a "Search mods…" placeholder text overlay on top of the
    /// EditableTextWidget when the search field is empty. Bannerlord's
    /// EditableTextWidget has no native placeholder attribute, so the
    /// prefab overlays a RichTextWidget with DoNotAcceptEvents=true and
    /// IsVisible bound here; the overlay disappears as soon as the user
    /// starts typing.
    /// </summary>
    [DataSourceProperty]
    public bool BetaDepsSearchPlaceholderVisible => string.IsNullOrEmpty(_modSearchText);

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
    // v0.7.6 click-to-edit shared helper.
    // EditableTextWidget next to each slider binds two-way to
    // Slot{n}_EditableValueText. The setter calls this method, which:
    //   1. Trims + normalises the typed string
    //   2. Parses as int (for IsInteger slots) or float (for IsFloating slots)
    //   3. Clamps to the slot's declared [MinValue, MaxValue] range
    //   4. Writes IntValue or FloatValue back through the slot
    //   5. Notifies FloatValue + ValueText (but NOT EditableValueText) so the
    //      slider bar moves but the user's typed text isn't clobbered mid-edit
    //
    // Returns true if the typed input changed the underlying value.
    private bool SetSlotFromEditableText(int slotIndex, string? typed)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
        var p = _slots[slotIndex];
        if (p == null) return false;
        if (string.IsNullOrWhiteSpace(typed)) return false;
        var s = typed!.Trim();

        try
        {
            if (p.IsInteger)
            {
                int iv;
                if (!int.TryParse(s, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out iv))
                {
                    // Accept "3.5" on an integer slot too -- round to nearest.
                    if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var fv))
                        iv = (int)System.Math.Round(fv);
                    else
                        return false;
                }
                var minI = (int)p.MinValue;
                var maxI = (int)p.MaxValue;
                if (iv < minI) iv = minI;
                if (iv > maxI) iv = maxI;
                if (p.IntValue != iv)
                {
                    p.IntValue = iv;
                    ViewModel.NotifyPropertyChanged($"Slot{slotIndex}_FloatValue");
                    ViewModel.NotifyPropertyChanged($"Slot{slotIndex}_IntValue");
                    ViewModel.NotifyPropertyChanged($"Slot{slotIndex}_ValueText");
                    return true;
                }
            }
            else if (p.IsFloating)
            {
                if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var fv))
                    return false;
                var minF = (float)p.MinValue;
                var maxF = SafeMaxFloat(p);
                if (fv < minF) fv = minF;
                if (fv > maxF) fv = maxF;
                if (System.Math.Abs(p.FloatValue - fv) > 0.0001f)
                {
                    p.FloatValue = fv;
                    ViewModel.NotifyPropertyChanged($"Slot{slotIndex}_FloatValue");
                    ViewModel.NotifyPropertyChanged($"Slot{slotIndex}_ValueText");
                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            try { DiagLog.LogCaught(Tag, $"SetSlotFromEditableText(slot={slotIndex})", ex); } catch { }
        }
        return false;
    }

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
        set { if (_slots[0] != null) { _slots[0]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot0_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot0_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot0_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot0_EditableValueText));
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
        set { if (_slots[1] != null) { _slots[1]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot1_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot1_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot1_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot1_EditableValueText));
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
        set { if (_slots[2] != null) { _slots[2]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot2_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot2_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot2_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot2_EditableValueText));
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
        set { if (_slots[3] != null) { _slots[3]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot3_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot3_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot3_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot3_EditableValueText));
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
        set { if (_slots[4] != null) { _slots[4]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot4_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot4_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot4_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot4_EditableValueText));
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
        set { if (_slots[5] != null) { _slots[5]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot5_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot5_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot5_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot5_EditableValueText));
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
        set { if (_slots[6] != null) { _slots[6]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot6_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot6_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot6_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot6_EditableValueText));
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
        set { if (_slots[7] != null) { _slots[7]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot7_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot7_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot7_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot7_EditableValueText));
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
        set { if (_slots[8] != null) { _slots[8]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot8_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot8_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot8_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot8_EditableValueText));
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
        set { if (_slots[9] != null) { _slots[9]!.IntValue = (int)value; ViewModel.NotifyPropertyChanged(nameof(Slot9_IntValue)); ViewModel.NotifyPropertyChanged(nameof(Slot9_ValueText)); ViewModel.NotifyPropertyChanged(nameof(Slot9_EditableValueText));} }
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
            ViewModel.NotifyPropertyChanged(nameof(Slot9_EditableValueText));
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

    // ---- v0.7.6 click-to-edit: EditableTextWidget bindings for slots 0-9 ----
    //
    // Each EditableValueText getter formats the current IntValue/FloatValue
    // the same way the read-only ValueText getter does. The setter dispatches
    // to SetSlotFromEditableText() which parses + clamps + writes back. We do
    // NOT NotifyPropertyChanged on EditableValueText from the setter -- the
    // user's typed text shouldn't get clobbered mid-edit. The slider's own
    // setter notifies EditableValueText so dragging updates the input field.
    //
    // Slots 10-19 have matching properties in OptionsVMMixin.SlotProperties.g.cs.
    // Slots 20+ don't have sliders (BuildUnifiedSliderBlock is gated to n<20),
    // so they keep the existing read-only RichTextWidget value display.

    private string FormatSlotValueText(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _slots.Length) return string.Empty;
        var p = _slots[slotIndex];
        if (p == null) return string.Empty;
        try
        {
            var fmt = p.ValueFormat;
            if (string.IsNullOrEmpty(fmt) || fmt.StartsWith("{=")) fmt = p.IsInteger ? "0" : "0.##";
            if (p.IsInteger)  return p.IntValue.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            if (p.IsFloating) return p.FloatValue.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { }
        return string.Empty;
    }

    [DataSourceProperty] public string Slot0_EditableValueText { get => FormatSlotValueText(0); set { SetSlotFromEditableText(0, value); } }
    [DataSourceProperty] public string Slot1_EditableValueText { get => FormatSlotValueText(1); set { SetSlotFromEditableText(1, value); } }
    [DataSourceProperty] public string Slot2_EditableValueText { get => FormatSlotValueText(2); set { SetSlotFromEditableText(2, value); } }
    [DataSourceProperty] public string Slot3_EditableValueText { get => FormatSlotValueText(3); set { SetSlotFromEditableText(3, value); } }
    [DataSourceProperty] public string Slot4_EditableValueText { get => FormatSlotValueText(4); set { SetSlotFromEditableText(4, value); } }
    [DataSourceProperty] public string Slot5_EditableValueText { get => FormatSlotValueText(5); set { SetSlotFromEditableText(5, value); } }
    [DataSourceProperty] public string Slot6_EditableValueText { get => FormatSlotValueText(6); set { SetSlotFromEditableText(6, value); } }
    [DataSourceProperty] public string Slot7_EditableValueText { get => FormatSlotValueText(7); set { SetSlotFromEditableText(7, value); } }
    [DataSourceProperty] public string Slot8_EditableValueText { get => FormatSlotValueText(8); set { SetSlotFromEditableText(8, value); } }
    [DataSourceProperty] public string Slot9_EditableValueText { get => FormatSlotValueText(9); set { SetSlotFromEditableText(9, value); } }

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

    // True if the settings object is per-save/per-campaign (its type or any base
    // type is named *PerSaveSettings*). Name-based so it needs no extra refs.
    private static bool IsPerSaveSettings(object? s)
    {
        for (var t = s?.GetType(); t != null && t != typeof(object); t = t.BaseType)
            if (t.Name.IndexOf("PerSaveSettings", System.StringComparison.Ordinal) >= 0) return true;
        return false;
    }

    // True if a campaign is currently loaded (TaleWorlds.CampaignSystem.Campaign.Current
    // != null), via reflection so the MCM project needs no CampaignSystem reference.
    private static bool IsCampaignLoaded()
    {
        try
        {
            var t = BetaDeps.Foundation.ReflectionUtils.ResolveTypeByFullName("TaleWorlds.CampaignSystem.Campaign");
            var cur = t?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            return cur != null;
        }
        catch { return false; }
    }

    private void RebuildModList()
    {
        try
        {
            // Per-save (per-campaign) settings can only be edited inside a loaded
            // save, so at the main menu they appear as a redundant second entry next
            // to the mod's global settings (e.g. "Detailed Character Creation" +
            // "... (Per-Save)"). Hide them unless a campaign is loaded -- matches
            // upstream MCM and leaves one entry per mod on the main-menu screen.
            bool inCampaign = IsCampaignLoaded();
            _registered = SettingsRegistry.All
                .Where(r => inCampaign || !IsPerSaveSettings(r.Instance))
                .OrderBy(r => r.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // v0.8.2 slice 1: fire the presentation-row survey here (gated on
            // the Modules\BetaDeps\presentation-survey.flag file). RebuildModList
            // runs every time the user opens the Mod Configuration tab, which
            // is a much more usable trigger than OnBeforeInitialModuleScreenSetAsRoot
            // -- the user can drop the flag any time, then open Mod Config to
            // trigger the survey. RunIfRequested is _ran-guarded internally so
            // it only writes the output file once per session.
            try { MCM.Internal.PresentationRowSurvey.RunIfRequested(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, "PresentationRowSurvey.RunIfRequested", ex); }

            // v0.5.6 polish: annotate cryptic DisplayName strings with their
            // source folder. AIInfluence ships a secondary settings class with
            // DisplayName "BUG-FIX-0" -- without context, users can't tell what
            // mod that's from. If the DisplayName doesn't already contain the
            // source assembly name (case-insensitive, ignoring spaces), prefix
            // "<AssemblyName> — <DisplayName>" so users see "AIInfluence —
            // BUG-FIX-0". Skipped when the DisplayName is empty (no useful
            // string to prefix) or when the assembly name is generic / matches
            // already.
            _prefixedSettings.Clear();
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
                    _prefixedSettings.Add(r);
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

            // Consolidate multiple settings registered by the SAME mod (assembly)
            // under ONE sidebar entry. e.g. AIInfluence registers "AIInfluence
            // Settings" + a secondary "BUG-FIX-0"; both are AIInfluence and should
            // be one entry. Keep one primary per assembly; stash the rest in
            // _extraSettings to merge into the primary's presentation on select.
            _extraSettings.Clear();
            try
            {
                var primaries = new List<RegisteredSettings>();
                foreach (var grp in _registered.GroupBy(r => r.SourceAssemblyName ?? string.Empty, System.StringComparer.OrdinalIgnoreCase))
                {
                    var list = grp.ToList();
                    if (string.IsNullOrEmpty(grp.Key) || list.Count == 1) { primaries.AddRange(list); continue; }
                    // Primary = the settings whose own name already referenced the mod
                    // (so it was NOT assembly-prefixed); fall back to the first.
                    var primary = list.FirstOrDefault(r => !_prefixedSettings.Contains(r)) ?? list[0];
                    primaries.Add(primary);
                    var extras = list.Where(r => !ReferenceEquals(r, primary)).ToList();
                    if (extras.Count > 0) _extraSettings[primary] = extras;
                }
                _registered = primaries
                    .OrderBy(r => r.DisplayName, System.StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (System.Exception cex) { DiagLog.LogCaught(Tag, "RebuildModList/consolidate", cex); }

            // Polish (post-v0.8): the subtitle was "N mod(s) registered. Use
            // the search field or Prev/Next buttons to cycle." That duplicated
            // the mod-count badge now in the title AND repeated guidance that
            // the empty-state hint + footer hint already cover. Setting to
            // empty collapses the RichTextWidget (CoverChildren height) so
            // the title sits directly above the search/cycler row.
            _summaryText = string.Empty;
            try { NotifyVisualState(); } catch { }
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
            // Rebuild the sidebar rows for the (possibly) new filtered set.
            RebuildModListRows();
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ApplyFilter", ex);
        }
    }

    /// <summary>Phase 2.1: rebuild the left-sidebar ModList from the current
    /// filtered mod set. Each row selects its filtered index on click; the row
    /// at _currentModIndex is marked selected.</summary>
    private void RebuildModListRows()
    {
        try
        {
            _modList.Clear();
            var filtered = _filteredRegistered ?? System.Array.Empty<RegisteredSettings>();
            for (int i = 0; i < filtered.Length; i++)
            {
                var name = MCM.Internal.TextHelper.StripLocalizationKeys(filtered[i].DisplayName ?? string.Empty);
                _modList.Add(new ModRowVM(i, name, i == _currentModIndex, OnModRowSelected));
            }
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "RebuildModListRows", ex); }
    }

    /// <summary>Flip the IsSelected flag on each sidebar row without rebuilding,
    /// so the highlight follows the active mod and the list/scroll stay stable.</summary>
    private void UpdateModListSelection()
    {
        try
        {
            for (int i = 0; i < _modList.Count; i++)
                _modList[i].IsSelected = (i == _currentModIndex);
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "UpdateModListSelection", ex); }
    }

    /// <summary>Click callback from a ModRowVM in the sidebar.</summary>
    private void OnModRowSelected(int filteredIndex) => SelectMod(filteredIndex);

    // Phase 2.3: collapsed group state, keyed "<modId><group>" so the same
    // group name in two mods collapses independently. Persists for the life of
    // the Options screen (the mixin instance).
    // Phase 2.3 / refinement: track EXPANDED groups. Default (not in set) =
    // COLLAPSED, so a mod opens showing just its group headers; the user expands
    // the ones they want. Per-mod independent; persists for the screen's life.
    private readonly System.Collections.Generic.HashSet<string> _expandedGroups =
        new(System.StringComparer.Ordinal);
    private string _currentModId = string.Empty;

    private static string GroupKey(string modId, string group) => (modId ?? string.Empty) + "" + (group ?? string.Empty);

    private static void SplitGroup(string full, out string parent, out string leaf)
    {
        var s = full ?? string.Empty;
        int i = s.IndexOf('/');
        if (i < 0) { parent = string.Empty; leaf = s; }
        else { parent = s.Substring(0, i).Trim(); leaf = s.Substring(i + 1).Trim(); }
    }

    /// <summary>Build _rowList from _presentation. "Parent/Child" groups are
    /// bucketed under a single parent header with their children indented; the
    /// rest stay top-level. Collapsed groups skip their property rows.</summary>
    private void RebuildRowList()
    {
        _rowList.Clear();

        // 1. Parse the flat presentation into ordered (group -> props) buckets.
        var groups = new List<(string full, List<SettingsPropertyVM> props)>();
        foreach (var (header, prop) in _presentation)
        {
            if (prop == null) groups.Add((header, new List<SettingsPropertyVM>()));
            else if (groups.Count > 0) groups[groups.Count - 1].props.Add(prop);
        }

        // 2. Identify which parent prefixes have nested "X/Y" children. A plain
        // group whose name equals one of these prefixes (e.g. a "Disease System"
        // group that also has "Disease System/Seasonal" sub-groups) is folded into
        // that single parent rather than rendered as a second same-named header.
        var parentsWithChildren = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var g in groups)
        {
            SplitGroup(g.full, out var pp, out _);
            if (!string.IsNullOrEmpty(pp)) parentsWithChildren.Add(pp);
        }

        // 3. Emit rows, grouping "Parent/Child" entries under one parent header.
        var emittedParents = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            SplitGroup(groups[gi].full, out var parent, out var leaf);

            if (string.IsNullOrEmpty(parent))
            {
                // A plain group that is also the prefix of nested sub-groups is the
                // merged parent's OWN settings; skip it here -- it's emitted under
                // the unified parent header below.
                if (parentsWithChildren.Contains(groups[gi].full)) continue;

                // top-level group (no "/") -- default collapsed, indent 0
                var key = GroupKey(_currentModId, groups[gi].full);
                bool expanded = _expandedGroups.Contains(key);
                var ck = key;
                _rowList.Add(new PresentationRowVM(groups[gi].full, expanded, () => ToggleGroupCollapse(ck), 0));
                if (expanded) foreach (var p in groups[gi].props) _rowList.Add(new PresentationRowVM(p, 0));
                continue;
            }

            if (emittedParents.Contains(parent)) continue; // children already emitted with the parent
            emittedParents.Add(parent);

            // parent header -- default COLLAPSED (same as top-level), indent 0, so
            // opening a mod shows only the top-level headers; expanding a parent
            // reveals its child sub-groups (themselves collapsed).
            var pkey = GroupKey(_currentModId, "§P§" + parent);
            bool parentExpanded = _expandedGroups.Contains(pkey);
            var cpkey = pkey;
            _rowList.Add(new PresentationRowVM(parent, parentExpanded, () => ToggleGroupCollapse(cpkey), 0));
            if (!parentExpanded) continue;

            // 3a. The merged parent's OWN settings: a plain group whose name equals
            // the parent prefix. Its properties sit directly under the parent header
            // (indent 1), ahead of the sub-section headers.
            foreach (var g in groups)
            {
                if (!string.Equals(g.full, parent, System.StringComparison.Ordinal)) continue;
                foreach (var p in g.props) _rowList.Add(new PresentationRowVM(p, 1));
                break;
            }

            // 3b. Child sub-groups "parent/leaf", in original order, indented one level.
            for (int gj = gi; gj < groups.Count; gj++)
            {
                SplitGroup(groups[gj].full, out var p2, out var l2);
                if (string.IsNullOrEmpty(p2) || !string.Equals(p2, parent, System.StringComparison.Ordinal)) continue;
                var key = GroupKey(_currentModId, groups[gj].full);
                bool childExpanded = _expandedGroups.Contains(key);
                var ck = key;
                _rowList.Add(new PresentationRowVM(l2, childExpanded, () => ToggleGroupCollapse(ck), 1));
                if (childExpanded) foreach (var p in groups[gj].props) _rowList.Add(new PresentationRowVM(p, 1));
            }
        }
    }

    /// <summary>Flip a (child/top-level) group's collapsed state and rebuild.</summary>
    private void ToggleGroupCollapse(string key)
    {
        try
        {
            if (!_expandedGroups.Remove(key)) _expandedGroups.Add(key);
            RebuildRowList();
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "ToggleGroupCollapse", ex); }
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
            UpdateModListSelection(); // keep the sidebar highlight in sync

            var entry = _filteredRegistered[_currentModIndex];
            try { _currentSettingsVM = new SettingsVM(entry.Instance); }
            catch (System.Exception ex)
            {
                DiagLog.LogCaught(Tag, $"SettingsVM ctor for {entry.Id}", ex);
                _currentSettingsVM = null;
            }
            // v0.8.2: refresh the preset cycle row for whichever mod is now selected.
            try { RebuildPresetCycle(entry.Id); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"RebuildPresetCycle/{entry.Id}", ex); }

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

            // Merge any same-mod extra settings (e.g. AIInfluence's "BUG-FIX-0")
            // into this mod's presentation so they sit under the single entry.
            if (_extraSettings.TryGetValue(entry, out var extraList))
            {
                foreach (var ex in extraList)
                {
                    SettingsVM exVM;
                    try { exVM = new SettingsVM(ex.Instance); }
                    catch (System.Exception vex) { DiagLog.LogCaught(Tag, $"SelectMod/extraVM {ex.Id}", vex); continue; }
                    string lastG = null!;
                    foreach (var g in exVM.SettingPropertyGroups)
                    {
                        var gn = MCM.Internal.TextHelper.StripLocalizationKeys(g.GroupName ?? string.Empty);
                        if (!string.IsNullOrEmpty(gn) && !string.Equals(gn, lastG, System.StringComparison.Ordinal))
                        { _presentation.Add((gn, null)); lastG = gn; }
                        foreach (var p in g.SettingProperties) { _currentFlatProps.Add(p); _presentation.Add((string.Empty, p)); }
                    }
                }
            }

            // Build _rowList from _presentation, honoring collapsed groups
            // (Phase 2.3). Factored out so a group-collapse toggle can rebuild
            // without re-running the whole SelectMod.
            _currentModId = entry.Id;
            try { RebuildRowList(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"SelectMod/rowList {entry.Id}", ex); }

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

    // v0.8 UI cleanup: ExecuteRunSelfTest, RunSelfTestConfirmed, and the
    // @SelfTestButtonText binding were removed when Self-Test got folded
    // into Report-a-Bug. _selfTestRunning stays — it's still used by
    // RunSelfTestQuiet as a re-entrancy gate.
    private bool _selfTestRunning;

    /// <summary>
    /// Substitute the current user's profile prefix with %USERPROFILE% in
    /// a path string so logs and inquiry popups don't leak the Windows
    /// username when users copy/paste them into public GitHub issues or
    /// Nexus comments. Null/empty input returns the input unchanged.
    /// Case-insensitive prefix match.
    /// </summary>
    private static string RedactUserPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
        try
        {
            var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile) &&
                path!.StartsWith(userProfile, System.StringComparison.OrdinalIgnoreCase))
            {
                return "%USERPROFILE%" + path.Substring(userProfile.Length);
            }
        }
        catch { /* fall through to unchanged */ }
        return path!;
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
            var prompt = new TaleWorlds.Library.InquiryData(
                titleText: "Report a Bug",
                text: "BetaDeps will run a quick Self-Test (~5 seconds, restores your " +
                      "settings when done) and then open a GitHub issue draft in your " +
                      "browser with the results pre-filled.\n\n" +
                      "After the browser opens, write a short description of what you " +
                      "were doing when the issue happened, then click Submit on the " +
                      "GitHub page.\n\n" +
                      "Continue?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Run + Open GitHub",
                negativeText: "Cancel",
                affirmativeAction: () =>
                {
                    // v0.8 UI cleanup: Report-a-Bug auto-runs Self-Test inline so
                    // the GitHub issue body contains fresh diagnostics. Quiet mode
                    // (no result popup) — the user already confirmed once; a second
                    // dialog between "Run + Open GitHub" and the browser opening
                    // would be a worse UX than one continuous flow.
                    try { RunSelfTestQuiet(); }
                    catch (System.Exception ex) { DiagLog.LogCaught(Tag, "ExecuteSendToGitHub/selftest", ex); }
                    OpenGitHubIssueUrl();
                },
                negativeAction: () => { });
            TaleWorlds.Library.InformationManager.ShowInquiry(prompt, pauseGameActiveState: true);
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteSendToGitHub", ex);
        }
    }

    // ---------- Preset cycle row (v0.8.2 Suberfudge feature) ----------

    /// <summary>Display string for the currently-cycled preset entry.</summary>
    [DataSourceProperty]
    public string PresetCycleText
    {
        get
        {
            if (_presetCycle == null || _presetCycle.Length == 0) return PRESET_SENTINEL_CURRENT;
            if (_presetCycleIndex < 0 || _presetCycleIndex >= _presetCycle.Length) return PRESET_SENTINEL_CURRENT;
            return _presetCycle[_presetCycleIndex];
        }
    }

    /// <summary>True when the per-mod panel has at least one saved preset
    /// (i.e. cycle has more than the two sentinels). Used by the XML to
    /// hide the cycle row entirely when there's nothing meaningful to show.</summary>
    [DataSourceProperty]
    public bool PresetCycleVisible => _presetCycle != null && _presetCycle.Length > 0;

    // Phase 2.4: preset "Custom" dropdown. PresetOptions is the popup list,
    // PresetButtonText is the closed-button label, PresetDropdownOpen toggles the
    // popup. Built alongside _presetCycle in RebuildPresetCycle.
    private MBBindingList<PresetOptionVM> _presetOptions = new();
    [DataSourceProperty] public MBBindingList<PresetOptionVM> PresetOptions => _presetOptions;

    private string _presetButtonText = PRESET_SENTINEL_CURRENT;
    [DataSourceProperty] public string PresetButtonText => _presetButtonText;

    private bool _presetDropdownOpen;
    [DataSourceProperty]
    public bool PresetDropdownOpen
    {
        get => _presetDropdownOpen;
        set { if (_presetDropdownOpen == value) return; _presetDropdownOpen = value; ViewModel.NotifyPropertyChanged(nameof(PresetDropdownOpen)); }
    }

    [DataSourceMethod]
    public void ExecuteTogglePresetDropdown() => PresetDropdownOpen = !PresetDropdownOpen;

    /// <summary>Apply a chosen preset option (or sentinel), update the button
    /// label, and close the popup. Mirrors ExecutePresetApply's switch.</summary>
    private void OnPresetOptionSelected(string pick)
    {
        try
        {
            PresetDropdownOpen = false;
            if (_filteredRegistered == null || _filteredRegistered.Length == 0) return;
            var entry = _filteredRegistered[_currentModIndex];
            var settingsId = entry?.Id ?? string.Empty;
            if (string.IsNullOrEmpty(settingsId)) return;

            if (pick == PRESET_SENTINEL_CURRENT)
            {
                _presetButtonText = PRESET_SENTINEL_CURRENT;
                ViewModel.NotifyPropertyChanged(nameof(PresetButtonText));
                return;
            }
            if (pick == PRESET_SENTINEL_SAVE)
            {
                PromptSavePresetName(entry!, settingsId);
                return;
            }
            ApplyPresetLoad(entry!, settingsId, pick);
            _presetButtonText = pick;
            ViewModel.NotifyPropertyChanged(nameof(PresetButtonText));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"OnPresetOptionSelected({pick})", ex); }
    }

    [DataSourceMethod] public void ExecutePresetCycleNext() => CyclePreset(+1);
    [DataSourceMethod] public void ExecutePresetCyclePrev() => CyclePreset(-1);

    private void CyclePreset(int delta)
    {
        try
        {
            var n = _presetCycle?.Length ?? 0;
            if (n <= 0) return;
            _presetCycleIndex = ((_presetCycleIndex + delta) % n + n) % n;
            ViewModel.NotifyPropertyChanged(nameof(PresetCycleText));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"CyclePreset({delta})", ex); }
    }

    /// <summary>
    /// Apply the currently-cycled preset entry:
    ///   - "(Current settings)" sentinel -> no-op, just shows confirmation
    ///   - "(Save current as new...)" sentinel -> prompt for name, save
    ///   - any other entry -> load that preset and refresh the panel
    /// </summary>
    [DataSourceMethod]
    public void ExecutePresetApply()
    {
        try
        {
            if (_filteredRegistered == null || _filteredRegistered.Length == 0) return;
            var entry = _filteredRegistered[_currentModIndex];
            var settingsId = entry?.Id ?? string.Empty;
            if (string.IsNullOrEmpty(settingsId)) return;

            var pick = PresetCycleText;
            DiagLog.Log(Tag, $"ExecutePresetApply: id={settingsId} pick='{pick}'");

            if (pick == PRESET_SENTINEL_CURRENT)
            {
                ShowInfo("No change", "'(Current settings)' is already applied — nothing to do.");
                return;
            }
            if (pick == PRESET_SENTINEL_SAVE)
            {
                PromptSavePresetName(entry!, settingsId);
                return;
            }
            ApplyPresetLoad(entry!, settingsId, pick);
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "ExecutePresetApply", ex); }
    }

    /// <summary>
    /// Rebuild _presetCycle for the currently-selected mod. Called from
    /// SelectMod after _filteredRegistered[_currentModIndex] is set, and
    /// from ApplyPresetLoad after a save/load mutates the on-disk preset
    /// list (so the cycle picks up the new name).
    /// </summary>
    private void RebuildPresetCycle(string settingsId)
    {
        try
        {
            var saved = MCM.Internal.SettingsStorage.EnumeratePresets(settingsId);
            var list = new List<string>(saved.Count + 2) { PRESET_SENTINEL_CURRENT };
            foreach (var n in saved) list.Add(n);
            list.Add(PRESET_SENTINEL_SAVE);
            _presetCycle = list.ToArray();
            _presetCycleIndex = 0;
            ViewModel.NotifyPropertyChanged(nameof(PresetCycleText));
            ViewModel.NotifyPropertyChanged(nameof(PresetCycleVisible));

            // Phase 2.4: mirror the same entries into the dropdown popup list.
            _presetOptions.Clear();
            foreach (var name in list) _presetOptions.Add(new PresetOptionVM(name, OnPresetOptionSelected));
            _presetButtonText = PRESET_SENTINEL_CURRENT;
            _presetDropdownOpen = false;
            ViewModel.NotifyPropertyChanged(nameof(PresetButtonText));
            ViewModel.NotifyPropertyChanged(nameof(PresetDropdownOpen));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"RebuildPresetCycle({settingsId})", ex); }
    }

    private void PromptSavePresetName(MCM.Internal.RegisteredSettings entry, string settingsId)
    {
        try
        {
            TaleWorlds.Library.InformationManager.ShowTextInquiry(new TaleWorlds.Library.TextInquiryData(
                titleText: "Name this preset",
                text: $"Snapshot the current {entry.DisplayName} settings under what name?\n(Letters, digits, spaces, hyphens — unsafe characters will be replaced with _)",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: true,
                affirmativeText: "Save",
                negativeText: "Cancel",
                affirmativeAction: name =>
                {
                    // Flush in-memory singleton -> Global\<id>.json FIRST so the
                    // preset captures whatever the user is currently seeing in
                    // the panel (including unsaved changes from this MCM session).
                    // Without this, SavePreset snapshots stale disk content from
                    // before the user touched anything — the preset would not
                    // match what's on screen and Apply would appear to "not stick".
                    try { MCM.Internal.SettingsStorage.Save(entry.Instance, settingsId); }
                    catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"PromptSavePresetName/flush({settingsId})", ex); }

                    var ok = MCM.Internal.SettingsStorage.SavePreset(settingsId, name);
                    if (ok)
                    {
                        // Refresh the cycle so the new preset name appears
                        // between (Current settings) and (Save current as new...).
                        try { RebuildPresetCycle(settingsId); }
                        catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"PromptSavePresetName/RebuildPresetCycle({settingsId})", ex); }
                    }
                    ShowInfo(ok ? "Preset saved" : "Save failed",
                             ok ? $"Saved '{name}' for {entry.DisplayName}. It will now appear in the preset dropdown at the top of the panel."
                                : $"Could not save preset '{name}'. Check runtime.log for the error.");
                },
                negativeAction: () => { },
                shouldInputBeObfuscated: false
            ), pauseGameActiveState: true);
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "PromptSavePresetName", ex); }
    }

    private void PromptLoadPreset(MCM.Internal.RegisteredSettings entry, string settingsId,
        System.Collections.Generic.IReadOnlyList<string> presets)
    {
        try
        {
            if (presets == null || presets.Count == 0)
            {
                ShowInfo("No presets to load",
                         $"You haven't saved any presets for {entry.DisplayName} yet. Use 'Save as new...' first.");
                return;
            }
            ShowPresetPicker(entry, settingsId, presets, index: 0);
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "PromptLoadPreset", ex); }
    }

    private void ShowPresetPicker(MCM.Internal.RegisteredSettings entry, string settingsId,
        System.Collections.Generic.IReadOnlyList<string> presets, int index)
    {
        try
        {
            if (index < 0 || index >= presets.Count) index = 0;
            var name = presets[index];
            var counter = $"Preset {index + 1} of {presets.Count}";
            // Cycling navigation: Affirmative = Load this one, Negative = Next.
            // For the last entry, Negative wraps back to 0.
            var nextIndex = (index + 1) % presets.Count;

            TaleWorlds.Library.InformationManager.ShowInquiry(new TaleWorlds.Library.InquiryData(
                titleText: $"Load preset — {entry.DisplayName}",
                text: $"{counter}\n\n  {name}\n\nLoad this preset into the live settings? The current values will be overwritten.",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: presets.Count > 1,
                affirmativeText: "Load this",
                negativeText: presets.Count > 1 ? "Next preset" : "",
                affirmativeAction: () => ApplyPresetLoad(entry, settingsId, name),
                negativeAction: () => ShowPresetPicker(entry, settingsId, presets, nextIndex)
            ), pauseGameActiveState: true);
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "ShowPresetPicker", ex); }
    }

    private void ApplyPresetLoad(MCM.Internal.RegisteredSettings entry, string settingsId, string presetName)
    {
        try
        {
            if (!MCM.Internal.SettingsStorage.LoadPresetIntoLiveFile(settingsId, presetName))
            {
                ShowInfo("Load failed",
                         $"Could not load preset '{presetName}'. Check runtime.log for the error.");
                return;
            }
            // Reload the in-memory singleton from the freshly-overwritten
            // live file so the UI reflects the new values immediately.
            try { MCM.Internal.SettingsStorage.Load(entry.Instance, settingsId); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ApplyPresetLoad/reload({settingsId})", ex); }

            // Re-bind the SettingsVM so the UI surfaces the new values.
            // SelectMod with the current index reconstructs _currentSettingsVM
            // against the now-updated entry.Instance.
            try { SelectMod(_currentModIndex); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ApplyPresetLoad/SelectMod({settingsId})", ex); }

            ShowInfo("Preset loaded",
                     $"Loaded '{presetName}' into {entry.DisplayName}. Click Done to persist, or change values and Done to save on top of the preset.");
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "ApplyPresetLoad", ex); }
    }

    private static void ShowInfo(string title, string body)
    {
        try
        {
            TaleWorlds.Library.InformationManager.ShowInquiry(new TaleWorlds.Library.InquiryData(
                titleText: title,
                text: body,
                isAffirmativeOptionShown: true,
                isNegativeOptionShown: false,
                affirmativeText: "OK",
                negativeText: "",
                affirmativeAction: () => { },
                negativeAction: () => { }
            ), pauseGameActiveState: true);
        }
        catch { /* never let an info popup break the calling action */ }
    }

    /// <summary>
    /// Self-Test invoked from Report-a-Bug. Runs the McmSelfTest harness
    /// synchronously and writes selftest.log + selftest.json to disk; does
    /// NOT show a result popup (Report-a-Bug proceeds directly to opening
    /// the GitHub issue draft afterwards). Wrapped in try/catch so a
    /// self-test failure on one mod doesn't abort the bug-report flow.
    /// </summary>
    private void RunSelfTestQuiet()
    {
        if (_selfTestRunning)
        {
            DiagLog.Log(Tag, "RunSelfTestQuiet: already running; skipped");
            return;
        }
        _selfTestRunning = true;
        try
        {
            DiagLog.Log(Tag, "RunSelfTestQuiet: starting McmSelfTest.RunAll() (Report-a-Bug flow)");
            McmSelfTest.RunAll();
            DiagLog.Log(Tag, "RunSelfTestQuiet: complete");
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "RunSelfTestQuiet", ex);
        }
        finally
        {
            _selfTestRunning = false;
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
                    var disabled = System.IO.File.ReadAllLines(disabledPath)
                        .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    int show = System.Math.Min(20, disabled.Count);
                    sb.AppendLine($"**Auto-disable history (last {show} of {disabled.Count} entries)**:");
                    foreach (var l in disabled.Skip(System.Math.Max(0, disabled.Count - show)))
                        sb.AppendLine($"- {l}");
                }
                else
                {
                    sb.AppendLine("**Auto-disable history**: none (no incompatible mods recorded)");
                }
                sb.AppendLine();

                var incompatPath = System.IO.Path.Combine(dir, "incompatible-mods.log");
                if (System.IO.File.Exists(incompatPath))
                {
                    var content = System.IO.File.ReadAllText(incompatPath);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        sb.AppendLine("**Incompatible mods (latest post-load scan)**:");
                        sb.AppendLine("```");
                        sb.AppendLine(content.Trim());
                        sb.AppendLine("```");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "BuildGitHubIssueBody/diagnostics", ex);
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
