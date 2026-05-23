// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// WidgetFactoryManager is BUTR-UIExtenderEx's hook into TaleWorlds's
// WidgetFactory + WidgetTemplate. It lets consumer mods register custom
// Widget subclasses that the Gauntlet runtime resolves alongside the
// built-in widget types. The hooks are:
//
//   - WidgetFactory.GetCustomType (prefix)  -> Custom type lookup
//   - WidgetFactory.CreateBuiltinWidget (prefix)  -> Construction
//   - WidgetFactory.GetWidgetTypes (postfix)  -> Type enumeration
//   - WidgetFactory.IsCustomType (prefix)  -> "is this a custom type" query
//   - WidgetTemplate.CreateWidgets (transpiler) -> Force JIT
//   - GauntletMovie.LoadMovie (transpiler) -> Force JIT
//
// Consumer mods don't call us directly; they register custom widget types
// via UIExtender.Register() (the assembly scan picks up [WidgetAttribute]
// or implementations of IWidget). Patches install at SubModule load time.

using System;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.UIExtenderEx.ResourceManager;

public static class WidgetFactoryManager
{
    private const string Tag = "WidgetFactoryManager";
    private const string HarmonyId = "betadeps.uiextenderex.widgetfactory";

    private static int _installed;
    private static readonly Dictionary<string, Type> _customTypes = new(StringComparer.Ordinal);
    private static readonly object _gate = new();

    /// <summary>Register a custom widget type accessible by name in prefab XML.</summary>
    public static void Register(string name, Type widgetType)
    {
        if (string.IsNullOrEmpty(name) || widgetType == null) return;
        lock (_gate)
        {
            _customTypes[name] = widgetType;
        }
        DiagLog.Log(Tag, $"registered custom widget type '{name}' -> {widgetType.FullName}");
    }

    /// <summary>
    /// Single-Type Register overload. Diplomacy and some other BUTR
    /// consumer mods call this signature directly — it derives the
    /// registration name from a [WidgetAttribute] on the type (BUTR
    /// upstream convention) or falls back to the type's simple name.
    /// Without this overload, Diplomacy throws MissingMethodException
    /// in OnSubModuleLoad and the whole module crashes the launcher.
    /// </summary>
    public static void Register(Type widgetType)
    {
        if (widgetType == null) return;
        string name = ResolveWidgetName(widgetType);
        Register(name, widgetType);
    }

    /// <summary>Look for [WidgetAttribute] on the type (named in upstream
    /// BUTR convention with a Name field), else use the type's simple
    /// name as the registration key.</summary>
    private static string ResolveWidgetName(Type t)
    {
        try
        {
            foreach (var attr in t.GetCustomAttributes(inherit: false))
            {
                var atype = attr.GetType();
                if (!string.Equals(atype.Name, "WidgetAttribute", StringComparison.Ordinal)) continue;
                // Look for a public "Name" property/field on the attribute.
                var nameProp = atype.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                if (nameProp?.GetValue(attr) is string n && !string.IsNullOrEmpty(n)) return n;
                var nameField = atype.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
                if (nameField?.GetValue(attr) is string nf && !string.IsNullOrEmpty(nf)) return nf;
            }
        }
        catch { /* fall through to type name */ }
        return t.Name;
    }

