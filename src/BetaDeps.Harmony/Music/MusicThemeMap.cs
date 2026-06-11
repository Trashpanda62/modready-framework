// BetaDeps.Harmony -- MusicThemeMap
//
// The bridge between TaleWorlds' vanilla MusicTheme enum and our BYO
// MusicContext taxonomy. The PSAI redirect prefix (PsaiRedirectManager)
// takes the integer theme id the game asked PSAI to play, looks up which
// of our contexts it belongs to, and -- if that context has BYO tracks --
// rewrites the request to our custom 9000N theme id.
//
// The vanilla MusicTheme values are reproduced here as integer constants
// straight from the decompiled TaleWorlds.MountAndBlade enum (see
// decomp/TaleWorlds.MountAndBlade, "public enum MusicTheme"). We key on the
// raw int rather than the enum type so this assembly takes no compile-time
// dependency on the enum's member set (which drifts across game versions);
// an unknown id simply falls through to "no redirect".
//
// Battle-size note: vanilla doesn't cleanly separate small/medium/large for
// every theme. We map the explicit BattleSmall/BattleMedium ids, treat the
// "shock" themes as Large, and bucket the remaining combat/pagan/turns
// variants as Medium. Good enough for MVP; the picker folder convention lets
// a user fill a flat Battle pool that all three sizes draw from once U1 wires
// per-size enable.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System.Collections.Generic;

namespace BetaDeps.Harmony.Music;

public static class MusicThemeMap
{
    // Our generated themes occupy 90001..90011 (prefix 9000 + local id 1..11).
    public const int CustomThemeIdMin = 90001;
    public const int CustomThemeIdMax = 90099;

    // Vanilla MusicTheme enum values (from the decompiled enum).
    private static readonly Dictionary<int, MusicContext> Map = new()
    {
        // ---- Menu ----
        { 5,     MusicContext.Menu },          // MainTheme
        { 10244, MusicContext.Menu },          // NavalMainTheme (menu theme when War Sails active)

        // ---- Campaign (peace / standard) ----
        { 1,     MusicContext.CampaignPeace }, // CampaignStandard
        { 28,    MusicContext.CampaignPeace }, // AseraiCampaignA
        { 29,    MusicContext.CampaignPeace }, // BattaniaCampaignA
        { 30,    MusicContext.CampaignPeace }, // EmpireCampaignA
        { 31,    MusicContext.CampaignPeace }, // EmpireCampaignB
        { 32,    MusicContext.CampaignPeace }, // KhuzaitCampaignA
        { 33,    MusicContext.CampaignPeace }, // SturgiaCampaignA
        { 34,    MusicContext.CampaignPeace }, // VlandiaCampaignA
        { 10247, MusicContext.CampaignPeace }, // NordCampaign

        // ---- Campaign (war / dramatic / tension) ----
        { 3,     MusicContext.CampaignWar },   // AseraiCampaignDramatic
        { 8,     MusicContext.CampaignWar },   // StealthA
        { 16,    MusicContext.CampaignWar },   // BattaniaCampaignDramatic
        { 18,    MusicContext.CampaignWar },   // SturgiaCampaignDramatic
        { 20,    MusicContext.CampaignWar },   // KhuzaitCampaignDramatic
        { 22,    MusicContext.CampaignWar },   // EmpireCampaignDramatic
        { 24,    MusicContext.CampaignWar },   // VlandiaCampaignDramatic

        // ---- Campaign (dark / low morale) ----
        { 7,     MusicContext.CampaignDark },  // CampaignDark

        // ---- Battle: small ----
        { 12,    MusicContext.BattleSmall },   // BattleSmall

        // ---- Battle: medium (default combat bucket) ----
        { 6,     MusicContext.BattleMedium },  // BattlePaganB
        { 9,     MusicContext.BattleMedium },  // BattleTurnsNegative
        { 10,    MusicContext.BattleMedium },  // BattleMedium
        { 11,    MusicContext.BattleMedium },  // BattleTurnsPositive
        { 27,    MusicContext.BattleMedium },  // BattlePaganA
        { 35,    MusicContext.BattleMedium },  // CombatB
        { 36,    MusicContext.BattleMedium },  // CombatA
        { 39,    MusicContext.BattleMedium },  // PaganTurnsNegative
        { 40,    MusicContext.BattleMedium },  // PaganTurnsPositive
        { 10246, MusicContext.BattleMedium },  // BattleNord

        // ---- Battle: large (shock = highest intensity) ----
        { 14,    MusicContext.BattleLarge },   // BattlePositiveShock
        { 15,    MusicContext.BattleLarge },   // BattleNegativeShock

        // ---- Siege ----
        { 13,    MusicContext.Siege },         // BattleSiege
        { 38,    MusicContext.Siege },         // PaganSiege

        // ---- Victory ----
        { 2,     MusicContext.Victory },       // BattleVictory
        { 4,     MusicContext.Victory },       // AseraiVictory
        { 17,    MusicContext.Victory },       // BattaniaVictory
        { 19,    MusicContext.Victory },       // SturgiaVictory
        { 21,    MusicContext.Victory },       // KhuzaitVictory
        { 23,    MusicContext.Victory },       // EmpireVictory
        { 25,    MusicContext.Victory },       // VlandiaVictory

        // ---- Defeat ----
        { 26,    MusicContext.Defeat },        // BattleDefeat

        // ---- Naval (War Sails combat + sea campaign sailing) ----
        { 10241, MusicContext.Naval },         // VikingSeaBattle1
        { 10242, MusicContext.Naval },         // VikingSeaBattle2
        { 10243, MusicContext.Naval },         // MediterraneanSeaBattle1
        { 10248, MusicContext.Naval },         // SeaCampaignNorthern
        { 10249, MusicContext.Naval },         // SeaCampaignSouthern
    };

    /// <summary>
    /// Map a vanilla MusicTheme id to one of our PSAI-path contexts. Returns
    /// false for unknown ids and for any of our own custom ids (so the
    /// redirect prefix never recurses on a request it just rewrote).
    /// </summary>
    public static bool TryGetContext(int vanillaThemeId, out MusicContext context)
    {
        if (IsCustomThemeId(vanillaThemeId)) { context = default; return false; }
        return Map.TryGetValue(vanillaThemeId, out context);
    }

    /// <summary>True when the id is one of our generated 9000N themes.</summary>
    public static bool IsCustomThemeId(int themeId)
        => themeId >= CustomThemeIdMin && themeId <= CustomThemeIdMax;
}
