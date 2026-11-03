// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

namespace Bannerlord.UIExtenderEx.ViewModels;

/// <summary>Non-generic view of a ViewModel mixin (UIExtenderEx 3.x surface).</summary>
public interface IViewModelMixin
{
    void OnRefresh();
    void OnFinalize();
}
