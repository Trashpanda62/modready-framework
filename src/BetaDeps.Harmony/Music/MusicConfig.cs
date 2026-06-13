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
            cfg.ScanAll();

            DiagLog.Log(Tag, $"loaded BYO tree from '{cfg.ByoRoot}': " +
                             $"{cfg.TotalTracks} track(s) across {cfg._pools.Count(kv => kv.Value.Count > 0)} active context(s).");
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

    private void ScanAll()
    {
        foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
        {
            var files = GatherTracks(ctx);
            var settings = new ContextSettings
            {
                // Default: a context is on iff the user dropped tracks in. U1
                // (MCM) will later override Enabled/Mode/VolumeTrim from storage.
                Enabled = files.Count > 0,
                Mode = PlaybackMode.Shuffle,
                VolumeTrim = 1.0f,
            };
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
}
