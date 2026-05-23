// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System.Collections.Generic;

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// v1 patch that sets one or more XML attributes on the XPath-selected
/// target node. Returns a (name, value) sequence so a single patch can set
/// multiple attributes at once. The v2 variant in Prefabs2 takes a single
/// attribute.
/// </summary>
public abstract class PrefabExtensionSetAttributePatch : IPrefabPatch
{
    public abstract IEnumerable<KeyValuePair<string, string>> Attributes { get; }
}
