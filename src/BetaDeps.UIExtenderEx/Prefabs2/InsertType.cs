// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

namespace Bannerlord.UIExtenderEx.Prefabs2;

/// <summary>
/// How a <see cref="PrefabExtensionInsertPatch"/> places its content relative
/// to the XPath-selected target node.
/// </summary>
public enum InsertType
{
    /// <summary>Insert as a sibling immediately before the target (NOT as a
    /// child -- see PrefabPatcher.ApplyFragments).</summary>
    Prepend,
    /// <summary>Replace the target node but keep its children under the
    /// replacement.</summary>
    ReplaceKeepChildren,
    /// <summary>Replace the target node entirely.</summary>
    Replace,
    /// <summary>Insert as the last child of the target.</summary>
    Child,
    /// <summary>Insert as a sibling after the target.</summary>
    Append,
    /// <summary>Remove the target node.</summary>
    Remove,
}
