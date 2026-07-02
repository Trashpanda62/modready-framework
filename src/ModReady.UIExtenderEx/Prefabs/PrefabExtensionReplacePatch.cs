// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System.Xml;

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// v1 patch that replaces the XPath-selected target node entirely with the
/// returned content. Return type is XmlDocument (NOT XmlNode) to match the
/// upstream Bannerlord.UIExtenderEx ABI — consumer mods like Character
/// Development Editor declare `public override XmlDocument GetPrefabExtension()`
/// and the CLR will refuse to load the type with "does not have an
/// implementation" if the abstract method's return type differs.
/// </summary>
public abstract class PrefabExtensionReplacePatch : IPrefabPatch
{
    public abstract XmlDocument GetPrefabExtension();
}
