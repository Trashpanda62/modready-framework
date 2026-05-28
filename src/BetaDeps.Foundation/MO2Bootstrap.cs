// BetaDeps.Foundation -- MO2Bootstrap
//
// Task #14 (v0.8): Auto-detect Mod Organizer 2 (USVFS) deployment and
// self-copy alias folders to the REAL game Modules folder on disk.
//
// THE PROBLEM
// -----------
// MO2 deploys mods via USVFS (User-Space Virtual File System): per-process
// hooks on CreateFile* / FindFirst* that overlay each mod's tree onto the
// game directory. From inside Bannerlord the game SEES Modules\BetaDeps\,
// Modules\Bannerlord.Harmony\, etc. — but those paths are virtual.
//
// When BetaDeps's BootstrapAliasFolders calls Directory.CreateDirectory()
// to materialise an alias folder, USVFS intercepts the write. Depending
// on MO2's mod setup, the write either:
//   (a) lands in MO2's "Overwrite" folder (not on the real game disk, so
//       the next launch — or a different MO2 instance — doesn't see it),
//   (b) is silently dropped if MO2's mod is read-only.
//
// Either way the launcher on the NEXT real launch sees no alias folders.
// Rohzdear and other MO2 users have been hitting this for months —
// documented in the v0.7.5 sticky as "MO2 still needs a manual workaround
// (hardlinks deployment, or copy outside MO2). Full auto-detect is
// planned for v0.8."
//
// THE FIX
// -------
// Before BootstrapAliasFolders runs:
//   1. Detect USVFS in the current process (LoadedModule list contains
//      usvfs_x64.dll; or parent process is ModOrganizer.exe; or one of a
//      handful of MO2 env vars is set).
//   2. If detected, compute the REAL game Modules path from
//      Process.GetCurrentProcess().MainModule.FileName, which is always
//      the real Bannerlord.exe path even under USVFS (because the kernel
//      loads the exe before any USVFS hook installs).
//      Bannerlord.exe lives in <GamePath>\bin\Win64_Shipping_Client\, so
//      <GamePath>\Modules\ is two-levels-up + \Modules.
//   3. Expose the real path via TryGetRealModulesPath() so
//      BootstrapAliasFolders can prefer it over the location-derived
//      (potentially-virtualised) path.
//
// On non-MO2 launches every method here is a fast no-op: the detection
// short-circuits and the existing location-walk path is used unchanged.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Diagnostics;
using System.IO;

namespace BetaDeps.Foundation;

public static class MO2Bootstrap
{
    private const string Tag = "MO2Bootstrap";

    private static bool _detectionRan;
    private static bool _isUnderMO2;
    private static string? _realModulesPath;
    private static string _detectionReason = "(not run)";

    /// <summary>
    /// True when the current process is running under MO2's USVFS virtual
    /// filesystem. Result cached after first call.
    /// </summary>
    public static bool IsUnderMO2()
    {
        EnsureDetected();
        return _isUnderMO2;
    }

    /// <summary>
    /// Returns the REAL on-disk path to the game's Modules folder when
    /// running under MO2 (i.e. the path the kernel sees, bypassing USVFS).
    /// Null when not under MO2 or when the path could not be derived.
    /// Result cached after first call.
    /// </summary>
    public static string? TryGetRealModulesPath()
    {
        EnsureDetected();
        return _realModulesPath;
    }

    /// <summary>
    /// Free-text reason describing why the detector decided MO2 is or
    /// isn't in play. Useful for the diag log.
    /// </summary>
    public static string DetectionReason
    {
        get { EnsureDetected(); return _detectionReason; }
    }

    private static void EnsureDetected()
    {
        if (_detectionRan) return;
        _detectionRan = true;
        try
        {
            _isUnderMO2 = DetectUSVFS(out _detectionReason);
            DiagLog.Log(Tag, $"detection: under_mo2={_isUnderMO2} ({_detectionReason})");

            if (_isUnderMO2)
            {
                _realModulesPath = DeriveRealModulesPath();
                if (_realModulesPath != null)
                    DiagLog.Log(Tag, $"real Modules path resolved to: {_realModulesPath}");
                else
                    DiagLog.Log(Tag, "real Modules path could not be derived; BootstrapAliasFolders will fall back to virtualised path");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "EnsureDetected", ex);
        }
    }

