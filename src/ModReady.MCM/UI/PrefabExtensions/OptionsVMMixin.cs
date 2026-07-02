// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// OptionsVMMixin attaches to TaleWorlds OptionsVM and exposes the bindings for
// the Mod Config tab: a single scrollable {RowList} of PresentationRowVM rows
// (the legacy fixed-slot fan was retired in slice 4d). Defensive try/catch
// around the rebuild paths so any binding-side failure logs rather than crashes
// the game.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Extensions;
using Bannerlord.UIExtenderEx.ViewModels;

using ModReady.Foundation;

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

    private bool _modConfigVisible;
    private string _modConfigTitle = "Mod Configuration";
    private string _registeredModsList = string.Empty;
    private string _summaryText = string.Empty;

    private RegisteredSettings[] _registered = System.Array.Empty<RegisteredSettings>();
    // v1.0: filtered view of _registered. Driven by ModReadyModSearchText.
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

    // Flat "presentation list" that interleaves header markers with property
    // entries. Built in SelectMod -> RebuildPresentationList. Each entry is
    // either ("header text", null) or (string.Empty, SettingsPropertyVM).
    private List<(string header, SettingsPropertyVM? prop)> _presentation = new();

    // Dynamic row list for the single-page scrollable ItemTemplate. The prefab
    // binds @RowList (see OptionsPrefabExtensions.RowListXml); rows are built in
    // SelectMod -> RebuildRowList from _presentation. This replaced the retired
    // fixed-slot fan (slice 4d).
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

    [DataSourceProperty] public bool   ModReadyModConfigTabVisible           { get => _modConfigVisible; set { _modConfigVisible = value; } }

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
    public string ModReadyModConfigTitle
    {
        get
        {
            var count = _registered?.Length ?? 0;
            if (count <= 0) return _modConfigTitle;
            return $"{_modConfigTitle}  ·  {count} mod{(count == 1 ? "" : "s")}";
        }
        set { _modConfigTitle = value; }
    }
    [DataSourceProperty] public string ModReadyModConfigRegisteredModsList   { get => _registeredModsList; set { _registeredModsList = value; } }
    [DataSourceProperty] public string ModReadyModConfigSummary              { get => _summaryText; set { _summaryText = value; } }

    // v0.7.6 visual polish:
    //   * empty-state visibility: when no mods registered, show a hint message
    //     instead of an empty settings panel.
    //   * toggle-button labels: instead of "Toggle X", show "X: ON" or "X: OFF"
    //     so users can read current state without clicking. Backed by flag files.
    [DataSourceProperty] public bool ModReadyModConfigHasMods   => _registered != null && _registered.Length > 0;
    [DataSourceProperty] public bool ModReadyModConfigIsEmpty   => !ModReadyModConfigHasMods;

    private static string? ResolveModReadyDir()
    {
        try
        {
            var ownPath = typeof(OptionsVMMixin).Assembly.Location;
            if (string.IsNullOrEmpty(ownPath)) return null;
            var binDir = System.IO.Path.GetDirectoryName(ownPath);
            var modReadyDir = System.IO.Path.GetDirectoryName(binDir);   // ModReady\bin\.. -> ModReady\
            return modReadyDir;
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
            ViewModel.NotifyPropertyChanged(nameof(ModReadyModConfigHasMods));
            ViewModel.NotifyPropertyChanged(nameof(ModReadyModConfigIsEmpty));
            // Polish (post-v0.8): the title contains the registered-mod count
            // dynamically. Refresh after RebuildModList changes _registered so
            // late-registering fluent settings update the count in real time.
            ViewModel.NotifyPropertyChanged(nameof(ModReadyModConfigTitle));
            // RebuildModList clears _summaryText (the old "N mod(s) registered"
            // line was redundant with the title's count). Notify so the
            // RichTextWidget bound to ModReadyModConfigSummary collapses
            // in-place after the polish change.
            ViewModel.NotifyPropertyChanged(nameof(ModReadyModConfigSummary));
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
    public string ModReadyModSearchText
    {
        get => _modSearchText;
        set
        {
            var trimmed = value ?? string.Empty;
            if (_modSearchText == trimmed) return;
            _modSearchText = trimmed;
            ApplyFilter();
            ViewModel.NotifyPropertyChanged(nameof(ModReadyModSearchText));
            ViewModel.NotifyPropertyChanged(nameof(ModReadySearchClearVisible));
            ViewModel.NotifyPropertyChanged(nameof(ModReadySearchPlaceholderVisible));
        }
    }

    /// <summary>
    /// v1.0: visibility of the Clear button next to the search field. Hidden
    /// when no filter is active so the row stays uncluttered.
    /// </summary>
    [DataSourceProperty]
    public bool ModReadySearchClearVisible => !string.IsNullOrEmpty(_modSearchText);

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
    public bool ModReadySearchPlaceholderVisible => string.IsNullOrEmpty(_modSearchText);

    [DataSourceProperty] public string SelectedModName     => _selectedModName;
    [DataSourceProperty] public string SelectedModSummary  => _selectedModSummary;

    // (v0.4.23 removed the _currentRows / CurrentRows NavigatableListPanel
    //  experiment — the current XML uses fixed Slot0..Slot9 + pagination
    //  rather than a scrollable list. v0.6 audit deleted the dead members.)

    [DataSourceProperty] public string HoveredOptionName => _hoveredOptionName;
    private string _hoveredOptionName = string.Empty;

    private string _hoveredHintText = string.Empty;
    [DataSourceProperty] public string HoveredHintText => _hoveredHintText;
    [DataSourceProperty] public bool   IsHintVisible   => !string.IsNullOrEmpty(_hoveredHintText);

    private void ClearHoveredHint()
    {
        if (string.IsNullOrEmpty(_hoveredHintText) && string.IsNullOrEmpty(_hoveredOptionName)) return;
        _hoveredHintText = string.Empty;
        _hoveredOptionName = string.Empty;
        ViewModel.NotifyPropertyChanged(nameof(HoveredHintText));
        ViewModel.NotifyPropertyChanged(nameof(HoveredOptionName));
        ViewModel.NotifyPropertyChanged(nameof(IsHintVisible));
    }

    public OptionsVMMixin(ViewModel vm) : base(vm)
    {
        // M14: clear any focus refcount left stuck by a prior Options visit (a
        // search field torn down while focused never fires OnLoseFocus), which
        // would otherwise keep Q/E tab navigation suppressed for the session.
        TabSwitchGuardPatch.ResetFocusCount();
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

    // M14: the static HoverCallback/HoverEndCallback assigned above capture
    // `this`, so once this Options screen closes they keep rooting this mixin
    // (and the whole VM graph it references) until the next screen open
    // reassigns them. Null them on teardown so the graph can be collected; the
    // next OptionsVM re-subscribes in its constructor.
    public override void OnFinalize()
    {
        SettingsPropertyVM.HoverCallback = null;
        SettingsPropertyVM.HoverEndCallback = null;
        base.OnFinalize();
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
            var t = ModReady.Foundation.ReflectionUtils.ResolveTypeByFullName("TaleWorlds.CampaignSystem.Campaign");
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
            // the Modules\ModReady\presentation-survey.flag file). RebuildModList
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
                    // v0.9.0: prefix removed -- show the mod's own DisplayName
                    // verbatim (no "<Assembly> — " clutter). _prefixedSettings is
                    // left empty; consolidation below picks the first settings as
                    // the primary entry, and the duplicate-name disambiguator still
                    // runs to keep same-named mods distinguishable.
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
                            // Idempotent: RebuildModList runs many times per session and
                            // dup.DisplayName mutates the persistent registry override, so
                            // a naive append produced "X (hint) (hint)" on re-runs.
                            var suffix = " (" + hint + ")";
                            var cur = dup.DisplayName ?? string.Empty;
                            if (!cur.EndsWith(suffix, System.StringComparison.Ordinal))
                                dup.DisplayName = cur + suffix;
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
    /// the ModReadyModSearchText setter and by RebuildModList. Always resets
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
                DiagLog.Log(Tag, $"ApplyFilter: query='{q}' total={_registered.Length} matched={_filteredRegistered.Length}");
                if (_filteredRegistered.Length == 0 && _registered.Length > 0)
                    DiagLog.Log(Tag, $"  registered names: {string.Join(", ", _registered.Select(r => r.DisplayName ?? "(null)"))}");
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
    // v0.9.2: mods whose indent-0 headers have already been seeded as expanded.
    // The first time a mod's list builds, every top-level / parent header is
    // opened so the user sees the section structure on entry (child sub-groups
    // stay collapsed). After seeding, normal toggle state in _expandedGroups
    // takes over, so a user collapse sticks across rebuilds.
    private readonly System.Collections.Generic.HashSet<string> _seededMods =
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
        // v0.9.2: on the first build for this mod, default every indent-0 header
        // (top-level groups + parents-with-children) to EXPANDED so the section
        // structure is visible on entry. Child sub-groups stay collapsed.
        bool seed = !string.IsNullOrEmpty(_currentModId) && !_seededMods.Contains(_currentModId);
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

                // top-level group (no "/") -- expanded on first build, indent 0
                var key = GroupKey(_currentModId, groups[gi].full);
                if (seed) _expandedGroups.Add(key);
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
            if (seed) _expandedGroups.Add(pkey);
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

        // Mark this mod seeded so subsequent rebuilds honor user toggle state
        // (a user-collapsed header stays collapsed) instead of re-expanding.
        if (seed) _seededMods.Add(_currentModId);
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
                _presentation = new List<(string, SettingsPropertyVM?)>();
                RebuildRowList();
                _selectedModName = string.IsNullOrEmpty(_modSearchText) ? "(no mods)" : "(no matches)";
                _selectedModSummary = string.IsNullOrEmpty(_modSearchText)
                    ? "0 of 0"
                    : $"0 of 0 matching \"{_modSearchText}\"";
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
                        // A [SettingPropertyGroup(IsMainToggle=true)] property is pulled
                        // out of SettingProperties into the group's toggle slot. The live
                        // flat-row UI has no header-toggle prefab (that lives in the unused
                        // ModOptionsVM), so render the master toggle as the group's first
                        // row -- otherwise it has no row anywhere and the user can't turn
                        // the feature back on once it's off.
                        if (g.GroupToggleProperty is { } groupToggle)
                        {
                            _currentFlatProps.Add(groupToggle);
                            _presentation.Add((string.Empty, groupToggle));
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
                        if (g.GroupToggleProperty is { } gToggle)
                        { _currentFlatProps.Add(gToggle); _presentation.Add((string.Empty, gToggle)); }
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
            NotifyHeader();
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "SelectMod", ex); }
    }


    private void NotifyHeader()
    {
        try
        {
            ViewModel.NotifyPropertyChanged(nameof(SelectedModName));
            ViewModel.NotifyPropertyChanged(nameof(SelectedModSummary));
        }
        catch (System.Exception ex) { DiagLog.LogCaught(Tag, "NotifyHeader", ex); }
    }


    [DataSourceMethod] public void ExecuteOpenModConfig()
    {
        ModReadyModConfigTabVisible = !ModReadyModConfigTabVisible;
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
            ModReadyModSearchText = string.Empty;
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteClearSearch", ex);
        }
    }

    /// <summary>
    /// Reset every property of the currently-selected settings class to its
    /// default. Attribute settings: construct a fresh instance of the type
    /// (which executes the field initializers + parameterless ctor) and copy
    /// each [SettingPropertyX]-decorated property onto the live singleton.
    /// Fluent settings have no such attributes, so they reset via
    /// IFluentSettings.ResetToDefaults() (the per-property default captured at
    /// construction). Either way the result is persisted via the same save path
    /// used when the user clicks Done on the Options panel.
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
            int copied;
            // Fluent settings (Diplomacy, RTSCamera, ImprovedGarrisons, BEW, ...)
            // carry no [SettingProperty] attributes to reflect -- their values
            // live in the builder/IRefs -- so the old attribute-only loop was a
            // silent no-op for them (Nexus v0.9.2 "Reset to Defaults does nothing").
            // Route to the captured-defaults reset instead.
            if (current is MCM.Internal.IFluentSettings fluent)
            {
                copied = fluent.ResetToDefaults();
            }
            else
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

                copied = 0;
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
            }

            // No inline Save — v0.3.3 buffers all edits until the user clicks
            // Done on the Options screen. Reset is now a *staged* operation:
            // Cancel will reload from disk and undo the reset; Done will
            // persist the defaults the same way it persists any other edit.

            DiagLog.Log(Tag, $"ExecuteResetDefaults: reset {copied} property(ies) on {current.Id} (in-memory; persisted on Done)");

            // Full rebind, not a bare RebuildRowList(). Two things just went stale
            // under the existing UI: (1) the row VMs were wrapping the pre-reset
            // values, and (2) the preset selector still showed whatever preset the
            // user had applied -- "I clicked reset to defaults but it stayed on
            // preset 1" (Steve, 2026-06-15). SelectMod re-wraps the SAME in-memory
            // instance (NO disk Load, so the in-memory reset survives -- unlike
            // ApplyPresetLoad which intentionally reloads) in a fresh SettingsVM +
            // row VMs that re-read the defaults, and its RebuildPresetCycle resets
            // the selector back to "(Current settings)". Mirrors ApplyPresetLoad's
            // known-good refresh path.
            try { SelectMod(_currentModIndex); }
            catch (System.Exception ex)
            {
                DiagLog.LogCaught(Tag, "ExecuteResetDefaults/refresh", ex);
                RebuildRowList(); // fall back to at least repainting the rows
            }
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "ExecuteResetDefaults", ex);
        }
    }

    // ---- Self-test button -----------------------------------------------
    //
    // Visible during ModReady development for one-click smoke-testing of
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
    /// "Send to GitHub" button. Opens the ModReady GitHub issues page in
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
                text: "ModReady will run a quick Self-Test (~5 seconds, restores your " +
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

                    var ok = MCM.Internal.SettingsStorage.SavePreset(settingsId, name, entry.Instance);
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
            if (!MCM.Internal.SettingsStorage.LoadPresetIntoLiveFile(settingsId, presetName, entry.Instance))
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
            // useful summary the user needs to triage (ModReady version,
            // last-good count, auto-disabled mods, top of selftest report)
            // plus instructions for the user to drag-drop the full log
            // files as attachments.

            var titleText = $"ModReady Self-Test report  {System.DateTime.Now:yyyy-MM-dd}";
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

            var url = "https://github.com/Trashpanda62/modready-framework/issues/new"
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
    /// ModReady version, the Bannerlord version, the auto-disable diagnostics
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
            sb.AppendLine($"- Branch:    {ModReady.Foundation.VersionProbe.Branch} (Bannerlord v{ModReady.Foundation.VersionProbe.Major}.{ModReady.Foundation.VersionProbe.Minor})");
            var asmName = typeof(MCMSubModule).Assembly.GetName();
            sb.AppendLine($"- ModReady:  v{asmName.Version}");
        }
        catch { }
        sb.AppendLine();
        sb.AppendLine("### Logs");
        sb.AppendLine("Please drag-drop these two files from your install into the GitHub issue (they auto-attach):");
        try
        {
            var rt = ModReady.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            sb.AppendLine($"- runtime.log  (`{RedactUserPath(rt)}`)");
            if (!string.IsNullOrEmpty(dir))
                sb.AppendLine($"- selftest.log (`{RedactUserPath(System.IO.Path.Combine(dir, "selftest.log"))}`)");
        }
        catch { }
        sb.AppendLine();

        // Auto-disable diagnostics section: this is the most actionable
        // piece for someone debugging the user's crash.
        sb.AppendLine("### ModReady runtime detection state");
        try
        {
            var rt = ModReady.Foundation.RuntimeLog.Path;
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

                var disabledPath = System.IO.Path.Combine(dir, "modready-disabled-mods.log");
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
