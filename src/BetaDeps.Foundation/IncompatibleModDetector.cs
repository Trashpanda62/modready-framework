// BetaDeps.Foundation -- IncompatibleModDetector
//
// v0.6 Auto-Disable Crashing Mods detection layer.
//
// The Bannerlord launcher itself shows a "could not be loaded correctly
// due to a dependency conflict" dialog when a mod's DLL can't construct
// its SubModule class -- typically because the mod was compiled against
// an older TaleWorlds API and now references types or methods that no
// longer exist (e.g. Banner Kings targets game v1.2.11, won't work on
// v1.4.5). The user clicks OK on the dialog and the game continues
// loading without that mod -- but the broken mod stays "enabled" in
// LauncherData.xml, so the user gets the same dialog next launch and
// the next launch and the next.
//
// This detector closes that loop: it reads LauncherData.xml to find
// what the user TRIED to enable, reflects on Module.CurrentModule.SubModules
// to see what actually loaded, and diffs the two. Anything enabled but
// not loaded is flagged as a candidate for auto-disable.
//
// v0.6 MVP scope:
//   - Detect and report only. Don't write LauncherData.xml yet.
//   - Findings go to runtime.log and to a per-session report file the
//     in-game UI can read (selftest-style).
//   - Filters out vanilla TaleWorlds modules (Native, SandBox, etc.) so
//     the "didn't load" list contains only third-party mods we'd want to
//     surface to the user.
//
// Future (v0.7+):
//   - Wire to InformationManager.ShowInquiry popup at main menu
//   - One-click "Disable for next launch" that edits LauncherData.xml
//   - Crash-dump correlation (which mod was on the stack when last crash hit)
//
// Original work. MIT, copyright 2026 Trashpanda62.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace BetaDeps.Foundation;

/// <summary>One row of the detector's output.</summary>
public sealed class IncompatibleModFinding
{
    public string ModId = "";
    public string Reason = "";
    /// <summary>Path to the mod's Modules\&lt;ModId&gt;\ folder, if it exists.</summary>
    public string ModuleFolder = "";
}

public static class IncompatibleModDetector
{
    private const string Tag = "IncompatibleModDetector";

