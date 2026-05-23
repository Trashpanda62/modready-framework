// BetaDeps.Foundation -- VersionProbe
//
// Detects which game branch is running so the rest of BetaDeps knows
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

namespace BetaDeps.Foundation;

public enum GameBranch
{
    Unknown,
    Public,   // e1.3.x or earlier
    Beta,     // e1.4.x and above
}

public static class VersionProbe
{
    private static GameBranch? _cached;
    private static int? _cachedMajor;
    private static int? _cachedMinor;

    /// <summary>Returns the detected branch. Cached after first call.</summary>
    public static GameBranch Branch
    {
        get
        {
            if (_cached.HasValue) return _cached.Value;
            _cached = Detect();
            return _cached.Value;
        }
    }

    /// <summary>Major version number (e.g. 1 for e1.4.2).</summary>
    public static int Major => _cachedMajor ?? 0;

    /// <summary>Minor version number (e.g. 4 for e1.4.2).</summary>
    public static int Minor => _cachedMinor ?? 0;

    public static bool IsBeta => Branch == GameBranch.Beta;
    public static bool IsPublic => Branch == GameBranch.Public;

    private static GameBranch Detect()
    {
        try
        {
            // TaleWorlds.Library.ApplicationVersion exposes Major/Minor/Revision.
            // We probe via reflection to avoid a hard compile-time link in case
            // we ever target a build without TaleWorlds.Library in scope.
            var appVerType = Type.GetType("TaleWorlds.Library.ApplicationVersion, TaleWorlds.Library", throwOnError: false)
                          ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.Library.ApplicationVersion");

            // The "current" version often lives on ApplicationVersionHelper or
            // Module / Common. Try a few well-known surface points by reflection.
            // First: TaleWorlds.MountAndBlade.Module.CurrentModule?.Version? -- too brittle.
            // Better: a generic scan of any static method named "GameVersion" or
            // a static property named "Version" on a type called ApplicationVersionHelper.

            // Plan A: TaleWorlds.ModuleManager.ApplicationVersionHelper.GameVersion()
            var helperType = Type.GetType("TaleWorlds.ModuleManager.ApplicationVersionHelper, TaleWorlds.ModuleManager", throwOnError: false)
                          ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.ModuleManager.ApplicationVersionHelper");
            if (helperType != null)
            {
                var m = helperType.GetMethod("GameVersion", BindingFlags.Public | BindingFlags.Static);
                if (m != null)
                {
                    var v = m.Invoke(null, null);
                    if (v != null && TryExtractMajorMinor(v, out var maj, out var min))
                    {
                        _cachedMajor = maj;
                        _cachedMinor = min;
                        return ClassifyBranch(maj, min);
                    }
                }
            }

            // Plan B: TaleWorlds.MountAndBlade.Module.CurrentModule.Version
            var moduleType = Type.GetType("TaleWorlds.MountAndBlade.Module, TaleWorlds.MountAndBlade", throwOnError: false)
                          ?? FindTypeAcrossLoadedAssemblies("TaleWorlds.MountAndBlade.Module");
            if (moduleType != null)
            {
                var currentProp = moduleType.GetProperty("CurrentModule", BindingFlags.Public | BindingFlags.Static);
                if (currentProp != null)
                {
                    var current = currentProp.GetValue(null);
                    if (current != null)
                    {
                        var verProp = current.GetType().GetProperty("Version") ?? current.GetType().GetProperty("ModuleVersion");
                        var v = verProp?.GetValue(current);
                        if (v != null && TryExtractMajorMinor(v, out var maj, out var min))
                        {
                            _cachedMajor = maj;
                            _cachedMinor = min;
                            return ClassifyBranch(maj, min);
                        }
                    }
                }
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

    private static GameBranch ClassifyBranch(int major, int minor)
    {
        if (major < 1) return GameBranch.Unknown;
        if (major == 1 && minor >= 4) return GameBranch.Beta;
        if (major == 1) return GameBranch.Public;
        return GameBranch.Beta; // future majors -> beta side until told otherwise
    }
}
