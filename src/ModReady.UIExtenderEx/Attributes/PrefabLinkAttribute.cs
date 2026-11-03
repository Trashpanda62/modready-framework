// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Assembly-level link between a prefab (by patch type or prefab name), its
/// ViewModel and optional mixin (UIExtenderEx 3.x surface). Upstream uses it for
/// build-time analyzer validation only; it has no runtime effect.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PrefabLinkAttribute : Attribute
{
    public Type? Patch { get; }
    public string? Prefab { get; }
    public Type? ViewModel { get; }
    public Type? Mixin { get; }

    public PrefabLinkAttribute(Type patch, Type viewModel) { Patch = patch; ViewModel = viewModel; }
    public PrefabLinkAttribute(Type patch, Type viewModel, Type mixin) { Patch = patch; ViewModel = viewModel; Mixin = mixin; }
    public PrefabLinkAttribute(string prefab, Type viewModel) { Prefab = prefab; ViewModel = viewModel; }
    public PrefabLinkAttribute(string prefab, Type viewModel, Type mixin) { Prefab = prefab; ViewModel = viewModel; Mixin = mixin; }
}
