// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModOptionsVM -- the screen-level ViewModel for the in-game Mod Configuration
// tab. Holds the per-mod list on the left (ModSettingsList), the currently
// selected mod's property grid on the right (SelectedMod), and the Done /
// Cancel / Default button bindings. Bound to the ModOptionsView.xml prefab.

using System.Collections.Generic;
using System.Linq;

using BetaDeps.Foundation;

using MCM.Abstractions;
using MCM.Internal;

using TaleWorlds.Library;

namespace MCM.UI.GUI.ViewModels;

public class ModOptionsVM : ViewModel
{
    private const string Tag = "ModOptionsVM";

    private MBBindingList<SettingsVM> _modSettingsList = new();
    private SettingsVM? _selectedMod;
    private string _doneText = "Done";
    private string _cancelText = "Cancel";
    private string _defaultText = "Defaults";
    private string _titleText = "Mod Configuration";

    [DataSourceProperty]
    public MBBindingList<SettingsVM> ModSettingsList
    {
        get => _modSettingsList;
        set { _modSettingsList = value; OnPropertyChangedWithValue(value, nameof(ModSettingsList)); }
    }

    [DataSourceProperty]
    public SettingsVM? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (_selectedMod == value) return;
            _selectedMod = value;
            OnPropertyChangedWithValue(value, nameof(SelectedMod));
        }
    }

    [DataSourceProperty] public string TitleText  { get => _titleText;   set { _titleText = value;   OnPropertyChangedWithValue(value, nameof(TitleText));  } }
    [DataSourceProperty] public string DoneText   { get => _doneText;    set { _doneText = value;    OnPropertyChangedWithValue(value, nameof(DoneText));   } }
    [DataSourceProperty] public string CancelText { get => _cancelText;  set { _cancelText = value;  OnPropertyChangedWithValue(value, nameof(CancelText)); } }
    [DataSourceProperty] public string DefaultText{ get => _defaultText; set { _defaultText = value; OnPropertyChangedWithValue(value, nameof(DefaultText)); } }

    public ModOptionsVM()
    {
        Refresh();
    }

    /// <summary>Reload the mod list from SettingsRegistry + FluentSettingsRegistry.</summary>
    public void Refresh()
    {
        try
        {
            _modSettingsList.Clear();
            var seenIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var reg in SettingsRegistry.All.OrderBy(r => r.DisplayName))
            {
                _modSettingsList.Add(new SettingsVM(reg.Instance));
                seenIds.Add(reg.Id);
            }
            // 2.3/H6 + M15: SettingsRegistry.DiscoverAll already merges fluent
            // registrations, so only add fluent instances it didn't pick up
            // (e.g. ones registered after discovery) -- the old unconditional
            // second loop double-listed every fluent panel.
            foreach (var fluent in FluentSettingsRegistry.All.OrderBy(f => f.DisplayName))
            {
                if (!string.IsNullOrEmpty(fluent.Id) && seenIds.Contains(fluent.Id)) continue;
                _modSettingsList.Add(new SettingsVM(fluent));
            }
            SelectedMod = _modSettingsList.FirstOrDefault();
            DiagLog.Log(Tag, $"refreshed: {_modSettingsList.Count} settings panels");
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "Refresh", ex);
        }
    }

    /// <summary>Apply pending changes across every settings panel.</summary>
    public void ExecuteDone()
    {
        foreach (var s in _modSettingsList)
        {
            try { s.Apply(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteDone({s.DisplayName})", ex); }
        }
    }

    /// <summary>Discard pending changes; reload from disk.</summary>
    public void ExecuteCancel()
    {
        foreach (var s in _modSettingsList)
        {
            try { s.Revert(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteCancel({s.DisplayName})", ex); }
        }
    }

    /// <summary>Reset every panel's properties to their declared defaults.</summary>
    public void ExecuteDefault()
    {
        foreach (var s in _modSettingsList)
        {
            try { s.RestoreDefaults(); }
            catch (System.Exception ex) { DiagLog.LogCaught(Tag, $"ExecuteDefault({s.DisplayName})", ex); }
        }
    }

    // ---- CREST-compat surface -----------------------------------------
    //
    // CrestSettings's static ctor calls ModOptionsVM.RefreshVisibility() from
    // a dropdown PropertyChanged handler. Original BUTR-MCM-UI variant uses
    // this to retell the UI to re-evaluate IsPropertyVisibleHook for every
    // currently-rendered property. We honor the same surface; when our UI
    // tab is open, this triggers a property-grid refresh.

    private static ModOptionsVM? _instance;

    internal static void SetCurrent(ModOptionsVM? vm) { _instance = vm; }

    /// <summary>Tell the UI to re-evaluate visibility hooks across all rendered properties.</summary>
    public static void RefreshVisibility()
    {
        var vm = _instance;
        if (vm == null)
        {
            DiagLog.Log(Tag, "RefreshVisibility() called but no MCM tab is currently open");
            return;
        }
        try
        {
            vm.SelectedMod?.RefreshVisibility();
        }
        catch (System.Exception ex)
        {
            DiagLog.LogCaught(Tag, "RefreshVisibility", ex);
        }
    }
}
