// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System.Collections.Generic;

using MCM.Abstractions;

using TaleWorlds.Library;

namespace MCM.UI.GUI.ViewModels;

public class SettingsPropertyGroupVM : ViewModel
{
    private string _groupName = string.Empty;
    private MBBindingList<SettingsPropertyVM> _properties = new();
    private bool _isExpanded = true;

    // S2: optional group-level toggle (IsMainToggle = true property).
    // When present its bool value enables/disables the entire group.
    private readonly SettingsPropertyVM? _toggleVm;

    [DataSourceProperty]
    public string GroupName
    {
        get => _groupName;
        set { _groupName = value; OnPropertyChangedWithValue(value, nameof(GroupName)); }
    }

    [DataSourceProperty]
    public MBBindingList<SettingsPropertyVM> SettingProperties
    {
        get => _properties;
        set { _properties = value; OnPropertyChangedWithValue(value, nameof(SettingProperties)); }
    }

    [DataSourceProperty]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChangedWithValue(value, nameof(IsExpanded)); }
    }

    // S2 ---- group toggle surface ----------------------------------------
    /// <summary>True when a [SettingPropertyGroup(IsMainToggle=true)] property
    /// was declared for this group; the prefab uses this to show the toggle
    /// checkbox in the group header.</summary>
    [DataSourceProperty]
    public bool HasGroupToggle => _toggleVm != null;

    /// <summary>Current value of the group's main toggle.</summary>
    [DataSourceProperty]
    public bool GroupToggleValue
    {
        get => _toggleVm?.BoolValue ?? true;
        set
        {
            if (_toggleVm == null) return;
            _toggleVm.BoolValue = value;
            OnPropertyChangedWithValue(value, nameof(GroupToggleValue));
            // Propagate enable/disable state to every child property row.
            foreach (var p in _properties) p.IsVisible = value;
        }
    }

    /// <summary>Flip the group toggle. Bound to the group header checkbox.</summary>
    public void ExecuteToggleGroup() => GroupToggleValue = !GroupToggleValue;

    /// <summary>The IsMainToggle property VM, or null if this group has none.
    /// The header-toggle bindings above are only wired in the (unused) ModOptionsVM
    /// prefab; the LIVE Mod Config tab (OptionsVMMixin) flattens groups into a flat
    /// row list and needs this so the master enable/disable setting still renders as
    /// a row instead of vanishing from the UI entirely.</summary>
    public SettingsPropertyVM? GroupToggleProperty => _toggleVm;
    // S2 ---- end group toggle surface ------------------------------------

    /// <param name="toggleVm">Optional IsMainToggle property — removed from
    /// <paramref name="properties"/> by the caller and passed separately.</param>
    public SettingsPropertyGroupVM(string groupName, IReadOnlyList<SettingsPropertyVM> properties,
        SettingsPropertyVM? toggleVm = null)
    {
        _groupName = groupName ?? string.Empty;
        _toggleVm  = toggleVm;
        foreach (var p in properties) _properties.Add(p);
        // Apply initial toggle state: if the toggle starts false, hide children.
        if (_toggleVm != null && !_toggleVm.BoolValue)
            foreach (var p in _properties) p.IsVisible = false;
    }

    public void RefreshVisibility(BaseSettings owner)
    {
        // S2: group toggle off => force-hide all children regardless of the
        // per-property hook; skip individual RefreshVisibility calls.
        bool groupEnabled = _toggleVm == null || _toggleVm.BoolValue;
        foreach (var p in _properties)
        {
            if (!groupEnabled) { p.IsVisible = false; continue; }
            p.RefreshVisibility(owner);
        }
    }

    /// <summary>Toggle the group's expansion state. Bound to the group header click handler.</summary>
    public void ExecuteToggle() => IsExpanded = !IsExpanded;
}
