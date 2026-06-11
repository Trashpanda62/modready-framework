// BetaDeps.Harmony -- MusicContext
//
// The BYO music-picker's per-context taxonomy. Each value is a game
// situation the player can supply their own tracks for. The on-disk
// folder Modules\BetaDeps\Music\BYO\<Context>\ is the data model; this
// enum is the typed handle the rest of the music stack passes around.
//
// Two delivery paths sit behind these contexts (see V1.0-MUSIC-PICKER-PLAN.md):
//   - PSAI redirect    : Menu .. Naval  (every PSAI-driven context)
//   - Engine.Music loose: SettlementTown / Village / Tavern  (FMOD bard,
//                         PSAI can't reach it; settlements are the only
//                         place a loose Engine.Music channel is free)
//
// MusicThemeMap maps a vanilla TaleWorlds MusicTheme id onto one of the
// PSAI-path values here and back onto our custom 9000N theme id.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

namespace BetaDeps.Harmony.Music;

public enum MusicContext
{
    // ---- PSAI-redirect contexts (custom theme id = 9000 + LocalThemeId) ----
    Menu = 0,
    CampaignPeace,
    CampaignWar,
    CampaignDark,
    BattleSmall,
    BattleMedium,
    BattleLarge,
    Siege,
    Victory,
    Defeat,
    Naval,

    // ---- Engine.Music loose-file contexts (no PSAI theme) ----
    SettlementTown,
    SettlementVillage,
    SettlementTavern,
}

public static class MusicContextExtensions
{
    /// <summary>
    /// Folder name for this context under Music\BYO\. Settlement contexts
    /// nest under a "Settlement\" parent to match the picker's grouping and
    /// the plan's folder convention (Settlement\Town, Settlement\Village,
    /// Settlement\Tavern). Everything else is a single flat folder.
    /// </summary>
    public static string FolderRelativePath(this MusicContext ctx) => ctx switch
    {
        MusicContext.SettlementTown    => "Settlement/Town",
        MusicContext.SettlementVillage => "Settlement/Village",
        MusicContext.SettlementTavern  => "Settlement/Tavern",
        _ => ctx.ToString(),
    };

    /// <summary>
    /// True when this context is delivered through the PSAI redirect path
    /// (a generated 9000N theme), false when it uses the Engine.Music
    /// loose-file channel (settlements). Only PSAI-path contexts get a
    /// Theme written into the generated soundtrack.xml.
    /// </summary>
    public static bool IsPsaiPath(this MusicContext ctx)
        => ctx < MusicContext.SettlementTown;

    /// <summary>
    /// Whether this context is keyed by culture in vanilla (settlements are;
    /// the picker ships per-culture subfolders only there, with a _generic
    /// fallback). Non-settlement contexts may still carry an optional culture
    /// subfolder but default to a flat pool.
    /// </summary>
    public static bool IsCultureKeyed(this MusicContext ctx)
        => ctx >= MusicContext.SettlementTown;

    /// <summary>
    /// Local theme id (1-based) for PSAI-path contexts. This is the value written
    /// to soundtrack.xml as &lt;Id&gt;; PSAI prepends the "9000" ModuleIdPrefix via
    /// int.Parse("9000" + localId) string-concat at load time to get the effective
    /// id (see <see cref="CustomThemeId"/>). Returns -1 for settlement contexts,
    /// which have no PSAI theme.
    /// </summary>
    public static int LocalThemeId(this MusicContext ctx)
        => ctx.IsPsaiPath() ? ((int)ctx + 1) : -1;

    /// <summary>
    /// Effective PSAI theme id for PSAI-path contexts, or -1 for settlement
    /// contexts. This MUST mirror PSAI's own load-time mangling exactly:
    /// int.Parse("9000" + localId) string-concat (decompiled PsaiProject:
    /// "theme.Id = int.Parse(ModuleIdPrefix + theme.Id)"). Do NOT "simplify" to
    /// 90000 + localId arithmetic: for two-digit local ids the concat is
    /// non-contiguous -- Defeat (local 10) -> 900010 and Naval (local 11) -> 900011,
    /// NOT 90010/90011. Arithmetic would return ids PSAI never registered, so the
    /// redirect would silently miss those two themes. Because the set is
    /// non-contiguous, IsCustomThemeId is derived from these values rather than a
    /// [min,max] range.
    /// </summary>
    public static int CustomThemeId(this MusicContext ctx)
    {
        var local = ctx.LocalThemeId();
        return local < 0 ? -1 : int.Parse("9000" + local.ToString());
    }
}
