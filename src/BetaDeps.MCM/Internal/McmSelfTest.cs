// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Automated self-test harness for the Mod Config UI. Triggered from the
// "Run Self-Test" button on the Mod Config tab (BetaDeps dev/debug only).
//
// What it does, in order:
//   1. Snapshot every registered settings instance's on-disk JSON to a
//      timestamped backup folder.
//   2. For each settings instance:
//        a. Snapshot its current in-memory values.
//        b. For each [SettingProperty*]-decorated property:
//             - mutate the value (flip bool, nudge int/float, append text,
//               cycle dropdown -- all within Min/Max bounds)
//             - persist via SettingsStorage.Save
//             - reload via SettingsStorage.Load
//             - assert the mutated value survived the round-trip
//             - restore the original value
//        c. Mutate every property in memory at once, then call SaveAll
//           (Done semantics). Reload from disk, assert mutations landed.
//        d. Restore originals in memory, save them so disk matches the
//           pre-test state.
//        e. Mutate every property in memory again, then call ReloadAll
//           (Cancel semantics). Assert in-memory now matches disk (i.e.
//           the mutations were discarded by the reload).
//   3. Restore all settings JSON from the backup folder so the user's
//      actual configuration is untouched.
//   4. Write a pass/fail summary to runtime.log.
//
// IMPORTANT: the backup folder is preserved on disk even if the test
// completes successfully. If anything goes wrong mid-test, the user can
// manually copy files from Configs\ModSettings\Global\SelfTestBackup-<ts>\
// back over the live folder.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using BetaDeps.Foundation;

using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;

namespace MCM.Internal;

internal static class McmSelfTest
{
    private const string Tag = "MCM.SelfTest";

    public sealed class PropertyResult
    {
        public string PropertyName = string.Empty;
        public string Kind = string.Empty;
        public bool RoundTripPassed;
        public string? FailureReason;
    }

    public sealed class ModResult
    {
        public string ModId = string.Empty;
        public string ModDisplayName = string.Empty;
        public bool DonePassed;
        public bool CancelPassed;
        public List<PropertyResult> Properties = new();
        public string? FatalError;
    }

    public sealed class VisibilityEntry
    {
        public string ModFolder = string.Empty;
        public bool HasCode;
        public bool Registered;
        public List<string> RegisteredIds = new(); // settings Ids this folder accounts for
    }

    public sealed class DuplicateDllEntry
    {
        public string DllName = string.Empty;
        // Tuples of (modulePath, fileSize, fileVersion-or-null).
        public List<(string Path, long Size, string Version)> Copies = new();
    }

    public sealed class Report
    {
        public DateTime StartedAt;
        public DateTime FinishedAt;
        public string BackupDir = string.Empty;
        public List<ModResult> Mods = new();
        public List<VisibilityEntry> Visibility = new();
        public List<DuplicateDllEntry> DuplicateDlls = new();

        // Mods that intentionally fail our test due to consumer-mod design
        // choices we can't fix (and shouldn't try to). Listed by mod Id with
        // a one-line reason. These mods are excluded from PASS/FAIL tallies
        // and reported separately as "consumer quirks" so the report stays
        // actionable.
        public static readonly Dictionary<string, string> KnownConsumerQuirks =
            new(StringComparer.Ordinal)
            {
                ["DismembermentPlus"] =
                    "DismembermentRealism setter intentionally clobbers DismembermentChance " +
                    "as a 'realism preset' feature. Not a BetaDeps bug.",
            };

        public bool IsQuirk(ModResult m) => KnownConsumerQuirks.ContainsKey(m.ModId);

        // Stats EXCLUDE known consumer-mod quirks — those mods are tracked
        // separately and shouldn't drag the headline pass-rate down.
        private IEnumerable<ModResult> NonQuirkMods => Mods.Where(m => !IsQuirk(m));

        public int TotalProperties => NonQuirkMods.Sum(m => m.Properties.Count);
        public int PassedProperties => NonQuirkMods.Sum(m => m.Properties.Count(p => p.RoundTripPassed));
        public int FailedProperties => TotalProperties - PassedProperties;
        public int PassedDone => NonQuirkMods.Count(m => m.DonePassed);
        public int FailedDone => NonQuirkMods.Count(m => !m.DonePassed);
        public int PassedCancel => NonQuirkMods.Count(m => m.CancelPassed);
        public int FailedCancel => NonQuirkMods.Count(m => !m.CancelPassed);
        public int TestedMods => NonQuirkMods.Count();
        public int QuirkMods => Mods.Count - TestedMods;

        public int VisibilityMissing => Visibility.Count(v => v.HasCode && !v.Registered);
        public int DuplicateGroups => DuplicateDlls.Count;
    }

