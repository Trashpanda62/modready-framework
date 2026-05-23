// BetaDeps clean-room re-implementation of the Bannerlord.UIExtenderEx
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
// In this initial Phase 2 implementation the runtime engine records every
// registration to the diag log but does not yet rewrite prefabs or attach
// mixins -- those two pieces are scheduled as Phase 2 tasks #12 and #13.
// Consumer mods compile and load against this assembly correctly; they
// just won't see their UI changes applied yet.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Runtime;

using BetaDeps.Foundation;

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
    /// uses this for diagnostic warnings; in BetaDeps it currently just logs
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
    /// v0.5.8 (Retinues compat): some consumer mods call UIExtender.Disable()
    /// on submodule unload (e.g. Retinues.SubModule.DisableUIExtender). The
    /// upstream BUTR UIExtenderEx exposes a Disable() method; we omitted it
    /// because BetaDeps's runtime tears the patches down naturally at game
    /// shutdown — no per-mod disable hook is needed. Mods that explicitly
    /// invoke Disable() crash with MethodNotFoundException without this stub.
    /// Marking the registry disabled is enough to satisfy callers; the
    /// engine's Harmony patches stay installed for the rest of the session
    /// (game is about to shut down anyway).
    /// </summary>
    public void Disable()
    {
        if (!_registry.Enabled)
        {
            DiagLog.Log(Tag, $"Disable('{_moduleName}'): not enabled, no-op");
            return;
        }
        _registry.Enabled = false;
        DiagLog.Log(Tag, $"Disable('{_moduleName}'): registry marked disabled");
    }

    internal UIExtenderRegistry Registry => _registry;
}
