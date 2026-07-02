// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// The Options > Keybinds page renders ONLY the category ids yielded by
// TaleWorlds.MountAndBlade.Options.OptionsProvider.GetGameKeyCategoriesList
// -- a hardcoded vanilla whitelist (Action, Order Menu, Campaign Map, ...).
// A mod hotkey context registered with the input system is therefore
// invisible in Options no matter how correctly it is registered (upstream
// ButterLib's CreateWithOwnCategory has the same blind spot).
//
// This postfix appends every HotKeyCategoryContainer category id to the
// whitelist, so consumer-mod hotkey sections render alongside the vanilla
// ones. Dedup guards against a future game version adding one of our ids
// natively (GameKeyOptionCategoryVM Dictionary.Add throws on duplicates).
//
// SafeBind style: a resolution miss logs and skips -- keys still fire on
// their binds; only the Options visibility degrades.

using System;
using System.Collections.Generic;
using System.Linq;

using ModReady.Foundation;

using HarmonyLib;
using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.ButterLib.HotKeys;

internal static class OptionsKeybindCategoryPatch
{
    private const string Tag = "OptionsKeybindCategoryPatch";

    public static void Install(global::HarmonyLib.Harmony harmony)
    {
        try
        {
            var target = AccessTools2.TypeByName("TaleWorlds.MountAndBlade.Options.OptionsProvider")
                ?.GetMethod("GetGameKeyCategoriesList",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (target == null)
            {
                DiagLog.Log(Tag, "OptionsProvider.GetGameKeyCategoriesList not found; mod hotkey categories will not show in Options > Keybinds");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(OptionsKeybindCategoryPatch), nameof(GetGameKeyCategoriesListPostfix)));
            DiagLog.Log(Tag, "installed postfix on OptionsProvider.GetGameKeyCategoriesList");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    public static void GetGameKeyCategoriesListPostfix(ref IEnumerable<string> __result)
    {
        try
        {
            var extras = HotKeyCategoryContainer.RegisteredCategoryIds;
            if (extras.Length == 0) return;
            var list = (__result ?? Enumerable.Empty<string>()).ToList();
            foreach (var id in extras)
            {
                if (!list.Contains(id)) list.Add(id);
            }
            __result = list;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "GetGameKeyCategoriesListPostfix", ex);
        }
    }
}
