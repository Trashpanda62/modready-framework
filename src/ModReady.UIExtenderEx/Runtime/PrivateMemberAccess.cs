// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Reflection access to non-public ViewModel members for the UIExtenderEx 3.x
// mixin surface (BaseViewModelMixin.GetPrivate/SetPrivate). Lookups walk the
// type hierarchy and are cached per (type, name).
using System;
using System.Collections.Concurrent;
using System.Reflection;
using ModReady.Foundation;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class PrivateMemberAccess
{
    private const string Tag = "UIExtenderEx.PrivateAccess";
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<(Type, string), MemberInfo?> Cache = new();

    public static MemberInfo? Find(Type type, string name) => Cache.GetOrAdd((type, name), key =>
    {
        for (var t = key.Item1; t != null && t != typeof(object); t = t.BaseType)
        {
            var f = t.GetField(key.Item2, All);
            if (f != null) return f;
            var p = t.GetProperty(key.Item2, All);
            if (p != null) return p;
        }
        return null;
    });

    public static object? Get(object? target, string name)
    {
        if (target == null || string.IsNullOrEmpty(name)) return null;
        try
        {
            return Find(target.GetType(), name) switch
            {
                FieldInfo f => f.GetValue(f.IsStatic ? null : target),
                PropertyInfo p => p.GetValue(target),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Get({target.GetType().Name}.{name})", ex);
            return null;
        }
    }

    public static void Set(object? target, string name, object? value)
    {
        if (target == null || string.IsNullOrEmpty(name)) return;
        try
        {
            switch (Find(target.GetType(), name))
            {
                case FieldInfo f: f.SetValue(f.IsStatic ? null : target, value); break;
                case PropertyInfo p when p.CanWrite: p.SetValue(target, value); break;
                default: DiagLog.Log(Tag, $"Set: no writable member '{name}' on {target.GetType().FullName}"); break;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Set({target.GetType().Name}.{name})", ex);
        }
    }
}
