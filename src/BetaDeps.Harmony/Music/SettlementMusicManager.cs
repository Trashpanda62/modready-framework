// BetaDeps.Harmony -- SettlementMusicManager (BYO music-picker workstream C3)
//
// The settlement half of the non-destructive music picker. Town/Village/Tavern
// music in Bannerlord is FMOD-event-based (the musician bank), NOT PSAI, so the
// PSAI redirect (PsaiRedirectManager) can't reach it. The only non-destructive
// door is TaleWorlds.Engine.Music: inside a settlement a loose Engine.Music
// channel is free (proven Spike #2e/#4), so we LoadClip the user's BYO track
// onto it and advance track-by-track while the player stays in the settlement.
//
// Tick-driven (Pump(), called every frame from the SubModule, same pattern as
// PsaiRedirectManager): detect the current settlement context, acquire a channel
// on entry, advance when the clip ends (IsMusicPlaying transitions false), and
// release on exit. ALL engine access is reflection + try/catch -- this is a
// music nicety and must never take the game down.
//
// Confirmed Engine.Music API (Spike #4): GetFreeMusicChannelIndex, LoadClip,
// IsClipLoaded, IsMusicPlaying, PlayMusic, StopMusic, UnloadClip, SetVolume.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

using BetaDeps.Foundation;

namespace BetaDeps.Harmony.Music;

public static class SettlementMusicManager
{
    private const string Tag = "SettlementMusic";

    private static MusicConfig? _config;

    // Engine.Music reflection surface (resolved lazily on the tick).
    private static Type? _engineMusicType;
    private static MethodInfo? _getFreeChannel;
    private static MethodInfo? _loadClip;
    private static MethodInfo? _isMusicPlaying;
    private static MethodInfo? _playMusic;
    private static MethodInfo? _stopMusic;
    private static MethodInfo? _unloadClip;
    private static MethodInfo? _setVolume;
    private static bool _engineResolveLogged;

    // Campaign / Settlement reflection surface.
    private static Type? _campaignType;
    private static PropertyInfo? _campaignCurrent;
    private static Type? _settlementType;
    private static PropertyInfo? _settlementCurrent;

    // Active-playback state.
    private static MusicContext _currentContext = (MusicContext)(-1);
    private static int _channel = -1;
    private static bool _armed;

    /// <summary>Wire the config in. Called from the SubModule once MusicConfig loads.</summary>
    public static void Install(MusicConfig cfg)
    {
        _config = cfg;
        // Only worth running if at least one settlement context has tracks.
        _armed = cfg != null && (
            cfg.IsActive(MusicContext.SettlementTown) ||
            cfg.IsActive(MusicContext.SettlementVillage) ||
            cfg.IsActive(MusicContext.SettlementTavern));
        if (_armed)
            DiagLog.Log(Tag, "armed: BYO settlement tracks present; will manage the Engine.Music channel in settlements.");
    }