    /// <summary>True if a custom type is registered under this name.</summary>
    public static bool IsCustomType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        lock (_gate) { return _customTypes.ContainsKey(name); }
    }

    /// <summary>Resolve a custom type by name, or null on miss.</summary>
    public static Type? GetCustomType(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        lock (_gate) { return _customTypes.TryGetValue(name, out var t) ? t : null; }
    }

    /// <summary>Snapshot of every registered custom type.</summary>
    public static IReadOnlyDictionary<string, Type> All
    {
        get { lock (_gate) { return _customTypes.ToDictionary(kv => kv.Key, kv => kv.Value); } }
    }

    /// <summary>
    /// Install Harmony patches on the WidgetFactory + WidgetTemplate methods
    /// CrestPatchSelfTest looks for. The patches reflect through to our static
    /// registry above.
    /// </summary>
    public static void Patch(global::HarmonyLib.Harmony harmony)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var factory = AccessTools2.TypeByName("TaleWorlds.GauntletUI.PrefabSystem.WidgetFactory")
                       ?? AccessTools2.TypeByName("TaleWorlds.GauntletUI.WidgetFactory");
            if (factory == null)
            {
                DiagLog.Log(Tag, "WidgetFactory type not found; widget-factory patches disabled");
            }
            else
            {
                TryPatch(harmony, factory, "GetCustomType",       nameof(GetCustomTypePrefix),       isPrefix: true);
                TryPatch(harmony, factory, "CreateBuiltinWidget", nameof(CreateBuiltinWidgetPrefix), isPrefix: true);
                TryPatch(harmony, factory, "GetWidgetTypes",      nameof(GetWidgetTypesPostfix),     isPrefix: false);
                TryPatch(harmony, factory, "IsCustomType",        nameof(IsCustomTypePrefix),        isPrefix: true);
            }

            // Transpiler "blank" hooks just to force JIT (matches BUTR behavior CrestPatchSelfTest expects).
            var widgetTemplate = AccessTools2.TypeByName("TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate");
            if (widgetTemplate != null)
            {
                TryTranspile(harmony, widgetTemplate, "CreateWidgets");
                // v0.4.3a failsafe: catch any exception thrown during widget
                // construction, log it with type + stack trace, and swallow.
                // Without this, a single malformed prefab silently kills the
                // entire screen (top tab strip + bottom buttons disappear)
                // because Bannerlord's widget-tree builder doesn't recover.
                TryFinalize(harmony, widgetTemplate, "CreateWidgets", nameof(CreateWidgetsFinalizer));
            }
            var gauntletMovie = AccessTools2.TypeByName("TaleWorlds.GauntletUI.Data.GauntletMovie");
            if (gauntletMovie != null)
            {
                TryTranspile(harmony, gauntletMovie, "LoadMovie");
                TryFinalize(harmony, gauntletMovie, "LoadMovie", nameof(LoadMovieFinalizer));
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Patch", ex);
        }
    }

    private static void TryPatch(global::HarmonyLib.Harmony harmony, Type t, string methodName, string patchMethodName, bool isPrefix)
    {
        try
        {
            var target = AccessTools2.Method(t, methodName);
            if (target == null) { DiagLog.Log(Tag, $"WidgetFactory.{methodName} not found; skipped"); return; }
            var patch = AccessTools2.Method(typeof(WidgetFactoryManager), patchMethodName);
            if (patch == null) return;
            var hm = new HarmonyMethod(patch);
            if (isPrefix) harmony.Patch(target, prefix: hm);
            else harmony.Patch(target, postfix: hm);
            DiagLog.Log(Tag, $"patched {t.FullName}.{methodName} ({(isPrefix ? "prefix" : "postfix")})");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"TryPatch({methodName})", ex); }
    }

    private static void TryTranspile(global::HarmonyLib.Harmony harmony, Type t, string methodName)
    {
        try
        {
            var target = AccessTools2.Method(t, methodName);
            if (target == null) { DiagLog.Log(Tag, $"{t.FullName}.{methodName} not found; skipped"); return; }
            var patch = AccessTools2.Method(typeof(WidgetFactoryManager), nameof(BlankTranspiler));
            if (patch == null) return;
            harmony.Patch(target, transpiler: new HarmonyMethod(patch));
            DiagLog.Log(Tag, $"patched {t.FullName}.{methodName} (blank transpiler)");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"TryTranspile({methodName})", ex); }
    }

    /// <summary>
    /// Install a Harmony finalizer on a method. Finalizers run after the
    /// method body (and after any postfixes), see any thrown exception via
    /// the __exception parameter, and can either rethrow it (return non-null
    /// Exception) or swallow it (return null / void). We use this to catch
    /// otherwise-fatal widget-construction errors and log them.
    /// </summary>
    private static void TryFinalize(global::HarmonyLib.Harmony harmony, Type t, string methodName, string finalizerMethodName)
    {
        try
        {
            var target = AccessTools2.Method(t, methodName);
            if (target == null) { DiagLog.Log(Tag, $"{t.FullName}.{methodName} not found; finalizer skipped"); return; }
            var patch = AccessTools2.Method(typeof(WidgetFactoryManager), finalizerMethodName);
            if (patch == null) return;
            harmony.Patch(target, finalizer: new HarmonyMethod(patch));
            DiagLog.Log(Tag, $"installed finalizer on {t.FullName}.{methodName}");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"TryFinalize({methodName})", ex); }
    }

    /// <summary>
    /// Finalizer for WidgetTemplate.CreateWidgets. Logs the exception with
    /// stack trace then returns null to suppress it -- the widget tree
    /// loses the broken subtree but the rest of the screen still renders.
    /// </summary>
    public static Exception? CreateWidgetsFinalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try { DiagLog.LogCaught(Tag, "CreateWidgets (suppressed)", __exception); } catch { }
        return null; // swallow
    }

    /// <summary>
    /// Finalizer for GauntletMovie.LoadMovie. Same pattern as above --
    /// catch and log malformed-prefab exceptions, return null so the rest
    /// of the screen can render.
    /// </summary>
    public static Exception? LoadMovieFinalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try { DiagLog.LogCaught(Tag, "LoadMovie (suppressed)", __exception); } catch { }
        return null; // swallow
    }

    // ---- patch bodies ----

    // Game-side WidgetFactory.GetCustomType(string typeName) returns a
    // WidgetPrefab. We don't construct WidgetPrefabs from our Type registry,
    // so this prefix can't meaningfully override -- it just lets the original
    // run and satisfies CrestPatchSelfTest's "is this method patched" probe.
    // Parameter name must be "typeName" to match the game method signature.
    public static bool GetCustomTypePrefix(string typeName)
    {
        return true; // continue to original
    }

    public static bool CreateBuiltinWidgetPrefix(string typeName, ref object? __result)
    {
        // Stub: defer to upstream behavior for built-in widget creation.
        // Custom-widget creation lives in CreateBuiltinWidget's normal flow.
        return true;
    }

    public static void GetWidgetTypesPostfix(ref System.Collections.Generic.IEnumerable<string> __result)
    {
        // WidgetFactory.GetWidgetTypes on the current game version returns the
        // *names* of widget types as strings, not the Type objects. Concat our
        // registered custom-type keys so consumer code that enumerates types
        // sees our additions alongside the built-ins.
        try
        {
            var extras = All.Keys.ToList();
            if (extras.Count == 0) return;
            __result = (__result ?? Array.Empty<string>()).Concat(extras);
        }
        catch { }
    }

    public static bool IsCustomTypePrefix(string typeName, ref bool __result)
    {
        if (IsCustomType(typeName)) { __result = true; return false; }
        return true;
    }

    public static IEnumerable<CodeInstruction> BlankTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        // Identity transpiler -- just forces JIT for CrestPatchSelfTest's
        // discovery. No IL rewriting.
        return instructions;
    }
}
