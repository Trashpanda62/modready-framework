// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ViewModelBindingPatch is the binding-integration layer (task #8). Without
// it, mixin [DataSourceProperty] / [DataSourceMethod] members are unreachable
// from Gauntlet XML bindings like @MixinProperty because Bannerlord's VM
// lookup only consults the VM's own members.
//
// This patch installs Harmony hooks on TaleWorlds.Library.ViewModel:
//   - GetPropertyValue(string) postfix    -> mixin property read fallback
//   - GetPropertyType(string)  postfix    -> mixin property type fallback
//   - SetPropertyValue(string, object) prefix -> mixin property write
//   - ExecuteCommand(string, object[]) prefix -> mixin [DataSourceMethod] call
//
// Each hook first lets the VM's own logic run / consults the VM's own
// members; only if the VM doesn't have the requested name does the hook
// fall through to attached mixins via ViewModelMixinHost.GetMixins.
//
// SafeBind-style binding: each Harmony Patch call wraps in try/catch so
// signature drift on a future game version logs and skips that hook rather
// than CTD'ing the process.

using System;
using System.Threading;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;

using BetaDeps.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class ViewModelBindingPatch
{
    private const string Tag = "ViewModelBindingPatch";
    private const string HarmonyId = "betadeps.uiextenderex.vmbinding";

    private static int _installed;

    public static void Install()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var harmony = new HarmonyLib.Harmony(HarmonyId);
            var vm = typeof(ViewModel);

            TryPatch(harmony, vm, "GetPropertyValue", new[] { typeof(string) },
                postfix: nameof(GetPropertyValuePostfix));
            TryPatch(harmony, vm, "GetPropertyType", new[] { typeof(string) },
                postfix: nameof(GetPropertyTypePostfix));
            TryPatch(harmony, vm, "SetPropertyValue", new[] { typeof(string), typeof(object) },
                prefix: nameof(SetPropertyValuePrefix));
            TryPatch(harmony, vm, "ExecuteCommand", new[] { typeof(string), typeof(object[]) },
                prefix: nameof(ExecuteCommandPrefix));

            // slice 4c (2026-06-04): {X} binding-PATH resolution (item-source
            // DataSource="{RowList}", child contexts DataSource="{ChildVM}") goes
            // through ViewModel.GetViewModelAtPath, NOT GetPropertyValue. That
            // method resolves each path node against a per-concrete-type registry
            // (DataSourceTypeBindingPropertiesCollection: Dictionary<string,
            // PropertyInfo>) built by reflection over the VM type's own
            // [DataSourceProperty] members -- so mixin-hosted properties are
            // invisible to {} bindings (confirmed by decompiling
            // TaleWorlds.Library.ViewModel + GauntletUI.Data.GauntletView).
            // Registry-injection is impossible because the descriptor is a raw
            // PropertyInfo whose GetValue targets the VM instance, not the mixin.
            // So we postfix GetViewModelAtPath the same way we postfix
            // GetPropertyValue: only when the native lookup returns null, walk the
            // BindingPath node-by-node, resolving each node against the VM's own
            // properties first and then any attached mixin. No-op for every native
            // binding (they return non-null), so blast radius is the same as the
            // existing GetPropertyValue/Type postfixes. Patch both overloads --
            // the 1-arg IViewModel method GauntletMovie calls and the 2-arg impl.
            TryPatch(harmony, vm, "GetViewModelAtPath", new[] { typeof(BindingPath) },
                postfix: nameof(GetViewModelAtPathPostfix));
            TryPatch(harmony, vm, "GetViewModelAtPath", new[] { typeof(BindingPath), typeof(bool) },
                postfix: nameof(GetViewModelAtPathPostfix));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    private static void TryPatch(HarmonyLib.Harmony harmony, Type t, string methodName, Type[] paramTypes,
                                 string? prefix = null, string? postfix = null)
    {
        try
        {
            var target = AccessTools2.Method(t, methodName, paramTypes);
            if (target == null)
            {
                DiagLog.Log(Tag, $"skip {t.Name}.{methodName}({string.Join(",", paramTypes.Select(p => p.Name))}) -- method not found");
                return;
            }
            var prefixHm = prefix != null ? new HarmonyMethod(AccessTools2.Method(typeof(ViewModelBindingPatch), prefix)!) : null;
            var postfixHm = postfix != null ? new HarmonyMethod(AccessTools2.Method(typeof(ViewModelBindingPatch), postfix)!) : null;
            harmony.Patch(target, prefix: prefixHm, postfix: postfixHm);
            DiagLog.Log(Tag, $"installed {(prefix != null ? "prefix" : "postfix")} on {t.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryPatch({methodName})", ex);
        }
    }

    // ---- patch bodies ----

    /// <summary>
    /// Postfix: if the VM's GetPropertyValue returned null (no such member),
    /// check every attached mixin for a property with that name and return
    /// the mixin's value instead.
    /// </summary>
    public static void GetPropertyValuePostfix(ViewModel __instance, string name, ref object? __result)
    {
        if (__result != null) return;
        if (__instance == null || string.IsNullOrEmpty(name)) return;
        try
        {
            foreach (var mixin in ViewModelMixinHost.GetMixins(__instance))
            {
                var p = FindMixinProperty(mixin, name);
                if (p != null)
                {
                    __result = p.GetValue(mixin);
                    if (DiagLog.VerboseBinding && __result is System.Collections.ICollection coll)
                        DiagLog.Log(Tag, $"GetPropertyValue('{name}') -> {__result.GetType().Name} count={coll.Count} (VM={__instance.GetType().Name})");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetPropertyValuePostfix({name})", ex);
        }
    }

    /// <summary>
    /// Postfix: same fallback for GetPropertyType, so binding-table builders
    /// learn the right type for mixin properties.
    /// </summary>
    public static void GetPropertyTypePostfix(ViewModel __instance, string name, ref Type? __result)
    {
        if (__result != null) return;
        if (__instance == null || string.IsNullOrEmpty(name)) return;
        try
        {
            foreach (var mixin in ViewModelMixinHost.GetMixins(__instance))
            {
                var p = FindMixinProperty(mixin, name);
                if (p != null)
                {
                    __result = p.PropertyType;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetPropertyTypePostfix({name})", ex);
        }
    }

    /// <summary>
    /// Prefix: if the VM doesn't declare a property with this name, route the
    /// set call to a matching mixin property and skip the VM's own logic.
    /// </summary>
    public static bool SetPropertyValuePrefix(ViewModel __instance, string name, object? value)
    {
        if (__instance == null || string.IsNullOrEmpty(name)) return true;
        // v0.6 perf: previously logged every Slot/Next/Prev/Selected/BetaDeps
        // SetPropertyValue. Every Options refresh writes ~60 lines (10 slots
        // × ~5+ bindings each), which obscures real signal during slider /
        // dropdown debugging. Now gated behind DiagLog.VerboseBinding so the
        // default ship-build is quiet; set the flag to true when bisecting
        // a binding bug. (The earlier ExecuteCommand hover-event filter is
        // still in place below — see ExecuteCommandPrefix.)
        if (DiagLog.VerboseBinding &&
            (name.StartsWith("Next", StringComparison.Ordinal) ||
             name.StartsWith("Prev", StringComparison.Ordinal) ||
             name.StartsWith("BetaDeps", StringComparison.Ordinal) ||
             name.StartsWith("Slot", StringComparison.Ordinal) ||
             name.StartsWith("Selected", StringComparison.Ordinal)))
        {
            DiagLog.Log(Tag, $"SetPropertyValue({name}) <- {value} (VM={__instance.GetType().Name})");
        }
        try
        {
            // If the VM has the property, let the original handle it.
            var vmProp = __instance.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (vmProp != null) return true;

            foreach (var mixin in ViewModelMixinHost.GetMixins(__instance))
            {
                var p = FindMixinProperty(mixin, name);
                if (p != null && p.CanWrite)
                {
                    p.SetValue(mixin, value);
                    return false; // skip original; we handled it
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"SetPropertyValuePrefix({name})", ex);
        }
        return true; // not found anywhere; let original handle (likely no-op)
    }

    /// <summary>
    /// Prefix: route XML command bindings (button clicks etc.) to a mixin
    /// [DataSourceMethod] when the VM doesn't have a matching method.
    /// </summary>
    public static bool ExecuteCommandPrefix(ViewModel __instance, string commandName, object[]? parameters)
    {
        if (__instance == null || string.IsNullOrEmpty(commandName)) return true;
        // v0.5.0 perf: hover-event commands (ExecuteBeginHint/EndHint) fire on
        // every mouse-over the user does — they fill the log with thousands of
        // lines of noise per Mod Config session and obscure real failures.
        // Log only non-hover commands by default. (Set DiagLog level higher if
        // you need the full firehose for slider/dropdown debugging.)
        if (commandName != "ExecuteBeginHint" && commandName != "ExecuteEndHint")
            DiagLog.Log(Tag, $"ExecuteCommand({commandName}) (VM={__instance.GetType().Name})");
        try
        {
            // If the VM declares a method with this name, let the original run.
            var vmMethod = __instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => string.Equals(m.Name, commandName, StringComparison.Ordinal));
            if (vmMethod != null) return true;

            // Try each attached mixin for a [DataSourceMethod] with matching name.
            foreach (var mixin in ViewModelMixinHost.GetMixins(__instance))
            {
                var m = mixin.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(mi =>
                        string.Equals(mi.Name, commandName, StringComparison.Ordinal) &&
                        mi.GetCustomAttribute<DataSourceMethodAttribute>(inherit: true) != null);
                if (m != null)
                {
                    var args = parameters ?? Array.Empty<object>();
                    m.Invoke(mixin, args.Length == m.GetParameters().Length ? args : Array.Empty<object>());
                    return false; // skip original
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ExecuteCommandPrefix({commandName})", ex);
        }
        return true; // not found; let original run (no-op or error path)
    }

    /// <summary>
    /// Postfix shared by both GetViewModelAtPath overloads. When the native
    /// path walk returns null (the path hit a node the per-type binding registry
    /// doesn't know -- i.e. a mixin-hosted property), re-walk the BindingPath
    /// resolving each node against the current object's own properties first and
    /// then any attached mixin, and return the final object. This is what makes
    /// {RowList} / {ChildVM} binding-path resolution see mixin members, mirroring
    /// how GetPropertyValuePostfix makes @X value bindings see them.
    /// __0 is the BindingPath arg (positional so it binds regardless of the
    /// original parameter name); the 2-arg overload's bool is ignored.
    /// </summary>
    public static void GetViewModelAtPathPostfix(ViewModel __instance, BindingPath __0, ref object? __result)
    {
        if (__result != null) return;
        if (__instance == null || __0 == null) return;
        try
        {
            // Fast no-op for the entire native game UI: only a VM with one of our
            // mixins attached can carry a non-native path node, and
            // GetViewModelAtPath is always invoked on the root VM the path is
            // relative to. If the root has no mixin, there is nothing we could add
            // -- bail before any reflection so the postfix costs ~one lookup on
            // every other screen in the game.
            bool hasMixin = false;
            foreach (var _m in ViewModelMixinHost.GetMixins(__instance)) { hasMixin = true; break; }
            if (!hasMixin) return;

            var nodes = __0.Nodes; // string[]
            // Native GetViewModelAtPath uses BindingPath.SubPath, which DROPS the
            // first node -- node[0] is the root/anchor reference (e.g. "Root"),
            // never resolved as a property. Resolution starts at node[1] against
            // 'this' (__instance, the VM the call was made on). Mirror that: we
            // need an anchor + at least one real node. A length-1 path resolves
            // to 'this' natively, so there's nothing for us to add there.
            if (nodes == null || nodes.Length < 2) return;

            // Verbose binding trace (off in ship builds; flip DiagLog.Verbose
            // Binding to debug a future {} mixin-path binding).
            bool trace = DiagLog.VerboseBinding;
            if (trace)
                DiagLog.Log(Tag, $"GVMAP path='{__0.Path}' nodes=[{string.Join(",", nodes)}] anchor='{nodes[0]}' VM={__instance.GetType().Name}");

            object? current = __instance; // anchor node[0] == this; skip it
            for (int i = 1; i < nodes.Length; i++)
            {
                if (current == null) return;
                var next = ResolvePathNode(current, nodes[i]);
                if (trace) DiagLog.Log(Tag, $"  node '{nodes[i]}' on {current.GetType().Name} -> {next?.GetType().Name ?? "null"}");
                if (next == null) return; // unresolved -> leave __result null (native behavior)
                current = next;
            }
            __result = current;
            if (trace)
                DiagLog.Log(Tag, $"GVMAP resolved '{__0.Path}' -> {current?.GetType().Name ?? "null"}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "GetViewModelAtPathPostfix", ex);
        }
    }

    /// <summary>
    /// Resolve a single binding-path node off <paramref name="current"/>: try the
    /// object's own public/non-public instance property first (native [DataSource
    /// Property] members), then, if current is a ViewModel, any attached mixin's
    /// matching property. Returns the node's value or null if unresolved.
    /// </summary>
    private static object? ResolvePathNode(object current, string node)
    {
        if (current == null || string.IsNullOrEmpty(node)) return null;

        // 0. Numeric node = list index. The ItemTemplate iterator resolves each
        // row via path ...\RowList\<i>, so once 'current' is the bound list the
        // next node is an integer index, NOT a property name. Native handles this
        // via GetChildAtPath; we index the list (IList or an int indexer) and
        // return the element (a real PresentationRowVM, whose own @bindings then
        // resolve natively). Out-of-range returns null, which ends iteration.
        if (char.IsDigit(node[0]) && int.TryParse(node, out int idx))
        {
            var byIndex = TryGetByIndex(current, idx);
            if (byIndex != null) return byIndex;
            // not an indexable / out of range -> fall through (rare: a property
            // whose name is digits) so we don't mask a legitimate lookup.
        }

        // 1. Native property on the concrete type.
        var pi = current.GetType().GetProperty(node,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (pi != null)
        {
            var v = pi.GetValue(current);
            if (v != null) return v;
        }
        // 2. Mixins attached to this VM.
        if (current is ViewModel vm)
        {
            foreach (var mixin in ViewModelMixinHost.GetMixins(vm))
            {
                var mp = FindMixinProperty(mixin, node);
                if (mp != null) return mp.GetValue(mixin);
            }
        }
        return null;
    }

    /// <summary>
    /// Index into a bound collection by integer position. Handles the non-generic
    /// IList fast path and falls back to a reflected int indexer (MBBindingList&lt;T&gt;
    /// and friends). Returns null if not indexable or out of range.
    /// </summary>
    private static object? TryGetByIndex(object current, int idx)
    {
        if (idx < 0) return null;
        if (current is System.Collections.IList il)
            return idx < il.Count ? il[idx] : null;
        var ct = current.GetType();
        var countProp = ct.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        if (countProp?.GetValue(current) is int count && idx >= count) return null;
        var indexer = ct.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, null,
            returnType: null, types: new[] { typeof(int) }, modifiers: null);
        if (indexer == null) return null;
        try { return indexer.GetValue(current, new object[] { idx }); }
        catch { return null; }
    }

    /// <summary>
    /// Find a property on a mixin instance by name. Only [DataSourceProperty]-
    /// tagged members are eligible -- the binding contract says only those
    /// are exposed to Gauntlet.
    /// </summary>
    private static PropertyInfo? FindMixinProperty(object mixin, string name)
    {
        return mixin.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
    }
}
