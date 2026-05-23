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

    public SettingsPropertyGroupVM(string groupName, IReadOnlyList<SettingsPropertyVM> properties)
    {
        _groupName = groupName ?? string.Empty;
        foreach (var p in properties) _properties.Add(p);
    }

    public void RefreshVisibility(BaseSettings owner)
    {
        foreach (var p in _properties) p.RefreshVisibility(owner);
    }

    /// <summary>Toggle the group's expansion state. Bound to the group header click handler.</summary>
    public void ExecuteToggle() => IsExpanded = !IsExpanded;
}
