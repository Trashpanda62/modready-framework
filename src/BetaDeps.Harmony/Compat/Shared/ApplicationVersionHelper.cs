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
    /// Returns null if no probe below resolves.
    /// </summary>
    /// <remarks>
    /// BUG FIX (B19): the old Plan A/B here probed for a static
    /// "GameVersion" method on TaleWorlds.ModuleManager.ModuleHelper and on
    /// a TaleWorlds.ModuleManager.ApplicationVersionHelper type. Neither
    /// exists on the current game -- ModuleHelper has no GameVersion member
    /// (confirmed against every decompiled call site: GetModules,
    /// GetActiveModules, GetModuleFullPath, GetXmlPath, IsModuleActive,
    /// etc., never GameVersion), and ApplicationVersionHelper doesn't exist
    /// in TaleWorlds.ModuleManager at all. Both probes always missed, so
    /// this unconditionally returned null. The real surface (also what
    /// TaleWorlds.Core.CurrentVersion uses internally, and what
    /// BetaDeps.Foundation.VersionProbe already relies on as its Plan A) is
    /// TaleWorlds.Library.ApplicationVersion.FromParametersFile().
    /// </remarks>
    public static object? GameVersion()
    {
        try
        {
            // Plan A: TaleWorlds.Library.ApplicationVersion.FromParametersFile()
            // -- the real, decomp-verified way the engine itself resolves the
            // running game's version.
            var appVerType = AccessTools2.TypeByName("TaleWorlds.Library.ApplicationVersion");
            if (appVerType != null)
            {
                var m = AccessTools2.Method(appVerType, "FromParametersFile", Array.Empty<Type>());
                if (m != null) return m.Invoke(null, null);
            }

            // Plan B: TaleWorlds.MountAndBlade.Module.CurrentModule.Version,
            // in case FromParametersFile isn't reachable this early.
            var moduleType = AccessTools2.TypeByName("TaleWorlds.MountAndBlade.Module");
            if (moduleType != null)
            {
                var currentProp = AccessTools2.Property(moduleType, "CurrentModule");
                var current = currentProp?.GetValue(null);
                if (current != null)
                {
                    var verProp = AccessTools2.Property(current.GetType(), "Version")
                               ?? AccessTools2.Property(current.GetType(), "ModuleVersion");
                    var v = verProp?.GetValue(current);
                    if (v != null) return v;
                }
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
