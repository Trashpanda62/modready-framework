// ModReady.Foundation -- VersionProbe
//
// Detects which game branch is running so the rest of ModReady knows
// whether to engage beta-specific sigsafe Harmony binds (e1.4.x) or
// stick to the public-branch fast path (e1.3.x).
//
// We avoid the BUTR ApplicationVersionHelper here -- that's BUTR code
// we can't use. Instead, this reads ApplicationVersion directly from
// TaleWorlds.Library, the public game type.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

namespace ModReady.Foundation;

public enum GameBranch
{
    Unknown,
    Public,   // e1.3.x or earlier
    Beta,     // e1.4.x and above
}

public static class VersionProbe
{
    private static readonly object _gate = new();
    private static GameBranch? _cached;
    private static int? _cachedMajor;
    private static int? _cachedMinor;

    /// <summary>
    /// Returns the detected branch. Only a SUCCESSFUL detection is memoized.
    /// An Unknown result (the version types aren't loaded yet -- e.g. when this
    /// is read during very early SubModule construction) is returned transiently
    /// WITHOUT caching, so a later read after TaleWorlds.Library loads can still
    /// succeed. Caching Unknown permanently used to disable beta sigsafe patches
    /// for the whole session once anything probed too early.
    /// </summary>
    public static GameBranch Branch
    {
        get
        {
            var c = _cached;
            if (c.HasValue) return c.Value;
            lock (_gate)
            {
                if (_cached.HasValue) return _cached.Value;
                var result = Detect();          // sets _cachedMajor/_cachedMinor on success
                if (result != GameBranch.Unknown) _cached = result;   // memoize success only
                return result;
            }
        }
    }

    /// <summary>Major version number (e.g. 1 for e1.4.2). Triggers detection if needed.</summary>
    public static int Major { get { if (!_cached.HasValue) { _ = Branch; } return _cachedMajor ?? 0; } }

    /// <summary>Minor version number (e.g. 4 for e1.4.2). Triggers detection if needed.</summary>
    public static int Minor { get { if (!_cached.HasValue) { _ = Branch; } return _cachedMinor ?? 0; } }

    public static bool IsBeta => Branch == GameBranch.Beta;
    public static bool IsPublic => Branch == GameBranch.Public;

    private static GameBranch Detect()
    {
        try
        {
            // The reliable game version comes from ApplicationVersion.FromParametersFile()
            // (TaleWorlds.Library) -- the exact value TaleWorlds.Core.MBSaveLoad.CurrentVersion
            // caches (decomp: `CurrentVersion { get; } = ApplicationVersion.FromParametersFile()`).
            // The old probe RESOLVED appVerType but never used it: Plan A called
            // TaleWorlds.ModuleManager.ApplicationVersionHelper.GameVersion() (a type that does
            // NOT exist on the current game) and Plan B read Module.CurrentModule.Version (the
            // MODULE's version, not the game's) -- so detection always returned Unknown.
            var appVerType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", throwOnError: false)
                          ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.Library.ApplicationVersion");

            object? ver = null;

            // Plan A: ApplicationVersion.FromParametersFile() -- the game's own version source.
            if (appVerType != null)
            {
                var fromParams = appVerType.GetMethod("FromParametersFile", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (fromParams != null) ver = fromParams.Invoke(null, null);
            }

            // Plan B: TaleWorlds.Core.MBSaveLoad.CurrentVersion (same value, already cached).
            if (ver == null)
            {
                var saveLoadType = Type.GetType("TaleWorlds.Core.MBSaveLoad, TaleWorlds.Core", throwOnError: false)
                                ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.Core.MBSaveLoad");
                ver = saveLoadType?.GetProperty("CurrentVersion", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }

            // Plan C (last resort): Module.CurrentModule.Version -- the MODULE's version, only
            // used if the two real game-version sources above are unavailable.
            if (ver == null)
            {
                var moduleType = Type.GetType("TaleWorlds.MountAndBlade.Module, TaleWorlds.MountAndBlade", throwOnError: false)
                              ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.MountAndBlade.Module");
                var current = moduleType?.GetProperty("CurrentModule", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (current != null)
                {
                    var verProp = current.GetType().GetProperty("Version") ?? current.GetType().GetProperty("ModuleVersion");
                    ver = verProp?.GetValue(current);
                }
            }

            if (ver != null && TryExtractMajorMinor(ver, out var maj, out var min))
            {
                _cachedMajor = maj;
                _cachedMinor = min;
                return ClassifyBranch(ver, maj, min);
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.WriteException("VersionProbe", "Detect", ex);
        }
        return GameBranch.Unknown;
    }

    private static Type? FindTypeAcrossLoadedAssemblies(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static bool TryExtractMajorMinor(object versionObj, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        try
        {
            var t = versionObj.GetType();
            var majProp = t.GetProperty("Major") ?? t.GetField("Major") as MemberInfo;
            var minProp = t.GetProperty("Minor") ?? t.GetField("Minor") as MemberInfo;
            object? majVal = (majProp is PropertyInfo pm) ? pm.GetValue(versionObj)
                          : (majProp is FieldInfo fm) ? fm.GetValue(versionObj)
                          : null;
            object? minVal = (minProp is PropertyInfo pn) ? pn.GetValue(versionObj)
                          : (minProp is FieldInfo fn) ? fn.GetValue(versionObj)
                          : null;
            if (majVal == null || minVal == null) return false;
            major = Convert.ToInt32(majVal);
            minor = Convert.ToInt32(minVal);
            return true;
        }
        catch { return false; }
    }

    private static GameBranch ClassifyBranch(object versionObj, int major, int minor)
    {
        // Branch is decided by ApplicationVersion's version TYPE, NOT the minor number.
        // 1.4.x used to be the beta line but is now the PUBLIC/stable line, so the old
        // "minor >= 4 => Beta" rule misreported public 1.4.6 as beta and wrong-gated the
        // beta-only sigsafe patches. Read ApplicationVersionType when the game exposes it.
        try
        {
            var t = versionObj.GetType();
            MemberInfo? tm = (MemberInfo?)t.GetProperty("ApplicationVersionType") ?? t.GetField("ApplicationVersionType");
            object? tv = (tm is PropertyInfo p) ? p.GetValue(versionObj)
                       : (tm is FieldInfo f) ? f.GetValue(versionObj)
                       : null;
            var name = tv?.ToString() ?? string.Empty;
            if (name.IndexOf("Development", StringComparison.OrdinalIgnoreCase) >= 0
             || name.IndexOf("Beta", StringComparison.OrdinalIgnoreCase) >= 0)
                return GameBranch.Beta;
        }
        catch { /* fall through to the version-number default */ }

        // A valid 1.x with a non-beta (or unreadable) type is the public/stable line today.
        return major >= 1 ? GameBranch.Public : GameBranch.Unknown;
    }
}
