// ModReady clean-room re-implementation of the Bannerlord.UIExtenderEx
// entry-point class. MIT, copyright 2026 Maxfield Management Group.
//
// Consumer mods call:
//
//     var ext = UIExtender.Create("MyModule");
//     ext.Register(typeof(MyModule).Assembly);
//     ext.Verify();
//     ext.Enable();
//
// Register() scans the assembly for [PrefabExtension] and [ViewModelMixin]
// types and records them. Enable() hands the registry to the runtime engine
// (Runtime.UIExtenderEngine) which then applies prefab patches via the
// Gauntlet movie hook and attaches mixins on VM construction.
//
// The runtime engine applies prefab patches as Gauntlet movies load
// (WidgetPrefabHook -> PrefabPatcher, v1 and v2 patch families) and
// attaches mixins after each target VM finishes constructing
// (ViewModelMixinHook -> ViewModelMixinHost, deferred first-touch attach).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Runtime;

using ModReady.Foundation;

namespace Bannerlord.UIExtenderEx;

/// <summary>
/// Public entry point for the UIExtenderEx system. One instance per consumer
/// module. The instance owns the registry of that module's prefab patches
/// and ViewModel mixins, and hands the registry to the runtime engine when
/// <see cref="Enable"/> is called.
/// </summary>
public class UIExtender
{
    private const string Tag = "UIExtender";

    /// <summary>All extenders ever created, keyed by module name. Mirrors the
    /// upstream public API surface so a mod can fetch another mod's extender.</summary>
    private static readonly Dictionary<string, UIExtender> Instances = new();

    private readonly string _moduleName;
    private readonly UIExtenderRegistry _registry;

    /// <summary>
    /// Create a new UIExtender for the given module. If one already exists for
    /// that module name, the existing instance is returned (idempotent).
    /// </summary>
    public static UIExtender Create(string moduleName)
    {
        if (string.IsNullOrEmpty(moduleName))
            throw new ArgumentException("moduleName must be non-empty", nameof(moduleName));

        lock (Instances)
        {
            if (Instances.TryGetValue(moduleName, out var existing))
                return existing;
            var ext = new UIExtender(moduleName, allocateRegistry: true);
            Instances[moduleName] = ext;
            DiagLog.Log(Tag, $"Created UIExtender for module '{moduleName}'");
            return ext;
        }
    }

    /// <summary>Returns the UIExtender for a given module name, or null if none has been created.</summary>
    public static UIExtender? GetUIExtenderFor(string moduleName)
    {
        lock (Instances)
        {
            return Instances.TryGetValue(moduleName, out var ext) ? ext : null;
        }
    }

    private UIExtender(string moduleName, bool allocateRegistry)
    {
        _moduleName = moduleName;
        _registry = new UIExtenderRegistry(moduleName);
    }

    /// <summary>Legacy constructor preserved for API compatibility. Prefer
    /// <see cref="Create(string)"/>.</summary>
    [Obsolete("Use UIExtender.Create(moduleName) for new code.")]
    public UIExtender(string moduleName) : this(moduleName, allocateRegistry: true)
    {
        lock (Instances)
        {
            Instances[moduleName] = this;
        }
    }

    /// <summary>
    /// Scans the given assembly for [PrefabExtension] and [ViewModelMixin]
    /// classes and records them in this extender's registry. Safe to call
    /// multiple times; duplicates are deduplicated by patch type.
    /// </summary>
    public void Register(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            // Some types failed to load (missing optional refs). Use whatever did.
            types = ex.Types.Where(t => t != null).ToArray()!;
            DiagLog.Log(Tag, $"Register({assembly.GetName().Name}): partial type load -- {ex.Types.Length - types.Length} types skipped");
        }

