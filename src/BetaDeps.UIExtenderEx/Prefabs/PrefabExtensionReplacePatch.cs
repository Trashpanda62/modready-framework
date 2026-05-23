// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System.Xml;

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// v1 patch that replaces the XPath-selected target node entirely with the
/// returned content.
/// </summary>
public abstract class PrefabExtensionReplacePatch : IPrefabPatch
{
    public abstract XmlNode GetPrefabExtension();
}
