// BetaDeps.MCM -- MusicRowVM
//
// One row in the BYO music-picker section injected into the native Options >
// Sound tab (below the volume sliders). Each row is a MusicContext (Menu,
// Campaign*, Battle*, Siege, Naval, Settlement*) with: enable checkbox, a
// shuffle/sequential toggle, a volume slider, and a read-only track count.
//
// Writes straight back into MusicConfig.Current's per-context ContextSettings.
// Settlement contexts honor changes live (SettlementMusicManager re-reads the
// settings each tick); PSAI contexts (menu/campaign/battle/...) apply on the
// next launch when the soundtrack is regenerated -- the documented MVP behavior.
//
// A real ViewModel so its @bindings / Command resolve natively once the
// ItemTemplate hands the row to the widget (same pattern as ModRowVM).
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

using Bannerlord.UIExtenderEx.Attributes;

using BetaDeps.Foundation;
using BetaDeps.Harmony.Music;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

public sealed class MusicRowVM : ViewModel
{
    private const string Tag = "MusicRowVM";

    private readonly MusicContext _ctx;

    public MusicRowVM(MusicContext ctx)
    {
        _ctx = ctx;
        DisplayName = Label(ctx);
    }

    [DataSourceProperty] public string DisplayName { get; }

    private ContextSettings Settings =>
        MusicConfig.Current?.SettingsFor(_ctx) ?? new ContextSettings();

    private int TrackCount =>
        MusicConfig.Current?.PoolFor(_ctx)?.Count ?? 0;

    [DataSourceProperty]
    public string TrackCountText
    {
        get
        {
            var n = TrackCount;
            if (n > 0) return n == 1 ? "1 track" : n + " tracks";
            // Short, single-ish line; the .ogg/.wav + relaunch guidance lives in
            // the section footer so rows stay compact and don't overlap.
            return "(empty) BYO/" + _ctx.FolderRelativePath();
        }
    }

    [DataSourceProperty]
    public bool EnableValue
    {
        get => Settings.Enabled;
        set
        {
            try
            {
                var s = MusicConfig.Current?.SettingsFor(_ctx);
                if (s == null || s.Enabled == value) return;
                s.Enabled = value;
                OnPropertyChanged(nameof(EnableValue));
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"EnableValue({_ctx})", ex); }
        }
    }

    /// <summary>True = Sequential, false = Shuffle (mirrors PlaybackMode).</summary>
    [DataSourceProperty]
    public bool SequentialValue
    {
        get => Settings.Mode == PlaybackMode.Sequential;
        set
        {
            try
            {
                var s = MusicConfig.Current?.SettingsFor(_ctx);
                if (s == null) return;
                var mode = value ? PlaybackMode.Sequential : PlaybackMode.Shuffle;
                if (s.Mode == mode) return;
                s.Mode = mode;
                // Apply to the live pool so settlement playback honors the change
                // immediately (PSAI contexts ignore mode -- their cursor is unused).
                MusicConfig.Current?.PoolFor(_ctx)?.ApplyMode(mode);
                OnPropertyChanged(nameof(SequentialValue));
                OnPropertyChanged(nameof(ModeText));
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"SequentialValue({_ctx})", ex); }
        }
    }

    [DataSourceProperty] public string ModeText => SequentialValue ? "Sequential" : "Shuffle";

    /// <summary>0..100 slider value mapped to ContextSettings.VolumeTrim (0..1).</summary>
    [DataSourceProperty]
    public float VolumeValue
    {
        get => Clamp01(Settings.VolumeTrim) * 100f;
        set
        {
            try
            {
                var s = MusicConfig.Current?.SettingsFor(_ctx);
                if (s == null) return;
                var trim = Clamp01(value / 100f);
                if (Math.Abs(s.VolumeTrim - trim) < 0.0001f) return;
                s.VolumeTrim = trim;
                OnPropertyChanged(nameof(VolumeValue));
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"VolumeValue({_ctx})", ex); }
        }
    }

    /// <summary>Naval row hides unless War Sails is installed.</summary>
    [DataSourceProperty]
    public bool IsRowVisible
    {
        get
        {
            if (_ctx != MusicContext.Naval) return true;
            var modulesRoot = MusicConfig.Current?.ModuleDir is { Length: > 0 } md
                ? System.IO.Path.GetDirectoryName(md) : null;
            return NavalGate.IsAvailable(modulesRoot);
        }
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    private static string Label(MusicContext ctx) => ctx switch
    {
        MusicContext.Menu => "Main Menu",
        MusicContext.CampaignPeace => "Campaign - Peace",
        MusicContext.CampaignWar => "Campaign - War",
        MusicContext.CampaignDark => "Campaign - Low Morale",
        MusicContext.BattleSmall => "Battle - Small",
        MusicContext.BattleMedium => "Battle - Medium",
        MusicContext.BattleLarge => "Battle - Large",
        MusicContext.Siege => "Siege",
        MusicContext.Victory => "Victory",
        MusicContext.Defeat => "Defeat",
        MusicContext.Naval => "Naval (War Sails)",
        MusicContext.SettlementTown => "Settlement - Town",
        MusicContext.SettlementVillage => "Settlement - Village",
        MusicContext.SettlementTavern => "Settlement - Tavern",
        _ => ctx.ToString(),
    };
}
