// ModReady.ButterLib -- RosterRepairBehavior
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// PROBLEM (diagnosed 2026-07-09):
//   A leftover custom troop (e.g. a WarbandForge retinue whose CharacterObject
//   template no longer resolves) sits in a party's TroopRoster with a null
//   Culture. EVERY native model that walks the roster then NREs on it:
//   DefaultPartyWageModel.GetTotalWage (map bar), DefaultMobilePartyFood-
//   ConsumptionModel (settlement food buy), CharacterObject.GetBodyProperties
//   (party screen portrait, every frame). The result is a save that hangs the
//   map/party screen on load. Guarding each consumer is unbounded whack-a-mole;
//   the real fix is to remove the corrupt troop-stack once, on load.
//
// FIX:
//   On OnGameLoadedEvent (after deserialization, before the map screen builds),
//   scan every party's member + prisoner roster and remove any troop-stack whose
//   CharacterObject is null or whose Culture is null (the un-priceable/un-
//   renderable leftover). One pass clears the whole NRE cascade; the parties are
//   otherwise untouched. Idempotent -- a clean save removes nothing.

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.SaveRepair;

public sealed class RosterRepairBehavior : CampaignBehaviorBase
{
    private const string Tag = "RosterRepair";

    public override void RegisterEvents()
    {
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
    }

    public override void SyncData(IDataStore dataStore) { }

    private void OnGameLoaded(CampaignGameStarter starter)
    {
        try { RepairAllParties(); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, "OnGameLoaded", ex); } catch { } }
    }

    private static void RepairAllParties()
    {
        int removedStacks = 0, removedHeadcount = 0, parties = 0, unremovable = 0;
        List<MobileParty> all;
        try { all = MobileParty.All?.ToList() ?? new List<MobileParty>(); }
        catch { return; }

        foreach (var party in all)
        {
            if (party == null) continue;
            int before = removedStacks;
            removedStacks += Scrub(party.MemberRoster, party, ref removedHeadcount, ref unremovable);
            removedStacks += Scrub(party.PrisonRoster, party, ref removedHeadcount, ref unremovable);
            if (removedStacks > before) parties++;
        }

        if (removedStacks > 0 || unremovable > 0)
            DiagLog.Log(Tag, $"removed {removedStacks} corrupt troop-stack(s) ({removedHeadcount} troops, null CharacterObject/Culture) from {parties} party(ies); {unremovable} unremovable null-character stack(s). Clears the leftover-troop NRE cascade (map/party screen).");
        else
            DiagLog.Log(Tag, "rosters clean; no corrupt troops found.");
    }

    private static int Scrub(TroopRoster? roster, MobileParty party, ref int headcount, ref int unremovable)
    {
        if (roster == null) return 0;

        var corrupt = new List<CharacterObject>();
        for (int i = 0; i < roster.Count; i++)
        {
            CharacterObject? ch;
            bool bad;
            try
            {
                TroopRosterElement el = roster.GetElementCopyAtIndex(i);
                ch = el.Character;
                bad = ch == null || ch.Culture == null;
            }
            catch { ch = null; bad = true; } // dereferencing the element/culture threw -> corrupt

            if (bad)
            {
                if (ch != null) corrupt.Add(ch);
                else unremovable++; // null CharacterObject: RemoveTroop can't target it
            }
        }

        int removedStacks = 0;
        foreach (var ch in corrupt)
        {
            try
            {
                int count = roster.GetTroopCount(ch);
                roster.RemoveTroop(ch, count);
                headcount += count;
                removedStacks++;
                DiagLog.Log(Tag, $"  removed '{SafeId(ch)}' x{count} from {party.StringId} ({(roster == party.PrisonRoster ? "prisoners" : "members")})");
            }
            catch (Exception ex) { try { DiagLog.LogCaught(Tag, $"RemoveTroop({SafeId(ch)}) from {party.StringId}", ex); } catch { } }
        }
        return removedStacks;
    }

    private static string SafeId(CharacterObject ch)
    {
        try { return ch.StringId ?? "<null-id>"; } catch { return "<throwing-id>"; }
    }
}
