// ModReady.Foundation -- RuntimeLog
//
// Append-only diagnostic log written to Modules\ModReady\runtime.log
// during gameplay. Other ModReady assemblies call into this via
// reflection (see DiagLog.cs) so they don't have a hard build-time
// reference back to Foundation.
//
// Design constraints:
//   * Never throw out of a log call -- if writing fails, the line is
//     silently dropped. The game continues.
//   * Thread-safe: log lines from background threads (Mission ticks,
//     finalizers) must serialize cleanly.
//   * No external dependencies -- only System.IO + System.Threading.
//   * Log location MUST anchor on Modules\ModReady\ regardless of which
//     folder Foundation.dll was loaded from. Foundation.dll is mirrored
//     into every alias bin folder (Bannerlord.Harmony, .UIExtenderEx,
//     .ButterLib, .MBOptionScreen) so the launcher can construct alias
//     SubModules. If the alias loads first, Assembly.GetExecutingAssembly()
//     .Location points to the alias folder, NOT to Modules\ModReady\.
//     We resolve the canonical ModReady folder by walking up the tree
//     looking for a sibling "ModReady" folder under Modules\.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ModReady.Foundation;

public static class RuntimeLog
{
    private static readonly object _gate = new();
    // volatile: the lock-free fast-path read in Path's getter (and EnsureHeader)
    // can run on a different thread (finalizers fire on engine tick threads)
    // than the thread that first assigns these under _gate. Without volatile,
    // a weak memory model could publish a partially-initialized value and send
    // the header / first lines to the cwd fallback path.
    private static volatile string? _resolvedPath;
    private static volatile bool _headerWritten;

    /// <summary>
    /// Absolute path to the runtime log file under Modules\ModReady\.
    /// Resolved lazily so the very first call from OnSubModuleLoad works
    /// even if the working directory isn't the game root.
    /// </summary>
    public static string Path
    {
        get
        {
            if (_resolvedPath != null) return _resolvedPath;
            lock (_gate)
            {
                if (_resolvedPath != null) return _resolvedPath;
                _resolvedPath = ResolvePath();
                return _resolvedPath;
            }
        }
    }

    /// <summary>
    /// Append a timestamped line. Format: [HH:mm:ss.fff] [tag] message
    /// </summary>
    public static void Write(string tag, string message)
    {
        try
        {
            var line = string.Format(
                "[{0:HH\\:mm\\:ss\\.fff}] [T{1}] [{2}] {3}",
                DateTime.Now,
                Thread.CurrentThread.ManagedThreadId,
                tag ?? "?",
                message ?? string.Empty);

            lock (_gate)
            {
                EnsureHeader();
                File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Never throw out of a log call.
        }
    }

    /// <summary>
    /// Log a caught exception. The stack trace is included on its own
    /// line so the per-line tag/timestamp doesn't get noisy.
    /// </summary>
    public static void WriteException(string tag, string where, Exception ex)
    {
        if (ex == null) { Write(tag, where + ": (null exception)"); return; }
        // Format the header line inline (mirrors Write) so the summary line and
        // the stack trace land under ONE lock -- two separate lock acquisitions
        // let another thread's write interleave between them.
        var line = string.Format(
            "[{0:HH\\:mm\\:ss\\.fff}] [T{1}] [{2}] {3}",
            DateTime.Now,
            Thread.CurrentThread.ManagedThreadId,
            tag ?? "?",
            where + ": " + ex.GetType().Name + " -- " + (ex.Message ?? string.Empty));
        try
        {
            lock (_gate)
            {
                EnsureHeader();
                File.AppendAllText(Path, line + Environment.NewLine + ex.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
    }

    private static string ResolvePath()
    {
        // Foundation.dll may be loaded from any of:
        //   Modules\ModReady\bin\Win64_Shipping_Client\
        //   Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\
        //   Modules\Bannerlord.UIExtenderEx\bin\Win64_Shipping_Client\
        //   Modules\Bannerlord.ButterLib\bin\Win64_Shipping_Client\
        //   Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\
        // We want runtime.log to live in a dependency module folder (preferring
        // Modules\Bannerlord.Harmony\runtime.log) regardless of which path the CLR
        // loaded us from -- and never inside / creating Modules\ModReady\.
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var asmPath = asm.Location;
            if (!string.IsNullOrEmpty(asmPath))
            {
                // Walk up looking for a "Modules" folder. When found, write
                // runtime.log inside a DEPENDENCY MODULE folder -- deliberately
                // NOT Modules\ModReady\. The ModReady installer ships no ModReady
                // folder, and we must not recreate one on disk just to hold a log.
                // Prefer Modules\Bannerlord.Harmony\ (a core dependency, always
                // present -> deterministic, predictable log location); otherwise
                // fall back to whichever module folder this assembly actually
                // loaded from. BOTH already exist, so we never create a folder.
                var dir = System.IO.Path.GetDirectoryName(asmPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    var modulesFolder = System.IO.Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(modulesFolder)
                        && string.Equals(System.IO.Path.GetFileName(modulesFolder), "Modules", StringComparison.OrdinalIgnoreCase))
                    {
                        // dir is the module folder we loaded from (Modules\<X>).
                        var harmonyDir = System.IO.Path.Combine(modulesFolder, "Bannerlord.Harmony");
                        var home = Directory.Exists(harmonyDir) ? harmonyDir : dir;
                        return System.IO.Path.Combine(home, "runtime.log");
                    }
                    dir = modulesFolder;
                }
            }
        }
        catch { }

        // Fallback path 1: the old behavior (walk up 3, write next to the assembly).
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var asmPath = asm.Location;
            if (!string.IsNullOrEmpty(asmPath))
            {
                var binDir = System.IO.Path.GetDirectoryName(asmPath);
                if (!string.IsNullOrEmpty(binDir))
                {
                    var win64Dir = System.IO.Path.GetDirectoryName(binDir);
                    if (!string.IsNullOrEmpty(win64Dir))
                    {
                        var moduleDir = System.IO.Path.GetDirectoryName(win64Dir);
                        if (!string.IsNullOrEmpty(moduleDir))
                            return System.IO.Path.Combine(moduleDir, "runtime.log");
                    }
                }
            }
        }
        catch { }
        // Fallback path 2: cwd. Not ideal but better than nothing.
        return System.IO.Path.Combine(Environment.CurrentDirectory, "modready-runtime.log");
    }

    private static void EnsureHeader()
    {
        if (_headerWritten) return;
        _headerWritten = true;
        // Resolve the path ONCE here. The Path getter resolves + caches under
        // _gate (which Write/WriteException already hold when calling us -- the
        // lock is reentrant), so this both primes _resolvedPath and gives us a
        // guaranteed-non-null path. (Do NOT use _resolvedPath! directly: on the
        // very first log call _resolvedPath is still null and Path is what
        // resolves it.)
        var p = Path;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch { }
        try
        {
            var header =
                "==== ModReady runtime.log opened " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" + Environment.NewLine;
            File.AppendAllText(p, header, Encoding.UTF8);
        }
        catch { }
    }
}
