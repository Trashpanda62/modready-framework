// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Side table that tracks the mixin instances attached to each constructed
// target ViewModel. Keyed by VM via ConditionalWeakTable so attached
// mixins are eligible for GC when the VM goes away.
//
// Attachment timing (H12, Phase 2B): the Harmony postfix on the base
// ViewModel() constructor runs BEFORE any derived constructor body, so
// constructing mixins there hands them a half-initialized VM -- mixins that
// read VM state in their ctor get nulls, throw, and used to be permanently
// skipped. Instead, the ctor postfix only RECORDS the match (Register);
// the mixin instances are constructed lazily on first touch -- the first
// binding access, refresh, or GetMixins call -- which by definition happens
// after construction has completed. A mixin whose ctor still throws is
// skipped for that one VM instance only, not globally.
//
// Lifecycle: OnVmRefresh (driven by per-target Harmony postfixes installed
// at Enable time, see ViewModelMixinHook.InstallRefreshHooks) forwards to
// mixin.OnRefresh; OnVmFinalize (postfix on ViewModel.OnFinalize) forwards
// to mixin.OnFinalize and detaches, so event handlers the mixin hooked up
// are released per screen close instead of leaking.

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

    /// <summary>A mixin registration matched to a VM at construction time but
    /// not yet instantiated. Carries the registry so module-level Disable()
    /// between construction and first touch is still honored.</summary>
    private sealed class PendingMixin
    {
        public UIExtenderRegistry Registry { get; }
        public MixinRegistration Registration { get; }
        public PendingMixin(UIExtenderRegistry registry, MixinRegistration registration)
        {
            Registry = registry;
            Registration = registration;
        }
    }

    private sealed class AttachedMixin
    {
        public object Instance { get; }
        public MixinRegistration Registration { get; }
        public AttachedMixin(object instance, MixinRegistration registration)
        {
            Instance = instance;
            Registration = registration;
        }
    }

    /// <summary>Matched-but-not-constructed mixins, recorded by the base-ctor
    /// postfix. Weak keyed: a VM finalized before first touch just drops.</summary>
    private static readonly ConditionalWeakTable<ViewModel, List<PendingMixin>> _pending = new();

    /// <summary>Per-VM list of attached mixin instances. Weak keyed so
    /// GC works correctly when the VM is released.</summary>
    private static readonly ConditionalWeakTable<ViewModel, List<AttachedMixin>> _attached = new();

    // Diagnostic: log each unique VM type name we see once, when it looks
    // Option/Setting related. Lets us discover the right TargetTypeName for
    // mixins targeting the Options screen across different game versions.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _seenOptionLikeTypes
        = new(System.StringComparer.Ordinal);

    /// <summary>
    /// Record which registered mixins match this just-constructed VM. Called
    /// from the ViewModel constructor Harmony postfix. Does NOT construct the
    /// mixin instances -- see the header comment for why.
    /// </summary>
    public static void Register(ViewModel vm)
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

            List<PendingMixin>? matches = null;
            foreach (var registry in UIExtenderEngine.Enabled)
            {
                foreach (var mixinReg in registry.Mixins)
                {
                    if (registry.IsDisabled(mixinReg.MixinType)) continue;
                    if (!MixinMatches(mixinReg, vmType)) continue;
                    (matches ??= new List<PendingMixin>()).Add(new PendingMixin(registry, mixinReg));
                }
            }
            if (matches != null)
                _pending.Add(vm, matches);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Register({vm.GetType().FullName})", ex);
        }
    }

    /// <summary>
    /// First-touch flush: construct any pending mixins for this VM. Safe to
    /// call from multiple threads -- the pending entry is claimed by removal,
    /// so exactly one caller constructs.
    /// </summary>
    private static void EnsureAttached(ViewModel vm)
    {
        if (!_pending.TryGetValue(vm, out var pendings)) return;
        if (!_pending.Remove(vm)) return; // another thread claimed the flush
        try
        {
            var list = _attached.GetValue(vm, _ => new List<AttachedMixin>());
            int attachedCount = 0;
            foreach (var pending in pendings)
            {
                // Re-check disable state: Disable() may have run between the
                // VM's construction and this first touch.
                if (!pending.Registry.Enabled ||
                    pending.Registry.IsDisabled(pending.Registration.MixinType)) continue;

                object? instance = TryConstruct(pending.Registration.MixinType, vm);
                if (instance == null) continue; // logged; skipped for this VM only

                lock (list) { list.Add(new AttachedMixin(instance, pending.Registration)); }
                attachedCount++;
            }
            if (attachedCount > 0)
                DiagLog.Log(Tag, $"attached {attachedCount} mixin(s) to {vm.GetType().FullName}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"EnsureAttached({vm.GetType().FullName})", ex);
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
    /// or an empty array if none. Triggers the first-touch flush.</summary>
    public static IReadOnlyList<object> GetMixins(ViewModel vm)
    {
        if (vm == null) return Array.Empty<object>();
        EnsureAttached(vm);
        if (_attached.TryGetValue(vm, out var list))
        {
            lock (list) { return list.Select(am => am.Instance).ToArray(); }
        }
        return Array.Empty<object>();
    }

    /// <summary>
    /// Forward a refresh-method invocation on the VM to attached mixins.
    /// Only mixins whose declared refresh method (RefreshMethodName, default
    /// "RefreshValues") matches the invoked method are notified.
    /// </summary>
    public static void OnVmRefresh(ViewModel vm, MethodBase originalMethod)
    {
        if (vm == null || originalMethod == null) return;
        try
        {
            EnsureAttached(vm);
            if (!_attached.TryGetValue(vm, out var list)) return;
            AttachedMixin[] snapshot;
            lock (list) { snapshot = list.ToArray(); }
            foreach (var am in snapshot)
            {
                var wanted = am.Registration.Attribute.RefreshMethodName ?? "RefreshValues";
                if (!string.Equals(wanted, originalMethod.Name, StringComparison.Ordinal)) continue;
                InvokeLifecycle(am.Instance, "OnRefresh");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"OnVmRefresh({vm.GetType().FullName})", ex);
        }
    }

    /// <summary>
    /// VM is being finalized: notify mixins (OnFinalize) and detach them so
    /// any event handlers they hooked are released. Pending-but-untouched
    /// entries are simply dropped (their mixins were never constructed).
    /// </summary>
    public static void OnVmFinalize(ViewModel vm)
    {
        if (vm == null) return;
        try
        {
            _pending.Remove(vm);
            if (!_attached.TryGetValue(vm, out var list)) return;
            _attached.Remove(vm);
            AttachedMixin[] snapshot;
            lock (list) { snapshot = list.ToArray(); list.Clear(); }
            foreach (var am in snapshot)
            {
                InvokeLifecycle(am.Instance, "OnFinalize");
            }
            if (snapshot.Length > 0)
                DiagLog.Log(Tag, $"finalized + detached {snapshot.Length} mixin(s) from {vm.GetType().FullName}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"OnVmFinalize({vm.GetType().FullName})", ex);
        }
    }

    /// <summary>Invoke a no-arg lifecycle method (OnRefresh / OnFinalize) on a
    /// mixin instance. A throwing override is logged and contained -- one bad
    /// mixin must not break the VM's refresh/finalize, nor its sibling mixins.</summary>
    private static void InvokeLifecycle(object mixin, string methodName)
    {
        try
        {
            var m = mixin.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            m?.Invoke(mixin, null);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"{mixin.GetType().FullName}.{methodName}", ex);
        }
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
