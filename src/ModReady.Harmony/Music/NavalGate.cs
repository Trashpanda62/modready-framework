// ModReady.Harmony -- NavalGate (BYO music-picker workstream C4)
//
// War Sails ships as the module folder Modules\NavalDLC and drives naval-combat
// music through the same PSAI pipeline (prefix-1024 themes). So the Naval BYO
// context is just another PSAI redirect -- but only meaningful when War Sails is
// installed. NavalGate is the single detection point:
//   - PsaiRedirectManager adds "NavalDLC" to the additive soundtrack merge only
//     when available (otherwise PSAI rejects an unknown module name).
//   - UI-A greys the Naval picker row ("Requires War Sails DLC") when absent.
//
// Detection is deliberately twofold so it works at every lifecycle point:
//   - folder under the Modules root  (reliable before assemblies load)
//   - a loaded NavalDLC / War Sails assembly in the AppDomain (post-load)
//
// Engine-free (System.IO + reflection over loaded assembly NAMES only -- no
// TaleWorlds type link), so it is unit-tested off-engine.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;

namespace ModReady.Harmony.Music;

public static class NavalGate
{
    /// <summary>The War Sails module folder name under Modules\.</summary>
    public const string NavalModuleFolder = "NavalDLC";

    /// <summary>
    /// True if War Sails (NavalDLC) appears installed. Pass the absolute path to
    /// the Modules\ root when known (the folder check is the most reliable signal,
    /// available even before the DLC's assemblies load); a loaded-assembly scan is
    /// the fallback. Never throws -- returns false on any error.
    /// </summary>
    public static bool IsAvailable(string? modulesRoot)
    {
        try
        {
            if (!string.IsNullOrEmpty(modulesRoot))
            {
                var dir = Path.Combine(modulesRoot!, NavalModuleFolder);
                if (Directory.Exists(dir)) return true;
            }
        }
        catch { /* fall through to the assembly scan */ }

        return IsNavalAssemblyLoaded();
    }

    /// <summary>
    /// True if a NavalDLC / War Sails assembly is already in the AppDomain.
    /// Name-only inspection; no type is loaded.
    /// </summary>
    public static bool IsNavalAssemblyLoaded()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name ?? string.Empty;
                if (n.IndexOf("NavalDLC", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("WarSails", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        catch { }
        return false;
    }
}
