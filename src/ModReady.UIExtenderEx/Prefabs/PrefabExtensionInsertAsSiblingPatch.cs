// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System.Xml;

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// v1 patch that inserts XML as a sibling of the XPath-selected target.
/// Insert mode determines whether the sibling lands before, after, or
/// replaces the target.
/// </summary>
public abstract class PrefabExtensionInsertAsSiblingPatch : IPrefabPatch
{
    public enum InsertType
    {
        /// <summary>Insert as a sibling before the target.</summary>
        Prepend,
        /// <summary>Insert as a sibling after the target.</summary>
        Append,
        /// <summary>Replace the target node with our sibling content.</summary>
        Replace,
    }

    public abstract InsertType Type { get; }

    // Return type must be XmlDocument (not XmlNode) to match upstream
    // Bannerlord.UIExtenderEx ABI; see PrefabExtensionReplacePatch for
    // the same fix rationale.
    public abstract XmlDocument GetPrefabExtension();
}
