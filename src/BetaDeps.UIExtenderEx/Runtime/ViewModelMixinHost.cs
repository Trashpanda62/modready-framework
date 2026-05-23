// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Side table that tracks the mixin instances attached to each constructed
// target ViewModel. Keyed by VM via ConditionalWeakTable so attached
// mixins are eligible for GC when the VM goes away.
//
// The attachment logic in ViewModelMixinHook calls Attach() from a Harmony
// postfix on each target VM constructor; consumers of the mixin's
// [DataSourceMethod] / [DataSourceProperty] members can look them up via
// GetMixins(vm) once binding integration lands.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;

using BetaDeps.Foundation;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class ViewModelMixinHost
{
    private const string Tag = "ViewModelMixinHost";

    /// <summary>Per-VM list of attached mixin instances. Weak keyed so
    /// GC works correctly when the VM is released.</summary>
    private static readonly ConditionalWeakTable<ViewModel, List<object>> _attached = new();

    /// <summary>
    /// Construct and attach mixin instances matching the target VM. Called
    /// from the ViewModel constructor Harmony postfix.
    /// </summary>
    // Diagnostic: log each unique VM type name we see once, when it looks
    // Option/Setting related. Lets us discover the right TargetTypeName for
    // mixins targeting the Options screen across different game versions.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenOptionLikeTypes
        = new(System.StringComparer.Ordinal);

    public static void Attach(ViewModel vm)
    {
        if (vm == null) return;
        try
        {
            var vmType = vm.GetType();

            // Diagnostic: surface Options/Settings VM type names once each.
            var fn = vmType.FullName ?? string.Empty;
            if ((fn.IndexOf("Option", System.StringComparison.Ordinal) >= 0 ||
                 fn.IndexOf("Setting", System.StringComparison.Ordinal) >= 0) &&
                _seenOptionLikeTypes.TryAdd(fn, 0))
            {
                DiagLog.Log(Tag, $"DIAG: Option/Setting VM constructed: {fn}");
            }

            var registries = UIExtenderEngine.Enabled;
            int attachedCount = 0;
            foreach (var registry in registries)
            {
                foreach (var mixinReg in registry.Mixins)
                {
                    if (!MixinMatches(mixinReg, vmType)) continue;

                    object? instance = TryConstruct(mixinReg.MixinType, vm);
                    if (instance == null) continue;

                    var list = _attached.GetValue(vm, _ => new List<object>());
                    lock (list) { list.Add(instance); }
                    attachedCount++;
                }
            }
            if (attachedCount > 0)
                DiagLog.Log(Tag, $"attached {attachedCount} mixin(s) to {vmType.FullName}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Attach({vm.GetType().FullName})", ex);
        }
    }

    /// <summary>
    /// Decide whether a registered mixin should attach to a VM of type
    /// <paramref name="vmType"/>. The match logic is layered:
    ///   1. If <c>TargetTypeName</c> is set on the attribute, require the
    ///      VM's runtime type name to match it (with HandleDerived allowing
    ///      descendants). The generic argument of BaseViewModelMixin&lt;T&gt;
    ///      is ignored when TargetTypeName is set.
    ///   2. Otherwise, fall back to the generic argument matching. Guard
    ///      against the "match everything" case (generic argument is the
    ///      base ViewModel type + HandleDerived=true) by refusing it.
    /// </summary>
    private static bool MixinMatches(MixinRegistration mixinReg, Type vmType)
    {
        // 1. Name-based target.
        if (!string.IsNullOrEmpty(mixinReg.Attribute.TargetTypeName))
        {
            var target = mixinReg.Attribute.TargetTypeName!;
            if (mixinReg.Attribute.HandleDerived)
            {
                for (var t = vmType; t != null && t != typeof(object); t = t.BaseType)
                {
                    if (string.Equals(t.FullName, target, StringComparison.Ordinal)) return true;
                }
                return false;
            }
            return string.Equals(vmType.FullName, target, StringComparison.Ordinal);
        }

        // 2. Generic-argument target. Guard against match-all -- a mixin
        // declared as BaseViewModelMixin<ViewModel> with HandleDerived=true
        // would otherwise match every VM in the game. That's almost certainly
        // not intended; treat it as a misconfiguration and skip.
        if (mixinReg.TargetViewModelType == typeof(ViewModel))
        {
            return false;
        }

        return mixinReg.Attribute.HandleDerived
            ? mixinReg.TargetViewModelType.IsAssignableFrom(vmType)
            : mixinReg.TargetViewModelType == vmType;
    }

    /// <summary>Returns every mixin instance attached to <paramref name="vm"/>,
    /// or an empty array if none.</summary>
    public static IReadOnlyList<object> GetMixins(ViewModel vm)
    {
        if (vm == null) return Array.Empty<object>();
        if (_attached.TryGetValue(vm, out var list))
        {
            lock (list) { return list.ToArray(); }
        }
        return Array.Empty<object>();
    }

    /// <summary>Find a [DataSourceMethod]-tagged method on any attached mixin
    /// matching the given name. Used by future binding integration.</summary>
    public static MethodInfo? FindDataSourceMethod(ViewModel vm, string methodName)
    {
        foreach (var mixin in GetMixins(vm))
        {
            var m = mixin.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(mi =>
                    string.Equals(mi.Name, methodName, StringComparison.Ordinal) &&
                    mi.GetCustomAttribute<DataSourceMethodAttribute>() != null);
            if (m != null) return m;
        }
        return null;
    }

    private static object? TryConstruct(Type mixinType, ViewModel vm)
    {
        try
        {
            // Mixin must derive from BaseViewModelMixin<T> -- single-arg
            // constructor taking T (the target VM).
            var ctor = mixinType
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(vm.GetType());
                });
            if (ctor == null)
            {
                DiagLog.Log(Tag, $"{mixinType.FullName}: no single-arg ctor accepting {vm.GetType().Name}; mixin skipped");
                return null;
            }
            return ctor.Invoke(new object[] { vm });
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryConstruct({mixinType.FullName})", ex);
            return null;
        }
    }
}
