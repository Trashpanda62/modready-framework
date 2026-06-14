// BetaDeps.Harmony -- MusicConfig
//
// The folder-convention loader and the in-memory source of truth for the
// BYO music picker. On Load() it locates the deployed BetaDeps module
// directory (from this assembly's on-disk location, the same walk
// BootstrapAliasFolders uses), then scans
//
//     Modules\BetaDeps\Music\BYO\<Context>\ ... \*.ogg|*.wav
//
// into one PlaybackPool per MusicContext. A context folder with zero audio
// files yields an empty pool, which the rest of the stack treats as
// "vanilla plays" (graceful no-op) -- no folder is required to exist.
//
// Per-context settings (Enable / Mode / Volume trim) live here with sane
// defaults. Workstream U1 (MCM groups) will overwrite these from MCM
// storage; until then a context is enabled iff it has at least one track.
//
// MVP scope: each context is a single flat pool gathered recursively from
// its folder, so per-culture settlement subfolders (_generic\, Empire\, ...)
// are merged into one pool for now. Per-culture keying is a C3 refinement
// (settlement path); the PSAI redirect path (C2) doesn't need it.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BetaDeps.Foundation;

namespace BetaDeps.Harmony.Music;

public sealed class ContextSettings
{
    public bool Enabled { get; set; }
    public PlaybackMode Mode { get; set; } = PlaybackMode.Shuffle;
    /// <summary>0..1 multiplier applied on the Engine.Music path (C3). PSAI path
    /// uses PSAI's own mix for MVP; stored here for U1 to surface either way.</summary>
    public float VolumeTrim { get; set; } = 1.0f;
}

public sealed class MusicConfig
{
    private const string Tag = "MusicConfig";
    private static readonly string[] AudioExtensions = { ".ogg", ".wav" };

    private readonly Dictionary<MusicContext, PlaybackPool> _pools = new();
    private readonly Dictionary<MusicContext, ContextSettings> _settings = new();

    /// <summary>Absolute path to the deployed BetaDeps module directory, e.g.
    /// ...\Modules\BetaDeps. Empty string if it could not be resolved.</summary>
    public string ModuleDir { get; private set; } = "";

    /// <summary>Absolute path to ...\Modules\BetaDeps\Music\BYO.</summary>
    public string ByoRoot { get; private set; } = "";

    public IReadOnlyDictionary<MusicContext, PlaybackPool> Pools => _pools;

    public int TotalTracks => _pools.Values.Sum(p => p.Count);

    /// <summary>
    /// The live config built at load, so the Options-screen music UI (which lives
    /// in the MCM assembly) can read/write per-context settings without a hard
    /// reference back into the SubModule. Set at the end of Load().
    /// </summary>
    public static MusicConfig? Current { get; private set; }

    private MusicConfig() { }

    /// <summary>
    /// Build a MusicConfig by scanning the deployed module's BYO tree. Never
    /// throws -- on any failure it returns a config with empty pools (the
    /// whole feature then no-ops and vanilla music plays).
    /// </summary>
    public static MusicConfig Load()
    {
        var cfg = new MusicConfig();
        try
        {
            cfg.ModuleDir = ResolveModuleDir();
            if (string.IsNullOrEmpty(cfg.ModuleDir))
            {
                DiagLog.Log(Tag, "could not resolve BetaDeps module directory; BYO music disabled this session.");
                cfg.InitEmpty();
                return cfg;
            }

            cfg.ByoRoot = Path.Combine(cfg.ModuleDir, "Music", "BYO");
            var overlay = ReadSavedSettings();   // persisted per-context choices (empty if never saved)
            cfg.ScanAll(overlay);

            DiagLog.Log(Tag, $"loaded BYO tree from '{cfg.ByoRoot}': " +
                             $"{cfg.TotalTracks} track(s) across {cfg._pools.Count(kv => kv.Value.Count > 0)} active context(s)" +
                             (overlay.Count > 0 ? $"; applied {overlay.Count} saved setting(s)." : "; defaults (no saved settings)."));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Load", ex);
            cfg.InitEmpty();
        }
        Current = cfg;
        return cfg;
    }

