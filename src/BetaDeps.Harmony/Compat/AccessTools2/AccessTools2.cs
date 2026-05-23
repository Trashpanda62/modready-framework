// BetaDeps clean-room re-implementation of HarmonyLib.BUTR.Extensions.AccessTools2.
// MIT, copyright 2026 Maxfield Management Group.
//
// AccessTools2 is the BUTR community's signature-safe binding helper. Consumer
// mods compiled against the BUTR NuGet call methods like
//   AccessTools2.Method(typeof(SomeType), "SomeMethod")
//   AccessTools2.TypeByName("TaleWorlds.MountAndBlade.Mission")
// to look up methods/types defensively. Same surface as Harmony's AccessTools
// but with null returns on miss instead of throwing -- so patch sites can
// pattern-match on null and skip cleanly.
//
// Behavior matches BetaDeps.Harmony.SafeBind under the hood; the public
// surface is reproduced here because consumer mods reference the
// HarmonyLib.BUTR.Extensions.AccessTools2 type by name.

using System;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

namespace HarmonyLib.BUTR.Extensions;

public static class AccessTools2
{
    private const string Tag = "AccessTools2";

    /// <summary>
    /// Look up a type by its assembly-qualified or full name. Walks every
    /// loaded assembly. Returns null if no match is found.
    /// </summary>
    public static Type? TypeByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            var t = Type.GetType(name, throwOnError: false);
            if (t != null) return t;
        }
        catch { }
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(name, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Look up a method by type + name. Returns null on miss or ambiguity.</summary>
    public static MethodInfo? Method(Type? type, string name, Type[]? parameters = null, Type[]? generics = null)
    {
        if (type == null || string.IsNullOrEmpty(name)) return null;
        try
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            MethodInfo? found = null;
            if (parameters != null)
            {
                found = type.GetMethod(name, flags, binder: null, types: parameters, modifiers: null);
            }
            else
            {
                var all = type.GetMethods(flags).Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).ToArray();
                if (all.Length == 1) found = all[0];
                // Ambiguous -- caller must specify parameter types.
            }
            if (found != null && generics != null && found.IsGenericMethodDefinition)
                found = found.MakeGenericMethod(generics);
            return found;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Method({type.FullName}::{name})", ex);
            return null;
        }
    }

    /// <summary>Convenience overload: look up by type-name string + method name.</summary>
    public static MethodInfo? Method(string typeName, string methodName, Type[]? parameters = null, Type[]? generics = null)
        => Method(TypeByName(typeName), methodName, parameters, generics);

    /// <summary>Look up a constructor by parameter types. Returns null on miss.</summary>
    public static ConstructorInfo? Constructor(Type? type, Type[]? parameters = null, bool searchForStatic = false)
    {
        if (type == null) return null;
        try
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | (searchForStatic ? BindingFlags.Static : BindingFlags.Instance);
            if (parameters != null)
                return type.GetConstructor(flags, binder: null, types: parameters, modifiers: null);
            var all = type.GetConstructors(flags);
            return all.Length == 1 ? all[0] : null;
        }
        catch { return null; }
    }

    /// <summary>Look up a property by type + name. Returns null on miss.</summary>
    public static PropertyInfo? Property(Type? type, string name)
    {
        if (type == null || string.IsNullOrEmpty(name)) return null;
        try
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }
        catch { return null; }
    }

    /// <summary>Look up a field by type + name. Returns null on miss.</summary>
    public static FieldInfo? Field(Type? type, string name)
    {
        if (type == null || string.IsNullOrEmpty(name)) return null;
        try
        {
            return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }
        catch { return null; }
    }

    /// <summary>Look up a property getter. Returns null on miss.</summary>
    public static MethodInfo? PropertyGetter(Type? type, string name)
        => Property(type, name)?.GetGetMethod(nonPublic: true);

    /// <summary>Look up a property setter. Returns null on miss.</summary>
    public static MethodInfo? PropertySetter(Type? type, string name)
        => Property(type, name)?.GetSetMethod(nonPublic: true);

    /// <summary>Get an instance field's value via reflection. Returns default on miss.</summary>
    public static T? GetFieldValue<T>(object? instance, string name)
    {
        if (instance == null) return default;
        var f = Field(instance.GetType(), name);
        if (f == null) return default;
        try { return (T?)f.GetValue(instance); }
        catch { return default; }
    }

    /// <summary>Set an instance field's value via reflection. Returns true on success.</summary>
    public static bool SetFieldValue(object? instance, string name, object? value)
    {
        if (instance == null) return false;
        var f = Field(instance.GetType(), name);
        if (f == null) return false;
        try { f.SetValue(instance, value); return true; }
        catch { return false; }
    }
}