    /// <summary>
    /// Run the full self-test. Safe to invoke from the UI thread - completes
    /// synchronously and returns when done. Always restores the user's
    /// real settings before returning, even on failure.
    /// </summary>
    public static Report RunAll()
    {
        var report = new Report { StartedAt = DateTime.Now };
        DiagLog.Log(Tag, "===== self-test starting =====");

        string backupDir;
        try { backupDir = BackupAllSettings(); }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "BackupAllSettings", ex);
            report.FinishedAt = DateTime.Now;
            return report;
        }
        report.BackupDir = backupDir;
        DiagLog.Log(Tag, $"backed up settings to: {backupDir}");

        try
        {
            foreach (var entry in SettingsRegistry.All)
            {
                var modResult = new ModResult
                {
                    ModId = entry.Id ?? "Unknown",
                    ModDisplayName = entry.DisplayName ?? entry.Id ?? "Unknown",
                };
                try { TestOneMod(entry, modResult); }
                catch (Exception ex)
                {
                    modResult.FatalError = ClassifyFatal(ex);
                    DiagLog.LogCaught(Tag, $"TestOneMod({entry.Id})", ex);
                }
                report.Mods.Add(modResult);
            }
        }
        finally
        {
            try { RestoreAllSettings(backupDir); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "RestoreAllSettings", ex); }
        }

        // Audits run after the persistence test so they can't accidentally
        // perturb settings state. Both are read-only.
        try { report.Visibility = AuditVisibility(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "AuditVisibility", ex); }
        try { report.DuplicateDlls = AuditDuplicateDlls(); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "AuditDuplicateDlls", ex); }

        report.FinishedAt = DateTime.Now;
        WriteReportToLog(report);
        return report;
    }

    // --------------------------------------------------------------------
    // Visibility audit
    // --------------------------------------------------------------------

    private static List<VisibilityEntry> AuditVisibility()
    {
        var result = new List<VisibilityEntry>();
        // Locate Modules root by walking up from our own DLL location.
        string? modulesRoot = LocateModulesRoot();
        if (modulesRoot == null) return result;

        // For each registered settings instance, figure out which mod folder
        // owns its DLL so we can match folders -> registrations.
        var folderForRegisteredId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in SettingsRegistry.All)
        {
            try
            {
                var asmLoc = r.Instance.GetType().Assembly.Location ?? string.Empty;
                if (string.IsNullOrEmpty(asmLoc)) continue;
                // Walk up parents until one is a direct child of Modules root.
                var cur = Directory.GetParent(asmLoc);
                while (cur != null)
                {
                    if (string.Equals(cur.Parent?.FullName, Path.Combine(modulesRoot), StringComparison.OrdinalIgnoreCase)
                     || string.Equals(cur.Parent?.Name, "Modules", StringComparison.OrdinalIgnoreCase))
                    {
                        folderForRegisteredId[r.Id] = cur.Name;
                        break;
                    }
                    cur = cur.Parent;
                }
            }
            catch { }
        }

        foreach (var modDir in Directory.GetDirectories(modulesRoot))
        {
            var entry = new VisibilityEntry { ModFolder = Path.GetFileName(modDir) };
            // Does this folder contain compiled code? (bin\Win64_Shipping_Client\*.dll)
            var binDir = Path.Combine(modDir, "bin", "Win64_Shipping_Client");
            entry.HasCode = Directory.Exists(binDir) && Directory.GetFiles(binDir, "*.dll").Length > 0;
            // Which registrations claim this folder?
            foreach (var kv in folderForRegisteredId)
            {
                if (string.Equals(kv.Value, entry.ModFolder, StringComparison.OrdinalIgnoreCase))
                {
                    entry.RegisteredIds.Add(kv.Key);
                }
            }
            entry.Registered = entry.RegisteredIds.Count > 0;
            result.Add(entry);
        }
        return result;
    }

    // --------------------------------------------------------------------
    // Duplicate-DLL / dependency-conflict audit
    // --------------------------------------------------------------------

    /// <summary>Libraries BetaDeps ships and that consumer mods commonly
    /// re-bundle. Duplicates with different versions cause the load-race
    /// bugs that motivated BetaDeps in the first place.</summary>
    private static readonly string[] WatchedDlls =
    {
        "0Harmony.dll",
        "Bannerlord.ButterLib.dll",
        "Bannerlord.UIExtenderEx.dll",
        "MCMv5.dll",
        "Newtonsoft.Json.dll",
        "Serilog.dll",
        "Microsoft.Extensions.DependencyInjection.dll",
        "Microsoft.Extensions.Logging.dll",
        "Mono.Cecil.dll",
        "MonoMod.Core.dll",
        "MonoMod.Utils.dll",
    };

    private static List<DuplicateDllEntry> AuditDuplicateDlls()
    {
        var result = new List<DuplicateDllEntry>();
        string? modulesRoot = LocateModulesRoot();
        if (modulesRoot == null) return result;

        foreach (var dllName in WatchedDlls)
        {
            var copies = new List<(string, long, string)>();
            try
            {
                foreach (var found in Directory.GetFiles(modulesRoot, dllName, SearchOption.AllDirectories))
                {
                    long size = 0;
                    string ver = "unknown";
                    try
                    {
                        var fi = new FileInfo(found);
                        size = fi.Length;
                        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(found);
                        ver = vi.FileVersion ?? "unknown";
                    }
                    catch { }
                    copies.Add((found, size, ver));
                }
            }
            catch { }
            if (copies.Count > 1)
            {
                result.Add(new DuplicateDllEntry { DllName = dllName, Copies = copies });
            }
        }
        return result;
    }

    private static string? LocateModulesRoot()
    {
        try
        {
            var own = typeof(McmSelfTest).Assembly.Location ?? string.Empty;
            if (string.IsNullOrEmpty(own)) return null;
            var dir = Directory.GetParent(own);
            while (dir != null)
            {
                if (string.Equals(dir.Name, "Modules", StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }
        catch { }
        return null;
    }

    // --------------------------------------------------------------------
    // Backup / restore
    // --------------------------------------------------------------------

    private static string BackupAllSettings()
    {
        var anyPath = SettingsStorage.ResolvePath("__probe__");
        var globalDir = Path.GetDirectoryName(anyPath) ?? string.Empty;
        var backupDir = Path.Combine(globalDir, $"SelfTestBackup-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backupDir);
        if (Directory.Exists(globalDir))
        {
            foreach (var file in Directory.GetFiles(globalDir, "*.json"))
            {
                var target = Path.Combine(backupDir, Path.GetFileName(file));
                File.Copy(file, target, overwrite: true);
            }
        }
        return backupDir;
    }

    private static void RestoreAllSettings(string backupDir)
    {
        var anyPath = SettingsStorage.ResolvePath("__probe__");
        var globalDir = Path.GetDirectoryName(anyPath) ?? string.Empty;
        if (!Directory.Exists(backupDir) || string.IsNullOrEmpty(globalDir)) return;
        foreach (var file in Directory.GetFiles(backupDir, "*.json"))
        {
            var target = Path.Combine(globalDir, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }
        // Also re-load each in-memory instance so it picks up the restored JSON.
        foreach (var entry in SettingsRegistry.All)
        {
            try { SettingsStorage.Load(entry.Instance, entry.Id); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"RestoreReload({entry.Id})", ex); }
        }
    }

    // --------------------------------------------------------------------
    // Per-mod test
    // --------------------------------------------------------------------

    private static void TestOneMod(RegisteredSettings entry, ModResult modResult)
    {
        var instance = entry.Instance;
        var type = instance.GetType();
        var testableProps = EnumerateTestableProperties(type).ToList();

        // 1. Snapshot original values so we can restore them between sub-tests.
        var originals = SnapshotValues(instance, testableProps);

        // 2. Per-property round-trip
        foreach (var prop in testableProps)
        {
            var pr = new PropertyResult
            {
                PropertyName = prop.Name,
                Kind = ClassifyProperty(prop),
            };
            try
            {
                var original = prop.GetValue(instance);
                var isDropdown = prop.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null;
                // For Dropdown<T>, capture original SelectedValue as a string
                // so we can restore by SelectedValue assignment after the
                // round-trip — re-assigning the same Dropdown reference would
                // be a no-op since MutateValue mutates in-place.
                string? originalDropdownRepr = isDropdown ? ToSelectedValueRepr(original) : null;
                var mutated = MutateValue(prop, original);
                if (mutated == null || (!isDropdown && object.Equals(mutated, original)))
                {
                    // We couldn't think of a different value (e.g. dropdown
                    // with a single option, int with Min==Max). Count as pass.
                    pr.RoundTripPassed = true;
                }
                else
                {
                    // For dropdowns, capture the post-mutation SelectedValue
                    // as a string BEFORE save. Comparing the live Dropdown
                    // against itself after reload would be a false positive
                    // (same reference, same SelectedValue at any given moment).
                    string? expectedDropdownRepr = isDropdown ? ToSelectedValueRepr(mutated) : null;

                    // Read-only Dropdown properties (no setter) are the BUTR
                    // convention: mod instantiates Dropdown in ctor and never
                    // re-assigns it. MutateValue already changed SelectedIndex
                    // in-place, so SetValue would just throw — skip it. For
                    // writable properties (including writable dropdowns), set
                    // the mutated value back so save/load actually tests
                    // serialization of the new value.
                    if (prop.CanWrite)
                        prop.SetValue(instance, mutated);
                    // Re-read after SetValue to capture what the setter
                    // actually STORED, which can differ from `mutated` when
                    // the setter has side effects:
                    //   - Action-bool pattern: AIInfluence's TestX/ForceX/
                    //     ClearX bools accept true, fire the action, then
                    //     reset to false.
                    //   - Validation clamps: int props that clamp to bounds.
                    // We're testing "does save+load preserve what the setter
                    // chose to keep", not "does save+load preserve what we
                    // tried to write" — the former is the property's real
                    // round-trip contract.
                    var actualMutated = isDropdown ? mutated : prop.GetValue(instance);
                    SettingsStorage.Save(instance, entry.Id);
                    SettingsStorage.Load(instance, entry.Id);
                    var afterReload = prop.GetValue(instance);
                    if (isDropdown)
                    {
                        var actualRepr = ToSelectedValueRepr(afterReload);
                        pr.RoundTripPassed = string.Equals(expectedDropdownRepr, actualRepr, StringComparison.Ordinal);
                        if (!pr.RoundTripPassed)
                            pr.FailureReason = $"expected SelectedValue '{expectedDropdownRepr}', got '{actualRepr}'";
                    }
                    else
                    {
                        pr.RoundTripPassed = ValuesEqual(prop, afterReload, actualMutated);
                        if (!pr.RoundTripPassed)
                            pr.FailureReason = $"expected {Repr(actualMutated)}, got {Repr(afterReload)}";
                    }
                    // restore for next sub-test
                    if (isDropdown && originalDropdownRepr != null)
                    {
                        // Set SelectedValue back via the live Dropdown
                        // instance — we already have the right reference.
                        // (TrySet by reference wouldn't change SelectedIndex.)
                        var live = prop.GetValue(instance);
                        TrySetDropdownByRepr(live, originalDropdownRepr);
                    }
                    else
                    {
                        TrySet(prop, instance, original);
                    }
                }
            }
            catch (Exception ex)
            {
                pr.RoundTripPassed = false;
                pr.FailureReason = ex.GetType().Name + ": " + ex.Message;
                if (ex.InnerException != null)
                    pr.FailureReason += $" (inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message})";
            }
            modResult.Properties.Add(pr);
        }

        // Persist originals back to disk so subsequent sub-tests start clean.
        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }

        // 3. Done semantics: mutate everything, Save, reload, verify mutations landed.
        modResult.DonePassed = TestDoneSemantics(entry, testableProps, originals);

        // Restore again for the Cancel test.
        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }

        // 4. Cancel semantics: mutate everything, then Load (revert), verify in-memory == original.
        modResult.CancelPassed = TestCancelSemantics(entry, testableProps, originals);

        // Final restore.
        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }
    }

    private static bool TestDoneSemantics(RegisteredSettings entry, List<PropertyInfo> props, Dictionary<string, object?> originals)
    {
        var instance = entry.Instance;
        var mutations = new Dictionary<string, object?>();
        // Track each property's value AFTER each subsequent mutation. If
        // setter A clobbers property B, we'll see B's value change at the
        // step where A was mutated.
        var watchedTrails = new Dictionary<string, List<string>>();
        foreach (var p in props)
        {
            try
            {
                var current = p.GetValue(instance);
                bool isDropdown = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null;
                var mutated = MutateValue(p, current);
                bool changed = isDropdown
                    ? (mutated != null)  // dropdowns mutate in place; assume change attempted
                    : (mutated != null && !object.Equals(mutated, current));
                if (changed)
                {
                    if (p.CanWrite && !isDropdown)
                        p.SetValue(instance, mutated);
                    // Capture what the SETTER actually kept, not what we
                    // tried to write. Action-bool / clamping / read-only
                    // setters can store a different value than the input.
                    // The Done test verifies "save+load preserves what's
                    // stored", so the stored value is the right reference.
                    mutations[p.Name] = isDropdown ? mutated : p.GetValue(instance);

                    // Snapshot every other property's value after this mutation.
                    // We'll diff trails later to find cascade culprits.
                    foreach (var q in props)
                    {
                        try
                        {
                            var v = q.GetValue(instance);
                            string repr = q.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null
                                ? ToSelectedValueRepr(v)
                                : (v?.ToString() ?? "<null>");
                            if (!watchedTrails.TryGetValue(q.Name, out var trail))
                            {
                                trail = new List<string>();
                                watchedTrails[q.Name] = trail;
                            }
                            trail.Add($"after-{p.Name}={repr}");
                        }
                        catch { }
                    }
                }
            }
            catch { /* property won't accept mutation; skip */ }
        }
        if (mutations.Count == 0) return true;

        try { SettingsStorage.Save(instance, entry.Id); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"DoneSave({entry.Id})", ex); return false; }

        try { SettingsStorage.Load(instance, entry.Id); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"DoneReload({entry.Id})", ex); return false; }

        foreach (var p in props)
        {
            if (!mutations.TryGetValue(p.Name, out var expected)) continue;
            try
            {
                var actual = p.GetValue(instance);
                if (!ValuesEqual(p, actual, expected))
                {
                    DiagLog.Log(Tag, $"Done semantics fail on {entry.Id}.{p.Name}: expected {Repr(expected)}, got {Repr(actual)}");
                    // Dump the value-trail for the failing property so we can
                    // see at which other property's mutation it got clobbered.
                    if (watchedTrails.TryGetValue(p.Name, out var trail))
                    {
                        DiagLog.Log(Tag, $"  trail for {p.Name}: {string.Join(" | ", trail)}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"DoneVerify({entry.Id}.{p.Name})", ex);
                return false;
            }
        }
        return true;
    }

    private static bool TestCancelSemantics(RegisteredSettings entry, List<PropertyInfo> props, Dictionary<string, object?> originals)
    {
        var instance = entry.Instance;
        // Mutate in-memory but do NOT save.
        foreach (var p in props)
        {
            try
            {
                var current = p.GetValue(instance);
                var mutated = MutateValue(p, current);
                if (mutated != null && !object.Equals(mutated, current))
                    p.SetValue(instance, mutated);
            }
            catch { }
        }

        // Cancel = reload from disk, which still has originals (we saved them above).
        try { SettingsStorage.Load(instance, entry.Id); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"CancelReload({entry.Id})", ex); return false; }

        foreach (var p in props)
        {
            if (!originals.TryGetValue(p.Name, out var expected)) continue;
            try
            {
                var actual = p.GetValue(instance);
                if (!ValuesEqual(p, actual, expected))
                {
                    DiagLog.Log(Tag, $"Cancel semantics fail on {entry.Id}.{p.Name}: expected {Repr(expected)}, got {Repr(actual)}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"CancelVerify({entry.Id}.{p.Name})", ex);
                return false;
            }
        }
        return true;
    }

    // --------------------------------------------------------------------
    // Property enumeration / classification
    // --------------------------------------------------------------------

    private static IEnumerable<PropertyInfo> EnumerateTestableProperties(Type t)
    {
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
            if (attr == null) continue;
            // Buttons aren't testable — their value is an Action and we'd
            // be invoking the consumer mod's callback. Skip cleanly.
            if (attr is SettingPropertyButtonAttribute) continue;
            // Skip properties whose underlying CLR type is a delegate (Action,
            // Func, EventHandler, ...). Some mods (ChatAi) annotate their
            // Action-typed properties with [SettingPropertyText] by mistake;
            // our harness can't meaningfully test "is this string round-
            // trippable" when the storage is a delegate.
            if (typeof(Delegate).IsAssignableFrom(p.PropertyType)) continue;
            // Read-only Dropdown<T> properties are fine — DropdownConverter
            // updates SelectedIndex on the existing instance during Load.
            yield return p;
        }
    }

    private static string ClassifyProperty(PropertyInfo p)
    {
        var attr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
        if (attr is SettingPropertyBoolAttribute) return "bool";
        if (attr is SettingPropertyIntegerAttribute) return "int";
        if (attr is SettingPropertyFloatingIntegerAttribute) return "float";
        if (attr is SettingPropertyTextAttribute) return "text";
        if (attr is SettingPropertyDropdownAttribute) return "dropdown";
        return "unknown";
    }

    // --------------------------------------------------------------------
    // Value mutation
    // --------------------------------------------------------------------

    private static object? MutateValue(PropertyInfo p, object? current)
    {
        var attr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
        if (attr is SettingPropertyBoolAttribute)
        {
            var b = current is bool x ? x : false;
            return !b;
        }
        if (attr is SettingPropertyIntegerAttribute ia)
        {
            int cur = ToInt(current, ia.MinValue);
            int candidate = cur + 1;
            if (candidate > ia.MaxValue) candidate = cur - 1;
            if (candidate < ia.MinValue) candidate = cur;
            return CoerceForProperty(candidate, p);
        }
        if (attr is SettingPropertyFloatingIntegerAttribute fa)
        {
            // Some mods (XorberaxLegacy) attach FloatingInteger to int-typed
            // properties. Detect the real declared type and operate in that
            // type's domain so SetValue doesn't throw.
            var ptype = p.PropertyType;
            if (ptype == typeof(int))
            {
                int cur = ToInt(current, (int)fa.MinValue);
                int step = Math.Max((int)((fa.MaxValue - fa.MinValue) * 0.1f), 1);
                int candidate = cur + step;
                if (candidate > fa.MaxValue) candidate = cur - step;
                if (candidate < fa.MinValue) candidate = cur;
                return candidate;
            }
            float curF = ToFloat(current, fa.MinValue);
            float stepF = Math.Max((fa.MaxValue - fa.MinValue) * 0.1f, 0.01f);
            float candF = curF + stepF;
            if (candF > fa.MaxValue) candF = curF - stepF;
            if (candF < fa.MinValue) candF = curF;
            return CoerceForProperty(candF, p);
        }
        if (attr is SettingPropertyTextAttribute)
        {
            var s = current as string ?? string.Empty;
            return s + "_test";
        }
        if (attr is SettingPropertyDropdownAttribute)
        {
            // Cycle the dropdown's SelectedIndex by 1 if possible.
            if (current == null) return null;
            var t = current.GetType();
            var idxProp = t.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
            if (idxProp == null) return null;
            var items = ReadDropdownItemCount(current);
            if (items <= 1) return current; // can't mutate
            int cur = idxProp.GetValue(current) is int ci ? ci : 0;
            int next = (cur + 1) % items;
            try
            {
                if (idxProp.CanWrite) idxProp.SetValue(current, next);
                return current; // mutate-in-place
            }
            catch { return null; }
        }
        return null;
    }

    private static int ReadDropdownItemCount(object dropdown)
    {
        try
        {
            // Dropdown<T> has an indexable Count via the underlying list -
            // we expose it through a "Count" or "GetItemsCount" reflection probe.
            var t = dropdown.GetType();
            var countProp = t.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (countProp?.GetValue(dropdown) is int c) return c;
            var itemsProp = t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (itemsProp?.GetValue(dropdown) is System.Collections.ICollection coll) return coll.Count;
        }
        catch { }
        return 0;
    }

    // --------------------------------------------------------------------
    // Equality / repr / snapshots
    // --------------------------------------------------------------------

    private static bool ValuesEqual(PropertyInfo p, object? a, object? b)
    {
        var attr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
        if (attr is SettingPropertyDropdownAttribute)
        {
            // The on-disk wire format is the dropdown's SelectedValue (see
            // DropdownConverter.WriteJson). SelectedIndex is only stable when
            // T has structural equality - for custom T types whose Equals is
            // reference-based (e.g. FasterTime.KeyDropdownOption), the
            // deserialized instance won't match anything in Items via IndexOf,
            // so SelectedIndex drifts even when SelectedValue round-trips.
            // Compare SelectedValue (via string repr to handle any T).
            var av = ToSelectedValueRepr(a);
            var bv = ToSelectedValueRepr(b);
            return av == bv;
        }
        if (a is float fa && b is float fb)
        {
            // Allow 0.001 tolerance for float serialization round-trip.
            return Math.Abs(fa - fb) < 0.001f;
        }
        return object.Equals(a, b);
    }

    private static int ToSelectedIndex(object? v)
    {
        if (v == null) return -1;
        if (v is int i) return i;
        return ReadSelectedIndex(v);
    }

    /// <summary>
    /// Extract a stable repr of the dropdown's SelectedValue. Handles three
    /// inputs: a raw int (legacy snapshot — return the value at Items[i]
    /// can't be reconstructed without the live dropdown, so just stringify
    /// the int), a string (already a repr), or a live Dropdown&lt;T&gt;.
    /// </summary>
    private static string ToSelectedValueRepr(object? v)
    {
        if (v == null) return "<null>";
        if (v is string s) return s;
        if (v is DropdownSnap ds) return ds.ValueRepr;
        if (v is int idx) return "idx:" + idx.ToString();
        try
        {
            var t = v.GetType();
            var selProp = t.GetProperty("SelectedValue", BindingFlags.Public | BindingFlags.Instance);
            if (selProp != null)
            {
                var sel = selProp.GetValue(v);
                return sel?.ToString() ?? "<null-selected>";
            }
        }
        catch { }
        return v.ToString() ?? "<unknown>";
    }

    private static int ToInt(object? v, int fallback)
    {
        if (v is int i) return i;
        if (v is float f) return (int)f;
        if (v is double d) return (int)d;
        return fallback;
    }

    private static float ToFloat(object? v, float fallback)
    {
        if (v is float f) return f;
        if (v is int i) return (float)i;
        if (v is double d) return (float)d;
        return fallback;
    }

    private static object? CoerceForProperty(object? v, PropertyInfo p)
    {
        if (v == null) return null;
        var t = p.PropertyType;
        if (v.GetType() == t) return v;
        try { return Convert.ChangeType(v, t); } catch { return v; }
    }

    private static int ReadSelectedIndex(object? dropdown)
    {
        if (dropdown == null) return -1;
        try
        {
            var t = dropdown.GetType();
            var idxProp = t.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
            if (idxProp?.GetValue(dropdown) is int i) return i;
        }
        catch { }
        return -1;
    }

    private static string Repr(object? v)
    {
        if (v == null) return "<null>";
        if (v is string s) return $"\"{s}\"";
        if (v is DropdownSnap ds) return $"Dropdown(idx={ds.Index}, val={ds.ValueRepr})";
        // For live Dropdown<T> instances, show SelectedValue rather than the
        // raw object reference / type name.
        try
        {
            var sel = v.GetType().GetProperty("SelectedValue", BindingFlags.Public | BindingFlags.Instance);
            if (sel != null)
            {
                var sv = sel.GetValue(v);
                var idxProp = v.GetType().GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
                var idx = idxProp?.GetValue(v) as int? ?? -1;
                return $"Dropdown(idx={idx}, val={sv ?? "<null>"})";
            }
        }
        catch { }
        return v.ToString() ?? "<null>";
    }

    /// <summary>Per-dropdown snapshot. Captures both SelectedIndex (so we can
    /// restore by index after a test) and the SelectedValue repr (so we can
    /// compare round-trip semantics correctly).</summary>
    private sealed class DropdownSnap
    {
        public int Index;
        public string ValueRepr = string.Empty;
    }

    private static Dictionary<string, object?> SnapshotValues(object instance, IEnumerable<PropertyInfo> props)
    {
        var snap = new Dictionary<string, object?>();
        foreach (var p in props)
        {
            try
            {
                var val = p.GetValue(instance);
                if (p.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null)
                {
                    snap[p.Name] = new DropdownSnap
                    {
                        Index = ReadSelectedIndex(val),
                        ValueRepr = ToSelectedValueRepr(val),
                    };
                }
                else
                {
                    snap[p.Name] = val;
                }
            }
            catch { }
        }
        return snap;
    }

    private static void RestoreValues(object instance, Dictionary<string, object?> snapshot)
    {
        foreach (var kv in snapshot)
        {
            var p = instance.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) continue;
            try
            {
                if (p.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null)
                {
                    var dd = p.GetValue(instance);
                    if (dd != null && kv.Value is DropdownSnap ds)
                    {
                        var idxProp = dd.GetType().GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
                        if (idxProp != null && idxProp.CanWrite) idxProp.SetValue(dd, ds.Index);
                    }
                }
                else
                {
                    TrySet(p, instance, kv.Value);
                }
            }
            catch { }
        }
    }

    private static void TrySet(PropertyInfo p, object instance, object? value)
    {
        try { if (p.CanWrite) p.SetValue(instance, value); } catch { }
    }

    /// <summary>Walk the dropdown's Items list looking for one whose
    /// ToString() matches the given repr, then set SelectedIndex to that
    /// position. Best-effort: if Items isn't accessible or no item matches,
    /// silently do nothing.</summary>
    private static void TrySetDropdownByRepr(object? dropdown, string repr)
    {
        if (dropdown == null) return;
        try
        {
            var t = dropdown.GetType();
            // Try common item-list properties / fields.
            System.Collections.IEnumerable? items = null;
            var itemsProp = t.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (itemsProp != null) items = itemsProp.GetValue(dropdown) as System.Collections.IEnumerable;
            if (items == null)
            {
                var itemsField = WalkInstanceField(t, "_items");
                if (itemsField != null) items = itemsField.GetValue(dropdown) as System.Collections.IEnumerable;
            }
            if (items == null) return;
            int i = 0, found = -1;
            foreach (var it in items)
            {
                if (string.Equals(it?.ToString() ?? "<null>", repr, StringComparison.Ordinal)) { found = i; break; }
                i++;
            }
            if (found < 0) return;
            var idxProp = t.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
            if (idxProp != null && idxProp.CanWrite) idxProp.SetValue(dropdown, found);
        }
        catch { }
    }

    private static FieldInfo? WalkInstanceField(Type? t, string name)
    {
        while (t != null && t != typeof(object))
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (f != null) return f;
            t = t.BaseType;
        }
        return null;
    }

    // --------------------------------------------------------------------
    // Report writing
    // --------------------------------------------------------------------

    /// <summary>Build the report text. errorsOnly=true filters out passing
    /// mods, passing properties, and content-only / OK visibility entries.
    /// <summary>v0.5.0: classify a fatal exception into a short, actionable
    /// reason that a Nexus commenter can paste back to a mod author. Picks
    /// out the innermost exception, recognizes common patterns (no parameterless
    /// constructor, settings class threw on construction, type-load issues,
    /// JSON file corruption, missing dependency types) and returns a one-line
    /// summary. Falls back to the raw exception name/message for unknown cases.</summary>
    private static string ClassifyFatal(Exception ex)
    {
        // Unwrap TargetInvocationException chains so we report the real cause.
        Exception cur = ex;
        while (cur is System.Reflection.TargetInvocationException && cur.InnerException != null)
            cur = cur.InnerException;

        var type = cur.GetType().Name;
        var msg = cur.Message ?? string.Empty;

        if (cur is System.Reflection.ReflectionTypeLoadException)
            return "TYPE LOAD: mod DLL references types that aren't installed (likely missing dependency mod or wrong game version)";
        if (cur is MissingMethodException && msg.IndexOf("ctor", StringComparison.OrdinalIgnoreCase) >= 0)
            return "NO PARAMETERLESS CONSTRUCTOR: mod's settings class needs a public ctor with no arguments — contact the mod author";
        if (cur is MissingMethodException)
            return "MISSING METHOD: " + msg + " (mod built against a different API version)";
        if (cur is TypeLoadException)
            return "TYPE LOAD: " + msg + " (mod likely built for a different game version)";
        if (cur is FileNotFoundException && msg.IndexOf(".dll", StringComparison.OrdinalIgnoreCase) >= 0)
            return "MISSING DLL: " + msg + " (a dependency mod isn't installed)";
        if (cur is Newtonsoft.Json.JsonReaderException || cur is Newtonsoft.Json.JsonSerializationException)
            return "BAD JSON: the mod's saved settings file is corrupt — delete it from Documents\\Mount and Blade II Bannerlord\\Configs\\ModSettings\\Global\\ and relaunch";
        if (cur is NullReferenceException)
            return "NULL REFERENCE in mod settings construction — likely a bug in the mod, contact its author";
        if (cur is System.InvalidOperationException && msg.IndexOf("Sequence contains no matching", StringComparison.OrdinalIgnoreCase) >= 0)
            return "SETTINGS CLASS GETTER THREW: the mod's settings property getter ran a LINQ query that found no match — usually fixed by enabling the mod's primary dependency";

        // Fall back to a short generic line.
        var shortMsg = msg.Length > 200 ? msg.Substring(0, 200) + "..." : msg;
        return type + ": " + shortMsg;
    }

    /// Also filters the duplicate-DLL audit to only entries where the version
    /// strings actually differ between copies (matching versions are
    /// harmless redundancy, not a load-race).</summary>

    /// <summary>v0.5.0: write the full self-test report to selftest.log next
    /// to runtime.log, and also write a condensed errors-only version into
    /// runtime.log itself so a user uploading runtime.log gets the gist
    /// without us forcing them to attach two files.</summary>
    private static void WriteReportToLog(Report report)
    {
        try
        {
            var fullText = BuildReportText(report, errorsOnly: false);
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rtPath);
            if (!string.IsNullOrEmpty(dir))
            {
                var selftestPath = System.IO.Path.Combine(dir, "selftest.log");
                System.IO.File.WriteAllText(selftestPath, fullText);
                DiagLog.Log(Tag, $"wrote selftest.log ({fullText.Length} chars)");
            }

            // Echo errors-only summary into runtime.log for quick triage.
            var summary = BuildReportText(report, errorsOnly: true);
            foreach (var line in summary.Split('\n'))
                DiagLog.Log(Tag, line.TrimEnd('\r'));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "WriteReportToLog", ex);
        }
    }

    private static string BuildReportText(Report report, bool errorsOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== MCM SELF-TEST REPORT =====");
        sb.AppendLine($"Started:  {report.StartedAt:HH:mm:ss}");
        sb.AppendLine($"Finished: {report.FinishedAt:HH:mm:ss}");
        sb.AppendLine($"Backup:   {report.BackupDir}");
        sb.AppendLine($"Mods:     {report.Mods.Count} (testing {report.TestedMods}, {report.QuirkMods} known consumer-mod quirk(s))");
        sb.AppendLine($"Properties: {report.PassedProperties}/{report.TotalProperties} passed round-trip");
        sb.AppendLine($"Done:     {report.PassedDone}/{report.TestedMods} mods passed Done semantics");
        sb.AppendLine($"Cancel:   {report.PassedCancel}/{report.TestedMods} mods passed Cancel semantics");
        sb.AppendLine($"Visibility: {report.VisibilityMissing} enabled-with-code mod folder(s) NOT in registry");
        sb.AppendLine($"Duplicate DLLs: {report.DuplicateGroups} watched library(ies) shipped in 2+ mods");
        sb.AppendLine();

        // v0.5.0+: list of registered mods so users can paste this into a
        // Nexus comment and the author can see exactly which modlist
        // produced which result. Useful for community-driven compat reports.
        sb.AppendLine("--- Registered mods (testing order) ---");
        if (report.Mods.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            int idx = 1;
            foreach (var m in report.Mods)
            {
                bool isPass = m.FatalError == null
                           && m.Properties.All(p => p.RoundTripPassed)
                           && m.DonePassed
                           && m.CancelPassed;
                string status = m.FatalError != null
                    ? "FATAL"
                    : (report.IsQuirk(m) ? "QUIRK" : (isPass ? "PASS" : "FAIL"));
                sb.AppendLine($"  {idx,2}. [{status,-5}] {m.ModId}  ({m.ModDisplayName})");
                idx++;
            }
        }
        sb.AppendLine();

        // Per-mod table — skip quirks (reported in their own section below).
        bool anyModFail = false;
        foreach (var m in report.Mods)
        {
            if (report.IsQuirk(m)) continue;
            bool isPass = m.FatalError == null
                       && m.Properties.All(p => p.RoundTripPassed)
                       && m.DonePassed
                       && m.CancelPassed;
            if (errorsOnly && isPass) continue;
            anyModFail |= !isPass;

            string status = m.FatalError != null ? $"FATAL: {m.FatalError}" : (isPass ? "PASS" : "FAIL");
            sb.AppendLine($"[{status}] {m.ModId} ({m.ModDisplayName})");
            sb.AppendLine($"    props: {m.Properties.Count(p => p.RoundTripPassed)}/{m.Properties.Count} passed");
            sb.AppendLine($"    done:  {(m.DonePassed ? "PASS" : "FAIL")}");
            sb.AppendLine($"    cancel:{(m.CancelPassed ? "PASS" : "FAIL")}");
            foreach (var p in m.Properties.Where(p => !p.RoundTripPassed))
            {
                sb.AppendLine($"      - {p.Kind} {p.PropertyName}: {p.FailureReason}");
            }
        }
        if (errorsOnly && !anyModFail)
        {
            sb.AppendLine("(all tested mods passed; per-mod table omitted)");
        }

        // Known consumer-mod quirks — these mods intentionally do things our
        // test marks as failures. Listed for transparency, not actionable
        // from BetaDeps's side.
        var quirkMods = report.Mods.Where(report.IsQuirk).ToList();
        if (quirkMods.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Known consumer-mod quirks (informational, not BetaDeps bugs) ---");
            foreach (var m in quirkMods)
            {
                var reason = Report.KnownConsumerQuirks.TryGetValue(m.ModId, out var r) ? r : "(no reason recorded)";
                sb.AppendLine($"  [QUIRK] {m.ModId}: {reason}");
            }
        }

        // Visibility audit -- in errors-only mode, only show missing-with-code entries.
        sb.AppendLine();
        sb.AppendLine("--- Visibility audit ---");
        if (errorsOnly)
        {
            var missing = report.Visibility.Where(v => v.HasCode && !v.Registered).ToList();
            if (missing.Count == 0)
            {
                sb.AppendLine("(no enabled-with-code mods are missing from the settings registry)");
            }
            else
            {
                sb.AppendLine($"{missing.Count} mod folder(s) have compiled code but no registered settings class:");
                foreach (var v in missing)
                {
                    sb.AppendLine($"  MISSING: {v.ModFolder}");
                }
            }
        }
        else
        {
            sb.AppendLine("Enabled mod folders, with their registered-settings status. ");
            sb.AppendLine("MISSING = mod folder has compiled code but no settings class was discovered.");
            foreach (var v in report.Visibility)
            {
                string vstatus;
                if (v.Registered) vstatus = $"OK ({string.Join(",", v.RegisteredIds)})";
                else if (!v.HasCode) vstatus = "(content-only, no code)";
                else vstatus = "MISSING (has code, no settings detected)";
                sb.AppendLine($"  [{(v.HasCode ? "C" : "-")}{(v.Registered ? "R" : "-")}] {v.ModFolder} -- {vstatus}");
            }
        }

        // Duplicate-DLL audit -- in errors-only mode, only show entries where
        // version strings actually differ across copies. Same-version
        // duplicates are harmless (the AssemblyResolve shim picks one and the
        // others are unused).
        sb.AppendLine();
        sb.AppendLine("--- Duplicate-DLL audit ---");
        var dlls = errorsOnly
            ? report.DuplicateDlls.Where(d => d.Copies.Select(c => c.Version).Distinct().Count() > 1).ToList()
            : report.DuplicateDlls;
        if (dlls.Count == 0)
        {
            sb.AppendLine(errorsOnly
                ? "(no library has duplicate copies with conflicting versions)"
                : "  No duplicate copies of watched libraries detected.");
        }
        else
        {
            sb.AppendLine("Multiple copies of these libraries exist across enabled mods. ");
            sb.AppendLine("If versions differ, the first loaded one wins for the whole AppDomain -- ");
            sb.AppendLine("this is the load-order-race bug class BetaDeps exists to prevent.");
            foreach (var d in dlls)
            {
                sb.AppendLine($"  {d.DllName}: {d.Copies.Count} cop(y/ies)");
                foreach (var c in d.Copies)
                {
                    sb.AppendLine($"    v{c.Version,-20} {c.Size,10} bytes  {c.Path}");
                }
            }
        }

        sb.AppendLine("===== END SELF-TEST REPORT =====");
        return sb.ToString();
    }
}
