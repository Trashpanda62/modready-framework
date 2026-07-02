// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks a string property for inclusion in the MCM panel as a text input
/// field. Consumer mods use this for free-form names, paths, URLs, etc.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyTextAttribute : SettingPropertyAttribute
{
    public SettingPropertyTextAttribute(string displayName) : base(displayName) { }

    /// <summary>
    /// 4-argument constructor used by older mods (e.g. IDontCare) compiled
    /// against a v2 MCM build that shipped this exact ctor signature.
    /// </summary>
    public SettingPropertyTextAttribute(string displayName, int order, bool requireRestart, string hintText)
        : base(displayName)
    {
        Order = order;
        RequireRestart = requireRestart;
        HintText = hintText;
    }
}
