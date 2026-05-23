// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Options-screen lifecycle patch. Installs a Harmony postfix on
// TaleWorlds.Library.ViewModel.ExecuteCommand that:
//   - on "ExecuteDone"          -> flushes every MCM settings instance to disk
//   - on "ExecuteCancelProcess" -> reloads every MCM settings instance from
//     disk (discards any pending in-memory edits)
//
// In-memory writebacks from sliders/checkboxes update the underlying
// BaseSettings object during the session. Done persists those edits;
// Cancel undoes them. Both are scoped to the Options screen VM so we don't
// trigger save/reload on unrelated ExecuteDone/ExecuteCancelProcess commands.

using System;
using System.Threading;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;

using TaleWorlds.Library;

namespace MCM.Internal;

internal static class SaveOnDonePatch
{
    private const string Tag = "MCM.SaveOnDonePatch";
    private const string HarmonyId = "betadeps.mcm.saveondone";
    private static int _installed;

    public static void Install()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var harmony = new Harmony(HarmonyId);
            var target = typeof(ViewModel).GetMethod(
                "ExecuteCommand",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(object[]) },
                modifiers: null);
            if (target == null)
            {
                DiagLog.Log(Tag, "ViewModel.ExecuteCommand not found; save-on-Done disabled");
                return;
            }

            var postfix = typeof(SaveOnDonePatch).GetMethod(nameof(ExecuteCommandPostfix),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            DiagLog.Log(Tag, "installed postfix on ViewModel.ExecuteCommand");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    private static void ExecuteCommandPostfix(ViewModel __instance, string commandName)
    {
        // Only respond to commands fired by the in-game Options screen VM.
        var vmTypeName = __instance?.GetType().FullName ?? string.Empty;
        if (!vmTypeName.EndsWith(".OptionsVM", StringComparison.Ordinal)) return;

        try
        {
            if (string.Equals(commandName, "ExecuteDone", StringComparison.Ordinal))
            {
                SaveAll();
            }
            else if (string.Equals(commandName, "ExecuteCancel", StringComparison.Ordinal))
            {
                // Cancel button at the bottom of the Options screen (see
                // tw-references/Options/SPOptions/Options.xml -> Standard.DialogCloseButtons
                // Parameter.CancelButtonAction="ExecuteCancel"). The tab toggles
                // use ExecuteCancelProcess for tab-switch cancellation; that's
                // a different event we do NOT want to react to.
                ReloadAll();
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ExecuteCommandPostfix({commandName})", ex);
        }
    }

    private static void SaveAll()
    {
        int saved = 0, failed = 0;
        foreach (var r in SettingsRegistry.All)
        {
            try
            {
                // FluentGlobalSettings has no Save() method on the class — route
                // through SettingsStorage directly. Attribute-based settings
                // expose Save() which internally calls SettingsStorage.Save.
                if (r.Instance is MCM.Abstractions.Base.Global.FluentGlobalSettings)
                {
                    SettingsStorage.Save(r.Instance, r.Id);
                }
                else
                {
                    var saveMethod = r.Instance.GetType().GetMethod(
                        "Save", BindingFlags.Public | BindingFlags.Instance);
                    saveMethod?.Invoke(r.Instance, null);
                }
                saved++;
            }
            catch (Exception ex)
            {
                failed++;
                DiagLog.LogCaught(Tag, $"Save({r.Id})", ex);
            }
        }
        DiagLog.Log(Tag, $"Done postfix: saved {saved} mod setting(s), {failed} failed");
    }

    private static void ReloadAll()
    {
        // Re-read each registered settings instance from its JSON file. This
        // overwrites any pending in-memory edits the user made before clicking
        // Cancel. Mods that haven't yet written their first JSON are unaffected
        // (SettingsStorage.Load writes defaults on missing-file).
        int reloaded = 0, failed = 0;
        foreach (var r in SettingsRegistry.All)
        {
            try
            {
                SettingsStorage.Load(r.Instance, r.Id);
                reloaded++;
            }
            catch (Exception ex)
            {
                failed++;
                DiagLog.LogCaught(Tag, $"Reload({r.Id})", ex);
            }
        }
        DiagLog.Log(Tag, $"Cancel postfix: reloaded {reloaded} mod setting(s), {failed} failed");
    }
}
