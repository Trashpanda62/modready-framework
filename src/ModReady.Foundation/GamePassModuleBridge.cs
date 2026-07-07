// ModReady.Foundation -- GamePassModuleBridge
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// PROBLEM
//   Bannerlord's engine loads each submodule DLL from
//   Modules\<Mod>\bin\<Common.ConfigName>\<DLLName>. On Game Pass /
//   Microsoft Store the ConfigName is "Gaming.Desktop.x64_Shipping_Client"
//   (a .NET 6 CoreCLR build). Many popular mods ship ONLY a
//   "Win64_Shipping_Client" (.NET Framework / Steam) build -- e.g.
//   AIInfluence 5.0.7, currently the top mod. The engine looks only in the
//   Gaming.Desktop folder, finds nothing, and the submodule "fails to
//   construct" -- surfaced to the player as the generic "dependency
//   conflict" dialog. It works fine on Steam (where ConfigName IS
//   Win64_Shipping_Client) and it works on the real BUTR stack, so users
//   correctly report "works with other Harmony, not ModReady."
//
//   The DLL itself is NOT the problem: a net472-targeted (even obfuscated)
//   assembly loads cleanly on .NET 6 -- verified 2026-07-05 by loading
//   AIInfluence.dll into a net6 host (LoadFrom OK, 2589/2601 types resolve;
//   the only misses were MCM/UIExtenderEx refs the standalone probe didn't
//   supply, which ARE present in-game). The engine just never looks in the
//   Win64 folder on Game Pass.
//
// FIX
//   Before consumer submodules load, scan every sibling module folder. For
//   each that has bin\Win64_Shipping_Client\<DLL> but NO
//   bin\Gaming.Desktop.x64_Shipping_Client, mirror the Win64 folder into a
//   Gaming.Desktop folder so the engine finds and loads the (net6-loadable)
//   DLL. Mods that already ship their own Gaming.Desktop build are left
//   untouched -- we never overwrite an author's real net6 binaries.
//
// TIMING / CALL SITE
//   Invoked from BannerlordHarmonySubModule's constructor (Modules\
//   Bannerlord.Harmony, the net6-present host that loads before every
//   consumer mod). The engine's LoadSubModules loop constructs our host's
//   SubModule during its own iteration -- earlier than any consumer mod's
//   iteration -- and re-checks File.Exists per submodule, so a folder we
//   materialise here is visible when the engine reaches the consumer mod in
//   the SAME pass.
//
// SAFETY
//   Game-Pass-only (guarded on our own load path being a Gaming.Desktop
//   folder), idempotent (skips when the target already exists), and every
//   step is individually try/caught -- a copy failure (locked file,
//   read-only folder) must never take the module chain down.

using System;
using System.Collections.Generic;
using System.IO;

namespace ModReady.Foundation;

public static class GamePassModuleBridge
{
    private const string Tag = "GamePassModuleBridge";
    private const string Win64Config = "Win64_Shipping_Client";
    private const string GamePassConfig = "Gaming.Desktop.x64_Shipping_Client";
    private static int _ran;

