// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ModReady.Foundation;

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

    // ---- UIExtenderEx 3.x binding-table surface --------------------------------
    // TaleWorlds resolves bindings through ViewModel._propertiesAndMethods, a
    // collection shared by every instance of the VM type. Additions copy it for
    // this instance first, so they never leak to other VMs of the same type.

    private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TableField = typeof(ViewModel).GetField("_propertiesAndMethods", InstanceAll);
    private static readonly FieldInfo? SharedCacheField = typeof(ViewModel).GetField("_cachedViewModelProperties", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    /// <summary>True when the VM binding table can be reached on this game build.</summary>
    public static bool TableIsReachable => TableField != null;

    public static void AddProperty(this ViewModel viewModel, string name, PropertyInfo propertyInfo)
    {
        var props = OwnTable(viewModel)?.Properties;
        if (props != null && !string.IsNullOrEmpty(name) && propertyInfo != null) props[name] = propertyInfo;
    }

    public static void AddMethod(this ViewModel viewModel, string name, MethodInfo methodInfo)
    {
        var methods = OwnTable(viewModel)?.Methods;
        if (methods != null && !string.IsNullOrEmpty(name) && methodInfo != null) methods[name] = methodInfo;
    }

    /// <summary>Batch several AddProperty/AddMethod calls against one VM.</summary>
    public static ViewModelBindingRegistration BeginRegistration(this ViewModel viewModel) => new(viewModel);

    public static PropertyInfo? SelectProperty(this ViewModel viewModel, string name)
    {
        var props = Table(viewModel)?.Properties;
        return props != null && name != null && props.TryGetValue(name, out var p) ? p : null;
    }

    public static MethodInfo? SelectMethod(this ViewModel viewModel, string name)
    {
        var methods = Table(viewModel)?.Methods;
        return methods != null && name != null && methods.TryGetValue(name, out var m) ? m : null;
    }

    public static IReadOnlyCollection<PropertyInfo> GetViewModelProperties(this ViewModel viewModel)
        => Table(viewModel)?.Properties?.Values.ToList() ?? new List<PropertyInfo>();

    public static IReadOnlyCollection<MethodInfo> GetViewModelMethods(this ViewModel viewModel)
        => Table(viewModel)?.Methods?.Values.ToList() ?? new List<MethodInfo>();

    public readonly struct ViewModelBindingRegistration : IDisposable
    {
        private readonly ViewModel _viewModel;
        internal ViewModelBindingRegistration(ViewModel viewModel) => _viewModel = viewModel;
        public void AddProperty(string name, PropertyInfo propertyInfo) => _viewModel.AddProperty(name, propertyInfo);
        public void AddMethod(string name, MethodInfo methodInfo) => _viewModel.AddMethod(name, methodInfo);
        public void Dispose() { }
    }

    private sealed class TableView
    {
        public Dictionary<string, PropertyInfo>? Properties;
        public Dictionary<string, MethodInfo>? Methods;
        public object? Raw;
    }

    private static TableView? Table(ViewModel? vm)
    {
        if (vm == null || TableField == null) return null;
        try
        {
            var raw = TableField.GetValue(vm);
            if (raw == null) return null;
            var t = raw.GetType();
            return new TableView
            {
                Raw = raw,
                Properties = t.GetProperty("Properties", InstanceAll)?.GetValue(raw) as Dictionary<string, PropertyInfo>,
                Methods = t.GetProperty("Methods", InstanceAll)?.GetValue(raw) as Dictionary<string, MethodInfo>,
            };
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Table", ex);
            return null;
        }
    }

    // The instance's table, copied off the per-type shared cache on first write.
    private static TableView? OwnTable(ViewModel? vm)
    {
        var view = Table(vm);
        if (view?.Raw == null || vm == null) return view;
        try
        {
            var shared = (SharedCacheField?.GetValue(null) as IDictionary)?[vm.GetType()];
            if (!ReferenceEquals(shared, view.Raw)) return view; // already this instance's own copy

            var props = new Dictionary<string, PropertyInfo>(view.Properties ?? new Dictionary<string, PropertyInfo>());
            var methods = new Dictionary<string, MethodInfo>(view.Methods ?? new Dictionary<string, MethodInfo>());
            var copy = Activator.CreateInstance(view.Raw.GetType(), InstanceAll, null, new object[] { props, methods }, null);
            TableField!.SetValue(vm, copy);
            return new TableView { Raw = copy, Properties = props, Methods = methods };
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OwnTable", ex);
            return view;
        }
    }
}
