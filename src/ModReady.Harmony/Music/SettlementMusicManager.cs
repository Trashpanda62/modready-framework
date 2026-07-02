// ModReady.Harmony -- SettlementMusicManager (BYO music-picker workstream C3)
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

using ModReady.Foundation;

namespace ModReady.Harmony.Music;

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
    private static bool _disabled;          // hard-off for the session (Engine.Music API missing / repeated start failures)
    private static bool _sawPlaying;        // have we observed IsMusicPlaying==true since the current clip was issued?
    private static int _ticksSinceStart;    // ticks since the current clip was issued (async-start grace window)
    private static int _startFailures;      // consecutive failed clip starts

    private const int StartGraceTicks = 60; // ~1s @60fps: wait this long for an async clip to start before advancing
    private const int MaxStartFailures = 5; // give up settlement playback after this many consecutive start failures

    /// <summary>Wire the config in. Called from the SubModule once MusicConfig loads.
    /// Whether settlement music actually runs is decided LIVE each tick (see Pump),
    /// not snapshotted here, so enabling a settlement context mid-session takes effect.</summary>
    public static void Install(MusicConfig cfg)
    {
        _config = cfg;
        DiagLog.Log(Tag, "installed; manages the Engine.Music channel whenever a settlement BYO context is enabled.");
    }

    /// <summary>
    /// Tick entry. No-op until the engine types resolve; then drives the
    /// per-settlement channel state machine. Fully guarded.
    /// </summary>
    public static void Pump()
    {
        if (_config == null || _disabled) return;

        // Cheap LIVE gate, re-evaluated every tick (so a mid-session Options toggle
        // is honored): only do settlement work when at least one settlement context
        // is enabled + non-empty. IsActive is a dictionary lookup -- no game reflection,
        // so this is a negligible per-frame cost when the feature isn't in use.
        bool anyActive = _config.IsActive(MusicContext.SettlementTown)
                      || _config.IsActive(MusicContext.SettlementVillage)
                      || _config.IsActive(MusicContext.SettlementTavern);
        if (!anyActive)
        {
            if (_channel >= 0) ReleaseChannel();   // all settlement contexts disabled mid-visit
            return;
        }

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
            else if (IsMusicPlaying(_channel))
            {
                _sawPlaying = true;   // clip is up; now a later false transition means it truly ended
            }
            else if (_sawPlaying)
            {
                // We saw it playing and now it isn't -> clip ended naturally; advance.
                if (!PlayNext(ctx)) { ReleaseChannel(); NoteStartFailure(ctx); }
            }
            else if (++_ticksSinceStart > StartGraceTicks)
            {
                // The clip we issued never reported playing within the grace window
                // (async load is slow / failed). Advance rather than treating
                // "not started yet" as "ended" and skipping a track every frame.
                DiagLog.Log(Tag, $"{ctx}: clip did not start within grace; advancing.");
                if (!PlayNext(ctx)) { ReleaseChannel(); NoteStartFailure(ctx); }
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
            if (!PlayNext(ctx))
            {
                // Couldn't start the first track (engine threw) -> release the
                // channel instead of holding it idle and spinning on it every tick.
                ReleaseChannel();
                NoteStartFailure(ctx);
                return;
            }
            // Silence the PSAI campaign/menu theme so it doesn't play UNDER the settlement
            // track (the "you hear both" overlap bug).
            PsaiRedirectManager.SetSettlementSuspended(true);
            DiagLog.Log(Tag, $"entered {ctx}; playing BYO music on Engine.Music channel {ch}.");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"AcquireAndPlay({ctx})", ex); }
    }

    /// <summary>Load + start the next pool track on the current channel. Returns true
    /// on a successful start, false on an empty pool or an engine throw (caller decides
    /// whether to release). Resets the async-start grace state on success.</summary>
    private static bool PlayNext(MusicContext ctx)
    {
        try
        {
            var pool = _config!.PoolFor(ctx);
            var track = pool?.Next();
            if (string.IsNullOrEmpty(track)) return false;   // empty pool: leave vanilla bard

            LoadClip(_channel, track!);
            var trim = _config.SettingsFor(ctx).VolumeTrim;
            SetVolume(_channel, trim);
            PlayMusic(_channel);
            _sawPlaying = false;        // wait until we observe it playing before honoring an end-of-clip
            _ticksSinceStart = 0;
            _startFailures = 0;         // a successful start clears the consecutive-failure count
            return true;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"PlayNext({ctx})", ex); return false; }
    }

    /// <summary>Count a failed clip start; after too many in a row, give up settlement
    /// playback for the session rather than thrash the channel + flood the log.</summary>
    private static void NoteStartFailure(MusicContext ctx)
    {
        if (++_startFailures < MaxStartFailures) return;
        _disabled = true;
        DiagLog.Log(Tag, $"settlement playback failed to start {_startFailures}x ({ctx}); disabling for this session.");
    }

    private static void ReleaseChannel()
    {
        try
        {
            // Restore PSAI's volume (undo the settlement mute) before we let go of the
            // channel, so the campaign/menu theme comes back at its previous level.
            PsaiRedirectManager.SetSettlementSuspended(false);
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
            _sawPlaying = false;
            _ticksSinceStart = 0;
        }
    }

    // Thin reflection invokers. ResolveReflection guarantees the required ones are
    // non-null before Pump uses them, but that invariant lives in another method;
    // these guard explicitly so a future caller that bypasses ResolveReflection
    // degrades to a safe no-op/default instead of a NullReferenceException.
    private static int GetFreeChannel() => _getFreeChannel == null ? -1 : Convert.ToInt32(_getFreeChannel.Invoke(null, null) ?? -1);
    private static void LoadClip(int ch, string path) => _loadClip?.Invoke(null, new object[] { ch, path });
    private static bool IsMusicPlaying(int ch) => _isMusicPlaying?.Invoke(null, new object[] { ch }) is bool b && b;
    private static void PlayMusic(int ch) => _playMusic?.Invoke(null, new object[] { ch });
    private static void StopMusic(int ch) => _stopMusic?.Invoke(null, new object[] { ch });
    private static void UnloadClip(int ch) => _unloadClip?.Invoke(null, new object[] { ch });
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
                _disabled = true;
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
