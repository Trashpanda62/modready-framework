// BetaDeps clean-room re-implementation of Bannerlord.BUTR.Shared.Helpers.ModuleInfoHelper.
// MIT, copyright 2026 Maxfield Management Group.
//
// Consumer mods call ModuleInfoHelper to enumerate the loaded modules at
// runtime (to validate load order, look up module versions, find a
// module that owns a particular type, etc.). Wraps the TaleWorlds ModuleManager
// API via reflection so the surface is independent of game-version drift.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.BUTR.Shared.Helpers;

/// <summary>
/// Minimal ModuleInfo surface compatible with the BUTR API consumers expect.
/// Real ModuleInfo has many more fields; we expose the ones community mods
/// actually read in the wild (Id, Name, Version, Folder).
/// </summary>
public sealed class ModuleInfo
{
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Folder { get; }

    public ModuleInfo(string id, string name, string version, string folder)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Version = version ?? string.Empty;
        Folder = folder ?? string.Empty;
    }

    public override string ToString() => $"{Id} v{Version}";
}

public static class ModuleInfoHelper
{
    private const string Tag = "ModuleInfoHelper";

    /// <summary>
    /// Returns every module the game launched with, in load order. Adapts
    /// the TaleWorlds.ModuleManager.ModuleHelper API via reflection so this
    /// works across the game versions community mods compile against.
    /// </summary>
    public static IEnumerable<ModuleInfo> GetLoadedModules()
    {
        var list = new List<ModuleInfo>();
        try
        {
            // Plan A: TaleWorlds.ModuleManager.ModuleHelper.GetModules() (newer game)
            var helperType = AccessTools2.TypeByName("TaleWorlds.ModuleManager.ModuleHelper");
            if (helperType != null)
            {
                var m = AccessTools2.Method(helperType, "GetModules");
                if (m != null)
                {
                    if (m.Invoke(null, null) is System.Collections.IEnumerable seq)
                    {
                        foreach (var moduleObj in seq)
                        {
                            var info = ExtractModuleInfo(moduleObj);
                            if (info != null) list.Add(info);
                        }
                        return list;
                    }
                }
            }

            // Plan B: TaleWorlds.MountAndBlade.Module.CurrentModule + ModuleInfos
            var moduleType = AccessTools2.TypeByName("TaleWorlds.MountAndBlade.Module");
            if (moduleType != null)
            {
                var currentProp = AccessTools2.Property(moduleType, "CurrentModule");
                var current = currentProp?.GetValue(null);
                if (current != null)
                {
                    var moduleInfosProp = current.GetType().GetProperty("ModuleInfos") ?? current.GetType().GetProperty("LoadedModules");
                    if (moduleInfosProp?.GetValue(current) is System.Collections.IEnumerable seq)
                    {
                        foreach (var moduleObj in seq)
                        {
                            var info = ExtractModuleInfo(moduleObj);
                            if (info != null) list.Add(info);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "GetLoadedModules", ex);
        }
        return list;
    }

    /// <summary>
    /// Finds which loaded module owns the given .NET type, by walking up the
    /// assembly's file path to find a Modules\&lt;name&gt;\ directory.
    /// </summary>
    public static ModuleInfo? GetModuleByType(Type? type)
    {
        if (type == null) return null;
        try
        {
            var asmPath = type.Assembly.Location;
            if (string.IsNullOrEmpty(asmPath)) return null;
            var current = Path.GetDirectoryName(asmPath);
            while (!string.IsNullOrEmpty(current))
            {
                var parent = Path.GetDirectoryName(current);
                if (parent != null && Path.GetFileName(parent).Equals("Modules", StringComparison.OrdinalIgnoreCase))
                {
                    var moduleFolderName = Path.GetFileName(current);
                    return GetLoadedModules().FirstOrDefault(m =>
                        string.Equals(m.Id, moduleFolderName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(m.Folder), moduleFolderName, StringComparison.OrdinalIgnoreCase));
                }
                current = parent;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"GetModuleByType({type.FullName})", ex);
        }
        return null;
    }

    private static ModuleInfo? ExtractModuleInfo(object moduleObj)
    {
        if (moduleObj == null) return null;
        try
        {
            var t = moduleObj.GetType();
            var id = AccessTools2.Property(t, "Id")?.GetValue(moduleObj)?.ToString() ?? string.Empty;
            var name = AccessTools2.Property(t, "Name")?.GetValue(moduleObj)?.ToString() ?? string.Empty;
            var folder = AccessTools2.Property(t, "FolderPath")?.GetValue(moduleObj)?.ToString()
                       ?? AccessTools2.Property(t, "Folder")?.GetValue(moduleObj)?.ToString()
                       ?? string.Empty;
            var version = AccessTools2.Property(t, "Version")?.GetValue(moduleObj)?.ToString() ?? string.Empty;
            return new ModuleInfo(id, name, version, folder);
        }
        catch { return null; }
    }
}
