// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Xml;

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// v1 escape hatch for arbitrary XML mutation. The runtime engine calls
/// <see cref="Apply"/> with the parent node of the XPath-selected target,
/// and the patch is free to perform any in-place XML edit.
/// </summary>
public abstract class CustomPatch : IPrefabPatch
{
    /// <summary>
    /// Mutate <paramref name="parent"/> in place. The XPath that selected
    /// this parent is the one declared on the <c>[PrefabExtension]</c>
    /// attribute applied to the derived class.
    /// </summary>
    public abstract void Apply(XmlNode parent);
}
