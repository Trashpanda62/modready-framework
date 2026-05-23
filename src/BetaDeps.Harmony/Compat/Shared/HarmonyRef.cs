// BetaDeps clean-room re-implementation of Bannerlord.BUTR.Shared.Helpers.HarmonyRef.
// MIT, copyright 2026 Maxfield Management Group.
//
// HarmonyRef is the BUTR community's lazy-initialized Harmony instance
// wrapper. Consumer mods declare
//     private static readonly HarmonyRef Harmony = new("my.mod.id");
// and call .Patch / .Unpatch through it. Internally creates the Harmony
// instance on first use, so patch sites don't pay construction cost up
// front.

using System;
using System.Reflection;

namespace Bannerlord.BUTR.Shared.Helpers;

/// <summary>
/// Lazily-initialized HarmonyLib.Harmony wrapper. Construction does not
/// create the underlying Harmony instance; the first Patch/Unpatch call
/// does.
/// </summary>
public sealed class HarmonyRef
{
    private readonly string _id;
    private global::HarmonyLib.Harmony? _instance;
    private readonly object _gate = new();

    public string Id => _id;

    public HarmonyRef(string id)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>Get-or-create the underlying Harmony instance.</summary>
    public global::HarmonyLib.Harmony Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (_gate)
            {
                _instance ??= new global::HarmonyLib.Harmony(_id);
                return _instance;
            }
        }
    }

    /// <summary>Forward to HarmonyLib.Harmony.Patch.</summary>
    public MethodInfo Patch(
        MethodBase original,
        global::HarmonyLib.HarmonyMethod? prefix = null,
        global::HarmonyLib.HarmonyMethod? postfix = null,
        global::HarmonyLib.HarmonyMethod? transpiler = null,
        global::HarmonyLib.HarmonyMethod? finalizer = null)
        => Instance.Patch(original, prefix, postfix, transpiler, finalizer);

    /// <summary>Forward to HarmonyLib.Harmony.Unpatch.</summary>
    public void Unpatch(MethodBase original, global::HarmonyLib.HarmonyPatchType type, string? harmonyID = null)
        => Instance.Unpatch(original, type, harmonyID);

    /// <summary>Forward to HarmonyLib.Harmony.UnpatchAll.</summary>
    public void UnpatchAll() => Instance.UnpatchAll(_id);
}