        int prefabCount = 0;
        int mixinCount = 0;
        foreach (var t in types)
        {
            if (t == null) continue;
            try
            {
                foreach (var attr in t.GetCustomAttributes<PrefabExtensionAttribute>(inherit: false))
                {
                    _registry.Prefabs.Add(new PrefabRegistration(_moduleName, t, attr));
                    prefabCount++;
                }
                var mixinAttr = t.GetCustomAttribute<ViewModelMixinAttribute>(inherit: false);
                if (mixinAttr != null)
                {
                    var targetVm = ResolveMixinTargetVm(t);
                    if (targetVm != null)
                    {
                        _registry.Mixins.Add(new MixinRegistration(_moduleName, t, targetVm, mixinAttr));
                        mixinCount++;
                    }
                    else
                    {
                        DiagLog.Log(Tag, $"Register: mixin {t.FullName} has [ViewModelMixin] but does not derive from BaseViewModelMixin<T>; skipped");
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"Register/scan {t?.FullName}", ex);
            }
        }
        DiagLog.Log(Tag, $"Register({assembly.GetName().Name}): {prefabCount} prefab patches, {mixinCount} mixins recorded for module '{_moduleName}'");
    }

    /// <summary>Resolves the TViewModel type from a BaseViewModelMixin&lt;TViewModel&gt; subclass by walking the inheritance chain.</summary>
    private static Type? ResolveMixinTargetVm(Type mixinType)
    {
        for (var t = mixinType; t != null && t != typeof(object); t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition().FullName == "Bannerlord.UIExtenderEx.ViewModels.BaseViewModelMixin`1")
                return t.GetGenericArguments()[0];
        }
        return null;
    }

    /// <summary>
    /// Verifies the registered set is internally consistent. The upstream API
    /// uses this for diagnostic warnings; in ModReady it currently just logs
    /// counts. Safe to skip.
    /// </summary>
    public void Verify()
    {
        DiagLog.Log(Tag, $"Verify('{_moduleName}'): {_registry.Prefabs.Count} prefab patches, {_registry.Mixins.Count} mixins registered");
    }

    /// <summary>
    /// Activates the registered patches and mixins. After this call the
    /// runtime engine will apply prefab edits as Gauntlet movies load and
    /// will attach mixins to target VMs as they're constructed.
    /// </summary>
    public void Enable()
    {
        if (_registry.Enabled)
        {
            DiagLog.Log(Tag, $"Enable('{_moduleName}'): already enabled, no-op");
            return;
        }
        _registry.Enabled = true;
        UIExtenderEngine.OnEnable(_registry);
        DiagLog.Log(Tag, $"Enable('{_moduleName}'): registry handed to runtime engine");
    }

    /// <summary>
    /// v0.5.8 (Retinues compat), made real in Phase 2B: disables this module's
    /// registrations. The engine's global Harmony patches stay installed (they
    /// are shared by every module), but the runtime checks the registry's
    /// Enabled flag at apply time, so a disabled module's prefab patches stop
    /// applying to newly loaded movies and its mixins stop attaching to newly
    /// constructed VMs. Already-applied prefab edits and already-attached
    /// mixins are not rolled back. Enable() after Disable() reactivates the
    /// same registry without re-registering (no duplicates).
    /// </summary>
    public void Disable()
    {
        if (!_registry.Enabled)
        {
            DiagLog.Log(Tag, $"Disable('{_moduleName}'): not enabled, no-op");
            return;
        }
        _registry.Enabled = false;
        UIExtenderEngine.InvalidateEnabled();
        DiagLog.Log(Tag, $"Disable('{_moduleName}'): registry marked disabled");
    }

    /// <summary>
    /// v0.7.2 (BannerCraft v1.3.13 compat): disable a single prefab / mixin
    /// patch without disabling the whole module. The upstream BUTR API takes
    /// a System.Type identifying the patch class. The runtime engine checks
    /// the disabled set at apply time, so the type stops being applied to
    /// newly loaded movies / newly constructed VMs from this call onward;
    /// already-applied instances are not rolled back.
    /// </summary>
    public void Disable(Type patchType)
    {
        if (patchType == null)
        {
            DiagLog.Log(Tag, $"Disable(null) on '{_moduleName}': ignored");
            return;
        }
        _registry.SetDisabled(patchType);
        DiagLog.Log(Tag, $"Disable('{_moduleName}', {patchType.FullName}): marked inactive in registry");
    }

    internal UIExtenderRegistry Registry => _registry;
}
