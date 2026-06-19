// BetaDeps.Harmony -- PsaiRedirectManager
//
// The PSAI half of the BYO music picker (workstream C2). It does three
// things, all reflection-only against TaleWorlds.PSAI.dll (no compile-time
// reference -- psai.net types aren't in the reference assemblies and their
// shape can drift):
//
//   1. Install() -- generate Music\soundtrack.xml from MusicConfig and install
//      a Harmony prefix on psai.net.PsaiCore.TriggerMusicTheme(int, float).
//   2. TryLoadRuntime() -- once PSAI is fully initialized, call
//      PsaiCore.LoadSoundtrackFromProjectFile(["Native", "NavalDLC"?, "BetaDeps"])
//      so our generated 9000N themes are merged into the live soundtrack.
//      Driven from the SubModule tick until it succeeds once.
//   3. The prefix -- when the game asks PSAI to play a vanilla theme whose
//      MusicContext has BYO tracks, rewrite the requested themeId (by ref) to
//      our custom 9000N id and let the original run. PSAI then plays the user's
//      pool and keeps doing its own intensity crossfades inside it. Because we
//      own the chokepoint, every re-request re-redirects -- this is what fixes
//      the Spike #1 "reverts after ~1s" problem (the menu re-requested its theme
//      and overrode a raw TriggerMusicTheme call).
//
// Re-entrancy is structural, not flag-based: the prefix never rewrites an id
// that's already in our 9000N range (MusicThemeMap.IsCustomThemeId), so the
// original running with our id doesn't bounce back through a redirect.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;

namespace BetaDeps.Harmony.Music;

public static class PsaiRedirectManager
{
    private const string Tag = "PsaiRedirect";
    private const string HarmonyId = "betadeps.music.psai";

    private static MusicConfig? _config;
    private static bool _prepared;                   // Install() ran, soundtrack generated
    private static bool _patchInstalled;             // Harmony prefix attached
    private static bool _reflectionWaitLogged;
    private static bool _runtimeLoaded;
    private static volatile bool _enabled;          // prefix redirects only when true
    private static int _lastRedirectContext = -999;  // log throttle

    // Cached reflection surface (resolved in Install).
    private static Type? _psaiCoreType;
    private static PropertyInfo? _instanceProp;
    private static MethodInfo? _isInitializedMethod;
    private static MethodInfo? _getCurrentThemeIdMethod;
    private static MethodInfo? _loadSoundtrackMethod;
    private static MethodInfo? _triggerMethod;
    private static MethodInfo? _menuModeEnterMethod;
    private static MethodInfo? _menuModeLeaveMethod;

