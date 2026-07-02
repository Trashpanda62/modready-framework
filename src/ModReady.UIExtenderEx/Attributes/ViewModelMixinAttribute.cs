// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Marks a class as a ViewModel mixin. The class should extend
/// <see cref="ViewModels.BaseViewModelMixin{TVM}"/> for the target ViewModel
/// type. At runtime, an instance of the mixin is attached to each constructed
/// target VM, and its [DataSourceProperty]/[DataSourceMethod] members are
/// exposed to Gauntlet bindings as if they were declared on the VM itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ViewModelMixinAttribute : BaseUIExtenderAttribute
{
    /// <summary>If set, the engine will invoke this method on the mixin
    /// whenever the named refresh method runs on the target VM.</summary>
    public string? RefreshMethodName { get; set; }

    /// <summary>When true, the mixin is attached to derived classes of the
    /// target VM as well as the exact type.</summary>
    public bool HandleDerived { get; set; }

    /// <summary>
    /// Optional fully-qualified type name of the target VM. When set, this
    /// narrows the match beyond the generic argument: the mixin only attaches
    /// to VMs whose runtime type name matches (or, with HandleDerived, derives
    /// from a type with that name). Useful when the target VM lives in an
    /// assembly the mixin can't reference at compile time.
    /// </summary>
    public string? TargetTypeName { get; set; }

    public ViewModelMixinAttribute() { }

    public ViewModelMixinAttribute(string refreshMethodName)
    {
        RefreshMethodName = refreshMethodName;
    }

    public ViewModelMixinAttribute(bool handleDerived)
    {
        HandleDerived = handleDerived;
    }

    public ViewModelMixinAttribute(string? refreshMethodName, bool handleDerived)
    {
        RefreshMethodName = refreshMethodName;
        HandleDerived = handleDerived;
    }
}
