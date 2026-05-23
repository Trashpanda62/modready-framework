// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCMv1-compat attributes. A handful of older mods (BloodMod1313, IDontCare,
// possibly others) compiled against MCMv1, where there was ONE generic
// SettingProperty attribute that carried min/max for numeric properties and
// dispatched on the property's CLR type rather than via attribute subclasses.
//
// We provide concrete v1 attribute types here in the exact namespace the IL
// of those mods expects -- MCM.Abstractions.Attributes.v1. Each derives from
// the v2 abstract base so GetCustomAttribute<v2.SettingPropertyAttribute>
// still finds them. The widget selector in SettingsPropertyVM.Create looks
// at the property's CLR type to pick the right widget for v1 attributes.

using System;

namespace MCM.Abstractions.Attributes.v1;

/// <summary>
/// MCMv1 SettingProperty attribute. Carries an optional numeric range and a
/// hint string; widget type is inferred from the property's CLR type at
/// render time. Multiple constructor overloads exist to match every shape
/// seen in the wild.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class SettingPropertyAttribute : MCM.Abstractions.Attributes.SettingPropertyAttribute
{
    /// <summary>Min value for numeric properties (0 if unused).</summary>
    public decimal MinValue { get; set; }
    /// <summary>Max value for numeric properties (100 if unused).</summary>
    public decimal MaxValue { get; set; } = 100m;
    /// <summary>Display format for slider value labels.</summary>
    public string ValueFormat { get; set; } = "0";

    // ---- ctor overloads matching shapes seen in real MCMv1 mods ------------
    public SettingPropertyAttribute(string displayName) : base(displayName) { }

    public SettingPropertyAttribute(string displayName, string hintText)
        : base(displayName) { HintText = hintText; }

    public SettingPropertyAttribute(string displayName, int minValue, int maxValue)
        : base(displayName) { MinValue = minValue; MaxValue = maxValue; }

    public SettingPropertyAttribute(string displayName, int minValue, int maxValue, string hintText)
        : base(displayName) { MinValue = minValue; MaxValue = maxValue; HintText = hintText; }

    public SettingPropertyAttribute(string displayName, float minValue, float maxValue)
        : base(displayName) { MinValue = (decimal)minValue; MaxValue = (decimal)maxValue; }

    public SettingPropertyAttribute(string displayName, float minValue, float maxValue, string hintText)
        : base(displayName) { MinValue = (decimal)minValue; MaxValue = (decimal)maxValue; HintText = hintText; }

    public SettingPropertyAttribute(string displayName, double minValue, double maxValue)
        : base(displayName) { MinValue = (decimal)minValue; MaxValue = (decimal)maxValue; }

    public SettingPropertyAttribute(string displayName, double minValue, double maxValue, string hintText)
        : base(displayName) { MinValue = (decimal)minValue; MaxValue = (decimal)maxValue; HintText = hintText; }
}

/// <summary>MCMv1 group attribute. Mirrors v2 -- groupName + optional order.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class SettingPropertyGroupAttribute : MCM.Abstractions.Attributes.SettingPropertyGroupAttribute
{
    public SettingPropertyGroupAttribute(string groupName) : base(groupName) { }
    public SettingPropertyGroupAttribute(string groupName, int groupOrder)
        : base(groupName) { GroupOrder = groupOrder; }
}
