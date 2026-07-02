// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks an int property for inclusion in the MCM panel as a discrete slider
/// or input field bounded by [MinValue, MaxValue]. ValueFormat is a standard
/// .NET numeric format string (e.g. "0" for raw integers, "Number key {0}").
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyIntegerAttribute : SettingPropertyAttribute
{
    public int MinValue { get; }
    public int MaxValue { get; }
    public string ValueFormat { get; }

    public SettingPropertyIntegerAttribute(string displayName, int minValue, int maxValue, string valueFormat = "0")
        : base(displayName)
    {
        MinValue = minValue;
        MaxValue = maxValue;
        ValueFormat = valueFormat ?? "0";
    }
}
