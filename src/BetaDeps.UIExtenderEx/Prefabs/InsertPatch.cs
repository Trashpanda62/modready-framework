// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

namespace Bannerlord.UIExtenderEx.Prefabs;

/// <summary>
/// Placement mode for a v1 <see cref="PrefabExtensionInsertPatch"/>. Differs
/// from <see cref="Prefabs2.InsertType"/> in shape; this is the older API.
/// </summary>
public enum InsertPatch
{
    /// <summary>Insert as the first child of the target.</summary>
    Prepend,
    /// <summary>Insert as the last child of the target.</summary>
    Append,
    /// <summary>Replace the target node entirely.</summary>
    Replace,
    /// <summary>Insert under the target as a child.</summary>
    Child,
}
