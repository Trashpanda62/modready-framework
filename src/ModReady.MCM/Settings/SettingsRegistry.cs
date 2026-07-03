// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// SettingsRegistry is the discovery layer between consumer mods and the
// MCM UI tab. AttributeGlobalSettings<T>.Instance is lazy -- a mod's
// settings don't get constructed until something reads .Instance. Without
// discovery the UI would render an empty list because nobody has poked
// each mod's settings into life yet.
//
// On MCM SubModule load we sweep every loaded assembly, find each
// AttributeGlobalSettings<T> subclass, and call its TSelf.Instance accessor.
// That triggers construction + JSON load, and the instance lands in our
// registry where the UI can read it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using ModReady.Foundation;

using MCM.Abstractions;
using MCM.Abstractions.Events;

using Mono.Cecil;

namespace MCM.Internal;

public sealed class RegisteredSettings
{
    public BaseSettings Instance { get; }
    public Type SettingsType => Instance.GetType();
    public string Id => Instance.Id;

    /// <summary>
    /// Returns the mod author's chosen DisplayName by default. v0.5.6 added
    /// an override so OptionsVMMixin.RebuildModList can decorate it for the
    /// Mod Config picker (disambiguating duplicates like "Raise your Torch",
    /// prefixing cryptic names like "BUG-FIX-0" with the source folder).
    /// v1.0 (BEW shim follow-up): TextHelper.StripLocalizationKeys is applied
    /// at the getter so "{=Key}Fallback" tokens never leak through to the
    /// search filter or any other downstream consumer — single source of
    /// truth for the clean string. Idempotent: the rendering site still
    /// re-strips, which is cheap and defensive.
    /// </summary>
    public string DisplayName
    {
        get => MCM.Internal.TextHelper.StripLocalizationKeys(_displayNameOverride ?? Instance.DisplayName);
        set => _displayNameOverride = value;
    }
    private string? _displayNameOverride;

    /// <summary>
    /// Name of the assembly the settings class lives in (used by the UI to
    /// annotate cryptic DisplayName strings like "BUG-FIX-0" with their
    /// source folder, e.g. "AIInfluence — BUG-FIX-0").
    /// </summary>
    public string SourceAssemblyName => SettingsType.Assembly.GetName().Name ?? string.Empty;

    internal RegisteredSettings(BaseSettings instance) { Instance = instance; }
}

public static class SettingsRegistry
{
    private const string Tag = "MCM.SettingsRegistry";

    private static readonly object _gate = new();
    private static readonly Dictionary<string, RegisteredSettings> _byId = new(StringComparer.Ordinal);
    private static bool _discoverRan;

    /// <summary>All discovered settings classes, keyed by their Id.</summary>
    public static IReadOnlyCollection<RegisteredSettings> All
    {
        get { lock (_gate) { return _byId.Values.ToArray(); } }
    }

