// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// UIExtenderEngine is the runtime that turns registered prefab patches and
// mixins into actual game behavior. In this initial Phase 2 cut it accepts
// registrations from UIExtender.Enable() and logs them, but the Gauntlet
// movie hook and the VM mixin proxy are stubs -- those land in tasks #12
// and #13.
//
// The point of shipping this stub now: consumer mods compile against the
// public API surface, the assembly loads cleanly, and CREST passes its
// "Bannerlord.UIExtenderEx is present" check. Visual UI changes (MCM tab
// content, companion swap UI) won't render yet -- that's the next iteration.

using System.Collections.Generic;

using BetaDeps.Foundation;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class UIExtenderEngine
{
    private const string Tag = "UIExtenderEngine";

    private static readonly object _gate = new();
    private static readonly List<UIExtenderRegistry> _enabled = new();

    /// <summary>Called from <see cref="UIExtender.Enable"/>.</summary>
    public static void OnEnable(UIExtenderRegistry registry)
    {
        lock (_gate)
        {
            _enabled.Add(registry);
            DiagLog.Log(Tag, $"OnEnable('{registry.ModuleName}'): {registry.Prefabs.Count} prefab patches, {registry.Mixins.Count} mixins now active");
        }
    }

    /// <summary>Snapshot of every enabled registry, used by the (future) Gauntlet hook + VM proxy.</summary>
    internal static IReadOnlyList<UIExtenderRegistry> Enabled
    {
        get
        {
            lock (_gate) { return _enabled.ToArray(); }
        }
    }
}