    /// <summary>
    /// Generate the soundtrack from the BYO tree now (it doesn't need PSAI), so
    /// the project file is on disk early. The PSAI reflection bind, the Harmony
    /// patch, and the soundtrack merge are all deferred to Pump() on the tick --
    /// at OnSubModuleLoad time TaleWorlds.PSAI.dll isn't loaded into the
    /// AppDomain yet, so resolving psai.net.PsaiCore here always fails.
    /// </summary>
    public static void Install(MusicConfig cfg)
    {
        if (_prepared) return;
        _prepared = true;
        _config = cfg;

        try
        {
            if (cfg.TotalTracks == 0)
            {
                DiagLog.Log(Tag, "no BYO tracks present; PSAI redirect inert (vanilla music untouched).");
                _config = null;   // nothing for Pump to do
                return;
            }

            var gen = SoundtrackXmlGenerator.Generate(cfg);
            if (!gen.Success || gen.ThemeCount == 0)
            {
                DiagLog.Log(Tag, "soundtrack generation produced no themes; redirect inert.");
                _config = null;
                return;
            }

            DiagLog.Log(Tag, $"{gen.ThemeCount} theme(s) staged on disk; waiting for PSAI to load before patching.");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    /// <summary>
    /// Tick-driven state machine: (1) wait until TaleWorlds.PSAI.dll is loaded
    /// and resolve psai.net.PsaiCore, (2) install the TriggerMusicTheme prefix,
    /// (3) once PSAI is initialized, merge our soundtrack and arm the redirect.
    /// Cheap no-op after step 3 succeeds. Called every frame from the SubModule.
    /// </summary>
    public static void Pump()
    {
        if (_config == null || _runtimeLoaded) return;

        try
        {
            // Step 1: resolve PSAI reflection (retry each tick until the
            // assembly is in the AppDomain).
            if (_psaiCoreType == null)
            {
                if (!ResolvePsaiReflection())
                {
                    if (!_reflectionWaitLogged)
                    {
                        _reflectionWaitLogged = true;
                        DiagLog.Log(Tag, "psai.net.PsaiCore not loaded yet; will keep watching for it.");
                    }
                    return;
                }
                DiagLog.Log(Tag, "psai.net.PsaiCore resolved.");
            }

            // Step 2: install the redirect prefix once.
            if (!_patchInstalled)
            {
                var target = SafeBind.Method(
                    _psaiCoreType!,
                    "TriggerMusicTheme",
                    expectedReturnType: SafeBind.Any,        // wildcard: matches PsaiResult (NOT void)
                    expectedParamCount: 2,
                    expectedParamTypes: new[] { typeof(int), typeof(float) });
                if (target == null)
                {
                    DiagLog.Log(Tag, "TriggerMusicTheme(int,float) not found; redirect disabled.");
                    _config = null;
                    return;
                }

                var harmony = new HarmonyLib.Harmony(HarmonyId);
                var prefix = new HarmonyMethod(typeof(PsaiRedirectManager), nameof(TriggerMusicThemePrefix));
                var postfix = new HarmonyMethod(typeof(PsaiRedirectManager), nameof(TriggerMusicThemePostfix));
                if (!SafeBind.TryPatch(harmony, target, prefix: prefix, postfix: postfix))
                {
                    DiagLog.Log(Tag, "failed to patch TriggerMusicTheme; redirect disabled.");
                    _config = null;
                    return;
                }

                // Also redirect the menu, which routes through MenuModeEnter
                // (NOT TriggerMusicTheme) and so is invisible to the prefix above.
                var menuTarget = SafeBind.Method(
                    _psaiCoreType!,
                    "MenuModeEnter",
                    expectedReturnType: SafeBind.Any,        // wildcard (NOT void)
                    expectedParamCount: 2,
                    expectedParamTypes: new[] { typeof(int), typeof(float) });
                if (menuTarget != null)
                {
                    var menuPrefix = new HarmonyMethod(typeof(PsaiRedirectManager), nameof(MenuModeEnterPrefix));
                    SafeBind.TryPatch(harmony, menuTarget, prefix: menuPrefix);
                }

                // Crucial: inject "BetaDeps" into EVERY LoadSoundtrackFromProjectFile
                // call. The game reloads its soundtrack on campaign init from only
                // the modules it registers (Native/NavalDLC) -- without this our
                // merged 9000N themes get wiped and TriggerMusicTheme returns
                // unknown_theme. The prefix makes our themes survive every reload.
                if (_loadSoundtrackMethod != null)
                {
                    var loadPrefix = new HarmonyMethod(typeof(PsaiRedirectManager), nameof(LoadSoundtrackPrefix));
                    SafeBind.TryPatch(harmony, _loadSoundtrackMethod, prefix: loadPrefix);
                }

                _patchInstalled = true;
                DiagLog.Log(Tag, "redirect prefix+postfix installed (TriggerMusicTheme + MenuModeEnter); awaiting PSAI init to merge soundtrack.");
            }

            // Step 3: merge + arm once PSAI is playing.
            TryLoadRuntime();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Pump", ex);
        }
    }

    /// <summary>
    /// Once PSAI is initialized, merge our soundtrack into the live one and arm
    /// the redirect. Idempotent: returns immediately after the first success.
    /// </summary>
    private static void TryLoadRuntime()
    {
        // Snapshot _config once: it's a mutable static that Pump/Install null out
        // from other code paths on a bind failure, and the prefix runs on PSAI/
        // engine threads (hence _enabled is volatile). Reading the field twice
        // (guard here, deref at IsActive below) could see non-null then null.
        var cfg = _config;
        if (_runtimeLoaded || cfg == null) return;
        if (_loadSoundtrackMethod == null || _instanceProp == null) return;

        try
        {
            if (!IsPsaiReady()) return;

            var instance = _instanceProp.GetValue(null);
            if (instance == null) return;

            // Capture what PSAI is currently playing so we can restart it after
            // the merge -- LoadSoundtrackFromProjectFile rebuilds the soundtrack
            // and resets PSAI to silence, so without a re-trigger the music would
            // simply stop until the next game-driven theme request.
            int prevTheme = -1;
            try
            {
                if (_getCurrentThemeIdMethod != null)
                {
                    var cur = _getCurrentThemeIdMethod.Invoke(instance, null);
                    if (cur is int ci) prevTheme = ci;
                }
            }
            catch { /* best-effort */ }

            var modules = new List<string> { "Native" };
            if (NavalDlcPresent()) modules.Add("NavalDLC");
            modules.Add("BetaDeps");

            _loadSoundtrackMethod.Invoke(instance, new object[] { modules });
            _runtimeLoaded = true;
            _enabled = true;   // arm BEFORE the re-trigger so the prefix redirects it

            // Definitive probe: does PSAI's live soundtrack actually contain our
            // themes right after the merge? Distinguishes "merge didn't persist"
            // from "something strips them later".
            ProbeThemes(instance, "post-merge");

            // Restart in-context so music resumes after the merge reset PSAI to
            // silence. The MENU is special: it runs in PSAI "menu mode", where
            // plain TriggerMusicTheme is ignored (decomp line 3425). So if PSAI
            // was on a menu theme, drive it through the menu's own door
            // (MenuModeLeave + MenuModeEnter) with our custom Menu id. This also
            // serves as the decisive by-ear test that PSAI actually emits audio
            // for a loose-file custom theme.
            bool isMenuTheme = (prevTheme == 5 || prevTheme == 10244);
            bool handled = false;
            if (isMenuTheme && cfg.IsActive(MusicContext.Menu)
                && _menuModeEnterMethod != null && _menuModeLeaveMethod != null)
            {
                try
                {
                    int customMenu = MusicContext.Menu.CustomThemeId();
                    _menuModeLeaveMethod.Invoke(instance, null);
                    _menuModeEnterMethod.Invoke(instance, new object[] { customMenu, 0.5f });
                    handled = true;
                    DiagLog.Log(Tag, $"forced menu via MenuModeEnter(custom {customMenu}).");
                }
                catch (Exception ex) { DiagLog.LogCaught(Tag, "TryLoadRuntime/forceMenu", ex); }
            }
            if (!handled && prevTheme > 0 && _triggerMethod != null)
            {
                try { _triggerMethod.Invoke(instance, new object[] { prevTheme, 0.5f }); }
                catch (Exception ex) { DiagLog.LogCaught(Tag, "TryLoadRuntime/re-trigger", ex); }
            }

            DiagLog.Log(Tag, $"soundtrack merged ([{string.Join(", ", modules)}]); " +
                             $"BYO redirect now ARMED (prevTheme {prevTheme}).");
        }
        catch (Exception ex)
        {
            // Don't spin forever on a hard failure -- mark loaded so we stop,
            // leaving vanilla music intact.
            _runtimeLoaded = true;
            DiagLog.LogCaught(Tag, "TryLoadRuntime", ex);
        }
    }

    public static bool IsArmed => _enabled;

    // ---- the redirect prefix ------------------------------------------

    /// <summary>
    /// Harmony prefix on PsaiCore.TriggerMusicTheme(int themeId, float intensity).
    /// Rewrites themeId (by ref) to our custom 9000N id when the requested
    /// vanilla theme maps to an active BYO context. Returns true so the original
    /// runs with the rewritten id (PSAI keeps its intensity crossfades for free).
    /// Never throws out -- on any error it leaves themeId untouched.
    /// </summary>
    public static bool TriggerMusicThemePrefix(ref int themeId)
    {
        try
        {
            if (!_enabled || _config == null) return true;
            if (MusicThemeMap.IsCustomThemeId(themeId)) return true;          // already ours
            if (!MusicThemeMap.TryGetContext(themeId, out var ctx)) return true;
            if (!_config.IsActive(ctx)) return true;

            int custom = ctx.CustomThemeId();
            if (custom < 0) return true;

            if ((int)ctx != _lastRedirectContext)
            {
                _lastRedirectContext = (int)ctx;
                DiagLog.Log(Tag, $"redirect: vanilla theme {themeId} -> {ctx} (custom {custom}).");
            }
            themeId = custom;
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Harmony prefix on PsaiCore.MenuModeEnter(int menuThemeId, float intensity).
    /// The main menu plays through this method, not TriggerMusicTheme, so without
    /// this patch the menu always plays vanilla. Rewrites the menu theme id to our
    /// Menu BYO theme when it's active.
    /// </summary>
    public static bool MenuModeEnterPrefix(ref int menuThemeId)
    {
        try
        {
            if (!_enabled || _config == null) return true;
            if (MusicThemeMap.IsCustomThemeId(menuThemeId)) return true;
            if (!_config.IsActive(MusicContext.Menu)) return true;

            int custom = MusicContext.Menu.CustomThemeId();
            DiagLog.Log(Tag, $"menu redirect: vanilla theme {menuThemeId} -> Menu (custom {custom}).");
            menuThemeId = custom;
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Harmony prefix on PsaiCore.LoadSoundtrackFromProjectFile(List&lt;string&gt;).
    /// Ensures "BetaDeps" (and "NavalDLC" when installed) is in the module list
    /// for EVERY load -- the game's own reloads omit us, which is what made our
    /// themes vanish (unknown_theme) once a campaign loaded. Mutates the list in
    /// place; idempotent via the Contains guard.
    /// </summary>
    public static void LoadSoundtrackPrefix(object pathToProjectFiles)
    {
        try
        {
            if (pathToProjectFiles is not List<string> list) return;
            string before = string.Join(",", list);
            bool injected = false;
            if (!list.Contains("BetaDeps"))
            {
                if (NavalDlcPresent() && !list.Contains("NavalDLC"))
                    list.Add("NavalDLC");
                list.Add("BetaDeps");
                injected = true;
            }
            // Log EVERY call so we can see who reloads the soundtrack and when.
            DiagLog.Log(Tag, $"LoadSoundtrack call: before=[{before}] injected={injected} after=[{string.Join(",", list)}]");
        }
        catch { }
    }

    /// <summary>
    /// Diagnostic postfix on TriggerMusicTheme: logs the PsaiResult and the
    /// theme PSAI reports as current immediately after, but only for our custom
    /// ids (so we can see whether a redirected trigger was accepted, ignored, or
    /// deferred). Throttled to one line per (themeId, result) change.
    /// </summary>
    public static void TriggerMusicThemePostfix(int themeId, object __result)
    {
        try
        {
            if (!MusicThemeMap.IsCustomThemeId(themeId)) return;
            string result = __result?.ToString() ?? "<null>";
            int current = -2;
            if (_getCurrentThemeIdMethod != null && _instanceProp != null)
            {
                var inst = _instanceProp.GetValue(null);
                if (inst != null && _getCurrentThemeIdMethod.Invoke(inst, null) is int cur) current = cur;
            }
            string key = $"{themeId}:{result}:{current}";
            if (key == _lastPostfixKey) return;
            _lastPostfixKey = key;
            DiagLog.Log(Tag, $"trigger {themeId} -> result={result}, GetCurrentThemeId={current}");

            // On unknown_theme, probe the live soundtrack to see what's actually
            // loaded at this moment vs. what we merged earlier.
            if (result.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                && _instanceProp?.GetValue(null) is { } inst2)
            {
                ProbeThemes(inst2, $"at-trigger-{themeId}");
            }
        }
        catch { }
    }

    private static string _lastPostfixKey = "";

    // ---- reflection plumbing ------------------------------------------

    private static bool ResolvePsaiReflection()
    {
        _psaiCoreType = ReflectionUtils.ResolveTypeByFullName("psai.net.PsaiCore");
        if (_psaiCoreType == null) return false;

        const BindingFlags pubStatic = BindingFlags.Public | BindingFlags.Static;
        const BindingFlags pubInst = BindingFlags.Public | BindingFlags.Instance;

        _instanceProp = _psaiCoreType.GetProperty("Instance", pubStatic);
        _isInitializedMethod = _psaiCoreType.GetMethod("IsInstanceInitialized", pubStatic, null, Type.EmptyTypes, null);
        _getCurrentThemeIdMethod = _psaiCoreType.GetMethod("GetCurrentThemeId", pubInst, null, Type.EmptyTypes, null);
        _loadSoundtrackMethod = _psaiCoreType.GetMethod("LoadSoundtrackFromProjectFile", pubInst, null, new[] { typeof(List<string>) }, null);
        _triggerMethod = _psaiCoreType.GetMethod("TriggerMusicTheme", pubInst, null, new[] { typeof(int), typeof(float) }, null);
        _menuModeEnterMethod = _psaiCoreType.GetMethod("MenuModeEnter", pubInst, null, new[] { typeof(int), typeof(float) }, null);
        _menuModeLeaveMethod = _psaiCoreType.GetMethod("MenuModeLeave", pubInst, null, Type.EmptyTypes, null);

        // Instance + LoadSoundtrack are the must-haves; the readiness probes are
        // best-effort (we degrade to "assume ready once Instance is non-null").
        return _instanceProp != null && _loadSoundtrackMethod != null;
    }

    private static bool IsPsaiReady()
    {
        try
        {
            if (_isInitializedMethod != null)
            {
                var ok = _isInitializedMethod.Invoke(null, null);
                if (ok is bool b && !b) return false;
            }
            var instance = _instanceProp?.GetValue(null);
            if (instance == null) return false;

            // PSAI is "playing" once it has a current theme; -1 means it hasn't
            // started, so merging now would race the initial load.
            if (_getCurrentThemeIdMethod != null)
            {
                var id = _getCurrentThemeIdMethod.Invoke(instance, null);
                if (id is int themeId && themeId == -1) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reflect into PSAI's live soundtrack (PsaiCore.m_logik.m_soundtrack) and
    /// log whether each of our theme ids resolves via getThemeById. Purely
    /// diagnostic; never throws out.
    /// </summary>
    private static void ProbeThemes(object instance, string when)
    {
        try
        {
            var logik = instance.GetType().GetField("m_logik", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
            if (logik == null) { DiagLog.Log(Tag, $"probe[{when}]: m_logik not found"); return; }
            var soundtrack = logik.GetType().GetField("m_soundtrack", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(logik);
            if (soundtrack == null) { DiagLog.Log(Tag, $"probe[{when}]: m_soundtrack not found"); return; }
            var getThemeById = soundtrack.GetType().GetMethod("getThemeById", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (getThemeById == null) { DiagLog.Log(Tag, $"probe[{when}]: getThemeById not found"); return; }

            var sb = new System.Text.StringBuilder($"probe[{when}]:");
            // Vanilla sentinel (1) plus every theme we actually generate, so the
            // two two-digit ids (Defeat=900010 / Naval=900011) are probed like the
            // rest instead of being silently skipped by a hardcoded sample.
            var probeIds = new List<int> { 1 };
            probeIds.AddRange(MusicThemeMap.CustomThemeIds);
            foreach (var id in probeIds)
            {
                var theme = getThemeById.Invoke(soundtrack, new object[] { id });
                sb.Append($" {id}={(theme != null ? "OK" : "null")}");
            }
            DiagLog.Log(Tag, sb.ToString());
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ProbeThemes({when})", ex);
        }
    }

    private static bool NavalDlcPresent()
    {
        var modulesRoot = string.IsNullOrEmpty(_config?.ModuleDir)
            ? null
            : Path.GetDirectoryName(_config!.ModuleDir);   // ...\Modules
        return NavalGate.IsAvailable(modulesRoot);
    }
}
