// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes;

/// <summary>
/// Groups settings properties in the MCM panel. A property may carry multiple
/// SettingPropertyGroup attributes to nest under a path like "Foo/Bar/Baz"
/// using the GroupName as the path separator. Order of groups is controlled
/// by GroupOrder; lower values appear earlier.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public class SettingPropertyGroupAttribute : Attribute
{
    public string GroupName { get; }
    public int GroupOrder { get; set; }
    public bool IsMainToggle { get; set; }

    public SettingPropertyGroupAttribute(string groupName)
    {
        GroupName = groupName ?? string.Empty;
    }
}
