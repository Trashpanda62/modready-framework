// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks a property holding a DropdownDefault&lt;T&gt; (or compatible
/// IDropdownProvider) for inclusion in the MCM panel as a dropdown / combobox.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyDropdownAttribute : SettingPropertyAttribute
{
    /// <summary>Whether the dropdown should accept multiple simultaneous selections.</summary>
    public bool AllowMultiple { get; set; }

    public SettingPropertyDropdownAttribute(string displayName) : base(displayName) { }
}