    /// <summary>
    /// Tick entry. No-op until the engine types resolve; then drives the
    /// per-settlement channel state machine. Fully guarded.
    /// </summary>
    public static void Pump()
    {
        if (!_armed || _config == null) return;
        try
        {
            if (!ResolveReflection()) return;

            var ctx = DetectActiveContext();   // a settlement context with tracks, or -1

            // Context changed (left a settlement, moved town->tavern, etc.): release.
            if (_channel >= 0 && ctx != _currentContext)
                ReleaseChannel();

            if (ctx == (MusicContext)(-1))
                return;   // not in a BYO settlement context

            if (_channel < 0)
            {
                // Entered an active settlement context: grab a channel + start.
                AcquireAndPlay(ctx);
            }
            else if (!IsMusicPlaying(_channel))
            {
                // Clip ended naturally -> advance to the next track in the pool.
                PlayNext(ctx);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Pump", ex);
        }
    }

    // ---------------------------------------------------------------
    // Context detection
    // ---------------------------------------------------------------

    /// <summary>
    /// The active settlement context (Town/Village/Tavern) when the player is in
    /// a settlement whose BYO pool is enabled+non-empty; otherwise (MusicContext)-1.
    /// </summary>
    private static MusicContext DetectActiveContext()
    {
        try
        {
            // No campaign -> no settlement music (Settlement.CurrentSettlement
            // throws outside a campaign; guard via Campaign.Current first).
            if (_campaignCurrent?.GetValue(null) == null) return (MusicContext)(-1);
            var settlement = _settlementCurrent?.GetValue(null);
            if (settlement == null) return (MusicContext)(-1);

            bool isTown = GetBoolProp(settlement, "IsTown");
            bool isVillage = GetBoolProp(settlement, "IsVillage");
            bool inTavern = IsInTavern();

            var ctx = ClassifySettlement(isTown, isVillage, inTavern);
            if (ctx == null) return (MusicContext)(-1);
            return _config!.IsActive(ctx.Value) ? ctx.Value : (MusicContext)(-1);
        }
        catch
        {
            return (MusicContext)(-1);
        }
    }

    /// <summary>
    /// Pure mapping from the gathered settlement signals to a BYO context. Tavern
    /// wins over Town when the player is in the tavern location; village maps to
    /// the village pool; everything else town. Returns null when not a settlement
    /// we cover. Pure + unit-tested off-engine.
    /// </summary>
    public static MusicContext? ClassifySettlement(bool isTown, bool isVillage, bool inTavern)
    {
        if (inTavern && (isTown || !isVillage)) return MusicContext.SettlementTavern;
        if (isVillage) return MusicContext.SettlementVillage;
        if (isTown) return MusicContext.SettlementTown;
        return null;
    }

    /// <summary>
    /// Best-effort tavern detection: is the current campaign "location" the tavern?
    /// Resolved by reflection on the active location's StringId; falls back to false
    /// (so the player just gets Town music in a tavern) rather than guessing wrong.
    /// </summary>
    private static bool IsInTavern()
    {
        try
        {
            // CampaignMission.Current?.Location?.StringId == "tavern" (or contains it).
            var camType = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.CampaignSystem.CampaignMission");
            var cur = camType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var loc = cur?.GetType().GetProperty("Location")?.GetValue(cur);
            var sid = loc?.GetType().GetProperty("StringId")?.GetValue(loc) as string;
            return !string.IsNullOrEmpty(sid)
                   && sid!.IndexOf("tavern", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    private static bool GetBoolProp(object obj, string name)
    {
        try
        {
            var v = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);
            return v is bool b && b;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------
    // Channel management (Engine.Music via reflection)
    // ---------------------------------------------------------------

    private static void AcquireAndPlay(MusicContext ctx)
    {
        try
        {
            int ch = GetFreeChannel();
            if (ch < 0)
            {
                // PSAI still holds every channel (e.g. transient on entry). Try again next tick.
                return;
            }
            _channel = ch;
            _currentContext = ctx;
            DiagLog.Log(Tag, $"entered {ctx}; acquired Engine.Music channel {ch}.");
            PlayNext(ctx);
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"AcquireAndPlay({ctx})", ex); }
    }

    private static void PlayNext(MusicContext ctx)
    {
        try
        {
            var pool = _config!.PoolFor(ctx);
            var track = pool?.Next();
            if (string.IsNullOrEmpty(track)) return;   // empty pool: leave vanilla bard

            LoadClip(_channel, track!);
            var trim = _config.SettingsFor(ctx).VolumeTrim;
            SetVolume(_channel, trim);
            PlayMusic(_channel);
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"PlayNext({ctx})", ex); }
    }

    private static void ReleaseChannel()
    {
        try
        {
            if (_channel >= 0)
            {
                StopMusic(_channel);
                UnloadClip(_channel);
                DiagLog.Log(Tag, $"left {_currentContext}; released channel {_channel}.");
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ReleaseChannel", ex); }
        finally
        {
            _channel = -1;
            _currentContext = (MusicContext)(-1);
        }
    }

    // Thin reflection invokers (all guarded by ResolveReflection having run).
    private static int GetFreeChannel() => Convert.ToInt32(_getFreeChannel!.Invoke(null, null) ?? -1);
    private static void LoadClip(int ch, string path) => _loadClip!.Invoke(null, new object[] { ch, path });
    private static bool IsMusicPlaying(int ch) => _isMusicPlaying!.Invoke(null, new object[] { ch }) is bool b && b;
    private static void PlayMusic(int ch) => _playMusic!.Invoke(null, new object[] { ch });
    private static void StopMusic(int ch) => _stopMusic!.Invoke(null, new object[] { ch });
    private static void UnloadClip(int ch) => _unloadClip!.Invoke(null, new object[] { ch });
    private static void SetVolume(int ch, float v)
    {
        try { _setVolume?.Invoke(null, new object[] { ch, v }); } catch { /* SetVolume is optional */ }
    }

    // ---------------------------------------------------------------
    // Reflection resolution (retry each tick until the types are loaded)
    // ---------------------------------------------------------------

    private static bool ResolveReflection()
    {
        if (_engineMusicType != null && _settlementType != null) return true;
        try
        {
            _engineMusicType ??= ReflectionUtils.ResolveTypeByFullName("TaleWorlds.Engine.Music");
            _campaignType ??= ReflectionUtils.ResolveTypeByFullName("TaleWorlds.CampaignSystem.Campaign");
            _settlementType ??= ReflectionUtils.ResolveTypeByFullName("TaleWorlds.CampaignSystem.Settlements.Settlement");

            if (_engineMusicType == null || _campaignType == null || _settlementType == null)
            {
                if (!_engineResolveLogged)
                {
                    _engineResolveLogged = true;
                    DiagLog.Log(Tag, "Engine.Music / Campaign types not loaded yet; will keep watching.");
                }
                return false;
            }

            const BindingFlags S = BindingFlags.Public | BindingFlags.Static;
            _getFreeChannel ??= _engineMusicType.GetMethod("GetFreeMusicChannelIndex", S);
            _loadClip ??= _engineMusicType.GetMethod("LoadClip", S);
            _isMusicPlaying ??= _engineMusicType.GetMethod("IsMusicPlaying", S);
            _playMusic ??= _engineMusicType.GetMethod("PlayMusic", S);
            _stopMusic ??= _engineMusicType.GetMethod("StopMusic", S);
            _unloadClip ??= _engineMusicType.GetMethod("UnloadClip", S);
            _setVolume ??= _engineMusicType.GetMethod("SetVolume", S);
            _campaignCurrent ??= _campaignType.GetProperty("Current", S);
            _settlementCurrent ??= _settlementType.GetProperty("CurrentSettlement", S);

            // The methods we actually require to function.
            if (_getFreeChannel == null || _loadClip == null || _isMusicPlaying == null ||
                _playMusic == null || _stopMusic == null || _unloadClip == null ||
                _campaignCurrent == null || _settlementCurrent == null)
            {
                DiagLog.Log(Tag, "Engine.Music / Settlement API surface incomplete; settlement music disabled this session.");
                _armed = false;
                return false;
            }
            DiagLog.Log(Tag, "Engine.Music + Settlement reflection resolved.");
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ResolveReflection", ex);
            return false;
        }
    }
}
