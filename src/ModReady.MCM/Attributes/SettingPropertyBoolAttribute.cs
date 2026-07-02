// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
// Namespace deliberately matches upstream BUTR-MCM for drop-in compatibility.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks a bool property for inclusion in the MCM settings panel as a
/// checkbox/toggle.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyBoolAttribute : SettingPropertyAttribute
{
    public SettingPropertyBoolAttribute(string displayName) : base(displayName) { }
}
