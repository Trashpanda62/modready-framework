// ModReady clean-room.
using System.Collections.Generic;
namespace MCM.Abstractions;
public class SettingsPropertyGroupDefinition : IPropertyGroupDefinition
{
    public string GroupName { get; }
    public int GroupOrder { get; }
    public bool IsMainToggle { get; }
    public List<ISettingsPropertyDefinition> SettingProperties { get; } = new();
    public SettingsPropertyGroupDefinition(string groupName, int groupOrder = 0, bool isMainToggle = false)
    {
        GroupName = groupName ?? string.Empty;
        GroupOrder = groupOrder;
        IsMainToggle = isMainToggle;
    }
}
