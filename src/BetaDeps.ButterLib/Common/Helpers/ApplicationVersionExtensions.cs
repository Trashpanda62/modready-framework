// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Convenience extension methods on TaleWorlds.Library.ApplicationVersion.
// Worked through reflection so this assembly stays independent of
// TaleWorlds.Library at compile time. Consumer mods call:
//
//   if (gameVersion.IsAtLeast(1, 2, 0)) { ... }
//   if (gameVersion.IsSameMinor(otherVersion)) { ... }

using System;
using System.Reflection;

namespace Bannerlord.ButterLib.Common.Helpers;

public static class ApplicationVersionExtensions
{
    /// <summary>True if this version is &gt;= the supplied (major, minor, revision).</summary>
    public static bool IsAtLeast(this object? appVersion, int major, int minor, int revision = 0)
    {
        if (appVersion == null) return false;
        if (!TryReadVersion(appVersion, out var a, out var b, out var c)) return false;
        if (a != major) return a > major;
        if (b != minor) return b > minor;
        return c >= revision;
    }

    /// <summary>True if both versions share the same major+minor pair.</summary>
    public static bool IsSameMinor(this object? a, object? b)
    {
        if (a == null || b == null) return false;
        if (!TryReadVersion(a, out var amaj, out var amin, out _)) return false;
        if (!TryReadVersion(b, out var bmaj, out var bmin, out _)) return false;
        return amaj == bmaj && amin == bmin;
    }

    /// <summary>Return the Major/Minor/Revision tuple, or zeros on failure.</summary>
    public static (int Major, int Minor, int Revision) ToTuple(this object? appVersion)
    {
        if (appVersion == null) return (0, 0, 0);
        if (!TryReadVersion(appVersion, out var a, out var b, out var c)) return (0, 0, 0);
        return (a, b, c);
    }

    private static bool TryReadVersion(object v, out int major, out int minor, out int revision)
    {
        major = minor = revision = 0;
        try
        {
            var t = v.GetType();
            major    = ReadInt(v, t, "Major");
            minor    = ReadInt(v, t, "Minor");
            revision = ReadInt(v, t, "Revision");
            return true;
        }
        catch { return false; }
    }

    private static int ReadInt(object v, Type t, string name)
    {
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p != null && p.GetValue(v) is int pi) return pi;
        var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (f != null && f.GetValue(v) is int fi) return fi;
        return 0;
    }
}
