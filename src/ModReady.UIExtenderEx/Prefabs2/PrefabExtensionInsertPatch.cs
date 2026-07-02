// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// PrefabExtensionInsertPatch is a partial class -- this file holds the base
// class declaration plus the nested PrefabExtensionContentAttribute family.
// Splitting them across files keeps each file under ~80 lines and lets the
// content attributes live near the patch base that consumes them.

using System;

namespace Bannerlord.UIExtenderEx.Prefabs2;

/// <summary>
/// Base class for a prefab patch that inserts (or replaces / removes) XML
/// at the target XPath location. The derived class must override
/// <see cref="Type"/> to specify the InsertType, and must declare one
/// property or method carrying a content attribute (e.g.
/// <see cref="PrefabExtensionXmlNodeAttribute"/>) that supplies the XML.
/// </summary>
public abstract partial class PrefabExtensionInsertPatch
{
    /// <summary>The placement mode relative to the XPath-selected target.</summary>
    public abstract InsertType Type { get; }

    /// <summary>
    /// Marker base for the content-attribute family. A derived patch class
    /// must apply exactly one of these to a property or method that returns
    /// the XML content to insert.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public abstract class PrefabExtensionContentAttribute : Attribute { }

    /// <summary>
    /// Variant of <see cref="PrefabExtensionContentAttribute"/> whose content
    /// is a single XML fragment (string / XmlDocument / XmlNode). When
    /// <see cref="RemoveRootNode"/> is true, the engine strips the fragment's
    /// outer element before inserting, which lets a single patch insert
    /// multiple sibling nodes.
    /// </summary>
    public abstract class PrefabExtensionSingleContentAttribute : PrefabExtensionContentAttribute
    {
        public bool RemoveRootNode { get; }
        protected PrefabExtensionSingleContentAttribute(bool removeRootNode)
        {
            RemoveRootNode = removeRootNode;
        }
    }

    /// <summary>Content member returns a file name (with or without .xml) of
    /// an XML file in the module's GUI folder.</summary>
    public sealed class PrefabExtensionFileNameAttribute : PrefabExtensionSingleContentAttribute
    {
        public PrefabExtensionFileNameAttribute() : base(false) { }
        public PrefabExtensionFileNameAttribute(bool removeRootNode) : base(removeRootNode) { }
    }

    /// <summary>Content member returns a string containing raw XML.</summary>
    public sealed class PrefabExtensionTextAttribute : PrefabExtensionSingleContentAttribute
    {
        public PrefabExtensionTextAttribute() : base(false) { }
        public PrefabExtensionTextAttribute(bool removeRootNode) : base(removeRootNode) { }
    }

    /// <summary>Content member returns an XmlDocument; its root element and
    /// children are inserted.</summary>
    public sealed class PrefabExtensionXmlDocumentAttribute : PrefabExtensionSingleContentAttribute
    {
        public PrefabExtensionXmlDocumentAttribute() : base(false) { }
        public PrefabExtensionXmlDocumentAttribute(bool removeRootNode) : base(removeRootNode) { }
    }

    /// <summary>Content member returns a single XmlNode; the node and its
    /// children are inserted.</summary>
    public sealed class PrefabExtensionXmlNodeAttribute : PrefabExtensionSingleContentAttribute
    {
        public PrefabExtensionXmlNodeAttribute() : base(false) { }
        public PrefabExtensionXmlNodeAttribute(bool removeRootNode) : base(removeRootNode) { }
    }

    /// <summary>Content member returns an IEnumerable of XmlNode; nodes are
    /// inserted in order at the target location.</summary>
    public sealed class PrefabExtensionXmlNodesAttribute : PrefabExtensionContentAttribute { }
}
