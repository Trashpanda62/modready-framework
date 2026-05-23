// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Pure-XML prefab patcher. No game refs; testable in isolation. Given a
// loaded XmlDocument and a list of PrefabRegistration entries matching the
// current movie, applies each patch by:
//
//   1. Locating the XPath target node (or operating on the root if the
//      patch has no XPath).
//   2. Reading the patch class's content member (file / text / XmlNode etc.)
//      to produce an XML fragment.
//   3. Inserting/replacing/removing the fragment at the target per the
//      patch's InsertType.
//
// Patches that fail to apply log a diagnostic line and are skipped --
// other patches on the same movie continue normally. Never throws out of
// ApplyAll.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

using BetaDeps.Foundation;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class PrefabPatcher
{
    private const string Tag = "PrefabPatcher";

    /// <summary>
    /// Apply every registered patch whose <see cref="PrefabExtensionAttribute.Movie"/>
    /// matches <paramref name="movieName"/>. Mutates <paramref name="doc"/> in place.
    /// </summary>
    public static int ApplyAll(string movieName, XmlDocument doc, IReadOnlyList<PrefabRegistration> patches)
    {
        if (doc == null || patches == null || patches.Count == 0) return 0;

        int applied = 0;
        foreach (var reg in patches)
        {
            if (!string.Equals(reg.Attribute.Movie, movieName, StringComparison.Ordinal))
                continue;
            try
            {
                if (ApplyOne(doc, reg)) applied++;
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"ApplyOne({movieName}, {reg.PatchType.FullName})", ex);
            }
        }
        if (applied > 0)
            DiagLog.Log(Tag, $"{movieName}: applied {applied} of {patches.Count} candidate patch(es)");
        return applied;
    }

    private static bool ApplyOne(XmlDocument doc, PrefabRegistration reg)
    {
        // Locate the target node. No XPath means operate on the prefab root.
        XmlNode? target;
        if (string.IsNullOrEmpty(reg.Attribute.XPath))
        {
            target = doc.DocumentElement;
        }
        else
        {
            target = doc.SelectSingleNode(reg.Attribute.XPath);
        }
        if (target == null)
        {
            DiagLog.Log(Tag, $"{reg.Attribute.Movie}: XPath '{reg.Attribute.XPath ?? "<root>"}' returned no node for {reg.PatchType.FullName}; skipped");
            return false;
        }

        // Distinguish the patch family by base class.
        if (typeof(PrefabExtensionSetAttributePatch).IsAssignableFrom(reg.PatchType))
        {
            return ApplySetAttribute(target, reg);
        }
        if (typeof(PrefabExtensionInsertPatch).IsAssignableFrom(reg.PatchType))
        {
            return ApplyInsert(doc, target, reg);
        }

        DiagLog.Log(Tag, $"{reg.PatchType.FullName}: unknown patch base; skipped");
        return false;
    }

    private static bool ApplySetAttribute(XmlNode target, PrefabRegistration reg)
    {
        var instance = Activator.CreateInstance(reg.PatchType);
        var attrProp = reg.PatchType.GetProperty("Attribute", BindingFlags.Public | BindingFlags.Instance);
        var valProp  = reg.PatchType.GetProperty("Value",     BindingFlags.Public | BindingFlags.Instance);
        var name  = attrProp?.GetValue(instance) as string;
        var value = valProp?.GetValue(instance) as string;
        if (string.IsNullOrEmpty(name))
        {
            DiagLog.Log(Tag, $"{reg.PatchType.FullName}: SetAttribute patch has empty Attribute name; skipped");
            return false;
        }
        if (target.Attributes == null) return false;
        var attr = target.OwnerDocument!.CreateAttribute(name!);
        attr.Value = value ?? string.Empty;
        target.Attributes.SetNamedItem(attr);
        return true;
    }

    private static bool ApplyInsert(XmlDocument doc, XmlNode target, PrefabRegistration reg)
    {
        var instance = Activator.CreateInstance(reg.PatchType);
        var typeProp = reg.PatchType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
        var insertType = (InsertType)typeProp!.GetValue(instance);

        // Locate the content member and produce the fragment(s) to insert.
        if (!TryResolveContent(instance, reg.PatchType, out var fragments, out var removeRootNode, out var contentDescription))
        {
            DiagLog.Log(Tag, $"{reg.PatchType.FullName}: could not resolve content member; skipped");
            return false;
        }

        // Import fragments into the destination document so they belong to it.
        var imported = fragments.Select(n => doc.ImportNode(n, deep: true)).ToList();

        // If the patch is a SingleContent variant with RemoveRootNode=true, replace
        // the imported root with its children.
        if (removeRootNode && imported.Count == 1 && imported[0] is XmlElement rootEl)
        {
            var childCopy = rootEl.ChildNodes.Cast<XmlNode>().ToList();
            imported = childCopy;
        }

        // Apply per InsertType.
        switch (insertType)
        {
            case InsertType.Remove:
                target.ParentNode?.RemoveChild(target);
                return true;

            case InsertType.Replace:
                foreach (var n in imported) target.ParentNode?.InsertBefore(n, target);
                target.ParentNode?.RemoveChild(target);
                return true;

            case InsertType.ReplaceKeepChildren:
                // Replace the element but keep the original children under the new root.
                if (imported.Count == 1 && imported[0] is XmlElement newRoot && target is XmlElement oldEl)
                {
                    var children = oldEl.ChildNodes.Cast<XmlNode>().ToList();
                    foreach (var c in children) newRoot.AppendChild(c);
                    target.ParentNode?.InsertBefore(newRoot, target);
                    target.ParentNode?.RemoveChild(target);
                    return true;
                }
                DiagLog.Log(Tag, $"{reg.PatchType.FullName}: ReplaceKeepChildren needs a single root element; skipped");
                return false;

            case InsertType.Prepend:
                {
                    var first = target.FirstChild;
                    foreach (var n in imported) target.InsertBefore(n, first);
                    return true;
                }

            case InsertType.Child:
                {
                    // Gauntlet widgets store their visible kids inside a
                    // <Children> element, not as direct children of the widget
                    // node itself. If the target has one, append into it; if
                    // not (rare; some leaf widgets), append directly to the
                    // target as a fallback. This matches what consumer mods
                    // and the upstream patcher expect when InsertType.Child
                    // is used against a widget node.
                    XmlNode container = target;
                    foreach (XmlNode c in target.ChildNodes)
                    {
                        if (c.NodeType == XmlNodeType.Element &&
                            string.Equals(c.LocalName, "Children", StringComparison.Ordinal))
                        {
                            container = c;
                            break;
                        }
                    }
                    foreach (var n in imported) container.AppendChild(n);
                    return true;
                }

            case InsertType.Append:
                {
                    var parent = target.ParentNode;
                    if (parent == null) return false;
                    var anchor = target.NextSibling;
                    foreach (var n in imported)
                    {
                        if (anchor != null) parent.InsertBefore(n, anchor);
                        else parent.AppendChild(n);
                    }
                    return true;
                }
        }
        return false;
    }

    /// <summary>
    /// Find the property or method on the patch class carrying a content
    /// attribute and convert its return value into a list of XmlNodes.
    /// </summary>
    private static bool TryResolveContent(
        object instance, Type patchType,
        out List<XmlNode> fragments,
        out bool removeRootNode,
        out string contentDescription)
    {
        fragments = new List<XmlNode>();
        removeRootNode = false;
        contentDescription = "";

        // Scan public and non-public instance members for a content attribute.
        var members = patchType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var member in members)
        {
            var attrs = member.GetCustomAttributes(inherit: false)
                .OfType<PrefabExtensionInsertPatch.PrefabExtensionContentAttribute>()
                .ToArray();
            if (attrs.Length == 0) continue;
            var attr = attrs[0];
            contentDescription = attr.GetType().Name + " on " + member.Name;
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionSingleContentAttribute sca)
                removeRootNode = sca.RemoveRootNode;

            // Pull the member's value.
            object? value = null;
            try
            {
                value = member is PropertyInfo p ? p.GetValue(instance)
                      : member is MethodInfo m ? m.Invoke(instance, null)
                      : null;
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"TryResolveContent({patchType.FullName}.{member.Name})", ex);
                return false;
            }
            if (value == null) return false;

            // Convert by content-attribute family.
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionFileNameAttribute)
            {
                if (value is string fileName) return TryLoadFromFile(patchType, fileName, fragments);
                return false;
            }
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionTextAttribute)
            {
                if (value is string text) return TryParseXml(text, fragments);
                return false;
            }
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionXmlDocumentAttribute)
            {
                if (value is XmlDocument xdoc && xdoc.DocumentElement != null)
                {
                    fragments.Add(xdoc.DocumentElement);
                    return true;
                }
                return false;
            }
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionXmlNodeAttribute)
            {
                if (value is XmlNode node)
                {
                    fragments.Add(node);
                    return true;
                }
                return false;
            }
            if (attr is PrefabExtensionInsertPatch.PrefabExtensionXmlNodesAttribute)
            {
                if (value is System.Collections.IEnumerable seq)
                {
                    foreach (var item in seq) if (item is XmlNode n) fragments.Add(n);
                    return fragments.Count > 0;
                }
                return false;
            }
            return false;
        }
        return false;
    }

    private static bool TryLoadFromFile(Type patchType, string fileName, List<XmlNode> fragments)
    {
        // Resolve relative to the patch type's assembly directory + Modules\<moduleName>\GUI\.
        // For Phase 2 first cut we look next to the patch DLL with a sibling GUI\ folder.
        try
        {
            var asmDir = Path.GetDirectoryName(patchType.Assembly.Location);
            if (string.IsNullOrEmpty(asmDir)) return false;
            // Walk up to Modules\<X>\ and look in GUI\Prefabs2\.
            var moduleDir = Path.GetDirectoryName(Path.GetDirectoryName(asmDir));
            if (string.IsNullOrEmpty(moduleDir)) return false;
            var withExt = fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".xml";
            var candidates = new[]
            {
                Path.Combine(moduleDir, "GUI", "Prefabs2", withExt),
                Path.Combine(moduleDir, "GUI", withExt),
                Path.Combine(moduleDir, withExt),
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    var doc = new XmlDocument();
                    doc.Load(path);
                    if (doc.DocumentElement != null)
                    {
                        fragments.Add(doc.DocumentElement);
                        return true;
                    }
                }
            }
            DiagLog.Log(Tag, $"{patchType.FullName}: prefab content file '{withExt}' not found in any of {candidates.Length} candidate paths");
            return false;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryLoadFromFile({patchType.FullName}, {fileName})", ex);
            return false;
        }
    }

    private static bool TryParseXml(string xmlText, List<XmlNode> fragments)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlText);
            if (doc.DocumentElement != null)
            {
                fragments.Add(doc.DocumentElement);
                return true;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "TryParseXml", ex);
        }
        return false;
    }
}
