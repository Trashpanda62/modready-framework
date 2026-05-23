// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace MCM.Abstractions.Attributes.v2;

/// <summary>
/// Marks an Action-typed property for inclusion in the MCM panel as a
/// clickable button. The action runs when the user clicks; useful for
/// "Reset to defaults", "Reload config", etc.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SettingPropertyButtonAttribute : SettingPropertyAttribute
{
    /// <summary>Label rendered on the button itself (separate from DisplayName, which is the row label).</summary>
    public string Content { get; set; } = string.Empty;

    public SettingPropertyButtonAttribute(string displayName) : base(displayName) { }

    public SettingPropertyButtonAttribute(string displayName, string content) : base(displayName)
    {
        Content = content ?? string.Empty;
    }

    /// <summary>
    /// Full upstream-compatible signature: <c>(displayName, order, requireRestart, hintText)</c>.
    /// ArtemsLivelyAnimations and friends emit this exact ctor signature in
    /// their compiled IL; without it the runtime throws MissingMethodException
    /// when reflecting custom attributes off the settings property.
    /// </summary>
    public SettingPropertyButtonAttribute(string displayName, int order, bool requireRestart, string hintText)
        : base(displayName)
    {
        Order = order;
        RequireRestart = requireRestart;
        HintText = hintText ?? string.Empty;
    }
}
