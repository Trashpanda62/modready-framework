// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Options-screen lifecycle patch. Installs a Harmony postfix on
// TaleWorlds.Library.ViewModel.ExecuteCommand that:
//   - on "ExecuteDone"   -> flushes every MCM settings instance to disk
//   - on "ExecuteCancel" -> reloads every MCM settings instance from
//     disk (discards any pending in-memory edits)
//
// NOTE on command names: the bottom Cancel button fires "ExecuteCancel"
// (Options.xml CancelButtonAction). The TAB toggles fire "ExecuteCancelProcess"
// for tab-switch cancellation -- a DIFFERENT event we deliberately ignore.
//
// In-memory writebacks from sliders/checkboxes update the underlying
// BaseSettings object during the session. Done persists those edits;
// Cancel undoes them. Both are scoped to the Options screen VM so we don't
// trigger save/reload on unrelated commands.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;

using ModReady.Foundation;

using HarmonyLib;

using TaleWorlds.Library;

namespace MCM.Internal;

internal static class SaveOnDonePatch
{
    private const string Tag = "MCM.SaveOnDonePatch";
    private const string HarmonyId = "modready.mcm.saveondone";
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
            else
            {
                // Diagnostic breadcrumb: if a future game build renames
                // ExecuteDone/ExecuteCancel, Done/Cancel would silently stop
                // persisting edits. Log each distinct OptionsVM command ONCE
                // (bounded -- the screen fires only a handful) so the rename is
                // visible in the log without spamming every command.
                LogObservedCommandOnce(commandName);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ExecuteCommandPostfix({commandName})", ex);
        }
    }

    private static readonly HashSet<string> _seenOptionsCommands = new(StringComparer.Ordinal);
    private static void LogObservedCommandOnce(string commandName)
    {
        if (string.IsNullOrEmpty(commandName)) return;
        lock (_seenOptionsCommands)
        {
            if (!_seenOptionsCommands.Add(commandName)) return;
        }
        DiagLog.Log(Tag, $"OptionsVM command observed (not Done/Cancel): '{commandName}' -- if Done/Cancel ever stop persisting, check whether the game renamed them.");
    }

    private static void SaveAll()
    {
        int saved = 0, failed = 0, skipped = 0;
        foreach (var r in SettingsRegistry.All)
        {
            try
            {
                // Fluent settings have no upstream-shaped Save() method — route
                // through SettingsStorage directly (2.3/H6: all three fluent
                // scopes). Attribute-based settings expose Save() which
                // internally calls SettingsStorage.Save.
                if (r.Instance is IFluentSettings)
                {
                    SettingsStorage.Save(r.Instance, r.Id);
                }
                else
                {
                    var saveMethod = r.Instance.GetType().GetMethod(
                        "Save", BindingFlags.Public | BindingFlags.Instance);
                    if (saveMethod == null)
                    {
                        // Phase 2.5 / M12: a missing Save() used to count as
                        // "saved" -- the log claimed success while nothing
                        // persisted (ForeignSettingsAdapter case). Count it
                        // honestly and surface it once.
                        ModReady.Foundation.CompatWarn.Once(
                            "MCM.SaveOnDone", "Save()",
                            r.Instance.GetType().Assembly.GetName().Name,
                            $"'{r.Id}' has no Save() method; its settings are NOT persisted on Done");
                        skipped++;
                        continue;
                    }
                    saveMethod.Invoke(r.Instance, null);
                }
                saved++;
            }
            catch (Exception ex)
            {
                failed++;
                DiagLog.LogCaught(Tag, $"Save({r.Id})", ex);
            }
        }
        DiagLog.Log(Tag, $"Done postfix: saved {saved} mod setting(s), {failed} failed, {skipped} skipped (no Save method)");
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
