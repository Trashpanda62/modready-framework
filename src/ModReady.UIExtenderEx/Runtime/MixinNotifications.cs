// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// UIExtenderEx 3.x mixin notifications. When a mixin overrides
// BaseViewModelMixin.OnViewModelPropertyChanged, subscribe it to every
// property-change event the target VM raises (PropertyChanged plus the typed
// PropertyChangedWith*Value events, which TaleWorlds raises separately).
// Mixins that don't override the hook pay nothing.
//
// Also logs, once per mixin type, any [BUTRUnsafeAccessor] method: ModReady
// does not bind those yet, so a mod relying on one is named in runtime.log.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Bannerlord.UIExtenderEx.Attributes;
using ModReady.Foundation;
using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class MixinNotifications
{
    private const string Tag = "UIExtenderEx.MixinNotifications";

    private static readonly ConcurrentDictionary<Type, MethodInfo?> HookCache = new();
    private static readonly ConcurrentDictionary<Type, byte> AccessorWarned = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> ArgsNameCache = new();

    private static readonly MethodInfo ForwardMethod =
        typeof(Forwarder).GetMethod(nameof(Forwarder.On), BindingFlags.Public | BindingFlags.Instance)!;

    public static void Attach(ViewModel vm, object mixin)
    {
        try
        {
            WarnUnsupportedAccessors(mixin.GetType());

            var hook = HookCache.GetOrAdd(mixin.GetType(), FindOverride);
            if (hook == null) return;

            var forwarder = new Forwarder(mixin, hook);
            foreach (var ev in typeof(ViewModel).GetEvents(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!ev.Name.StartsWith("PropertyChanged", StringComparison.Ordinal)) continue;
                var handlerType = ev.EventHandlerType;
                var invoke = handlerType?.GetMethod("Invoke");
                var ps = invoke?.GetParameters();
                if (handlerType == null || ps == null || ps.Length != 2) continue;
                var bound = ForwardMethod.MakeGenericMethod(ps[1].ParameterType);
                ev.AddEventHandler(vm, Delegate.CreateDelegate(handlerType, forwarder, bound));
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Attach({mixin.GetType().FullName})", ex);
        }
    }

    // The mixin's own override of OnViewModelPropertyChanged(string), if any.
    private static MethodInfo? FindOverride(Type mixinType)
    {
        var m = mixinType.GetMethod("OnViewModelPropertyChanged", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(string) }, null);
        if (m == null) return null;
        var declaring = m.DeclaringType;
        return declaring != null && declaring.IsGenericType &&
               declaring.GetGenericTypeDefinition() == typeof(ViewModels.BaseViewModelMixin<>)
            ? null
            : m;
    }

    private static void WarnUnsupportedAccessors(Type mixinType)
    {
        if (!AccessorWarned.TryAdd(mixinType, 0)) return;
        var accessors = mixinType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<BUTRUnsafeAccessorAttribute>(inherit: false) != null)
            .Select(m => m.Name)
            .ToArray();
        if (accessors.Length > 0)
            DiagLog.Log(Tag, $"{mixinType.FullName} ({mixinType.Assembly.GetName().Name}) declares [BUTRUnsafeAccessor] methods ModReady does not bind yet: {string.Join(", ", accessors)}");
    }

    private static string? PropertyNameOf(object? args)
    {
        if (args == null) return null;
        var p = ArgsNameCache.GetOrAdd(args.GetType(), t => t.GetProperty("PropertyName", BindingFlags.Public | BindingFlags.Instance));
        return p?.GetValue(args) as string;
    }

    private sealed class Forwarder
    {
        private readonly object _mixin;
        private readonly MethodInfo _hook;

        public Forwarder(object mixin, MethodInfo hook)
        {
            _mixin = mixin;
            _hook = hook;
        }

        public void On<TArgs>(object sender, TArgs args)
        {
            try
            {
                var name = PropertyNameOf(args);
                if (name != null) _hook.Invoke(_mixin, new object[] { name });
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"{_mixin.GetType().Name}.OnViewModelPropertyChanged", ex);
            }
        }
    }
}
