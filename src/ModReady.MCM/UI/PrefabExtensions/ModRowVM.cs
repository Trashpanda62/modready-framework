// ModReady.MCM -- ModRowVM
//
// Phase 2.1 (UI polish): one row in the left "Mods" sidebar list of the Mod
// Configuration tab. The sidebar replaces the old Prev/Next mod cycler: the
// mixin exposes an MBBindingList<ModRowVM> ModList, the prefab binds a
// NavigatableListPanel to {ModList}, and each row's ExecuteSelect calls back
// into the mixin to select that mod. IsSelected drives the highlighted-row
// brush. A real ViewModel so its @bindings/Command resolve natively once the
// ItemTemplate hands the row VM to the widget (same path as PresentationRowVM).
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

using Bannerlord.UIExtenderEx.Attributes;

using ModReady.Foundation;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

public sealed class ModRowVM : ViewModel
{
    private const string Tag = "ModRowVM";

    private readonly int _index;            // position within the current filtered list
    private readonly Action<int>? _onSelect;
    private bool _isSelected;

    public ModRowVM(int index, string displayName, bool isSelected, Action<int>? onSelect)
    {
        _index = index;
        DisplayName = displayName ?? string.Empty;
        _isSelected = isSelected;
        _onSelect = onSelect;
    }

    [DataSourceProperty] public string DisplayName { get; }

    [DataSourceProperty]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(NotSelected));
        }
    }

    // Convenience inverse for the prefab (show the selected-highlight brush on
    // the selected row, the plain brush otherwise).
    [DataSourceProperty] public bool NotSelected => !_isSelected;

    [DataSourceMethod]
    public void ExecuteSelect()
    {
        try { _onSelect?.Invoke(_index); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteSelect", ex); }
    }
}
