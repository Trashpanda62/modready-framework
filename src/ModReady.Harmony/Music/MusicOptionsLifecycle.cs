// ModReady.Harmony -- MusicOptionsLifecycle
//
// Persists the BYO music picker's per-context settings across launches by
// hooking the native Options screen's close buttons:
//   Done   -> MusicConfig.SaveSettings()   (write Documents\...\Configs\ModReady)
//   Cancel -> MusicConfig.ReloadSettings()  (discard unsaved edits)
//
// Sigsafe: OptionsVM and its ExecuteDone/ExecuteCancel command methods are
// resolved via AccessTools2 + SafeBind (verified void / 0-arg), so a TaleWorlds
// signature drift on the beta branch degrades to "not patched" (logged) instead
// of a native CTD. Every failure is swallowed -- a settings-persistence fault
// must never take the game down.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

using ModReady.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

namespace ModReady.Harmony.Music;

public static class MusicOptionsLifecycle
{
    private const string Tag = "MusicOptionsLifecycle";
    private const string HarmonyId = "modready.music.options";
    private const string OptionsVmTypeName =
        "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.OptionsVM";

    private static bool _installed;

    /// <summary>Hook OptionsVM Done/Cancel so BYO music settings persist. Idempotent;
    /// safe to call at OnSubModuleLoad (TaleWorlds.MountAndBlade is loaded by then).</summary>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var vmType = AccessTools2.TypeByName(OptionsVmTypeName);
            if (vmType == null)
            {
                DiagLog.Log(Tag, $"type not found: {OptionsVmTypeName}; BYO music settings will not persist.");
                return;
            }

            var done   = SafeBind.Method(vmType, "ExecuteDone",   typeof(void), 0);
            var cancel = SafeBind.Method(vmType, "ExecuteCancel", typeof(void), 0);
            if (done == null && cancel == null)
            {
                DiagLog.Log(Tag, "no patchable ExecuteDone/ExecuteCancel on OptionsVM; settings will not persist.");
                return;
            }

            var harmony = new HarmonyLib.Harmony(HarmonyId);
            var patchedDone   = SafeBind.TryPatch(harmony, done,   postfix: new HarmonyMethod(typeof(MusicOptionsLifecycle), nameof(OnDone)));
            var patchedCancel = SafeBind.TryPatch(harmony, cancel, postfix: new HarmonyMethod(typeof(MusicOptionsLifecycle), nameof(OnCancel)));

            DiagLog.Log(Tag, $"Options persistence hooks installed (Done={patchedDone}, Cancel={patchedCancel}).");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "Install", ex); }
    }

    // Postfixes run after the native handler. Parameterless: we only need the
    // event; the settings themselves live in MusicConfig.Current.
    private static void OnDone()
    {
        try { MusicConfig.Current?.SaveSettings(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnDone", ex); }
    }

    private static void OnCancel()
    {
        try { MusicConfig.Current?.ReloadSettings(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "OnCancel", ex); }
    }
}
