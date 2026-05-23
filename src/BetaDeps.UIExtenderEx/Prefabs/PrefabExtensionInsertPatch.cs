// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Bannerlord.UIExtenderEx.Prefabs.PrefabExtensionInsertPatch (v1) -- predates
// the Prefabs2 partial-class shape. Consumer mods override Type +
// GetPrefabExtension(). The runtime engine renders the returned XmlDocument
// at the patch location.

using System.Xml;

namespace Bannerlord.UIExtenderEx.Prefabs;

public abstract class PrefabExtensionInsertPatch : IPrefabPatch
{
    /// <summary>Where to place the inserted content relative to the XPath target.</summary>
    public abstract InsertPatch Type { get; }

    /// <summary>
    /// Returns the XML to insert at the target. Called once per render.
    /// Implementations typically load a file or build the document in
    /// memory; the engine takes ownership of the returned document.
    /// </summary>
    public abstract XmlDocument GetPrefabExtension();
}
