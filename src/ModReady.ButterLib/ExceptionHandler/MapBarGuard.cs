// ModReady.ButterLib -- MapBarGuard
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// PROBLEM (diagnosed 2026-07-09 from a save-load "frozen loading screen"):
//   The campaign map bar VM refreshes the player-party summary in its ctor:
//   NavalMapInfoVM..ctor -> RefreshValues -> MapInfoVM.UpdatePlayerInfo ->
//   DefaultClanFinanceModel -> DefaultPartyWageModel.GetTotalWage. GetTotalWage
//   walks the player party's TroopRoster and dereferences
//   character.HeroObject.Clan (line ~70). A mod that injects a custom troop
//   with a null Character/HeroObject (Retinues' retinue troops) makes that
//   NRE -> the map-info VM constructor throws -> the map screen never finishes
//   building -> the load hangs forever. (Same class as the CampaignUIHelper
//   poisoning, one layer up the same call chain.)
//
// FIX:
//   Finalizer on MapInfoVM.UpdatePlayerInfo that swallows the exception so the
//   VM ctor completes and the map builds. The player-info line may be stale for
//   a frame, but UpdatePlayerInfo re-runs every tick, so it self-corrects once
//   the transient/bad data clears -- and the game is playable instead of hung.
//   First occurrence per session is logged (throttled) so the underlying mod
//   data bug is still visible, not silently perfect.

using System;
using System.Reflection;
using System.Threading;

using HarmonyLib;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.ExceptionHandler;

public static class MapBarGuard
{
    private const string Tag = "MapBarGuard";
    private static int _installed;
    private static int _hits;

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;
        try
        {
            var vm = AccessTools.TypeByName("TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar.MapInfoVM");
            if (vm == null) { DiagLog.Log(Tag, "MapInfoVM not found; guard not installed."); return; }

            var method = AccessTools.Method(vm, "UpdatePlayerInfo");
            if (method == null) { DiagLog.Log(Tag, "MapInfoVM.UpdatePlayerInfo not found; guard not installed."); return; }

            var harmony = new HarmonyLib.Harmony("modready.butterlib.mapbarguard");
            harmony.Patch(method, finalizer: new HarmonyMethod(typeof(MapBarGuard), nameof(Finalizer)));
            DiagLog.Log(Tag, "installed: MapInfoVM.UpdatePlayerInfo finalizer (keeps a mod's broken party/troop data from hanging the map screen on save-load)");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Install", ex); } catch { }
        }
    }

    // Return null = swallow (map bar keeps building). Non-NRE exceptions are
    // rethrown so we don't hide unrelated faults.
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception == null) return null;
        if (__exception is NullReferenceException)
        {
            if (Interlocked.Increment(ref _hits) == 1)
                DiagLog.Log(Tag, "swallowed a NullReferenceException in MapInfoVM.UpdatePlayerInfo -- a mod injected party/troop data the native wage/finance models can't price (e.g. a retinue troop with a null hero). Map screen kept building; player-info may be briefly stale.");
            return null;
        }
        return __exception;
    }
}
