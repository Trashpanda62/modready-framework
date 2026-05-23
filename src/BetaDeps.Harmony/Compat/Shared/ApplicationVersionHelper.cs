// BetaDeps clean-room re-implementation of Bannerlord.BUTR.Shared.Helpers.ApplicationVersionHelper.
// MIT, copyright 2026 Maxfield Management Group.
//
// Reads + parses TaleWorlds ApplicationVersion structs without taking a
// hard compile-time dependency on TaleWorlds.Library. Reflection-based so
// it survives game-version drift.

using System;

using BetaDeps.Foundation;

using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.BUTR.Shared.Helpers;

public static class ApplicationVersionHelper
{
    private const string Tag = "ApplicationVersionHelper";

    /// <summary>
    /// Returns the running game's ApplicationVersion, boxed as object so
    /// callers don't take a compile-time dep on TaleWorlds.Library.
    /// Returns null if the game's ModuleHelper isn't reachable.
    /// </summary>
    public static object? GameVersion()
    {
        try
        {
            var moduleHelperType = AccessTools2.TypeByName("TaleWorlds.ModuleManager.ModuleHelper");
            if (moduleHelperType != null)
            {
                var m = AccessTools2.Method(moduleHelperType, "GameVersion");
                if (m != null) return m.Invoke(null, null);
            }
            var versionHelperType = AccessTools2.TypeByName("TaleWorlds.ModuleManager.ApplicationVersionHelper");
            if (versionHelperType != null)
            {
                var m = AccessTools2.Method(versionHelperType, "GameVersion");
                if (m != null) return m.Invoke(null, null);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "GameVersion", ex);
        }
        return null;
    }

    /// <summary>
    /// Parses an ApplicationVersion from a string like "v1.4.2.0" or "e1.4.x".
    /// Returns true on success and sets <paramref name="version"/> to the
    /// boxed ApplicationVersion; false on parse failure.
    /// </summary>
    public static bool TryParse(string text, out object? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            var appVerType = AccessTools2.TypeByName("TaleWorlds.Library.ApplicationVersion");
            if (appVerType == null) return false;
            var tryParseMethod = AccessTools2.Method(appVerType, "TryParse", new[] { typeof(string), appVerType.MakeByRefType() });
            if (tryParseMethod == null) return false;
            var args = new object?[] { text, null };
            var result = (bool)tryParseMethod.Invoke(null, args)!;
            if (result) version = args[1];
            return result;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryParse({text})", ex);
            return false;
        }
    }
}
