// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

using BetaDeps.Foundation;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Extensions;

public static class ViewModelExtensions
{
    private const string Tag = "ViewModelExtensions";

    /// <summary>
    /// Invoke <c>OnPropertyChanged</c> on the ViewModel for the named property
    /// even when the underlying setter isn't accessible. Used by mixin code
    /// that updates VM-side state and needs Gauntlet bindings to refresh.
    /// </summary>
    public static void NotifyPropertyChanged(this ViewModel? vm, string propertyName)
    {
        if (vm == null || string.IsNullOrEmpty(propertyName)) return;
        try
        {
            var m = typeof(ViewModel).GetMethod("OnPropertyChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(vm, new object[] { propertyName });
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"NotifyPropertyChanged({propertyName})", ex);
        }
    }

    /// <summary>
    /// Invoke <c>RefreshValues</c> on the ViewModel (if it has one) so the
    /// VM re-computes all of its bindings. Used after a mixin attachment
    /// adds new properties that Gauntlet should now see.
    /// </summary>
    public static void RefreshValues(this ViewModel? vm)
    {
        if (vm == null) return;
        try
        {
            var m = vm.GetType().GetMethod("RefreshValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(vm, null);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"RefreshValues", ex);
        }
    }
}
