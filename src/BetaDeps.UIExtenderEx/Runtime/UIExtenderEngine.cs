// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// UIExtenderEngine is the runtime that turns registered prefab patches and
// mixins into actual game behavior. It accepts registrations from
// UIExtender.Enable(); WidgetPrefabHook reads the Enabled snapshot when
// Gauntlet movies load, and ViewModelMixinHost reads it when ViewModels
// are constructed. Disable() is honored by excluding the registry from the
// snapshot rather than uninstalling the shared Harmony patches.

using System.Collections.Generic;
using System.Linq;

using BetaDeps.Foundation;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class UIExtenderEngine
{
    private const string Tag = "UIExtenderEngine";

    private static readonly object _gate = new();
    private static readonly List<UIExtenderRegistry> _registered = new();

    // Cache the enabled snapshot so the per-VM-construction read in
    // ViewModelMixinHost.Register doesn't allocate a fresh array on every
    // ViewModel built. INVARIANT: anything that adds a registry OR flips a
    // UIExtenderRegistry.Enabled flag MUST mark this stale -- OnEnable below and
    // UIExtender.Enable/Disable are the only such sites (keep it that way).
    private static IReadOnlyList<UIExtenderRegistry> _enabledCache = System.Array.Empty<UIExtenderRegistry>();
    private static bool _enabledDirty = true;

    /// <summary>Mark the Enabled snapshot stale; rebuilt lazily on next read.</summary>
    internal static void InvalidateEnabled() { lock (_gate) { _enabledDirty = true; } }

    /// <summary>Called from <see cref="UIExtender.Enable"/>. Idempotent: an
    /// Enable -> Disable -> Enable cycle reuses the already-registered entry
    /// instead of adding it twice (which double-applied every prefab patch
    /// and double-attached every mixin).</summary>
    public static void OnEnable(UIExtenderRegistry registry)
    {
        bool isNew;
        lock (_gate)
        {
            isNew = !_registered.Contains(registry);
            if (isNew) _registered.Add(registry);
            _enabledDirty = true; // registry added or re-enabled -> snapshot stale
        }
        DiagLog.Log(Tag, $"OnEnable('{registry.ModuleName}'): {registry.Prefabs.Count} prefab patches, {registry.Mixins.Count} mixins now active{(isNew ? "" : " (re-enabled)")}");
        // Unconditional: InstallRefreshHooks dedupes per target method, so a
        // re-enable is a cheap no-op for already-hooked mixins while mixins
        // registered after the first Enable still get their hooks.
        ViewModelMixinHook.InstallRefreshHooks(registry);
    }

    /// <summary>Snapshot of every currently-enabled registry. Registries whose
    /// module called Disable() are registered but excluded here, which is what
    /// makes Disable actually take effect at apply time.</summary>
    internal static IReadOnlyList<UIExtenderRegistry> Enabled
    {
        get
        {
            lock (_gate)
            {
                if (_enabledDirty)
                {
                    _enabledCache = _registered.Where(r => r.Enabled).ToArray();
                    _enabledDirty = false;
                }
                return _enabledCache;
            }
        }
    }
}
