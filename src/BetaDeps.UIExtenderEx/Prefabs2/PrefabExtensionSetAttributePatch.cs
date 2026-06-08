// BetaDeps clean-room. MIT, copyright 2026 Maxfield Management Group.
using System;
using System.Collections.Generic;

namespace Bannerlord.UIExtenderEx.Prefabs2;

/// <summary>
/// Patch that sets one or more attributes on a target XML node identified by xpath.
/// Consumer mods inherit this and supply (xpath, attributes) via the constructor.
/// </summary>
public abstract class PrefabExtensionSetAttributePatch
{
    public virtual IReadOnlyList<Attribute> Attributes { get; }
    protected PrefabExtensionSetAttributePatch() { Attributes = Array.Empty<Attribute>(); }
    protected PrefabExtensionSetAttributePatch(IReadOnlyList<Attribute> attributes)
    { Attributes = attributes ?? Array.Empty<Attribute>(); }

    public readonly struct Attribute
    {
        public string Name { get; }
        public string Value { get; }
        public Attribute(string name, string value) { Name = name ?? string.Empty; Value = value ?? string.Empty; }
    }
}
