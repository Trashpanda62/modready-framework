// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks a float property for inclusion in the MCM panel as a continuous
/// slider bounded by [MinValue, MaxValue]. ValueFormat is a standard .NET
/// numeric format string (e.g. "0.0", "0.00x").
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyFloatingIntegerAttribute : SettingPropertyAttribute
{
    public float MinValue { get; }
    public float MaxValue { get; }
    public string ValueFormat { get; }

    public SettingPropertyFloatingIntegerAttribute(string displayName, float minValue, float maxValue, string valueFormat = "0.00")
        : base(displayName)
    {
        MinValue = minValue;
        MaxValue = maxValue;
        ValueFormat = valueFormat ?? "0.00";
    }
}
