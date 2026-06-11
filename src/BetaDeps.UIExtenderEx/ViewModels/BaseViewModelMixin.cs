// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

using System.Runtime.CompilerServices;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.ViewModels;

/// <summary>
/// Base class for ViewModel mixins. A mixin extends the data-source surface of
/// a TaleWorlds ViewModel without modifying the VM class itself -- the engine
/// attaches a mixin instance to each constructed target VM (matched on
/// <typeparamref name="TViewModel"/>) and routes Gauntlet bindings for any
/// [DataSourceProperty] / [DataSourceMethod] members on the mixin to that
/// attached instance.
///
/// Derived mixins should be marked with <see cref="Attributes.ViewModelMixinAttribute"/>.
/// </summary>
/// <typeparam name="TViewModel">The TaleWorlds ViewModel this mixin attaches to.</typeparam>
public abstract class BaseViewModelMixin<TViewModel> where TViewModel : ViewModel
{
    /// <summary>The target ViewModel instance this mixin is bound to.</summary>
    protected TViewModel ViewModel { get; }

    protected BaseViewModelMixin(TViewModel vm)
    {
        ViewModel = vm;
    }

    /// <summary>Called when the engine routes the target VM's named refresh
    /// method (set via <see cref="Attributes.ViewModelMixinAttribute.RefreshMethodName"/>)
    /// to the mixin. Override to update mixin-side state.</summary>
    public virtual void OnRefresh() { }

    /// <summary>Called when the target VM is being finalized so the mixin can
    /// release any resources it acquired.</summary>
    public virtual void OnFinalize() { }

    // Property-change helpers forwarding to the attached VM, matching the
    // upstream BUTR base-class surface. Consumer mixins (e.g. CDE's
    // CharacterAttributeItemVMMixin) call these from their constructors and
    // property setters; without them the type binds but every call throws
    // MissingMethodException at JIT time (found live 2026-06-11).
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => ViewModel?.OnPropertyChanged(propertyName);

    protected void OnPropertyChangedWithValue(object value, [CallerMemberName] string? propertyName = null)
        => ViewModel?.OnPropertyChangedWithValue(value, propertyName);

    protected void OnPropertyChangedWithValue<T>(T value, [CallerMemberName] string? propertyName = null) where T : struct
        => ViewModel?.OnPropertyChangedWithValue((object)value, propertyName);
}