    /// <summary>Look up a settings class by its Id.</summary>
    public static RegisteredSettings? TryGet(string id)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out var r) ? r : null;
        }
    }

    /// <summary>
    /// Resolves the Modules\ folder root from this assembly's location.
    /// Returns null if the location can't be parsed.
    /// </summary>
    private static string? TryGetModulesRoot()
    {
        try
        {
            var asmPath = typeof(SettingsRegistry).Assembly.Location;
            if (string.IsNullOrEmpty(asmPath)) return null;
            var dir = Path.GetDirectoryName(asmPath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (string.Equals(Path.GetFileName(dir), "Modules", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Returns true iff the given assembly belongs to a currently-enabled
    /// module folder (or doesn't live under Modules\ at all -- system/Talevworlds
    /// assemblies). Used to filter orphan-mod settings out of Mod Config when
    /// the underlying module is disabled but the DLL still happens to be in
    /// the AppDomain.
    ///
    /// If <paramref name="enabledFolders"/> is empty (detection failed), we
    /// permissively return true so the user doesn't lose all settings on a
    /// detection edge case.
    /// </summary>
    private static bool IsAssemblyOwningModuleEnabled(
        Assembly asm,
        string? modulesRoot,
        HashSet<string> enabledFolders)
    {
        if (enabledFolders == null || enabledFolders.Count == 0)
            return true; // fail-open: no signal -> don't filter
        if (asm == null) return true;

        string? asmLoc = null;
        try { asmLoc = asm.Location; } catch { /* dynamic assemblies have no location */ }
        if (string.IsNullOrEmpty(asmLoc)) return true; // can't classify -> don't filter

        if (string.IsNullOrEmpty(modulesRoot)) return true; // no Modules root -> don't filter

        // Is asmLoc under modulesRoot? If not, it's a system/TaleWorlds/
        // dotnet runtime DLL -- always pass through (those don't register
        // mod settings anyway, but no point filtering them).
        try
        {
            if (!asmLoc!.StartsWith(modulesRoot!, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { return true; }

        // Walk up the asm's directory until parent == modulesRoot. That
        // directory's name is the module folder.
        try
        {
            var dir = Path.GetDirectoryName(asmLoc);
            while (!string.IsNullOrEmpty(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parent)
                    && string.Equals(parent, modulesRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var folder = Path.GetFileName(dir);
                    return enabledFolders.Contains(folder);
                }
                dir = parent;
            }
        }
        catch { }
        return true; // walk failed -> fail open
    }

    /// <summary>
    /// Walk every loaded assembly for AttributeGlobalSettings&lt;T&gt; subclasses
    /// and register them. Idempotent; safe to call multiple times.
    /// </summary>
    public static void DiscoverAll()
    {
        try
        {
            // Eagerly load every DLL under each module's bin folder so the
            // scan below sees consumer mods' MCM helper DLLs even when the
            // main DLL hasn't dereferenced them yet. Without this, mods
            // that put their settings classes in a separate "* MCM.dll"
            // file (BUTR convention) stay invisible because .NET only
            // loads referenced assemblies lazily when the JIT first
            // touches one of their types.
            EagerLoadModuleAssemblies();
            WarnOnDuplicateSaveDefiners();

            int newlyRegistered = 0;
            var asms = AppDomain.CurrentDomain.GetAssemblies();

            // Log the non-system assemblies in the AppDomain so the user can
            // see which of their installed mods actually loaded a DLL. If a
            // mod's DLL isn't here, the mod is disabled in the launcher or
            // failed to load earlier (check the BUTR crash report).
            var modLikeAsms = asms
                .Select(a => a.GetName().Name ?? "?")
                .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                         && !n.StartsWith("Microsoft", StringComparison.Ordinal)
                         && !n.StartsWith("mscorlib", StringComparison.Ordinal)
                         && !n.StartsWith("netstandard", StringComparison.Ordinal)
                         && !n.StartsWith("Newtonsoft", StringComparison.Ordinal)
                         && !n.StartsWith("TaleWorlds", StringComparison.Ordinal)
                         && !n.StartsWith("MonoMod", StringComparison.Ordinal)
                         && !n.StartsWith("Mono.Cecil", StringComparison.Ordinal)
                         && !n.StartsWith("0Harmony", StringComparison.Ordinal)
                         && !n.StartsWith("Anonymously Hosted", StringComparison.Ordinal))
                .OrderBy(n => n)
                .ToArray();
            DiagLog.Log(Tag, $"DiscoverAll: scanning {asms.Length} loaded assemblies; non-system mod-like: {modLikeAsms.Length}");
            // Per-assembly listing intentionally suppressed -- pre-2026-05-17 we logged
            // one line per assembly here, which produced ~100 lines per DiscoverAll
            // call and dominated the runtime.log. The summary line above is sufficient
            // for normal operation; if you need the full list for diagnostics, set a
            // breakpoint or re-enable this loop temporarily.

            // Compute the set of currently-enabled module folder names so
            // we can filter out orphan-mod settings: assemblies whose DLLs
            // got eager-loaded but whose owning module is disabled in the
            // launcher. Without this filter, ROT's settings classes (and
            // similar) keep appearing in Mod Config even after the user
            // unchecks ROT-Core in BLSE.
            //
            // LauncherData.xml is the source of truth -- it's what the user
            // physically checked or unchecked in the launcher UI. Reading
            // TaleWorlds.Module.CurrentModule.ModuleList here would return
            // EVERY installed module's metadata, not just the enabled ones,
            // which is why the prior version of this filter never excluded
            // anything. Fall through to the other detection paths only if
            // we can't read the XML at all.
            var enabledFolders = ModReady.Foundation.IncompatibleModDetector.GetEnabledModsFromLauncherData();
            string? modulesRoot = TryGetModulesRoot();
            if (enabledFolders.Count == 0)
            {
                enabledFolders = ParseEnabledModulesFromCommandLine();
                if (enabledFolders.Count == 0)
                    enabledFolders = GetEnabledModulesFromTaleWorlds();
                if (enabledFolders.Count == 0 && !string.IsNullOrEmpty(modulesRoot))
                {
                    var alreadyLoaded = new HashSet<string>(
                        AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetName().Name ?? string.Empty)
                            .Where(n => n.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    enabledFolders = DetectEnabledModulesByLoadedAssemblies(modulesRoot!, alreadyLoaded);
                }
            }
            DiagLog.Log(Tag, $"DiscoverAll: enabled module folders for filter: {enabledFolders.Count} (source: LauncherData.xml preferred)");

            foreach (var asm in asms)
            {
                if (!IsAssemblyOwningModuleEnabled(asm, modulesRoot, enabledFolders))
                {
                    // Settings from an unloaded/disabled module -- skip so
                    // they don't ghost into Mod Config.
                    continue;
                }
                newlyRegistered += DiscoverInAssembly(asm);
            }

            // Also merge in fluent-builder settings. Mods that use the
            // ISettingsBuilder.BuildAsGlobal().Register() pattern (Diplomacy,
            // ImprovedGarrisons, RTSCamera, BetterSmithingContinued and many
            // others) never inherit AttributeGlobalSettings<T>, so the
            // assembly scan above doesn't find them. They register themselves
            // into FluentGlobalSettings.All during their OnSubModuleLoad,
            // which has already run by the time DiscoverAll fires.
            try
            {
                foreach (var fs in MCM.Internal.FluentSettingsRegistry.All)
                {
                    // Apply the same enabled-module filter to fluent settings.
                    if (!IsAssemblyOwningModuleEnabled(fs.GetType().Assembly, modulesRoot, enabledFolders))
                        continue;
                    lock (_gate)
                    {
                        if (string.IsNullOrEmpty(fs.Id)) continue;
                        if (_byId.ContainsKey(fs.Id)) continue;
                        _byId[fs.Id] = new RegisteredSettings(fs);
                    }
                    newlyRegistered++;
                    DiagLog.Log(Tag, $"REGISTERED '{fs.Id}' (fluent: {fs.GetType().FullName})");
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "DiscoverAll/FluentMerge", ex);
            }

            // S3: If ButterLib wired the bridge, register a built-in page for subsystem toggles.
            if (ModReady.Foundation.SubSystemBridge.IsAvailable)
            {
                const string ssId = "ModReady.ButterLib.SubSystems";
                lock (_gate)
                {
                    if (!_byId.ContainsKey(ssId))
                    {
                        _byId[ssId] = new RegisteredSettings(new SubSystemSettingsPage());
                        newlyRegistered++;
                        DiagLog.Log(Tag, $"REGISTERED '{ssId}' (built-in subsystem settings page)");
                    }
                }
            }

            if (!_discoverRan || newlyRegistered > 0)
                DiagLog.Log(Tag, $"DiscoverAll: registered {newlyRegistered} new settings class(es) (total {_byId.Count})");
            _discoverRan = true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "DiscoverAll", ex);
        }
    }

    /// <summary>
    /// Parse the enabled-modules list from the launcher's command line. BLSE
    /// (and TaleWorlds' own launcher) pass it as
    /// `_MODULES_*Name1*Name2*..._MODULES_`. Returns the set of module folder
    /// names; returns an empty set if the marker isn't present so callers can
    /// fall back to "scan everything".
    /// </summary>
    private static HashSet<string> ParseEnabledModulesFromCommandLine()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var cmdLine = Environment.CommandLine ?? string.Empty;
            // Truncate to a reasonable length for the log.
            DiagLog.Log(Tag, $"CommandLine: {(cmdLine.Length > 400 ? cmdLine.Substring(0,400) + "..." : cmdLine)}");
            const string start = "_MODULES_";
            var i = cmdLine.IndexOf(start, StringComparison.Ordinal);
            if (i < 0) return result;
            var j = cmdLine.IndexOf(start, i + start.Length, StringComparison.Ordinal);
            if (j < 0) return result;
            var middle = cmdLine.Substring(i + start.Length, j - (i + start.Length));
            foreach (var name in middle.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = name.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ParseEnabledModulesFromCommandLine", ex); }
        return result;
    }

    /// <summary>
    /// Fallback path -- ask TaleWorlds' Module.CurrentModule for the active
    /// ModuleList via reflection. This is what's actually loaded into the
    /// AppDomain regardless of how the launcher passed the list. Returns an
    /// empty set if reflection fails (early-startup edge cases, type renames,
    /// etc.) so the caller can fall back further.
    /// </summary>
    private static HashSet<string> GetEnabledModulesFromTaleWorlds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Module type lives in TaleWorlds.MountAndBlade. Resolve by walking
            // currently-loaded assemblies rather than Type.GetType with a fully
            // qualified name (assembly identity varies by game version).
            Type? moduleType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "TaleWorlds.MountAndBlade") continue;
                moduleType = asm.GetType("TaleWorlds.MountAndBlade.Module");
                if (moduleType != null) break;
            }
            if (moduleType == null) return result;

            var currentProp = moduleType.GetProperty("CurrentModule", BindingFlags.Public | BindingFlags.Static);
            var current = currentProp?.GetValue(null);
            if (current == null) return result;

            // Try ModuleList (preferred), Modules, or LoadedModules.
            System.Collections.IEnumerable? moduleList = null;
            foreach (var name in new[] { "ModuleList", "Modules", "LoadedModules" })
            {
                var prop = current.GetType().GetProperty(name);
                if (prop == null) continue;
                moduleList = prop.GetValue(current) as System.Collections.IEnumerable;
                if (moduleList != null) break;
            }
            if (moduleList == null) return result;

            foreach (var m in moduleList)
            {
                if (m == null) continue;
                // ModuleInfo has Id, Alias, FolderName -- try each in order.
                string? id = null;
                foreach (var name in new[] { "Id", "Alias", "FolderName", "Name" })
                {
                    var prop = m.GetType().GetProperty(name);
                    id = prop?.GetValue(m) as string;
                    if (!string.IsNullOrEmpty(id)) break;
                }
                if (!string.IsNullOrEmpty(id)) result.Add(id!);
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "GetEnabledModulesFromTaleWorlds", ex); }
        return result;
    }

    /// <summary>
    /// Heuristic: a module folder is "enabled" iff at least one of its bin DLLs
    /// is already loaded in the AppDomain. BLSE / TaleWorlds' launcher only
    /// load DLLs for enabled mods, so the presence of an assembly named after
    /// any DLL in that folder is a reliable signal that the user checked the
    /// mod in the launcher. Used as the last-resort fallback when neither the
    /// command-line marker nor Module.CurrentModule.ModuleList is populated
    /// (which is the case under BLSE LauncherEx).
    /// </summary>
    private static HashSet<string> DetectEnabledModulesByLoadedAssemblies(string modulesRoot, HashSet<string> alreadyLoaded)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var modDir in Directory.GetDirectories(modulesRoot))
            {
                var moduleBin = Path.Combine(modDir, "bin", "Win64_Shipping_Client");
                if (!Directory.Exists(moduleBin)) continue;
                foreach (var dll in Directory.GetFiles(moduleBin, "*.dll"))
                {
                    var simpleName = Path.GetFileNameWithoutExtension(dll);
                    if (alreadyLoaded.Contains(simpleName))
                    {
                        result.Add(Path.GetFileName(modDir));
                        break; // one match is enough; move to the next folder
                    }
                }
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "DetectEnabledModulesByLoadedAssemblies", ex); }
        return result;
    }

    /// <summary>
    /// Walk every module's bin folder and Assembly.LoadFrom each DLL that
    /// isn't already in the AppDomain. This is the standard MCM pattern for
    /// catching settings classes that live in helper assemblies a mod's main
    /// DLL only touches lazily (e.g. on first menu access). Errors per-DLL
    /// are caught and logged but don't abort the sweep.
    /// </summary>
    public static void EagerLoadModuleAssemblies()
    {
        try
        {
            // Locate the Modules root via our own assembly location.
            // MCMv5.dll lives in Modules\ModReady\bin\Win64_Shipping_Client\
            var ownPath = typeof(SettingsRegistry).Assembly.Location;
            if (string.IsNullOrEmpty(ownPath))
            {
                DiagLog.Log(Tag, "EagerLoad: own assembly has no Location, skipping");
                return;
            }
            var binDir         = Path.GetDirectoryName(ownPath);          // Win64_Shipping_Client
            var modReadyBin    = Path.GetDirectoryName(binDir);            // bin
            var modReadyModule = Path.GetDirectoryName(modReadyBin);       // ModReady
            var modulesRoot    = Path.GetDirectoryName(modReadyModule);    // Modules
            if (string.IsNullOrEmpty(modulesRoot) || !Directory.Exists(modulesRoot))
            {
                DiagLog.Log(Tag, $"EagerLoad: cannot locate Modules root from '{ownPath}'");
                return;
            }

            var alreadyLoaded = new HashSet<string>(
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetName().Name ?? string.Empty)
                    .Where(n => n.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            // Skip DLLs that are obviously framework or already-known runtime
            // libs -- they're either already loaded, or loading them eagerly
            // would only add noise/risk for zero discovery benefit. The
            // primary safety mechanism is now DllReferencesMcm below (Cecil
            // peek before LoadFrom); this list is just a fast-path skip for
            // names we know we never want to load regardless.
            static bool IsBoringDll(string name) =>
                name.StartsWith("System.",     StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("Microsoft.",  StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("MonoMod",     StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("Mono.Cecil",  StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("Newtonsoft.", StringComparison.OrdinalIgnoreCase)
             || name.StartsWith("TaleWorlds.", StringComparison.OrdinalIgnoreCase)
             || string.Equals(name, "0Harmony", StringComparison.OrdinalIgnoreCase)
             || string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase)
             || string.Equals(name, "netstandard", StringComparison.OrdinalIgnoreCase)
             // BetterExceptionWindow ships a native C++ test DLL with this name.
             // Trying to LoadFrom() it raises BadImageFormatException (no managed
             // manifest). Skip it explicitly so the log stays clean.
             || string.Equals(name, "BewNativeCodeCrashTest", StringComparison.OrdinalIgnoreCase);

            // Determine which modules are enabled. Best-effort cascade:
            //   1. Command-line _MODULES_*Name1*...*_MODULES_ marker (TaleWorlds launcher)
            //   2. TaleWorlds.Module.CurrentModule.ModuleList (canonical)
            //   3. LauncherData.xml IsSelected="true" entries (authoritative — what
            //      the user actually toggled in BLSE/vanilla launcher UI)
            //   4. Heuristic: any folder whose bin DLLs already appear in the AppDomain
            //      is enabled, because BLSE loads enabled mods' DLLs at boot.
            //
            // v0.7.5 fix: previously the heuristic could OVER-detect (e.g. it
            // saw 27 module folders as "enabled" when LauncherData said 18),
            // causing us to eager-load DLLs from disabled mods. Once loaded,
            // a broken adapter DLL like ArtemsLivelyAnimations's vendored
            // MCM.UI.Adapter.MCMv5 v5.11.2.0 would poison the AppDomain and
            // cause per-tick ReflectionTypeLoadException feedback loops at
            // new-campaign-init. Inserting LauncherData.xml as step 3 fixes
            // it — that file is the user's literal click-state, no inference.
            var enabledModules = ParseEnabledModulesFromCommandLine();
            if (enabledModules.Count == 0)
            {
                enabledModules = GetEnabledModulesFromTaleWorlds();
                DiagLog.Log(Tag, $"EagerLoad: cmdline empty, TaleWorlds.Module.ModuleList reported {enabledModules.Count} module(s)");
            }
            if (enabledModules.Count == 0)
            {
                try
                {
                    enabledModules = ModReady.Foundation.IncompatibleModDetector.GetEnabledModsFromLauncherData();
                    DiagLog.Log(Tag, $"EagerLoad: LauncherData.xml IsSelected=true: {enabledModules.Count} module(s)");
                }
                catch (System.Exception ex)
                {
                    try { DiagLog.LogCaught(Tag, "EagerLoad/LauncherData", ex); } catch { }
                }
            }
            if (enabledModules.Count == 0)
            {
                enabledModules = DetectEnabledModulesByLoadedAssemblies(modulesRoot, alreadyLoaded);
                DiagLog.Log(Tag, $"EagerLoad: heuristic AppDomain-scan detected {enabledModules.Count} enabled module folder(s) (last-resort fallback)");
            }
            else
            {
                DiagLog.Log(Tag, $"EagerLoad: {enabledModules.Count} enabled module(s) detected");
            }

            // Base names (version suffix stripped) of assemblies already live in
            // the AppDomain. Mods like Diplomacy ship one DLL per game version
            // (Bannerlord.Diplomacy.1.3.4 ... 1.3.13) as dormant compat shims;
            // the launcher/SubModule.xml activates exactly one. If we LoadFrom the
            // dormant siblings too, each carries the SAME SaveableTypeDefiner with
            // the SAME TypeSaveIds, and the save system throws
            // "An item with the same key has already been added. Key:
            // TaleWorlds.SaveSystem.Definition.TypeSaveId" at campaign load,
            // silently killing save/load. Seed this set from what's already loaded
            // so we never force-load a second variant of an active module.
            var loadedBases = new HashSet<string>(
                alreadyLoaded.Select(StripVersionSuffix),
                StringComparer.OrdinalIgnoreCase);

            int loadedCount = 0;
            int skippedCount = 0;
            foreach (var modDir in Directory.GetDirectories(modulesRoot))
            {
                var modFolderName = Path.GetFileName(modDir);
                // If we successfully parsed the enabled list AND this folder
                // isn't in it, skip the whole module. If parsing failed (empty
                // set), fall back to the old behaviour and scan everything --
                // better to over-discover than under-discover when we have no
                // signal.
                if (enabledModules.Count > 0 && !enabledModules.Contains(modFolderName))
                {
                    skippedCount++;
                    continue;
                }
                var moduleBin = Path.Combine(modDir, "bin", "Win64_Shipping_Client");
                if (!Directory.Exists(moduleBin)) continue;
                foreach (var dll in Directory.GetFiles(moduleBin, "*.dll"))
                {
                    var simpleName = Path.GetFileNameWithoutExtension(dll);
                    if (alreadyLoaded.Contains(simpleName)) { skippedCount++; continue; }
                    if (IsBoringDll(simpleName))            { skippedCount++; continue; }

                    // Version-variant guard: if a sibling with the same base name
                    // (version suffix stripped) is already loaded, skip this one.
                    // Prevents duplicate SaveableTypeDefiner registration from a
                    // mod's dormant per-game-version compat DLLs (e.g. Diplomacy's
                    // Bannerlord.Diplomacy.1.3.4..1.3.12 siblings when 1.3.13 is live).
                    var baseName = StripVersionSuffix(simpleName);
                    if (baseName != simpleName && loadedBases.Contains(baseName))
                    {
                        DiagLog.Log(Tag, $"  skipped version-variant: {Path.GetFileName(dll)} (base '{baseName}' already loaded)");
                        skippedCount++;
                        continue;
                    }

                    // Cecil peek BEFORE LoadFrom. ModuleDefinition.ReadModule
                    // is pure-managed PE parsing -- it never invokes the OS
                    // loader and never triggers native-import resolution, so
                    // even DLLs that would crash LoadFrom (NAudio, SharpDX,
                    // BewNativeCodeCrashTest, etc.) are safe to inspect.
                    // If the DLL doesn't reference any MCM assembly, it has
                    // no settings types we care about -- skip the LoadFrom
                    // entirely. This replaces the previous denylist approach.
                    if (!DllReferencesMcm(dll))
                    {
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        var asm = Assembly.LoadFrom(dll);
                        var loadedName = asm.GetName().Name ?? simpleName;
                        if (alreadyLoaded.Add(loadedName))
                        {
                            loadedBases.Add(StripVersionSuffix(loadedName));
                            loadedCount++;
                            DiagLog.Log(Tag, $"  eager-loaded: {Path.GetFileName(dll)} (from {Path.GetFileName(modDir)})");
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagLog.Log(Tag, $"  eager-load failed: {Path.GetFileName(dll)} ({ex.GetType().Name}: {ex.Message})");
                    }
                }
            }
            DiagLog.Log(Tag, $"EagerLoad: loaded {loadedCount} additional assemblies (skipped {skippedCount} already-loaded/boring)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "EagerLoadModuleAssemblies", ex);
        }
    }

    /// <summary>
    /// Strip a trailing dotted numeric version tail of 2+ segments from an
    /// assembly simple name, so per-game-version sibling DLLs collapse to one
    /// base. "Bannerlord.Diplomacy.1.3.13" -> "Bannerlord.Diplomacy".
    /// A single trailing segment (e.g. "MCMv5") is NOT stripped -- only tails
    /// of two or more all-numeric segments count as a version, so ordinary
    /// names ending in one number stay intact.
    /// </summary>
    /// <summary>
    /// Boot-time self-test: scan every loaded assembly for TaleWorlds save-type
    /// definers (types deriving from SaveableTypeDefiner) and warn loudly if two
    /// or more loaded assemblies that share a version-stripped base name each
    /// carry a definer. That is the exact shape that makes the save system throw
    /// "An item with the same key has already been added. Key:
    /// TaleWorlds.SaveSystem.Definition.TypeSaveId" at campaign load and silently
    /// break save/load (e.g. Diplomacy's per-game-version sibling DLLs all loaded
    /// at once). The version-variant guard in EagerLoadModuleAssemblies should
    /// prevent this; this check exists so any future regression -- or a genuine
    /// two-mod collision the guard can't cover -- fails loudly in runtime.log
    /// instead of presenting as an unexplained save/load failure.
    /// Read-only: never instantiates a definer, only reflects over type metadata.
    /// </summary>
    public static void WarnOnDuplicateSaveDefiners()
    {
        try
        {
            // base name -> list of loaded assembly simple-names carrying a definer
            var byBase = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string simpleName;
                try { simpleName = asm.GetName().Name ?? string.Empty; } catch { continue; }
                if (simpleName.Length == 0) continue;
                if (simpleName.StartsWith("TaleWorlds.", StringComparison.OrdinalIgnoreCase)) continue;

                if (!AssemblyDefinesSaveDefiner(asm)) continue;

                var baseName = StripVersionSuffix(simpleName);
                if (!byBase.TryGetValue(baseName, out var list))
                {
                    list = new List<string>();
                    byBase[baseName] = list;
                }
                list.Add(simpleName);
            }

            foreach (var kv in byBase)
            {
                if (kv.Value.Count < 2) continue;
                DiagLog.Log(Tag,
                    $"WARN: {kv.Value.Count} loaded assemblies share base '{kv.Key}' and each defines a " +
                    $"SaveableTypeDefiner ({string.Join(", ", kv.Value)}). Duplicate TypeSaveId registration " +
                    $"will break save/load. Only one version of a mod's DLL should be loaded -- check the " +
                    $"module's bin folder for stale per-version sibling DLLs.");
            }
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "WarnOnDuplicateSaveDefiners", ex); } catch { }
        }
    }

    /// <summary>
    /// True if the assembly defines at least one type whose base-type chain
    /// includes a "SaveableTypeDefiner" (matched by simple type name to avoid a
    /// hard reference to TaleWorlds.SaveSystem). Tolerates ReflectionTypeLoadException
    /// by scanning whatever types did load.
    /// </summary>
    private static bool AssemblyDefinesSaveDefiner(Assembly asm)
    {
        Type?[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
        catch { return false; }

        foreach (var t in types)
        {
            if (t == null) continue;
            try
            {
                for (var b = t.BaseType; b != null && b != typeof(object); b = b.BaseType)
                {
                    if (string.Equals(b.Name, "SaveableTypeDefiner", StringComparison.Ordinal))
                        return true;
                }
            }
            catch { }
        }
        return false;
    }

    internal static string StripVersionSuffix(string simpleName)
    {
        if (string.IsNullOrEmpty(simpleName)) return simpleName;
        var parts = simpleName.Split('.');
        int keep = parts.Length;
        while (keep > 1 && IsAllDigits(parts[keep - 1])) keep--;
        // Require at least two numeric segments were peeled to treat it as a version.
        if (parts.Length - keep < 2) return simpleName;
        return string.Join(".", parts, 0, keep);

        static bool IsAllDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }

    /// <summary>
    /// Assembly names that mean "this DLL contains MCM settings types" if any
    /// of them appear in the DLL's AssemblyRef table. Matching is by simple
    /// name only (version-agnostic) because consumer mods are compiled
    /// against various BUTR-MCM versions but our shim handles the redirect.
    /// </summary>
    private static readonly HashSet<string> _mcmAssemblyRefNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MCMv5",                       // our DLL name
        "Bannerlord.MBOptionScreen",   // some older mods reference the BUTR module-id name
        "MCM",                          // future-proofing for major-version bumps
        "MCM.UI",
        "MCM.Abstractions",            // v5 NuGet package assembly, referenced by many modern mods
        "Bannerlord.MCM",              // alternate module-id casing some mods use
        "Bannerlord.ButterLib",        // ButterLib-using mods often also use MCM
    };

    /// <summary>
    /// Cecil-based safe peek into a DLL's AssemblyRef table. Returns true if
    /// the DLL references any of our MCM-family assemblies (and therefore is
    /// worth loading via LoadFrom for settings discovery). Returns false if
    /// the DLL is content/game/audio/whatever or is malformed.
    ///
    /// Crucially, this method NEVER invokes the OS assembly loader. It reads
    /// the PE file with managed code only, so DLLs that would crash LoadFrom
    /// (native-interop wrappers like NAudio.Wasapi, SharpDX.D3DCompiler, the
    /// BewNativeCodeCrashTest C++ DLL) are completely safe to inspect.
    /// </summary>
    private static bool DllReferencesMcm(string dllPath)
    {
        try
        {
            using var module = ModuleDefinition.ReadModule(dllPath, new ReaderParameters
            {
                ReadingMode = ReadingMode.Deferred,
                InMemory = true,
            });
            foreach (var asmRef in module.AssemblyReferences)
            {
                if (_mcmAssemblyRefNames.Contains(asmRef.Name)) return true;
            }
            return false;
        }
        catch
        {
            // Unmanaged PE, encrypted, or malformed -- definitely not an
            // MCM consumer. Skip safely.
            return false;
        }
    }

    /// <summary>
    /// Discover within a single assembly. Returns the count of newly
    /// registered settings classes. Logs every candidate considered + every
    /// rejection reason so the user can debug missing mods from runtime.log.
    /// </summary>
    public static int DiscoverInAssembly(Assembly asm)
    {
        if (asm == null) return 0;

        // Only log per-assembly attempts for non-system assemblies (skip
        // mscorlib / System.* noise) and only the first time we touch them.
        var asmName = asm.GetName().Name ?? "?";
        bool isInteresting = !asmName.StartsWith("System", StringComparison.Ordinal)
                           && !asmName.StartsWith("Microsoft", StringComparison.Ordinal)
                           && !asmName.StartsWith("mscorlib", StringComparison.Ordinal)
                           && !asmName.StartsWith("netstandard", StringComparison.Ordinal)
                           && !asmName.StartsWith("Newtonsoft", StringComparison.Ordinal)
                           && !asmName.StartsWith("TaleWorlds", StringComparison.Ordinal)
                           && !asmName.StartsWith("MonoMod", StringComparison.Ordinal)
                           && !asmName.StartsWith("Mono.Cecil", StringComparison.Ordinal);

        int added = 0;
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray()!;
            if (isInteresting)
            {
                DiagLog.Log(Tag, $"DiscoverInAssembly({asmName}): partial type load -- skipped {ex.Types.Length - types.Length} types");
                // v0.5.4: dump LoaderExceptions so we know WHY types failed.
                // ROT in particular ships 2 types that fail to initialize on
                // current Bannerlord; this log line tells us what's missing.
                if (ex.LoaderExceptions != null)
                {
                    var seen = new System.Collections.Generic.HashSet<string>();
                    foreach (var le in ex.LoaderExceptions)
                    {
                        if (le == null) continue;
                        var key = le.GetType().Name + ": " + le.Message;
                        if (seen.Add(key))
                            DiagLog.Log(Tag, $"  -> LoaderException: {key}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (isInteresting) DiagLog.LogCaught(Tag, $"DiscoverInAssembly({asmName})/GetTypes", ex);
            return 0;
        }

        foreach (var t in types)
        {
            if (t == null) continue;
            if (!IsAttributeGlobalSettingsSubclass(t)) continue;

            // Per-candidate diagnostic logging was useful while we were figuring
            // out which mods MCM should pick up; now we only log success
            // (REGISTERED) and TRUE error cases (exceptions from Instance get).
            // "Already registered" duplicates and "no Instance" classes are
            // expected on re-runs of DiscoverAll and don't need a line each.

            try
            {
                var instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (instanceProp == null) continue;  // not a singleton-style settings class

                object? rawInstance;
                try { rawInstance = instanceProp.GetValue(null); }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"Instance get for {t.FullName}", ex);
                    continue;
                }
                if (rawInstance == null) continue;

                // The instance won't always be our BaseSettings -- it could
                // be a different MCM's BaseSettings from a separately-loaded
                // assembly. Use reflection to pull Id and DisplayName so we
                // can register it regardless of which assembly it came from.
                var id = rawInstance.GetType().GetProperty("Id")?.GetValue(rawInstance) as string ?? t.FullName;
                var displayName = rawInstance.GetType().GetProperty("DisplayName")?.GetValue(rawInstance) as string ?? id;

                // Wrap in a BaseSettings adapter so the registry can hold it.
                var settings = rawInstance as BaseSettings ?? new ForeignSettingsAdapter(rawInstance, id!, displayName!);

                lock (_gate)
                {
                    if (_byId.ContainsKey(id!)) continue;  // duplicate -- common on re-runs of DiscoverAll
                    _byId[id!] = new RegisteredSettings(settings);
                }
                DiagLog.Log(Tag, $"REGISTERED '{id}' ({t.FullName})");
                SettingsEvents.RaiseLoadingComplete(id!);
                added++;
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"DiscoverInAssembly/{t.FullName}", ex);
            }
        }
        return added;
    }

    /// <summary>
    /// Adapter that wraps a foreign settings instance (a class from another
    /// MCM assembly that doesn't inherit from OUR BaseSettings) so it can
    /// live in the SettingsRegistry. The wrapped instance is preserved via
    /// the <see cref="Wrapped"/> property for UI consumers that want to
    /// reflect over its real type.
    /// </summary>
    internal sealed class ForeignSettingsAdapter : BaseSettings
    {
        public object Wrapped { get; }
        public override string Id { get; }
        public override string DisplayName { get; }

        public ForeignSettingsAdapter(object wrapped, string id, string displayName)
        {
            Wrapped = wrapped;
            Id = id;
            DisplayName = displayName;
        }
    }

    // Recognized base-class FullNames across MCM API revisions. The BUTR
    // community renamed the namespace multiple times across MCM v3 -> v4 -> v5,
    // and consumer mods exist at every layer. Match any of them by string so
    // we discover settings classes regardless of which MCM iteration they
    // were compiled against.
    private static readonly string[] _settingsBaseFullNames = new[]
    {
        "MCM.Abstractions.Base.Global.AttributeGlobalSettings`1",        // v5 canonical
        "MCM.Abstractions.Settings.Base.Global.AttributeGlobalSettings`1", // v4 / transitional
        "MCM.Abstractions.Settings.Base.AttributeGlobalSettings`1",      // older
        "MCM.Common.AttributeGlobalSettings`1",                          // older still
        "MCM.Abstractions.Base.PerCampaign.AttributePerCampaignSettings`1",
        "MCM.Abstractions.Base.PerSave.AttributePerSaveSettings`1",
    };

    // Non-generic base type FullNames -- consumer mods sometimes derive from
    // these directly (e.g. fluent-built settings or programmatic registrations).
    private static readonly string[] _settingsNonGenericBaseFullNames = new[]
    {
        "MCM.Abstractions.Base.Global.BaseGlobalSettings",
        "MCM.Abstractions.Settings.Base.Global.BaseGlobalSettings",
        "MCM.Abstractions.Base.PerCampaign.BasePerCampaignSettings",
        "MCM.Abstractions.Base.PerSave.BasePerSaveSettings",
        "MCM.Abstractions.BaseSettings",
        "MCM.Abstractions.Settings.Base.BaseSettings",
    };

    private static bool IsAttributeGlobalSettingsSubclass(Type t)
    {
        if (t.IsAbstract || t.IsGenericTypeDefinition) return false;
        for (var b = t.BaseType; b != null && b != typeof(object); b = b.BaseType)
        {
            // Generic AttributeGlobalSettings<T> family
            if (b.IsGenericType)
            {
                var gtdName = b.GetGenericTypeDefinition().FullName;
                foreach (var candidate in _settingsBaseFullNames)
                {
                    if (string.Equals(gtdName, candidate, StringComparison.Ordinal)) return true;
                }
            }
            // Non-generic base classes (BaseGlobalSettings etc.)
            var bName = b.FullName;
            foreach (var candidate in _settingsNonGenericBaseFullNames)
            {
                    if (string.Equals(bName, candidate, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }
}
