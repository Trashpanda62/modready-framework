// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Harmony patch that suppresses OptionsVM.SelectPreviousCategory /
// SelectNextCategory while ANY EditableTextWidget is focused.
//
// Why this exists: the Mod Config tab on the Options screen has an inline
// search field for filtering the mod list. OptionsVM's tab navigation fires
// from a game-input hotkey (PreviousTabInputKey / NextTabInputKey, default
// Q / E) independent of UI focus. Without this patch, typing a Q or E in the
// search field also rotates away from the Mod Config tab as a side effect,
// instantly losing the user's search context.
//
// Approach: track focused-EditableTextWidget count via Harmony postfix on
// EditableTextWidget.OnGainFocus / OnLoseFocus, and short-circuit
// SelectPreviousCategory / SelectNextCategory while the count is > 0. The
// count is per-process — fine, because the vanilla Options screen has no
// EditableTextWidget of its own, so any focused EditableTextWidget while
// Options is open is necessarily one we injected.

using System;
using System.Reflection;
using System.Threading;

using BetaDeps.Foundation;

using HarmonyLib;

namespace MCM.Internal;

internal static class TabSwitchGuardPatch
{
    private const string Tag = "MCM.TabSwitchGuardPatch";
    private const string HarmonyId = "betadeps.mcm.tabswitchguard";
    private static int _installed;

    // Refcount, not bool, in case two EditableTextWidget instances are
    // visible at once (e.g. our search field plus a future filter field).
    // Interlocked-accessed so race-free across UI / input threads.
    private static int _focusedCount;

    /// <summary>
    /// Test hook used by the OptionsVM prefix patches. Returns true if any
    /// EditableTextWidget reports itself as focused right now.
    /// </summary>
    internal static bool AnyTextFieldFocused => Volatile.Read(ref _focusedCount) > 0;

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;

        try
        {
            var harmony = new Harmony(HarmonyId);

            // ----- EditableTextWidget focus tracking ------------------
            // EditableTextWidget lives in TaleWorlds.GauntletUI.BaseTypes.
            // Reflect by name so we don't add a hard reference to the
            // GauntletUI assembly here (Foundation is the only project
            // with a Bannerlord ref by convention).
            var editableType = AccessTools.TypeByName("TaleWorlds.GauntletUI.BaseTypes.EditableTextWidget");
            if (editableType == null)
            {
                DiagLog.Log(Tag, "Install: EditableTextWidget type not found; skipping focus tracking");
                return;
            }

            var onGain = AccessTools.Method(editableType, "OnGainFocus");
            var onLose = AccessTools.Method(editableType, "OnLoseFocus");
            if (onGain != null)
            {
                harmony.Patch(onGain,
                    postfix: new HarmonyMethod(typeof(TabSwitchGuardPatch), nameof(OnGainFocus_Postfix)));
            }
            if (onLose != null)
            {
                harmony.Patch(onLose,
                    postfix: new HarmonyMethod(typeof(TabSwitchGuardPatch), nameof(OnLoseFocus_Postfix)));
            }

            // ----- OptionsVM tab navigation guards --------------------
            // SelectPreviousCategory / SelectNextCategory are the methods
            // the Q/E hotkeys invoke. Prefix returning false short-circuits
            // the original.
            var optionsVMType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.OptionsVM");
            if (optionsVMType == null)
            {
                DiagLog.Log(Tag, "Install: OptionsVM type not found; skipping tab-switch guards");
                return;
            }

            var selPrev = AccessTools.Method(optionsVMType, "SelectPreviousCategory");
            var selNext = AccessTools.Method(optionsVMType, "SelectNextCategory");
            if (selPrev != null)
            {
                harmony.Patch(selPrev,
                    prefix: new HarmonyMethod(typeof(TabSwitchGuardPatch), nameof(SelectCategory_Prefix)));
            }
            if (selNext != null)
            {
                harmony.Patch(selNext,
                    prefix: new HarmonyMethod(typeof(TabSwitchGuardPatch), nameof(SelectCategory_Prefix)));
            }

            DiagLog.Log(Tag, "Install: Q/E tab-switch guard active");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    private static void OnGainFocus_Postfix()
    {
        Interlocked.Increment(ref _focusedCount);
    }

    private static void OnLoseFocus_Postfix()
    {
        // Clamp to >= 0 so a stray OnLoseFocus without matching OnGain
        // (defensive) can't drive the counter negative.
        int prev;
        do
        {
            prev = Volatile.Read(ref _focusedCount);
            if (prev <= 0) return;
        } while (Interlocked.CompareExchange(ref _focusedCount, prev - 1, prev) != prev);
    }

    // Prefix returns false to skip the original method, true to let it run.
    private static bool SelectCategory_Prefix()
    {
        if (AnyTextFieldFocused)
        {
            // Log only at debug verbosity; this fires whenever the user
            // presses Q/E while the search field is active, which can be
            // very chatty.
            return false;
        }
        return true;
    }
}