    // ---------- USVFS detection ----------

    private static bool DetectUSVFS(out string reason)
    {
        // Signal 1: usvfs_x64.dll loaded in the current process.
        // USVFS injects this DLL at launch time; its presence is the
        // single highest-fidelity signal.
        try
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                var mn = (m.ModuleName ?? "").ToLowerInvariant();
                if (mn == "usvfs_x64.dll" || mn == "usvfs.dll" || mn.StartsWith("usvfs_"))
                {
                    reason = $"loaded module: {m.ModuleName}";
                    return true;
                }
            }
        }
        catch { /* iteration can throw if a module unloads mid-scan; fall through */ }

        // Signal 2: parent process is ModOrganizer.exe (or a launcher MO2 spawned).
        try
        {
            var parentName = TryGetParentProcessName();
            if (!string.IsNullOrEmpty(parentName))
            {
                var pn = parentName!.ToLowerInvariant();
                if (pn == "modorganizer.exe" || pn == "modorganizer")
                {
                    reason = $"parent process: {parentName}";
                    return true;
                }
            }
        }
        catch { /* WMI/native calls can fail; fall through */ }

        // Signal 3: MO2-set env vars. MO2 doesn't officially document any,
        // but in practice it propagates a couple via its launcher shim.
        try
        {
            if (Environment.GetEnvironmentVariable("MOD_ORGANIZER_INST_PATH") != null)
            {
                reason = "env: MOD_ORGANIZER_INST_PATH";
                return true;
            }
            if (Environment.GetEnvironmentVariable("USVFS_PROCESS_ID") != null)
            {
                reason = "env: USVFS_PROCESS_ID";
                return true;
            }
        }
        catch { /* env access can throw under restricted hosts; fall through */ }

        reason = "no MO2/USVFS signal detected";
        return false;
    }

    private static string? TryGetParentProcessName()
    {
        // No managed API for "get parent PID" until .NET 5. We're on net472,
        // so use the Win32 toolhelp snapshot via Process.GetProcesses() and
        // ParentProcessId lookup. To keep this dependency-free we use a
        // smaller WMI-less approach: scan all Process records, find the one
        // whose Id matches ours, then look up its parent via ProcessId in
        // the Process API. .NET 4.x's Process.Parent doesn't exist, so we
        // approximate by scanning for processes whose StartTime is older
        // than ours AND named ModOrganizer.exe -- noisy but cheap. If this
        // proves too imprecise, swap to NtQueryInformationProcess via
        // P/Invoke in v0.8.2.
        try
        {
            var meStart = Process.GetCurrentProcess().StartTime;
            foreach (var p in Process.GetProcessesByName("ModOrganizer"))
            {
                try
                {
                    if (p.StartTime < meStart) return p.ProcessName + ".exe";
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    // ---------- Real-game-path derivation ----------

    private static string? DeriveRealModulesPath()
    {
        // Process.MainModule.FileName is the real on-disk path of the
        // executable, set by the kernel BEFORE USVFS hooks install. Even
        // under MO2 this returns the true Bannerlord.exe path.
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return null;

            // exePath = <GamePath>\bin\Win64_Shipping_Client\Bannerlord.exe
            // (or Bannerlord.BLSE.LauncherEx.exe when launched via BLSE).
            // Walk up: file -> Win64_Shipping_Client -> bin -> <GamePath>
            var binSubdir = Path.GetDirectoryName(exePath);          // Win64_Shipping_Client
            var binDir    = Path.GetDirectoryName(binSubdir);         // bin
            var gameRoot  = Path.GetDirectoryName(binDir);            // <GamePath>
            if (string.IsNullOrEmpty(gameRoot)) return null;

            var modulesRoot = Path.Combine(gameRoot!, "Modules");
            if (!Directory.Exists(modulesRoot))
            {
                DiagLog.Log(Tag, $"derived path doesn't exist on disk: {modulesRoot}");
                return null;
            }
            return modulesRoot;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "DeriveRealModulesPath", ex);
            return null;
        }
    }
}
