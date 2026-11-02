// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
using System;
using System.Linq;
using Bannerlord.UIExtenderEx.Runtime;
using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.ViewModels;

/// <summary>
/// Look up the mixin instances attached to a ViewModel (UIExtenderEx 3.x
/// surface), e.g. from a Harmony patch on the VM that needs mixin state.
/// </summary>
public static class ViewModelMixins
{
    /// <summary>The attached mixin of type <typeparamref name="TMixin"/>, or null.</summary>
    public static TMixin? Get<TMixin>(ViewModel viewModel) where TMixin : IViewModelMixin
    {
        if (viewModel == null) return default;
        return ViewModelMixinHost.GetMixins(viewModel).OfType<TMixin>().FirstOrDefault();
    }

    /// <summary>Apply <paramref name="selector"/> to the attached mixin of type
    /// <typeparamref name="TMixin"/>; default when no such mixin is attached.</summary>
    public static TValue? Select<TMixin, TValue>(ViewModel viewModel, Func<TMixin, TValue> selector) where TMixin : IViewModelMixin
    {
        var mixin = Get<TMixin>(viewModel);
        return mixin == null || selector == null ? default : selector(mixin);
    }
}
