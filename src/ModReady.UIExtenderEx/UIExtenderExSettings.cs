// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

namespace Bannerlord.UIExtenderEx;

/// <summary>
/// UIExtenderEx runtime switches (3.x surface). ModReady keeps them in memory:
/// mods may read or set them, but ModReady's prefab pipeline does not compile
/// generated prefabs, so DisableGeneratedPrefabs has no further effect.
/// </summary>
public sealed class UIExtenderExSettings
{
    public static UIExtenderExSettings Instance { get; } = new();

    public bool DumpXML { get; set; }
    public bool DisableGeneratedPrefabs { get; set; }
}