    // .NET Framework BCL polyfill assemblies. A Steam (net472) mod folder
    // ships these because net472 lacks them; .NET 6 (Game Pass) provides all
    // of them IN-BOX. If we copy the net472 copies into the Gaming.Desktop
    // folder and the CLR loads them, we get DUPLICATE type identities
    // (two System.Memory<T>, two Span<T>, ...). Code that marshals buffers
    // across that seam -- e.g. AIInfluence's OggVorbisEncoder TTS path --
    // reads/writes through the wrong copy and emits corrupted audio (static
    // on the loading screen, first seen 2026-07-05). NEVER bridge these;
    // let the runtime satisfy them. Compared case-insensitively, no extension.
    private static readonly HashSet<string> BclPolyfillDenylist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Buffers",
        "System.Memory",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Threading.Tasks.Extensions",
        "System.ValueTuple",
        "System.Text.Encodings.Web",
        "System.Runtime.InteropServices.RuntimeInformation",
        "netstandard",
        "mscorlib",
    };

    // The 4 dependency modules already ship real Gaming.Desktop builds, so the
    // bridge never triggers for them (their gp folder exists) -- listed here
    // defensively so we never touch them.
    //
    // The `ModReady` umbrella is deliberately Win64-only, but it IS bridged now:
    // without a Gaming.Desktop copy of its DLLs the engine pops "cannot find
    // modready.harmony.dll" / "cannot load ModReady.Foundation.dll" on Game Pass
    // (harmless, but ugly). Bridging its DLLs silences that. Running the net472
    // umbrella on .NET 6 previously crashed startup -- that's now prevented by a
    // hard no-op guard in ModReadyHarmonySubModule (RuntimeEnv.IsNetCore): on
    // Game Pass the umbrella loads but does nothing; the host owns GP.
    private static readonly HashSet<string> ModuleSkiplist =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Bannerlord.Harmony",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.ButterLib",
        "Bannerlord.MBOptionScreen",
    };

    public static void Apply()
    {
        if (System.Threading.Interlocked.Exchange(ref _ran, 1) != 0) return;
        try { Run(); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, "Apply", ex); } catch { } }
    }

    private static void Run()
    {
        // Derive our own bin config folder to detect the runtime. Our DLL sits
        // at ...\Modules\Bannerlord.Harmony\bin\<Config>\ModReady.Foundation.dll.
        var ownPath = typeof(GamePassModuleBridge).Assembly.Location;
        if (string.IsNullOrEmpty(ownPath)) { DiagLog.Log(Tag, "own assembly location empty; skipping."); return; }

        var binConfigDir = Path.GetDirectoryName(ownPath);                 // <Config>
        var configName = Path.GetFileName(binConfigDir);                   // Win64_... or Gaming.Desktop_...
        if (!string.Equals(configName, GamePassConfig, StringComparison.OrdinalIgnoreCase))
        {
            // Steam / GOG: ConfigName is Win64; every mod ships a matching
            // build, so nothing to bridge. No-op (logged once for clarity).
            DiagLog.Log(Tag, $"not Game Pass (config '{configName}'); no bridging needed.");
            return;
        }

        var binDir = Path.GetDirectoryName(binConfigDir);                  // bin
        var ownModuleDir = Path.GetDirectoryName(binDir);                  // Bannerlord.Harmony
        var modulesRoot = Path.GetDirectoryName(ownModuleDir);            // Modules
        if (string.IsNullOrEmpty(modulesRoot) || !Directory.Exists(modulesRoot))
        {
            DiagLog.Log(Tag, "could not derive Modules root; skipping.");
            return;
        }

        int bridged = 0, scanned = 0;
        foreach (var modDir in Directory.GetDirectories(modulesRoot))
        {
            scanned++;
            try
            {
                // Never bridge ModReady's own modules (esp. the deliberately
                // Win64-only umbrella -- bridging it crashes .NET 6 startup).
                if (ModuleSkiplist.Contains(Path.GetFileName(modDir))) continue;

                var win64 = Path.Combine(modDir, "bin", Win64Config);
                var gp = Path.Combine(modDir, "bin", GamePassConfig);

                // Only bridge Steam-only mods: Win64 present, Gaming.Desktop absent.
                // A mod that ships its own net6 build already has the gp folder --
                // never overwrite the author's real binaries.
                if (!Directory.Exists(win64)) continue;
                if (Directory.Exists(gp)) continue;

                // Must actually contain a DLL to be worth mirroring.
                if (Directory.GetFiles(win64, "*.dll", SearchOption.TopDirectoryOnly).Length == 0) continue;

                CopyDirectoryRecursive(win64, gp);
                bridged++;
                DiagLog.Log(Tag, $"bridged Win64 -> Gaming.Desktop for module '{Path.GetFileName(modDir)}' (Steam-only build made Game-Pass-loadable)");
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, $"bridge '{Path.GetFileName(modDir)}'", ex); } catch { }
            }
        }

        if (bridged > 0)
            DiagLog.Log(Tag, $"scanned {scanned} module folder(s); bridged {bridged} Steam-only mod(s) for Game Pass this launch.");
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            var name = Path.GetFileName(f);
            // Skip net472 BCL polyfills -- .NET 6 provides them in-box, and
            // shipping the net472 copy into the net6 folder causes duplicate
            // type-identity corruption (see BclPolyfillDenylist note).
            if (string.Equals(Path.GetExtension(name), ".dll", StringComparison.OrdinalIgnoreCase)
                && BclPolyfillDenylist.Contains(Path.GetFileNameWithoutExtension(name)))
            {
                DiagLog.Log(Tag, $"  skip BCL polyfill '{name}' (runtime provides it on .NET 6)");
                continue;
            }
            File.Copy(f, Path.Combine(dst, name), overwrite: true);
        }
        foreach (var d in Directory.GetDirectories(src))
            CopyDirectoryRecursive(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
