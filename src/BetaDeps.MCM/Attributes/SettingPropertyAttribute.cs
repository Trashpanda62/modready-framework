// BetaDeps clean-room re-implementation of the MCM v5 settings attribute
// surface. MIT, copyright 2026 Maxfield Management Group.
//
// Namespace deliberately matches the upstream BUTR-MCM layout so consumer
// mods built against the upstream MCMv5 NuGet resolve drop-in against our
// assembly with no source changes.

using System;

namespace MCM.Abstractions.Attributes;

/// <summary>
/// Base class for the MCM SettingProperty attribute family. Carries the
/// shared parameters every settings property exposes regardless of value
/// type. Consumer mods apply one of the derived attributes (Bool, Integer,
/// FloatingInteger, Dropdown, Text) to each property of their
/// AttributeGlobalSettings&lt;T&gt; subclass.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public abstract class SettingPropertyAttribute : Attribute
{
    /// <summary>Display name. Strings starting with "{=Id}" are localized
    /// at runtime; everything after the closing "}" is the fallback text.</summary>
    public string DisplayName { get; }

    /// <summary>Sort order within the containing group. Lower = earlier.</summary>
    public int Order { get; set; }

    /// <summary>True if changing this property requires a restart to take effect.</summary>
    public bool RequireRestart { get; set; }

    /// <summary>Optional hint text shown to the user (e.g. as a tooltip).</summary>
    public string? HintText { get; set; }

    /// <summary>True if this property serves as the master "on/off" toggle for its group.</summary>
    public bool IsToggle { get; set; }

    protected SettingPropertyAttribute(string displayName)
    {
        DisplayName = displayName ?? string.Empty;
    }
}
