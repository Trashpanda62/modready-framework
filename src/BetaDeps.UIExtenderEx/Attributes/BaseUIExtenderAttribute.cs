// BetaDeps clean-room re-implementation of the Bannerlord.UIExtenderEx
// public API surface. Implementation original; copyright 2026 Maxfield
// Management Group; MIT licensed.
//
// The namespace and type names match the upstream public API so consumer
// mods compiled against the standard UIExtenderEx names resolve against
// this assembly transparently.

using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Marker base class for the BetaDeps UIExtenderEx attribute family
/// (PrefabExtension, ViewModelMixin, etc.). Consumer mods don't apply
/// this directly -- they apply one of the derived attributes.
/// </summary>
public abstract class BaseUIExtenderAttribute : Attribute { }
