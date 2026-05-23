// BetaDeps clean-room.
using System;
using MCM.Common;
namespace MCM.Abstractions;
public class SettingsPropertyDefinition : ISettingsPropertyDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string HintText { get; }
    public int Order { get; }
    public bool RequireRestart { get; }
    public string GroupName { get; }
    public int GroupOrder { get; }
    public IRef PropertyReference { get; }
    public Type PropertyType => PropertyReference.Type;
    public SettingsPropertyDefinition(string id, IRef propertyReference,
        string displayName = "", string hintText = "", int order = 0, bool requireRestart = false,
        string groupName = "", int groupOrder = 0)
    {
        Id = id ?? string.Empty;
        PropertyReference = propertyReference ?? throw new ArgumentNullException(nameof(propertyReference));
        DisplayName = displayName ?? string.Empty;
        HintText = hintText ?? string.Empty;
        Order = order;
        RequireRestart = requireRestart;
        GroupName = groupName ?? string.Empty;
        GroupOrder = groupOrder;
    }
}
