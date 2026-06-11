// BetaDeps.Foundation -- SubModuleConstructionGuard
//
// v0.7 headline feature: single-launch auto-disable of known-broken mods.
//
// THE PROBLEM v0.6 LEFT OPEN:
// In v0.6 we wrote disabled-mod state to LauncherData.xml during BetaDeps
// startup. That worked for soft crashes (BannerKings dialog popup) because
// the launcher reads LauncherData.xml at process start and our changes were
// already in. But for hard crashes (ROT-class CTD), Bannerlord re-writes
// LauncherData.xml with its IN-MEMORY enabled-mods state right before
// terminating, undoing our disable. Next launch, the same mod loads, the
// same CTD happens.
//
// THE FIX:
// Harmony-patch the engine's SubModule construction loop. When the engine
// tries to construct a SubModule whose module ID is in our disabled list,
// the patch returns false (Harmony Prefix convention) and the original
// constructor is skipped entirely. The module simply doesn't exist in the
// running session -- bypassing the LauncherData.xml-overwrite issue.
//
// WHY DEFENSIVE:
// TaleWorlds doesn't publish stable internal API for SubModule loading.
// The exact method signature changes between game patches. Rather than
// hard-code "Module.LoadSubModule(ModuleInfo, SubModuleInfo)", we sniff
// for a method on TaleWorlds.MountAndBlade.Module whose name contains
// "SubModule" + "Load" and whose args look like they carry a module
// identifier. The prefix then reflects on the args looking for a string
// "Id" / "Name" / "ModuleId" property. If anything goes wrong we log and
// fall through (run original), never block load.
//
// Original work. MIT, copyright 2026 Trashpanda62.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class SubModuleConstructionGuard
{
    private const string Tag = "SubModuleConstructionGuard";
    private const string HarmonyId = "betadeps.foundation.submoduleguard";

    private static readonly HashSet<string> _disabledModuleIds =
        new(StringComparer.OrdinalIgnoreCase);
    private static int _installed;

    /// <summary>
    /// Install the Harmony patch and load the disabled-mods list. Idempotent.
    /// Call from BetaDeps.Harmony's OnSubModuleLoad AFTER the Harmony runtime
    /// gate is open -- we need Lib.Harmony available before this runs.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0)
        {
            DiagLog.Log(Tag, "Install: already installed, skipping.");
            return;
        }

        try
        {
            // Opt-in gate (v0.7.1+): same flag as IncompatibleModDetector.
            // Auto-disable + pre-construction guard both off by default; user
            // toggles via Mod Config "Toggle Auto-Disable" button which writes
            // Modules\BetaDeps\auto-disable-enabled.flag.
            try
            {
                var modulesRoot = ResolveModulesRoot();
                if (string.IsNullOrEmpty(modulesRoot))
                {
                    DiagLog.Log(Tag, "modules root unknown; skipping pre-construction patch install.");
                    return;
                }
                var enabledFlag = Path.Combine(modulesRoot!, "BetaDeps", "auto-disable-enabled.flag");
                if (!File.Exists(enabledFlag))
                {
                    DiagLog.Log(Tag, "auto-disable-enabled.flag NOT present (default) -- skipping pre-construction patch install. Click 'Toggle Auto-Disable' in Mod Config to enable.");
                    return;
                }
                DiagLog.Log(Tag, "auto-disable-enabled.flag present -- installing pre-construction patch.");
            }
            catch (Exception ex)
            {
                try { DiagLog.LogCaught(Tag, "  enabled-flag check", ex); } catch { }
                return;  // be conservative on error
            }

            LoadDisabledList();

            if (_disabledModuleIds.Count == 0)
            {
                DiagLog.Log(Tag, "no disabled modules in betadeps-disabled-mods.log; patch not installed (nothing to block).");
                return;
            }

            DiagLog.Log(Tag, $"loaded {_disabledModuleIds.Count} disabled module id(s) from log: {string.Join(", ", _disabledModuleIds)}");

            var moduleType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Module");
            if (moduleType == null)
            {
                DiagLog.Log(Tag, "TaleWorlds.MountAndBlade.Module type not found; aborting patch install.");
                return;
            }

            // Find candidate methods. We patch every method whose name contains
            // both "Load" and "SubModule" -- catches LoadSubModule,
            // LoadSubModules, InitializeSubModule, etc. across game versions.
            // The prefix is a no-op for calls that don't carry a module id we
            // recognize, so multi-patching is safe.
            //
            // M6 (Phase 4.3): VOID candidates only. The prefix blocks a
            // disabled module by returning false (skip original) WITHOUT
            // setting __result -- on a non-void method that hands the engine
            // a default null/false it never produced, which is worse than
            // not blocking. A non-void loader on some future game version
            // just isn't guarded (the disable markers still work via the
            // launcher set); log what we skipped so it's visible.
            var allCandidates = moduleType
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.IndexOf("SubModule", StringComparison.OrdinalIgnoreCase) >= 0
                         && m.Name.IndexOf("Load", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            var candidates = allCandidates.Where(m => m.ReturnType == typeof(void)).ToList();
            foreach (var nonVoid in allCandidates.Where(m => m.ReturnType != typeof(void)))
            {
                DiagLog.Log(Tag, $"  skipping non-void candidate {nonVoid.Name} (returns {nonVoid.ReturnType.Name}; skip-without-result would inject a default value into the engine)");
            }

            if (candidates.Count == 0)
            {
                DiagLog.Log(Tag, "no Load*SubModule* methods found on TaleWorlds.MountAndBlade.Module; patch install aborted.");
                return;
            }

            var harmony = new Harmony(HarmonyId);
            var prefixMethod = AccessTools.Method(typeof(SubModuleConstructionGuard), nameof(LoadSubModulePrefix));
            if (prefixMethod == null)
            {
                DiagLog.Log(Tag, "internal error: LoadSubModulePrefix method not found via reflection; patch install aborted.");
                return;
            }

            int patched = 0;
            foreach (var target in candidates)
            {
                try
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefixMethod));
                    DiagLog.Log(Tag, $"  patched: {target.DeclaringType?.FullName}.{target.Name}({string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name))})");
                    patched++;
                }
                catch (Exception ex)
                {
                    try { DiagLog.LogCaught(Tag, $"  patch failed for {target.Name}", ex); } catch { }
                }
            }

            DiagLog.Log(Tag, $"install complete. {patched}/{candidates.Count} candidate method(s) patched.");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Install", ex); } catch { }
        }
    }

    /// <summary>
    /// Harmony Prefix. Inspects the args, looks for something that carries
    /// a string module ID, and returns false (skip original) if that ID is
    /// in the disabled list. Never throws — always falls through on error
    /// so a misidentified arg shape can't take the game down.
    /// </summary>
    private static bool LoadSubModulePrefix(object[] __args)
    {
        try
        {
            if (__args == null || __args.Length == 0) return true;

            foreach (var arg in __args)
            {
                if (arg == null) continue;

                // Most engine methods pass a ModuleInfo or SubModuleInfo;
                // both expose an "Id" or "Name" string. Sniff for either.
                var id = TryExtractModuleId(arg);
                if (string.IsNullOrEmpty(id)) continue;

                if (_disabledModuleIds.Contains(id))
                {
                    DiagLog.Log(Tag, $"BLOCKED construction of disabled module: {id}");
                    return false; // skip original — module isn't constructed
                }
            }
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "LoadSubModulePrefix", ex); } catch { }
            // fall through to original on any error
        }
        return true;
    }

    private static string? TryExtractModuleId(object obj)
    {
        var t = obj.GetType();

        // Try common property/field names in order of likelihood.
        foreach (var name in new[] { "Id", "ModuleId", "Name", "ModuleName" })
        {
            try
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    var val = prop.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
                var field = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null && field.FieldType == typeof(string))
                {
                    var val = field.GetValue(obj) as string;
                    if (!string.IsNullOrEmpty(val)) return val;
                }
            }
            catch { /* swallow per-property reflect errors */ }
        }
        return null;
    }

    /// <summary>
    /// Parse Modules\BetaDeps\betadeps-disabled-mods.log into _disabledModuleIds.
    /// File format (one line per disable, written by IncompatibleModDetector):
    ///   [yyyy-MM-dd HH:mm:ss] {modId}\t{reason}
    ///
    /// v0.7.5 hardening:
    ///   1. Entries older than DisableLogTtl are FORGIVEN -- skipped on load.
    ///      A crash-suspect that hasn't recurred in 7+ days is more likely a
    ///      one-off than a chronic problem. The user shouldn't have to know to
    ///      manually clear the log to give that mod another chance.
    ///   2. Any BetaDeps-owned ID (per IncompatibleModDetector.IsBetaDepsOwnedId)
    ///      is NEVER honored as a block target -- protects against a bug
    ///      elsewhere that mistakenly wrote one of our own ids into the log.
    ///      Was the root cause OQrock + ehmealeo hit on v0.7.3/v0.7.4: a
    ///      cascading disable could starve the MCM tab of supporting state,
    ///      and the recovery toggle lives ON the missing tab. Now: we hard-
    ///      refuse to disable ourselves under any circumstances.
    /// </summary>
    private static readonly TimeSpan DisableLogTtl = TimeSpan.FromDays(7);

    private static void LoadDisabledList()
    {
        try
        {
            var modulesRoot = ResolveModulesRoot();
            if (string.IsNullOrEmpty(modulesRoot)) return;

            var path = Path.Combine(modulesRoot!, "BetaDeps", "betadeps-disabled-mods.log");
            if (!File.Exists(path)) return;

            int forgivenAge = 0;
            int forgivenOwn = 0;
            int kept = 0;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine?.Trim() ?? "";
                if (string.IsNullOrEmpty(line)) continue;

                // Try to parse a leading "[yyyy-MM-dd HH:mm:ss] " timestamp.
                DateTime? when = null;
                var afterBracket = line.IndexOf(']');
                if (line.StartsWith("[") && afterBracket > 0)
                {
                    var tsText = line.Substring(1, afterBracket - 1).Trim();
                    if (DateTime.TryParse(tsText, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                    {
                        when = parsed;
                    }
                }

                // TTL gate: skip entries older than DisableLogTtl. The line
                // stays in the file (we don't rewrite the log here -- the
                // append-only history is useful for triage), we just don't
                // ACT on it. If the same mod crashes again, a fresh entry
                // gets written and the new timestamp wins.
                if (when.HasValue && (DateTime.Now - when.Value) > DisableLogTtl)
                {
                    forgivenAge++;
                    continue;
                }

                var body = afterBracket >= 0 && afterBracket < line.Length - 1
                    ? line.Substring(afterBracket + 1).TrimStart()
                    : line;

                // Body is "{modId}\t{reason}" — split on tab, take the modId.
                var tabIdx = body.IndexOf('\t');
                var modId = tabIdx > 0 ? body.Substring(0, tabIdx).Trim() : body.Trim();
                if (string.IsNullOrEmpty(modId)) continue;

                // Belt-and-suspenders: NEVER honor a block on a BetaDeps-owned
                // id. Even if some upstream bug wrote it here, we refuse to
                // disable ourselves.
                if (IncompatibleModDetector.IsBetaDepsOwnedId(modId))
                {
                    forgivenOwn++;
                    continue;
                }

                _disabledModuleIds.Add(modId);
                kept++;
            }

            DiagLog.Log(Tag, $"LoadDisabledList: {kept} active block(s); forgiven {forgivenAge} aged-out + {forgivenOwn} self-owned.");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "LoadDisabledList", ex); } catch { }
        }
    }

    /// <summary>
    /// Walks up from BetaDeps.Foundation.dll's location to the Modules\ root.
    /// Mirrors IncompatibleModDetector.ResolveModulesRoot so both stay in sync.
    /// </summary>
    private static string? ResolveModulesRoot()
    {
        try
        {
            var ownPath = typeof(SubModuleConstructionGuard).Assembly.Location;
            if (string.IsNullOrEmpty(ownPath)) return null;
            // ownPath = ...\Modules\BetaDeps\bin\Win64_Shipping_Client\BetaDeps.Foundation.dll
            var binDir         = Path.GetDirectoryName(ownPath);
            var betaDepsBin    = Path.GetDirectoryName(binDir);
            var betaDepsModule = Path.GetDirectoryName(betaDepsBin);
            var modulesRoot    = Path.GetDirectoryName(betaDepsModule);
            return modulesRoot;
        }
        catch
        {
            return null;
        }
    }
}
