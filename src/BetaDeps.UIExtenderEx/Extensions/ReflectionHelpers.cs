// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Reflection helpers used by UIExtenderEx internals + sometimes by
// consumer mods that drive into ViewModel internals.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Bannerlord.UIExtenderEx.Extensions;

public static class ReflectionHelpers
{
    private const BindingFlags AllInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AllStatic   = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    /// Enumerate every property declared on <paramref name="t"/> and its base
    /// chain. Each property appears once even if shadowed.
    /// </summary>
    public static IEnumerable<PropertyInfo> AllPropertiesInTypeHierarchy(this Type? t)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
        {
            foreach (var p in cur.GetProperties(AllInstance | BindingFlags.DeclaredOnly))
            {
                if (seen.Add(p.Name)) yield return p;
            }
        }
    }

    /// <summary>
    /// Enumerate every method declared on <paramref name="t"/> and its base
    /// chain, deduplicated by name+param-signature.
    /// </summary>
    public static IEnumerable<MethodInfo> AllMethodsInTypeHierarchy(this Type? t)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
        {
            foreach (var m in cur.GetMethods(AllInstance | BindingFlags.DeclaredOnly))
            {
                var sig = m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")";
                if (seen.Add(sig)) yield return m;
            }
        }
    }

    /// <summary>Set a non-public instance field via reflection. Returns true on success.</summary>
    public static bool SetField(object? instance, string fieldName, object? value)
    {
        if (instance == null) return false;
        var f = instance.GetType().GetField(fieldName, AllInstance);
        if (f == null) return false;
        try { f.SetValue(instance, value); return true; }
        catch { return false; }
    }
}
