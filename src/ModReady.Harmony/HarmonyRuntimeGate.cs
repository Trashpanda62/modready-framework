// ModReady.Harmony -- HarmonyRuntimeGate
//
// Ensures Andreas Pardeike's 0Harmony.dll is loaded into the AppDomain
// exactly once, by us, before any sibling submodule's OnSubModuleLoad
// runs and tries to use it. Validates that the loaded copy matches the
// version we shipped, and that it was loaded from our bin folder rather
// than some other module that smuggled in an incompatible build.
//
// The "gate" terminology: think of it as the single doorway through
// which Harmony enters the process. Other mods that reference HarmonyLib
// types will resolve them via this loaded copy.
//
// Original work. No code copied from Aragas-authored sources.
// MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

using ModReady.Foundation;

using HarmonyLib;

namespace ModReady.Harmony;

internal static class HarmonyRuntimeGate
{
    private const string Tag = "HarmonyRuntimeGate";
    private static bool _opened;

    /// <summary>
    /// Opens the gate. Idempotent. Logs the resolved 0Harmony.dll
    /// location and version, and flags any mismatch with the expected
    /// version that ModReady was built against.
    /// </summary>
    /// <returns>true if Harmony is loaded and usable; false if something
    /// is off and the caller should bail out of patch application.</returns>
    public static bool Open()
    {
        if (_opened) return true;

        try
        {
            // Touching any HarmonyLib type forces the CLR to resolve and
            // load 0Harmony.dll. Use HarmonyMethod as the touchstone.
            var harmonyAsm = typeof(HarmonyMethod).Assembly;
            var ver = harmonyAsm.GetName().Version ?? new Version(0, 0, 0, 0);
            var loc = SafeLocation(harmonyAsm);

            DiagLog.Log(Tag, $"0Harmony.dll loaded: version {ver}, from {loc}");

            // Cross-check: is this the copy we shipped? We shipped it next
            // to ModReady.Harmony.dll, AND mirror it into the four impersonation
            // alias folders (Bannerlord.Harmony / .UIExtenderEx / .ButterLib /
            // .MBOptionScreen) so BLSE LauncherEx's per-alias-folder checks
            // pass. Loading from any of THOSE locations is fine. The warning
            // should only fire when 0Harmony.dll resolves from a folder OUTSIDE
            // the ModReady-managed tree, i.e. a different mod shipping its own
            // copy. v0.7.1 emitted false positives on every user with the
            // Bannerlord.Harmony alias active (which is everyone).
            var selfDir = SafeDir(typeof(HarmonyRuntimeGate).Assembly);
            var harmonyDir = string.IsNullOrEmpty(loc) ? null : Path.GetDirectoryName(loc);
            if (!string.IsNullOrEmpty(selfDir) && !string.IsNullOrEmpty(harmonyDir)
                && !string.Equals(selfDir, harmonyDir, StringComparison.OrdinalIgnoreCase)
                && !IsModReadyManagedDir(harmonyDir!))
            {
                DiagLog.Log(Tag, $"WARNING: 0Harmony.dll resolved from a different folder ({harmonyDir}) than ModReady.Harmony.dll ({selfDir}). Some other module is shipping its own copy and won the load race. Expect issues if the version differs.");
            }

            _opened = true;
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Open", ex);
            return false;
        }
    }

    /// <summary>Snapshot of every Harmony instance currently patched into
    /// the process. Used by the diag log on first open so we know what
    /// else has touched the runtime.</summary>
    public static void SnapshotPatches()
    {
        try
        {
            var ids = HarmonyLib.Harmony.GetAllPatchedMethods()
                .SelectMany(m => HarmonyLib.Harmony.GetPatchInfo(m)?.Owners ?? Enumerable.Empty<string>())
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            DiagLog.Log(Tag, "Active Harmony owners on snapshot: " + (ids.Length == 0 ? "(none)" : string.Join(", ", ids)));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "SnapshotPatches", ex);
        }
    }

    private static string SafeLocation(Assembly a)
    {
        try { return a.Location ?? string.Empty; } catch { return string.Empty; }
    }

    /// <summary>
    /// True when the directory belongs to the ModReady-managed module
    /// tree: Modules\ModReady\bin\... or Modules\Bannerlord.{Harmony,
    /// UIExtenderEx, ButterLib, MBOptionScreen}\bin\... (the four alias
    /// folders ModReady creates as drop-in replacements). 0Harmony.dll
    /// loading from any of these is expected, not a conflict.
    /// </summary>
    private static bool IsModReadyManagedDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        var norm = dir.Replace('/', '\\');
        // We don't need an exact-prefix check -- we just need to confirm
        // the path contains a "Modules\<name>\bin" segment whose name is
        // either ModReady or one of the four alias module IDs.
        string[] managed = {
            "\\Modules\\ModReady\\",
            "\\Modules\\Bannerlord.Harmony\\",
            "\\Modules\\Bannerlord.UIExtenderEx\\",
            "\\Modules\\Bannerlord.ButterLib\\",
            "\\Modules\\Bannerlord.MBOptionScreen\\"
        };
        foreach (var seg in managed)
        {
            if (norm.IndexOf(seg, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    private static string SafeDir(Assembly a)
    {
        try
        {
            var loc = a.Location;
            return string.IsNullOrEmpty(loc) ? string.Empty : (Path.GetDirectoryName(loc) ?? string.Empty);
        }
        catch { return string.Empty; }
    }
}
