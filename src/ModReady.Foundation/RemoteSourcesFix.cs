// ModReady.Foundation -- RemoteSourcesFix
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// PROBLEM
//   Mod DLLs downloaded with a browser carry the NTFS Zone.Identifier stream
//   ("mark of the web"). On .NET Framework, Assembly.LoadFrom refuses such a
//   file (FileLoadException 0x80131515) unless the HOST process config sets
//   <loadFromRemoteSources enabled="true"/>. Bannerlord ships no exe.config,
//   so a freshly-extracted mod fails to load and the engine surfaces it as
//   the misleading "dependency conflict" dialog.
//
// FIX
//   Ensure Bannerlord*.exe.config / TaleWorlds.MountAndBlade.Launcher*.exe.config
//   beside the running game contain <runtime><loadFromRemoteSources
//   enabled="true"/></runtime>. Merged into an existing config (BLSE's
//   appDomainManager entries are preserved), created when absent. The CLR
//   reads the config at process start, so the setting takes effect from the
//   NEXT launch onward -- after that, web-marked mod DLLs load normally and
//   the whole problem class disappears.
//
//   Deliberately NOT done here: stripping Zone.Identifier streams from module
//   files at runtime. That behavior pattern (a game process mass-deleting
//   ADS marks under Program Files) is heuristically indistinguishable from
//   malware and was flagged by Windows Defender as a trojan during testing
//   (2026-07-02). The config route is the supported .NET mechanism and is
//   AV-quiet.
//
// Runs once per process; every step is individually try/caught -- a failure
// here (e.g. unwritable Game Pass bin folder) must never take the module
// chain down.
//
// CALL SITE / LOAD-ORDER ASSUMPTION: Apply() is invoked only from
// BannerlordHarmonySubModule.OnSubModuleLoad (Modules\Bannerlord.Harmony).
// Every ModReady configuration ships and load-orders that module first, and
// every consumer mod DependedModules it, so in practice it is always present.
// If a future packaging ever makes Bannerlord.Harmony optional, this fix
// silently stops running -- move the call to whichever module remains
// mandatory.

using System;
using System.IO;
using System.Xml;

namespace ModReady.Foundation;

public static class RemoteSourcesFix
{
    private const string Tag = "RemoteSourcesFix";
    private static int _ran;

    public static void Apply()
    {
        if (System.Threading.Interlocked.Exchange(ref _ran, 1) != 0) return;

        try { EnsureHostConfigs(); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, "EnsureHostConfigs", ex); } catch { } }
    }

    /// <summary>Merge loadFromRemoteSources into the game + launcher exe
    /// configs beside the running executable. Effective from the next
    /// process start.</summary>
    private static void EnsureHostConfigs()
    {
        string? exePath = null;
        try { exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName; } catch { }
        if (string.IsNullOrEmpty(exePath)) return;
        var binDir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(binDir)) return;

        int written = 0;
        foreach (var pattern in new[] { "Bannerlord*.exe", "TaleWorlds.MountAndBlade.Launcher*.exe" })
        {
            foreach (var exe in Directory.GetFiles(binDir!, pattern))
            {
                if (EnsureConfigHasRemoteSources(exe + ".config")) written++;
            }
        }
        if (written > 0)
            DiagLog.Log(Tag, $"wrote loadFromRemoteSources into {written} exe.config file(s) in {binDir}; web-marked mod DLLs will load from the next launch");
    }

    /// <summary>Create or merge one exe.config so that
    /// /configuration/runtime/loadFromRemoteSources[@enabled='true'] exists.
    /// Returns true when the file was created or modified.</summary>
    private static bool EnsureConfigHasRemoteSources(string configPath)
    {
        try
        {
            var doc = new XmlDocument();
            if (File.Exists(configPath)) doc.Load(configPath);
            // Fresh file: minimal <configuration/> root, deliberately without
            // an <?xml?> declaration -- the CLR config parser doesn't need one
            // and the merge path above must keep recognising the file later.
            else doc.AppendChild(doc.CreateElement("configuration"));

            var configuration = doc.DocumentElement;
            if (configuration == null || configuration.Name != "configuration") return false; // not a config file we understand

            var runtime = configuration.SelectSingleNode("runtime") as XmlElement;
            if (runtime == null)
            {
                runtime = doc.CreateElement("runtime");
                configuration.AppendChild(runtime);
            }

            var lfrs = runtime.SelectSingleNode("loadFromRemoteSources") as XmlElement;
            if (lfrs != null && string.Equals(lfrs.GetAttribute("enabled"), "true", StringComparison.OrdinalIgnoreCase))
                return false; // already set

            if (lfrs == null)
            {
                lfrs = doc.CreateElement("loadFromRemoteSources");
                runtime.AppendChild(lfrs);
            }
            lfrs.SetAttribute("enabled", "true");

            // Atomic write: a truncated exe.config (player kills the process
            // mid-Save, AV interference) can stop the CLR from launching the
            // game at all next start -- strictly worse than not writing.
            // Save to a sibling temp file, then swap it over the target.
            var tmpPath = configPath + ".modready-tmp";
            doc.Save(tmpPath);
            if (File.Exists(configPath)) File.Replace(tmpPath, configPath, null);
            else File.Move(tmpPath, configPath);
            return true;
        }
        catch (Exception ex)
        {
            // Unwritable bin dir (Game Pass / restricted Program Files) lands
            // here; the game still runs, users just keep the manual-unblock
            // guidance for freshly-downloaded mods.
            try { DiagLog.LogCaught(Tag, $"EnsureConfigHasRemoteSources({Path.GetFileName(configPath)})", ex); } catch { }
            return false;
        }
    }
}
