// ModReady clean-room re-implementation of Bannerlord.BUTR.Shared.Helpers.ModuleInfoHelper.
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

using ModReady.Foundation;

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

        // Plan A: TaleWorlds.ModuleManager.ModuleHelper.GetModules(...) (newer game).
        // BUG FIX (B12): on the current game the only overload is the static
        // `GetModules(Func<ModuleInfoExtended, bool>)` predicate form -- there is
        // no parameterless overload. AccessTools2.Method(helperType, "GetModules")
        // with no parameter types still resolves that 1-param MethodInfo (it's the
        // sole match), so the old code's `m.Invoke(null, null)` threw
        // TargetParameterCountException. Worse, Plan B lived in the SAME try block,
        // so that exception jumped straight to the catch and Plan B never ran --
        // GetLoadedModules() always returned empty. Fix: build an all-true
        // predicate delegate matching the resolved method's actual parameter type
        // via reflection (so this keeps working even if the delegate's generic
        // argument changes across game versions), and give Plan A its own
        // try/catch so a Plan A failure still falls through to Plan B.
        try
        {
            var helperType = AccessTools2.TypeByName("TaleWorlds.ModuleManager.ModuleHelper");
            if (helperType != null)
            {
                var m = AccessTools2.Method(helperType, "GetModules");
                if (m != null)
                {
                    var parameters = m.GetParameters();
                    object?[]? invokeArgs = null;
                    if (parameters.Length == 0)
                    {
                        invokeArgs = null;
                    }
                    else if (parameters.Length == 1 && typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType))
                    {
                        var predicate = BuildAllTruePredicate(parameters[0].ParameterType);
                        if (predicate == null)
                        {
                            throw new InvalidOperationException(
                                $"GetModules predicate parameter type {parameters[0].ParameterType} is not a supported Func<T, bool> shape.");
                        }
                        invokeArgs = new object?[] { predicate };
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"GetModules has an unexpected signature ({parameters.Length} params).");
                    }

                    if (m.Invoke(null, invokeArgs) is System.Collections.IEnumerable seq)
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
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "GetLoadedModules:PlanA", ex);
        }

        // Plan B: TaleWorlds.MountAndBlade.Module.CurrentModule + ModuleInfos.
        // Own try/catch so it still runs even when Plan A throws above.
        try
        {
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
            DiagLog.LogCaught(Tag, "GetLoadedModules:PlanB", ex);
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

    /// <summary>
    /// Builds an all-true predicate delegate matching <paramref name="delegateType"/>,
    /// e.g. `Func&lt;ModuleInfoExtended, bool&gt;`. Used to satisfy
    /// ModuleHelper.GetModules(Func&lt;T, bool&gt;) via reflection without hard-coding T
    /// (see B12 fix note in GetLoadedModules above). Returns null if delegateType
    /// isn't a one-arg-returning-bool delegate shape.
    /// </summary>
    private static Delegate? BuildAllTruePredicate(Type delegateType)
    {
        try
        {
            var invoke = delegateType.GetMethod("Invoke");
            if (invoke == null || invoke.ReturnType != typeof(bool)) return null;
            var invokeParams = invoke.GetParameters();
            if (invokeParams.Length != 1) return null;

            var trueFuncMethod = typeof(ModuleInfoHelper)
                .GetMethod(nameof(AlwaysTrue), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(invokeParams[0].ParameterType);
            return Delegate.CreateDelegate(delegateType, trueFuncMethod);
        }
        catch { return null; }
    }

    private static bool AlwaysTrue<T>(T _) => true;

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