    // TaleWorlds-owned modules. We never flag these; they're game core.
    private static readonly HashSet<string> _vanillaModuleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Native",
        "SandBox",
        "SandBoxCore",
        "StoryMode",
        "CustomBattle",
        "Multiplayer",
        "NavalDLC",
        "BirthAndDeath",  // TaleWorlds dynasty DLC
    };

    // BetaDeps's own modules + its alias stubs. Filter these out too.
    private static readonly HashSet<string> _betaDepsModuleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "BetaDeps",
        "Bannerlord.Harmony",
        "Bannerlord.UIExtenderEx",
        "Bannerlord.ButterLib",
        "Bannerlord.MBOptionScreen",
    };

    // Known-incompatible mods on Bannerlord >= e1.4.x. These were compiled
    // against significantly older TaleWorlds APIs and destabilize the game
    // process so badly the engine often CTDs before BetaDeps can detect at
    // main-menu time. We disable them preemptively in LauncherData.xml.
    //
    // Format: ModId -> short reason. When the user re-enables in BLSE,
    // BetaDeps respects that on the next launch (the bad-list only acts
    // when the user has flipped the mod to enabled).
    // Hardcoded bad-list removed -- replaced by runtime detection below.
    // Rationale: a hardcoded list goes stale the moment TaleWorlds ships
    // a game update or a mod author publishes a fix. Runtime detection
    // checks every launch and adapts automatically: if a mod loaded
    // cleanly last session it stays enabled; if it crashed the game
    // before main-menu was reached, it gets auto-disabled on the next
    // launch with a clear reason.
    private static readonly Dictionary<string, string> _knownIncompatibleOnBetaBranch
        = new(StringComparer.OrdinalIgnoreCase);

    // File names under Modules\BetaDeps\ used by the runtime-detection scheme.
    private const string LastGoodFile         = "last-good-modlist.txt";   // mods that reached main-menu last clean boot
    private const string LaunchMarkerFile     = "session-launching.marker"; // present = previous launch never finished
    private const string AutoDisabledLogFile  = "betadeps-auto-disabled.log"; // append-only audit of what BetaDeps disabled and when

    // Removed: cascade-family table.
    //
    // BLSE LauncherEx already cascades satellite mods (ROT-Content,
    // ROT-Dragon, ROT-Map) automatically when their parent (ROT-Core) is
    // disabled. Duplicating that logic here would have been dead code.
    // When future entries are added to _knownIncompatibleOnBetaBranch, only
    // the parent ID needs to appear -- BLSE handles the satellites.

    /// <summary>
    /// Phase 1: very early. Reads LauncherData.xml, identifies known-
    /// incompatible mods, and writes LauncherData.xml back with those
    /// mods' IsSelected flipped to false. Touches ONLY the user's own
    /// config file under Documents -- never modifies other mods' install
    /// folders. Effect takes hold on the next launch (the launcher
    /// re-reads LauncherData.xml at startup).
    ///
    /// Idempotent. If a mod is already disabled in LauncherData.xml we
    /// skip it. User can re-enable in BLSE LauncherEx whenever they want
    /// to retry the mod -- the standard reversal path, no orphan files
    /// or hidden state in any mod folder.
    /// </summary>
    public static void RunEarlyPhase()
    {
        // STEP 0 (FIRST): write the launch marker BEFORE any other work.
        // Earlier versions wrote this at the end of the method, which meant
        // a crash anywhere upstream (in this method or in another mod that
        // construct-throws shortly after BetaDeps loads) would leave us with
        // no marker on disk -- and so the next launch couldn't detect that
        // the previous one crashed. By writing the marker first, even if
        // everything else fails the recovery loop still works on the next
        // launch.
        bool previousCrashedBeforeMarkerWrite = false;
        try
        {
            var modulesRoot = ResolveModulesRoot();
            if (!string.IsNullOrEmpty(modulesRoot))
            {
                var betaDepsDir = Path.Combine(modulesRoot!, "BetaDeps");
                var markerPath  = Path.Combine(betaDepsDir, LaunchMarkerFile);
                // Snapshot whether marker already existed BEFORE we overwrite,
                // because that's the signal for "previous session crashed".
                previousCrashedBeforeMarkerWrite = File.Exists(markerPath);
                try
                {
                    Directory.CreateDirectory(betaDepsDir);
                    File.WriteAllText(markerPath,
                        $"launch started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    try { DiagLog.LogCaught(Tag, "  write launch marker", ex); } catch { }
                }
            }
        }
        catch { /* never throw out of the marker write */ }

        try
        {
            DiagLog.Log(Tag, "RunEarlyPhase: scanning LauncherData.xml for known-incompatible mods");

            var launcherDataPath = GetLauncherDataPath();
            if (string.IsNullOrEmpty(launcherDataPath) || !File.Exists(launcherDataPath))
            {
                DiagLog.Log(Tag, $"  LauncherData.xml not found at: {launcherDataPath}");
                return;
            }

            // VersionProbe.Branch may report Unknown during the SubModule
            // construction phase (TaleWorlds.Library not loaded yet). The
            // known-bad list documents specific game-version mismatches per
            // mod rather than gating on branch, so we proceed regardless of
            // what the probe says here. Logged for diagnostic clarity.
            DiagLog.Log(Tag, $"  game branch reported as: {VersionProbe.Branch} (proceeding regardless)");

            // Load LauncherData.xml as a document so we can mutate the
            // specific IsSelected nodes without rewriting the whole shape.
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(launcherDataPath);

            int disabledCount = 0;
            var nowDisabled = new List<string>();

            // Static known-bad list (currently empty). Kept as a structural
            // hook in case a future emergency requires immediate disable of
            // a specific mod ahead of the runtime scan.
            foreach (var kvp in _knownIncompatibleOnBetaBranch)
            {
                if (TryDisableInXml(doc, kvp.Key, kvp.Value, nowDisabled))
                    disabledCount++;
            }

            // Runtime detection: did the previous session crash? If so,
            // diff currently-enabled mods against last-good-modlist.txt
            // to find suspects, and disable them. Use the snapshot we
            // captured at STEP 0 (above) -- File.Exists would now return
            // true because we just (re-)wrote the marker for THIS session.
            try
            {
                var modulesRoot = ResolveModulesRoot();
                if (!string.IsNullOrEmpty(modulesRoot))
                {
                    var betaDepsDir = Path.Combine(modulesRoot!, "BetaDeps");
                    var lastGoodPath = Path.Combine(betaDepsDir, LastGoodFile);

                    bool previousCrashed = previousCrashedBeforeMarkerWrite;

                    if (previousCrashed)
                    {
                        DiagLog.Log(Tag, "RuntimeScan: previous session never reached main menu (launch marker present)");

                        // Read last-good modlist (mods that were known to load cleanly).
                        var lastGood = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (File.Exists(lastGoodPath))
                        {
                            foreach (var line in File.ReadAllLines(lastGoodPath))
                            {
                                var name = line?.Trim();
                                if (!string.IsNullOrEmpty(name) && !name!.StartsWith("#"))
                                    lastGood.Add(name);
                            }
                            DiagLog.Log(Tag, $"  last-good baseline: {lastGood.Count} mod(s) known to load cleanly");
                        }
                        else
                        {
                            DiagLog.Log(Tag, "  no last-good baseline yet (first crash or fresh install); skipping suspect identification");
                        }

                        if (lastGood.Count > 0)
                        {
                            var enabled = ReadEnabledMods();
                            var suspects = enabled.Where(m =>
                                !lastGood.Contains(m) &&
                                !_vanillaModuleIds.Contains(m) &&
                                !_betaDepsModuleIds.Contains(m)).ToList();
                            DiagLog.Log(Tag, $"  suspects (enabled but not in last-good): {suspects.Count}");

                            foreach (var suspect in suspects)
                            {
                                // Only disable suspects that PLAUSIBLY caused
                                // the crash. Content-only mods (no DLL) can't
                                // CTD the engine -- they only provide XML and
                                // assets. Stale launcher entries (folder gone)
                                // can't have caused anything because they're
                                // not installed. Skip both categories so we
                                // don't over-aggressively disable mods that
                                // were never the problem.
                                var suspectFolder = string.IsNullOrEmpty(modulesRoot)
                                    ? null
                                    : Path.Combine(modulesRoot!, suspect);

                                if (suspectFolder != null && !Directory.Exists(suspectFolder))
                                {
                                    DiagLog.Log(Tag, $"  [SKIP-SUSPECT] {suspect}: stale launcher entry (folder missing on disk)");
                                    continue;
                                }

                                if (suspectFolder != null && !FolderHasDll(suspectFolder))
                                {
                                    DiagLog.Log(Tag, $"  [SKIP-SUSPECT] {suspect}: content-only mod (no DLLs to crash with)");
                                    continue;
                                }

                                if (TryDisableInXml(doc, suspect,
                                    "auto-detected: previous launch crashed and this mod was not in the last clean modlist",
                                    nowDisabled))
                                    disabledCount++;
                            }
                        }
                    }

                    // (Marker write moved to STEP 0 at the top of this
                    // method so it lands on disk before anything else can
                    // crash the game.)
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "RuntimeScan", ex);
            }

            if (disabledCount == 0)
            {
                DiagLog.Log(Tag, "  no known-incompatible mods are enabled. LauncherData.xml unchanged.");
                return;
            }

            // Backup the original once so the user can roll back manually
            // if they don't trust BetaDeps's judgement on a given mod.
            try
            {
                var backupPath = launcherDataPath + ".betadeps-backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(launcherDataPath, backupPath, overwrite: false);
                    DiagLog.Log(Tag, $"  wrote backup of original LauncherData.xml -> {backupPath}");
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "  backup LauncherData.xml", ex);
            }

            try
            {
                doc.Save(launcherDataPath);
                DiagLog.Log(Tag, $"RunEarlyPhase: LauncherData.xml updated. {disabledCount} mod(s) disabled: {string.Join(", ", nowDisabled)}");
                DiagLog.Log(Tag, "  effect takes hold on the NEXT game launch; this session may still crash on the already-loaded modlist");

                // Critical step: also remove the disabled mods from
                // Bannerlord's in-memory ModuleList. Without this, the
                // engine still has the mod queued for construction THIS
                // session -- it will hard-crash on mods like ROT that
                // throw during their SubModule ctor. Removing from
                // ModuleList stops the construction call from ever firing.
                // Also prevents Bannerlord from later writing LauncherData.xml
                // back over our changes (its write reflects in-memory state).
                foreach (var modId in nowDisabled)
                {
                    TryRemoveFromInMemoryModuleList(modId);
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "  save LauncherData.xml", ex);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "RunEarlyPhase", ex);
        }
    }

    /// <summary>
    /// Called from BetaDeps.MCM.OnBeforeInitialModuleScreenSetAsRoot once
    /// the main menu is about to display -- the universal "we reached
    /// interactive state" milestone. Writes the current loaded-mod set
    /// to last-good-modlist.txt and deletes the launch marker. Next
    /// session's runtime scan uses this as the baseline.
    /// </summary>
    public static void MarkBootSuccessful()
    {
        try
        {
            var modulesRoot = ResolveModulesRoot();
            if (string.IsNullOrEmpty(modulesRoot)) return;
            var betaDepsDir = Path.Combine(modulesRoot!, "BetaDeps");
            Directory.CreateDirectory(betaDepsDir);

            var loaded = ReadLoadedSubModuleIds();
            DiagLog.Log(Tag, $"MarkBootSuccessful: writing {loaded.Count} loaded mod IDs to {LastGoodFile}");

            var path = Path.Combine(betaDepsDir, LastGoodFile);
            using (var sw = new StreamWriter(path, append: false))
            {
                sw.WriteLine($"# BetaDeps last-good modlist  recorded {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sw.WriteLine("# Mods listed here loaded successfully to the main menu in this session.");
                sw.WriteLine("# Next session's runtime detection treats anything NOT in this list as a suspect after a crash.");
                foreach (var id in loaded.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    sw.WriteLine(id);
            }

            // Delete the launch marker -- we reached interactive state, no crash.
            var marker = Path.Combine(betaDepsDir, LaunchMarkerFile);
            try { if (File.Exists(marker)) File.Delete(marker); } catch { }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "MarkBootSuccessful", ex);
        }
    }

    private static string GetLauncherDataPath()
    {
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) return string.Empty;
            return Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "LauncherData.xml");
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Look up the UserModData/Id node for a given mod and return true if
    /// its IsSelected is currently "true". Returns false for absent mods,
    /// malformed entries, or explicitly-disabled mods.
    /// </summary>
    private static bool IsModEnabled(XmlDocument doc, string modId)
    {
        try
        {
            var idNode = doc.SelectSingleNode($"//UserModData[Id='{modId}']");
            var selNode = idNode?.SelectSingleNode("IsSelected");
            return string.Equals(selNode?.InnerText?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Flip a mod's IsSelected node to "false" in the in-memory XmlDocument.
    /// Returns true if a change was made (mod was previously enabled);
    /// returns false for absent mods or already-disabled mods. The caller
    /// appends successful disables to <paramref name="nowDisabled"/> and
    /// is responsible for saving the document.
    /// </summary>
    private static bool TryDisableInXml(XmlDocument doc, string modId, string reason, List<string> nowDisabled)
    {
        var idNode = doc.SelectSingleNode($"//UserModData[Id='{modId}']");
        if (idNode == null)
        {
            DiagLog.Log(Tag, $"  [SKIP] {modId}: no UserModData entry in LauncherData.xml (mod not installed?)");
            return false;
        }
        var selNode = idNode.SelectSingleNode("IsSelected");
        if (selNode == null)
        {
            DiagLog.Log(Tag, $"  [SKIP] {modId}: UserModData entry has no IsSelected child");
            return false;
        }
        if (!string.Equals(selNode.InnerText?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
        {
            DiagLog.Log(Tag, $"  [OK]   {modId}: already disabled in LauncherData.xml");
            return false;
        }
        selNode.InnerText = "false";
        nowDisabled.Add(modId);
        DiagLog.Log(Tag, $"  [DISABLE] {modId}: {reason}");
        WriteDisableMarker(modId, reason);
        return true;
    }

    /// <summary>
    /// Append to Modules\BetaDeps\betadeps-disabled-mods.log so the in-game
    /// notification at main menu (or the user reading manually) can see
    /// what BetaDeps disabled and why.
    /// </summary>
    private static void WriteDisableMarker(string modId, string reason)
    {
        try
        {
            var runtimeLogPath = RuntimeLog.Path;
            var dir = Path.GetDirectoryName(runtimeLogPath);
            if (string.IsNullOrEmpty(dir)) return;
            var markerPath = Path.Combine(dir, "betadeps-disabled-mods.log");
            File.AppendAllText(markerPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {modId}\t{reason}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>
    /// Scan enabled-vs-loaded modules. Writes findings to runtime.log and
    /// returns the list of incompatible-but-enabled third-party mods.
    /// </summary>
    public static List<IncompatibleModFinding> ScanAndReport()
    {
        var findings = new List<IncompatibleModFinding>();
        try
        {
            DiagLog.Log(Tag, "ScanAndReport: starting incompatible-mod detection");

            var enabled = ReadEnabledMods();
            DiagLog.Log(Tag, $"  LauncherData.xml: {enabled.Count} enabled mod(s)");

            var loaded = ReadLoadedSubModuleIds();
            DiagLog.Log(Tag, $"  Module.CurrentModule.SubModules: {loaded.Count} loaded submodule(s)");

            var modulesRoot = ResolveModulesRoot();
            if (string.IsNullOrEmpty(modulesRoot))
            {
                DiagLog.Log(Tag, "  could not resolve Modules\\ root; falling back to relative paths in findings");
            }

            foreach (var modId in enabled)
            {
                if (_vanillaModuleIds.Contains(modId)) continue;
                if (_betaDepsModuleIds.Contains(modId)) continue;
                if (loaded.Contains(modId)) continue;

                // This mod is in LauncherData.xml as enabled, but did not
                // appear in the engine's loaded SubModules list. Either it
                // failed to construct, was filtered out by Bannerlord's
                // dependency resolver, or its DLL didn't JIT clean.
                var folder = string.IsNullOrEmpty(modulesRoot)
                    ? modId
                    : Path.Combine(modulesRoot, modId);

                // If the folder doesn't exist on disk, the launcher entry is
                // stale (mod was deleted) -- don't surface as "incompatible".
                if (!string.IsNullOrEmpty(modulesRoot) && !Directory.Exists(folder))
                {
                    DiagLog.Log(Tag, $"  [SKIP] {modId} -- enabled but folder missing on disk (stale launcher entry)");
                    continue;
                }

                var reason = DiagnoseReason(folder);

                findings.Add(new IncompatibleModFinding
                {
                    ModId = modId,
                    Reason = reason,
                    ModuleFolder = folder,
                });

                DiagLog.Log(Tag, $"  [INCOMPAT] {modId} -- {reason}");
            }

            if (findings.Count == 0)
            {
                DiagLog.Log(Tag, "ScanAndReport: no incompatible mods detected. Modlist is clean.");
            }
            else
            {
                DiagLog.Log(Tag, $"ScanAndReport: {findings.Count} incompatible mod(s) detected. See list above.");
                WriteIncompatibleReport(findings);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ScanAndReport", ex);
        }
        return findings;
    }

    /// <summary>
    /// Best-effort reason for why a mod failed to load. Cheap probes only --
    /// we already know the engine rejected it, so this is just to give the
    /// user useful context in the report.
    /// </summary>
    private static string DiagnoseReason(string modFolder)
    {
        try
        {
            var subModuleXml = Path.Combine(modFolder, "SubModule.xml");
            if (!File.Exists(subModuleXml))
                return "no SubModule.xml found in module folder";

            // Parse minimal facts from the XML: declared game version (if any),
            // declared dependencies (so we can mention them in the message).
            var doc = new XmlDocument();
            doc.Load(subModuleXml);

            var deps = new List<string>();
            var depNodes = doc.SelectNodes("//DependedModule");
            if (depNodes != null)
            {
                foreach (XmlNode n in depNodes)
                {
                    var idAttr = n.Attributes?["Id"]?.Value;
                    if (!string.IsNullOrEmpty(idAttr)) deps.Add(idAttr);
                }
            }

            // If the mod declares dependencies and most of them are vanilla,
            // the likely cause is API-surface drift (DLL compiled against an
            // older Bannerlord build). That's the Banner Kings case.
            var thirdPartyDeps = deps.Where(d => !_vanillaModuleIds.Contains(d)
                                              && !_betaDepsModuleIds.Contains(d)).ToList();
            if (thirdPartyDeps.Count == 0 && deps.Count > 0)
                return "submodule failed to construct (likely DLL compiled against an older Bannerlord version)";

            if (thirdPartyDeps.Count > 0)
                return $"submodule failed to construct; declared third-party deps: {string.Join(", ", thirdPartyDeps)}";

            return "submodule failed to construct (no specific cause detected)";
        }
        catch (Exception ex)
        {
            return $"diagnosis threw {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Public accessor for the enabled-mod set as written to LauncherData.xml.
    /// Used by SettingsRegistry to filter out orphan settings whose owning
    /// module the user has disabled. Returns an empty set if the file is
    /// missing or unparseable (caller should fail-open).
    /// </summary>
    public static HashSet<string> GetEnabledModsFromLauncherData() => ReadEnabledMods();

    /// <summary>
    /// Returns the set of mod Ids the user has selected in BLSE / vanilla
    /// launcher. Reads LauncherData.xml under
    /// %USERPROFILE%\Documents\Mount and Blade II Bannerlord\Configs.
    /// </summary>
    private static HashSet<string> ReadEnabledMods()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) return result;

            var path = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "LauncherData.xml");
            if (!File.Exists(path))
            {
                DiagLog.Log(Tag, $"  LauncherData.xml not found at: {path}");
                return result;
            }

            var doc = new XmlDocument();
            doc.Load(path);

            // BLSE LauncherEx wraps each mod's enabled state in a UserModData
            // node with an Id child and an IsSelected child. Vanilla launcher
            // uses the same shape. Be liberal: select any UserModData node
            // whose IsSelected is "true".
            var modNodes = doc.SelectNodes("//UserModData[IsSelected='true']");
            if (modNodes != null)
            {
                foreach (XmlNode n in modNodes)
                {
                    var idNode = n.SelectSingleNode("Id");
                    if (idNode != null && !string.IsNullOrWhiteSpace(idNode.InnerText))
                        result.Add(idNode.InnerText.Trim());
                }
            }

            // Fallback: some LauncherData.xml shapes use SingleplayerData/ModDatas.
            if (result.Count == 0)
            {
                var fallback = doc.SelectNodes("//ModDatas/UserModData");
                if (fallback != null)
                {
                    foreach (XmlNode n in fallback)
                    {
                        var idNode = n.SelectSingleNode("Id");
                        var sel = n.SelectSingleNode("IsSelected");
                        if (idNode != null && sel != null
                            && string.Equals(sel.InnerText?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(idNode.InnerText))
                        {
                            result.Add(idNode.InnerText.Trim());
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ReadEnabledMods", ex);
        }
        return result;
    }

    /// <summary>
    /// Returns the set of mod Ids that are CURRENTLY ACTIVE: both enabled
    /// in LauncherData.xml AND have at least one DLL loaded in the
    /// AppDomain. Pure AppDomain-walks have a false-positive problem
    /// because BetaDeps' SettingsRegistry eagerly LoadFrom's DLLs from
    /// disabled module folders for scanning -- those DLLs end up in the
    /// AppDomain even though the user has the mod unchecked in BLSE.
    /// Intersecting with LauncherData.xml filters those out: a mod only
    /// counts as "loaded" if the user wanted it on AND something put its
    /// DLL into memory.
    /// </summary>
    private static HashSet<string> ReadLoadedSubModuleIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var enabled = ReadEnabledMods();
            if (enabled.Count == 0)
            {
                // No LauncherData.xml or unparseable -- can't filter. Fall back
                // to plain AppDomain walk (better than empty baseline) but log
                // the situation.
                DiagLog.Log(Tag, "  ReadLoadedSubModuleIds: LauncherData.xml empty/unreadable; cannot intersect, baseline may include orphans");
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                string? loc = null;
                try { loc = asm.Location; } catch { /* dynamic asm */ }
                if (string.IsNullOrEmpty(loc)) continue;
                var modId = ResolveModuleIdFromAssemblyPath(loc!);
                if (string.IsNullOrEmpty(modId)) continue;
                if (_betaDepsModuleIds.Contains(modId!)) continue;
                if (_vanillaModuleIds.Contains(modId!)) continue;

                // The critical intersect: only count this module as "loaded"
                // if the user actually had it enabled in the launcher. If a
                // DLL got eagerly loaded by BetaDeps's settings scanner from
                // a disabled mod's folder, we still skip it here -- the user
                // never wanted that mod running.
                if (enabled.Count > 0 && !enabled.Contains(modId!)) continue;

                result.Add(modId!);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ReadLoadedSubModuleIds", ex);
        }
        return result;
    }

    /// <summary>
    /// Given an absolute path to a mod's DLL, find which Modules\&lt;Id&gt;
    /// folder it lives under. Returns the folder name as the mod Id.
    /// </summary>
    private static string? ResolveModuleIdFromAssemblyPath(string asmPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(asmPath);
            // Typical path: Modules\<Id>\bin\Win64_Shipping_Client\<dll>
            // Walk up until parent's name is "Modules".
            while (!string.IsNullOrEmpty(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parent)
                    && string.Equals(Path.GetFileName(parent), "Modules", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(dir);
                }
                dir = parent;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Walks Bannerlord's in-memory module list (Module.CurrentModule.ModuleList)
    /// and removes the entry whose Id / Alias / FolderName matches the given mod.
    /// Best-effort: if reflection fails or the list shape isn't what we expect,
    /// we log and move on -- the LauncherData.xml change still takes effect on
    /// the next launch, just not this one. Critical for hard-crashing mods
    /// (ROT-Core on current Bannerlord): without this step the engine still
    /// proceeds to construct the mod in this session and dies before main menu.
    /// </summary>
    private static void TryRemoveFromInMemoryModuleList(string modId)
    {
        try
        {
            var moduleType = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.MountAndBlade.Module");
            if (moduleType == null)
            {
                DiagLog.Log(Tag, $"  in-memory remove({modId}): TaleWorlds.MountAndBlade.Module not loaded yet");
                return;
            }
            var currentProp = moduleType.GetProperty("CurrentModule",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var current = currentProp?.GetValue(null);
            if (current == null)
            {
                DiagLog.Log(Tag, $"  in-memory remove({modId}): Module.CurrentModule is null");
                return;
            }

            // ModuleList is the canonical "enabled mods, parsed" container.
            // Try the most likely names in order.
            IList? list = null;
            string foundName = "";
            foreach (var name in new[] { "ModuleList", "Modules", "LoadedModules" })
            {
                var p = current.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null) continue;
                list = p.GetValue(current) as IList;
                if (list != null) { foundName = name; break; }
            }
            if (list == null)
            {
                DiagLog.Log(Tag, $"  in-memory remove({modId}): no ModuleList/Modules/LoadedModules property found");
                return;
            }

            // Walk backwards because we're mutating the list. Each element
            // is typically ModuleInfo with an Id property; some game versions
            // also expose Alias/FolderName/Name.
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var item = list[i];
                if (item == null) continue;
                string? id = null;
                foreach (var propName in new[] { "Id", "Alias", "FolderName", "Name" })
                {
                    try
                    {
                        var p = item.GetType().GetProperty(propName);
                        id = p?.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(id)) break;
                    }
                    catch { }
                }
                if (string.Equals(id, modId, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    removed++;
                    DiagLog.Log(Tag, $"  removed {modId} from Module.CurrentModule.{foundName} (in-memory) -- engine will skip its SubModule construction");
                }
            }
            if (removed == 0)
                DiagLog.Log(Tag, $"  in-memory remove({modId}): no matching entry in {foundName} (may already be removed or under a different Id)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryRemoveFromInMemoryModuleList({modId})", ex);
        }
    }

    /// <summary>
    /// Returns true if the given module folder contains at least one DLL
    /// under its bin\Win64_Shipping_Client\ tree. Content-only mods (pure
    /// XML / asset packs) return false and are skipped from suspect
    /// auto-disable -- they can't crash the engine because they don't ship
    /// any code that the JIT could fail to compile.
    /// </summary>
    private static bool FolderHasDll(string moduleFolder)
    {
        try
        {
            var bin = Path.Combine(moduleFolder, "bin", "Win64_Shipping_Client");
            if (!Directory.Exists(bin)) return false;
            foreach (var f in Directory.EnumerateFiles(bin, "*.dll", SearchOption.TopDirectoryOnly))
                return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Resolve the absolute path of the Modules\ folder for this Bannerlord
    /// install. Reuses the assembly-location trick RuntimeLog uses.
    /// </summary>
    private static string? ResolveModulesRoot()
    {
        try
        {
            var asmPath = Assembly.GetExecutingAssembly().Location;
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

    private static void WriteIncompatibleReport(List<IncompatibleModFinding> findings)
    {
        try
        {
            var runtimeLogPath = RuntimeLog.Path;
            var dir = Path.GetDirectoryName(runtimeLogPath);
            if (string.IsNullOrEmpty(dir)) return;

            var reportPath = Path.Combine(dir, "incompatible-mods.log");
            using var sw = new StreamWriter(reportPath, append: false);
            sw.WriteLine($"==== BetaDeps incompatible-mod report  {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
            sw.WriteLine($"BetaDeps detected {findings.Count} mod(s) that are enabled in your launcher");
            sw.WriteLine("but did not finish loading. Disable them in BLSE LauncherEx to stop");
            sw.WriteLine("seeing the 'dependency conflict' dialog on each launch.");
            sw.WriteLine();
            for (int i = 0; i < findings.Count; i++)
            {
                var f = findings[i];
                sw.WriteLine($"{i + 1}. {f.ModId}");
                sw.WriteLine($"     reason: {f.Reason}");
                if (!string.IsNullOrEmpty(f.ModuleFolder))
                    sw.WriteLine($"     folder: {f.ModuleFolder}");
                sw.WriteLine();
            }
            DiagLog.Log(Tag, $"  wrote report: {reportPath}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "WriteIncompatibleReport", ex);
        }
    }
}
