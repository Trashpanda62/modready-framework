// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Attached to a class that extends a Gauntlet movie's prefab XML. The class
/// must derive from one of the prefab-patch base types (PrefabExtensionInsertPatch,
/// PrefabExtensionSetAttributePatch, etc.) and be discovered by
/// <c>UIExtender.Register(Assembly)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PrefabExtensionAttribute : BaseUIExtenderAttribute
{
    /// <summary>Name of the Gauntlet movie (prefab) to extend, e.g. "Options".</summary>
    public string Movie { get; }

    /// <summary>XPath expression locating the node to operate on. May be null
    /// when the patch is meant to apply to the prefab root.</summary>
    public string? XPath { get; }

    /// <summary>Optional auto-gen widget name. Retained for API compatibility;
    /// auto-gen prefab generation is not currently supported on the
    /// implementation side.</summary>
    public string? AutoGenWidgetName { get; }

    /// <param name="movie">Gauntlet movie name (without the .xml suffix).</param>
    /// <param name="xpath">XPath of the node to operate against. Optional.</param>
    public PrefabExtensionAttribute(string movie, string? xpath = null)
    {
        Movie = movie;
        XPath = xpath;
    }

    /// <param name="movie">Gauntlet movie name.</param>
    /// <param name="xpath">XPath of the node to operate against.</param>
    /// <param name="autoGenWidgetName">Optional auto-gen widget name (currently
    /// retained for API compatibility only).</param>
    public PrefabExtensionAttribute(string movie, string? xpath, string? autoGenWidgetName)
    {
        Movie = movie;
        XPath = xpath;
        AutoGenWidgetName = autoGenWidgetName;
    }
}