    private void InitEmpty()
    {
        foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
        {
            _pools[ctx] = new PlaybackPool(ctx, Array.Empty<string>());
            _settings[ctx] = new ContextSettings { Enabled = false };
        }
    }

    private void ScanAll(IReadOnlyDictionary<MusicContext, ContextSettings> overlay)
    {
        foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
        {
            var files = GatherTracks(ctx);
            ContextSettings settings;
            if (overlay != null && overlay.TryGetValue(ctx, out var saved))
            {
                // Persisted choice wins (the Options > Sound UI saved it on Done).
                settings = new ContextSettings { Enabled = saved.Enabled, Mode = saved.Mode, VolumeTrim = saved.VolumeTrim };
            }
            else
            {
                // First run / never saved: a context is on iff the user dropped tracks in.
                settings = new ContextSettings { Enabled = files.Count > 0, Mode = PlaybackMode.Shuffle, VolumeTrim = 1.0f };
            }
            _settings[ctx] = settings;
            _pools[ctx] = new PlaybackPool(ctx, files, settings.Mode);

            if (files.Count > 0)
                DiagLog.Log(Tag, $"  {ctx}: {files.Count} track(s) under '{ctx.FolderRelativePath()}'");
        }
    }

    private List<string> GatherTracks(MusicContext ctx)
    {
        var dir = Path.Combine(ByoRoot, ctx.FolderRelativePath().Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir)) return new List<string>();
        try
        {
            // Recursive: picks up an optional culture subfolder (or _generic\)
            // as a single merged pool for MVP. Stable ordering for sequential
            // mode + reproducible shuffle seeds.
            //
            // Exclude any "PC" subfolder: that's where SoundtrackXmlGenerator
            // stages the hardlinks PSAI's platform layer needs. Without this
            // filter the scan re-counts our own staged links and the pool
            // doubles every launch.
            return Directory
                .EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => !HasStagingSegment(f, dir))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GatherTracks({ctx})", ex);
            return new List<string>();
        }
    }

    /// <summary>True if the file sits under a "PC" staging subfolder of the
    /// context root (i.e. a hardlink we created, not a user track).</summary>
    private static bool HasStagingSegment(string filePath, string contextDir)
    {
        var rel = filePath.Substring(contextDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
        foreach (var seg in rel.Split('/', Path.DirectorySeparatorChar))
            if (string.Equals(seg, "PC", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public PlaybackPool? PoolFor(MusicContext ctx)
        => _pools.TryGetValue(ctx, out var p) ? p : null;

    public ContextSettings SettingsFor(MusicContext ctx)
        => _settings.TryGetValue(ctx, out var s) ? s : new ContextSettings { Enabled = false };

    /// <summary>True iff this context is enabled AND has at least one track.</summary>
    public bool IsActive(MusicContext ctx)
        => SettingsFor(ctx).Enabled && PoolFor(ctx) is { Count: > 0 };

    /// <summary>All PSAI-path contexts that are currently active (used by the
    /// soundtrack generator to decide which themes to emit).</summary>
    public IEnumerable<MusicContext> ActivePsaiContexts()
        => _pools.Keys.Where(c => c.IsPsaiPath() && IsActive(c));

    /// <summary>
    /// Resolve ...\Modules\BetaDeps from this assembly's location. The DLL
    /// lives at ...\Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Harmony.dll,
    /// so the module dir is two directories up from bin. Returns "" on failure.
    /// </summary>
    private static string ResolveModuleDir()
    {
        var ownPath = typeof(MusicConfig).Assembly.Location;
        if (string.IsNullOrEmpty(ownPath)) return "";
        var binDir = Path.GetDirectoryName(ownPath);            // Win64_Shipping_Client
        var betaDepsBin = Path.GetDirectoryName(binDir);        // bin
        var moduleDir = Path.GetDirectoryName(betaDepsBin);     // BetaDeps
        return string.IsNullOrEmpty(moduleDir) ? "" : moduleDir!;
    }

    // ---- persistence -----------------------------------------------------------
    // Settings live in the user's config tree (NOT the module dir, which can be
    // read-only and is wiped on update):
    //   Documents\Mount and Blade II Bannerlord\Configs\BetaDeps\byo-music.cfg
    // Line format: <Context>=<enabled 0/1>|<Shuffle|Sequential>|<volume 0..1>.
    // Hand-rolled (no JSON dependency in this assembly); tolerant of bad lines.

    private const string SettingsFileName = "byo-music.cfg";

    private static string? ResolveSettingsPath()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) return null;
            return Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "BetaDeps", SettingsFileName);
        }
        catch { return null; }
    }

    /// <summary>Persist the current per-context settings so they survive relaunch.
    /// Called when the user clicks Done in Options &gt; Sound. Never throws.</summary>
    public void SaveSettings()
    {
        try
        {
            var path = ResolveSettingsPath();
            if (path == null) { DiagLog.Log(Tag, "SaveSettings: no Documents path; not saved."); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var sb = new System.Text.StringBuilder();
            sb.Append("# BetaDeps BYO music settings -- auto-saved from Options > Sound. Delete to reset.\n");
            sb.Append("# <Context>=<enabled 0/1>|<Shuffle|Sequential>|<volume 0..1>\n");
            foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
            {
                var s = SettingsFor(ctx);
                sb.Append(ctx).Append('=')
                  .Append(s.Enabled ? '1' : '0').Append('|')
                  .Append(s.Mode).Append('|')
                  .Append(Clamp01(s.VolumeTrim).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))
                  .Append('\n');
            }
            // Atomic write: a torn file (alt-F4 mid-write -- this fires on the
            // Options "Done" click) must never silently drop the user's saved
            // choices. Write a temp then swap it into place.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            DiagLog.Log(Tag, $"SaveSettings: wrote {_settings.Count} context setting(s) to '{path}'.");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "SaveSettings", ex); }
    }

    /// <summary>Re-apply persisted settings (or per-context defaults for entries not
    /// in the file) over the live settings + pool play modes -- used to discard
    /// unsaved edits when the user clicks Cancel. Never throws.</summary>
    public void ReloadSettings()
    {
        try
        {
            var overlay = ReadSavedSettings();
            foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
            {
                var hasTracks = PoolFor(ctx) is { Count: > 0 };
                ContextSettings target = overlay.TryGetValue(ctx, out var saved)
                    ? saved
                    : new ContextSettings { Enabled = hasTracks, Mode = PlaybackMode.Shuffle, VolumeTrim = 1.0f };

                if (_settings.TryGetValue(ctx, out var live))
                {
                    live.Enabled = target.Enabled;
                    live.Mode = target.Mode;
                    live.VolumeTrim = target.VolumeTrim;
                }
                else _settings[ctx] = target;

                PoolFor(ctx)?.ApplyMode(target.Mode);
            }
            DiagLog.Log(Tag, "ReloadSettings: reverted to persisted/default settings (Cancel).");
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ReloadSettings", ex); }
    }

    /// <summary>Read the persisted settings file into a per-context map. Returns an
    /// empty map when the file is absent or unreadable (=&gt; defaults apply).</summary>
    private static Dictionary<MusicContext, ContextSettings> ReadSavedSettings()
    {
        var map = new Dictionary<MusicContext, ContextSettings>();
        try
        {
            var path = ResolveSettingsPath();
            if (path == null || !File.Exists(path)) return map;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var parts = line.Substring(eq + 1).Split('|');
                if (parts.Length < 3) continue;
                if (!Enum.TryParse<MusicContext>(key, ignoreCase: true, out var ctx)) continue;
                if (!Enum.TryParse<PlaybackMode>(parts[1].Trim(), ignoreCase: true, out var mode)) mode = PlaybackMode.Shuffle;
                if (!float.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var vol)) vol = 1.0f;
                map[ctx] = new ContextSettings
                {
                    Enabled = parts[0].Trim() == "1",
                    Mode = mode,
                    VolumeTrim = Clamp01(vol),
                };
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ReadSavedSettings", ex); }
        return map;
    }

    private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
}
