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
using MCM.UI.GUI.ViewModels;   // SettingsVM / SettingsPropertyVM -- UI-layer test path

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
        public bool UiLayerPassed = true;   // Phase 1.2: VM-setter -> disk -> VM round-trip
        public bool PresetPassed = true;    // Phase 1.2: save preset -> mutate -> apply -> assert
        public bool IsPerSave;              // tested inside a loaded campaign (per-campaign settings)
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
        public int PassedUiLayer => NonQuirkMods.Count(m => m.UiLayerPassed);
        public int FailedUiLayer => NonQuirkMods.Count(m => !m.UiLayerPassed);
        public int PassedPreset => NonQuirkMods.Count(m => m.PresetPassed);
        public int FailedPreset => NonQuirkMods.Count(m => !m.PresetPassed);
        public int PerSaveMods => NonQuirkMods.Count(m => m.IsPerSave);
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
        modResult.IsPerSave = IsPerSaveInstance(instance);
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
                var isDropdown = IsDropdownProperty(prop);
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

        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }

        // 5. UI-layer round-trip: drive values through SettingsVM / SettingsPropertyVM
        //    setters (the widget write path) -> Save -> Load -> fresh VM -> assert.
        //    Catches "the widget didn't write the property" and reaches fluent
        //    settings (which expose no [SettingProperty] PropertyInfos).
        modResult.UiLayerPassed = TestUiLayerSemantics(entry);

        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }

        // 6. Preset round-trip: save current as a preset, mutate live, apply the
        //    preset, assert live reverted to the snapshot, delete the preset.
        modResult.PresetPassed = TestPresetSemantics(entry, testableProps, originals);

        // Final restore.
        RestoreValues(instance, originals);
        try { SettingsStorage.Save(instance, entry.Id); } catch { }
    }

    /// <summary>
    /// Phase 1.2: exercise the UI binding path. Builds a SettingsVM, mutates each
    /// non-header/non-button row through its VM setter (BoolValue/IntValue/
    /// FloatValue/TextValue/CycleDropdown -- exactly what a checkbox/slider/
    /// dropdown widget calls), persists, reloads, rebuilds a fresh VM, and asserts
    /// every value survived the VM -> disk -> VM round-trip. Works for both
    /// attribute and fluent settings because the VM abstracts the backing store.
    /// </summary>
    private static bool TestUiLayerSemantics(RegisteredSettings entry)
    {
        var instance = entry.Instance;
        SettingsVM vm1;
        try { vm1 = new SettingsVM(instance); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"UiLayer/build({entry.Id})", ex); return false; }

        var rows = vm1.SettingPropertyGroups
            .SelectMany(g => g.SettingProperties)
            .Where(p => p.TypeKind != "button")
            .ToList();
        if (rows.Count == 0) return true; // nothing to drive (e.g. button-only mod)

        var originals = new Dictionary<string, object?>(StringComparer.Ordinal);
        var expected  = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var p in rows)
        {
            try
            {
                switch (p.TypeKind)
                {
                    case "bool":
                        originals[p.Name] = p.BoolValue;
                        p.BoolValue = !p.BoolValue;
                        expected[p.Name] = p.BoolValue;
                        break;
                    case "int":
                    {
                        originals[p.Name] = p.IntValue;
                        int min = (int)Math.Round(p.MinValue), max = (int)Math.Round(p.MaxValue);
                        int cur = p.IntValue;
                        int next = cur < max ? cur + 1 : (cur > min ? cur - 1 : cur);
                        p.IntValue = next;
                        expected[p.Name] = p.IntValue; // re-read: the setter may clamp
                        break;
                    }
                    case "float":
                    {
                        originals[p.Name] = p.FloatValue;
                        float min = (float)p.MinValue, max = (float)p.MaxValue;
                        float cur = p.FloatValue;
                        float step = (max > min) ? (max - min) * 0.1f : 1f;
                        float next = (cur + step <= max) ? cur + step : (cur - step >= min ? cur - step : cur);
                        p.FloatValue = next;
                        expected[p.Name] = p.FloatValue;
                        break;
                    }
                    case "text":
                        originals[p.Name] = p.TextValue;
                        p.TextValue = (p.TextValue ?? string.Empty) + "_uitest";
                        expected[p.Name] = p.TextValue;
                        break;
                    case "dropdown":
                        originals[p.Name] = p.DropdownDisplayText;
                        p.CycleDropdownNext();
                        expected[p.Name] = p.DropdownDisplayText;
                        break;
                }
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"UiLayer/mutate({entry.Id}.{p.Name})", ex); }
        }

        bool ok = true;
        try
        {
            SettingsStorage.Save(instance, entry.Id);   // WriteBack already updated the backing store
            SettingsStorage.Load(instance, entry.Id);
            var vm2 = new SettingsVM(instance);
            var rows2 = vm2.SettingPropertyGroups
                .SelectMany(g => g.SettingProperties)
                .Where(p => p.TypeKind != "button")
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var kv in expected)
            {
                if (!rows2.TryGetValue(kv.Key, out var p2)) continue;
                object? actual = p2.TypeKind switch
                {
                    "bool"     => p2.BoolValue,
                    "int"      => p2.IntValue,
                    "float"    => p2.FloatValue,
                    "text"     => p2.TextValue,
                    "dropdown" => p2.DropdownDisplayText,
                    _          => null
                };
                if (!UiValueEqual(p2.TypeKind, actual, kv.Value))
                {
                    ok = false;
                    DiagLog.Log(Tag, $"UI-layer fail on {entry.Id}.{kv.Key}: expected {Repr(kv.Value)}, got {Repr(actual)}");
                }
            }

            // Restore originals through the VM path (covers fluent too).
            foreach (var p in rows2.Values)
            {
                if (!originals.TryGetValue(p.Name, out var orig)) continue;
                try
                {
                    switch (p.TypeKind)
                    {
                        case "bool":  p.BoolValue  = orig is bool b && b; break;
                        case "int":   if (orig is int iv) p.IntValue = iv; break;
                        case "float": if (orig is float fv) p.FloatValue = fv; break;
                        case "text":  p.TextValue = orig as string ?? string.Empty; break;
                        case "dropdown":
                            // best-effort: cycle until the display text matches the
                            // original (bounded so a missing match can't spin forever).
                            for (int i = 0; i < 64 && !string.Equals(p.DropdownDisplayText, orig as string, StringComparison.Ordinal); i++)
                                p.CycleDropdownNext();
                            break;
                    }
                }
                catch { }
            }
            SettingsStorage.Save(instance, entry.Id);
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"UiLayer/verify({entry.Id})", ex); ok = false; }
        return ok;
    }

    /// <summary>True if the settings instance is a per-campaign (per-save)
    /// settings object (derives from BasePerSaveSettings / PerSaveSettings&lt;T&gt;).
    /// These only register inside a loaded campaign; the in-campaign "Run
    /// Self-Test" button is what exercises them.</summary>
    private static bool IsPerSaveInstance(object instance)
    {
        for (var t = instance.GetType(); t != null && t != typeof(object); t = t.BaseType)
            if (t.Name.IndexOf("PerSaveSettings", StringComparison.Ordinal) >= 0)
                return true;
        return false;
    }

    private static bool UiValueEqual(string typeKind, object? a, object? b)
    {
        if (typeKind == "float")
        {
            float fa = a is float x ? x : 0f, fb = b is float y ? y : 0f;
            return Math.Abs(fa - fb) <= 0.001f;
        }
        return object.Equals(a, b);
    }

    /// <summary>
    /// Phase 1.2: save the current live values as a preset, mutate live values +
    /// save (so disk differs from the preset), apply the preset, and assert live
    /// reverted to the snapshot. Value assertions use the raw [SettingProperty]
    /// props (non-empty for attribute mods); for fluent mods the preset file
    /// copy is still exercised even though there are no PropertyInfos to assert.
    /// </summary>
    private static bool TestPresetSemantics(RegisteredSettings entry, List<PropertyInfo> props, Dictionary<string, object?> originals)
    {
        var instance = entry.Instance;
        const string presetName = "__BetaDepsSelfTest__";
        try
        {
            // Live disk currently == originals (caller restored+saved before this).
            if (!SettingsStorage.SavePreset(entry.Id, presetName))
            {
                DiagLog.Log(Tag, $"Preset test: SavePreset failed for {entry.Id}");
                return false;
            }

            // Mutate live + persist so the live file diverges from the preset.
            foreach (var p in props)
            {
                try
                {
                    var m = MutateValue(p, p.GetValue(instance));
                    if (m != null && p.CanWrite) p.SetValue(instance, m);
                }
                catch { }
            }
            SettingsStorage.Save(instance, entry.Id);

            // Apply the preset. LoadPresetIntoLiveFile only copies the preset
            // file over the live file; the UI's ExecutePresetApply reloads the
            // instance from disk afterward (then re-binds the grid). Mirror that
            // exact sequence so the in-memory instance reflects the applied preset.
            if (!SettingsStorage.LoadPresetIntoLiveFile(entry.Id, presetName))
            {
                DiagLog.Log(Tag, $"Preset test: apply failed for {entry.Id}");
                return false;
            }
            SettingsStorage.Load(instance, entry.Id);

            bool ok = true;
            foreach (var p in props)
            {
                if (!originals.TryGetValue(p.Name, out var expected)) continue;
                try
                {
                    var actual = p.GetValue(instance);
                    if (!ValuesEqual(p, actual, expected))
                    {
                        ok = false;
                        DiagLog.Log(Tag, $"Preset apply mismatch on {entry.Id}.{p.Name}: expected {Repr(expected)}, got {Repr(actual)}");
                    }
                }
                catch (Exception ex) { DiagLog.LogCaught(Tag, $"PresetVerify({entry.Id}.{p.Name})", ex); ok = false; }
            }
            return ok;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Preset({entry.Id})", ex);
            return false;
        }
        finally
        {
            try { SettingsStorage.DeletePreset(entry.Id, presetName); } catch { }
            RestoreValues(instance, originals);
            try { SettingsStorage.Save(instance, entry.Id); } catch { }
        }
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
                bool isDropdown = IsDropdownProperty(p);
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
                            string repr = IsDropdownProperty(q)
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

        // Re-snapshot expected values AFTER all mutations are complete. Some
        // consumer mods (e.g. IDontCare) expose computed/aggregate properties
        // whose getter recomputes from other properties' state. When we
        // mutate property A early and later mutate property B that triggers
        // A's recompute, the value we captured at A's mutation step is stale.
        // The Done test asserts "save+load preserves what's currently in
        // memory after Done", so the in-memory value at this point is the
        // correct expectation.
        foreach (var p in props)
        {
            if (!mutations.ContainsKey(p.Name)) continue;
            try { mutations[p.Name] = p.GetValue(instance); }
            catch { /* leave the earlier-captured value as expectation */ }
        }

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
        // Type-first: trust the declared property type over the attribute,
        // because some mods (e.g. IDontCare — I Don't Care) decorate a
        // Dropdown<string> property with [SettingPropertyBool] (mod-author
        // bug). The mutation/save path WILL fail if we trust the attribute,
        // because writing a bool into a Dropdown<string> setter throws.
        if (IsDropdownProperty(p)) return "dropdown";

        var attr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
        if (attr is SettingPropertyBoolAttribute) return "bool";
        if (attr is SettingPropertyIntegerAttribute) return "int";
        if (attr is SettingPropertyFloatingIntegerAttribute) return "float";
        if (attr is SettingPropertyTextAttribute) return "text";
        if (attr is SettingPropertyDropdownAttribute) return "dropdown";
        return "unknown";
    }

    /// <summary>
    /// True if the property is a dropdown — either marked with
    /// [SettingPropertyDropdown] OR whose declared type is MCM.Common.Dropdown&lt;T&gt;.
    /// </summary>
    private static bool IsDropdownProperty(PropertyInfo p)
    {
        if (p.GetCustomAttributes(inherit: true).OfType<SettingPropertyDropdownAttribute>().FirstOrDefault() != null)
            return true;
        var t = p.PropertyType;
        while (t != null)
        {
            if (t.IsGenericType)
            {
                var gd = t.GetGenericTypeDefinition();
                if (gd.FullName == "MCM.Common.Dropdown`1") return true;
            }
            t = t.BaseType;
        }
        return false;
    }

    // --------------------------------------------------------------------
    // Value mutation
    // --------------------------------------------------------------------

    private static object? MutateValue(PropertyInfo p, object? current)
    {
        // Type-first: if the property is Dropdown<T>, treat it as a dropdown
        // regardless of any (possibly mis-applied) bool/int attribute. Same
        // rationale as ClassifyProperty — IDontCare decorates a Dropdown<string>
        // property with [SettingPropertyBool], so trusting the attribute would
        // flip a bool and crash on save.
        if (IsDropdownProperty(p))
        {
            // Fall through to the existing dropdown-handling block below.
            // (The attribute-based branches all return early; this guard
            // ensures we skip them for type-confirmed dropdowns.)
            return MutateDropdown(p, current);
        }

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
            return MutateDropdown(p, current);
        }
        return null;
    }

    /// <summary>
    /// Cycle a Dropdown<T>'s SelectedIndex by 1. Mutates in-place; returns
    /// the same reference (or null if the property/value can't be mutated).
    /// </summary>
    private static object? MutateDropdown(PropertyInfo p, object? current)
    {
        if (current == null) return null;
        var t = current.GetType();
        var idxProp = t.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
        if (idxProp == null) return null;
        var items = ReadDropdownItemCount(current);
        if (items <= 1) return current;
        int cur = idxProp.GetValue(current) is int ci ? ci : 0;
        int next = (cur + 1) % items;
        try
        {
            if (idxProp.CanWrite) idxProp.SetValue(current, next);
            return current;
        }
        catch { return null; }
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
        // Type-first dropdown check (same rationale as ClassifyProperty:
        // IDontCare uses [SettingPropertyBool] on Dropdown<string> properties).
        if (IsDropdownProperty(p))
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
                if (IsDropdownProperty(p))
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
                if (IsDropdownProperty(p))
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

                // v4 #5: machine-readable sidecar. selftest.json carries the
                // same SaveShield + PatchShield + environment data in a form
                // that AI assistants and CI tooling can parse without
                // needing to interpret the human-readable text layout.
                try
                {
                    var jsonPath = System.IO.Path.Combine(dir, "selftest.json");
                    var jsonText = BuildSelftestJson(report);
                    System.IO.File.WriteAllText(jsonPath, jsonText);
                    DiagLog.Log(Tag, $"wrote selftest.json ({jsonText.Length} chars)");
                }
                catch (Exception jsonEx)
                {
                    DiagLog.LogCaught(Tag, "WriteSelftestJson", jsonEx);
                }
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
        sb.AppendLine($"UI-layer: {report.PassedUiLayer}/{report.TestedMods} mods passed VM->disk->VM round-trip");
        sb.AppendLine($"Presets:  {report.PassedPreset}/{report.TestedMods} mods passed preset save/apply round-trip");
        sb.AppendLine($"Per-save: {report.PerSaveMods} mod(s) tested as per-campaign settings");
        sb.AppendLine($"Visibility: {report.VisibilityMissing} enabled-with-code mod folder(s) NOT in registry");
        sb.AppendLine($"Duplicate DLLs: {report.DuplicateGroups} watched library(ies) shipped in 2+ mods");
        sb.AppendLine();

        AppendEnvironment(sb);
        AppendPatchShieldStatus(sb);
        AppendSaveShieldStatus(sb);
        AppendInstalledVsEnabled(sb);
        AppendRuntimeLogTriage(sb);
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
                           && m.CancelPassed
                           && m.UiLayerPassed
                           && m.PresetPassed;
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
                       && m.CancelPassed
                       && m.UiLayerPassed
                       && m.PresetPassed;
            if (errorsOnly && isPass) continue;
            anyModFail |= !isPass;

            string status = m.FatalError != null ? $"FATAL: {m.FatalError}" : (isPass ? "PASS" : "FAIL");
            sb.AppendLine($"[{status}] {m.ModId} ({m.ModDisplayName}){(m.IsPerSave ? "  [per-save]" : "")}");
            sb.AppendLine($"    props:  {m.Properties.Count(p => p.RoundTripPassed)}/{m.Properties.Count} passed");
            sb.AppendLine($"    done:   {(m.DonePassed ? "PASS" : "FAIL")}");
            sb.AppendLine($"    cancel: {(m.CancelPassed ? "PASS" : "FAIL")}");
            sb.AppendLine($"    ui:     {(m.UiLayerPassed ? "PASS" : "FAIL")}");
            sb.AppendLine($"    preset: {(m.PresetPassed ? "PASS" : "FAIL")}");
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

        // v0.6: auto-disable diagnostics. Surfaces the runtime detection
        // state so users sending in a Self-Test can show us at a glance
        // which mods loaded clean this session, which BetaDeps auto-disabled
        // and why, and which suspects got skipped (content-only, stale, etc.).
        AppendAutoDisableDiagnostics(sb);

        sb.AppendLine("===== END SELF-TEST REPORT =====");
        return sb.ToString();
    }

    /// <summary>
    /// Reads the three runtime-detection files written by IncompatibleModDetector
    /// (last-good-modlist.txt, betadeps-disabled-mods.log, incompatible-mods.log)
    /// and appends a clean summary to the Self-Test report.
    /// </summary>
    private static void AppendAutoDisableDiagnostics(StringBuilder sb)
    {
        try
        {
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rtPath);
            if (string.IsNullOrEmpty(dir)) return;

            sb.AppendLine();
            sb.AppendLine("--- Auto-disable diagnostics (v0.6 runtime detection) ---");

            // last-good-modlist.txt: every mod that loaded clean to main menu
            // this session. The baseline against which next session's
            // crash-recovery diffs.
            var lastGoodPath = System.IO.Path.Combine(dir, "last-good-modlist.txt");
            if (System.IO.File.Exists(lastGoodPath))
            {
                var lines = System.IO.File.ReadAllLines(lastGoodPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                    .ToList();
                sb.AppendLine($"Mods that loaded cleanly this session ({lines.Count}):");
                if (lines.Count == 0)
                {
                    sb.AppendLine("  (none recorded yet -- this is the first successful boot, baseline establishing now)");
                }
                else
                {
                    foreach (var l in lines)
                        sb.AppendLine($"  GOOD: {l}");
                }
            }
            else
            {
                sb.AppendLine("Mods that loaded cleanly this session: (no baseline file yet — first BetaDeps run, or boot never reached main menu)");
            }

            // betadeps-disabled-mods.log: append-only history of every mod
            // BetaDeps has auto-disabled, with reason. Most recent entries
            // first (we tail the last 20 lines so the report doesn't bloat
            // for users with months of history).
            sb.AppendLine();
            var disabledPath = System.IO.Path.Combine(dir, "betadeps-disabled-mods.log");
            if (System.IO.File.Exists(disabledPath))
            {
                var lines = System.IO.File.ReadAllLines(disabledPath)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                int show = Math.Min(20, lines.Count);
                sb.AppendLine($"BetaDeps auto-disable history (last {show} of {lines.Count} entries):");
                foreach (var l in lines.Skip(Math.Max(0, lines.Count - show)))
                    sb.AppendLine($"  DISABLED: {l}");
            }
            else
            {
                sb.AppendLine("BetaDeps auto-disable history: (no disables ever recorded — no incompatible mods detected)");
            }

            // incompatible-mods.log: latest post-load scan of mods that were
            // enabled but failed to construct. Useful for users to see WHICH
            // of their mods didn't survive the load.
            sb.AppendLine();
            var incompatPath = System.IO.Path.Combine(dir, "incompatible-mods.log");
            if (System.IO.File.Exists(incompatPath))
            {
                var content = System.IO.File.ReadAllText(incompatPath);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    sb.AppendLine("Incompatible-mods scan (mods enabled in launcher but didn't load):");
                    foreach (var l in content.Split('\n'))
                    {
                        var trimmed = l.TrimEnd('\r');
                        if (!string.IsNullOrWhiteSpace(trimmed))
                            sb.AppendLine($"  {trimmed}");
                    }
                }
                else
                {
                    sb.AppendLine("Incompatible-mods scan: (no entries — all enabled mods loaded successfully)");
                }
            }
            else
            {
                sb.AppendLine("Incompatible-mods scan: (no scan file written this session)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"--- Auto-disable diagnostics: skipped, threw {ex.GetType().Name}: {ex.Message} ---");
        }
    }

    // --------------------------------------------------------------------
    // v0.7.2 selftest enhancements: environment, patchshield status,
    // installed-vs-enabled mod list. Triggered from BuildReportText so
    // every selftest.log uploaded to github carries this context.
    // --------------------------------------------------------------------

    private static void AppendEnvironment(StringBuilder sb)
    {
        sb.AppendLine("--- Environment ---");
        try
        {
            // BetaDeps version: pull from the Foundation assembly we know
            // was loaded (cleaner than reflecting on this MCM assembly).
            var betaDepsVersion = "(unknown)";
            try
            {
                var fdn = typeof(BetaDeps.Foundation.DiagLog).Assembly.GetName();
                betaDepsVersion = $"v{fdn.Version} ({fdn.Name})";
            }
            catch { }
            sb.AppendLine($"  BetaDeps:     {betaDepsVersion}");

            // Bannerlord game version + branch. VersionProbe is cached.
            try
            {
                sb.AppendLine($"  Bannerlord:   v{BetaDeps.Foundation.VersionProbe.Major}.{BetaDeps.Foundation.VersionProbe.Minor} (branch: {BetaDeps.Foundation.VersionProbe.Branch})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  Bannerlord:   (probe failed: {ex.GetType().Name})");
            }

            // Install path (where the runtime.log lives is next to BetaDeps).
            try
            {
                sb.AppendLine($"  Install dir:  {System.IO.Path.GetDirectoryName(BetaDeps.Foundation.RuntimeLog.Path)}");
            }
            catch { }

            sb.AppendLine($"  Date/time:    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (environment probe threw: {ex.GetType().Name}: {ex.Message})");
        }
        sb.AppendLine();
    }

    private static void AppendPatchShieldStatus(StringBuilder sb)
    {
        sb.AppendLine("--- PatchShield status ---");
        try
        {
            bool disabled = BetaDeps.Foundation.PatchShield.IsDisabled();
            sb.AppendLine($"  Enabled:                {(disabled ? "NO (opt-out flag present)" : "yes")}");
            sb.AppendLine($"  Methods shielded:       {BetaDeps.Foundation.PatchShield.ShieldedCount}");
            sb.AppendLine($"  Prefixes auto-unpatched: {BetaDeps.Foundation.PatchShield.UnpatchedCount}");
            sb.AppendLine($"  Swallowed exceptions:");
            sb.AppendLine($"    MissingMethodException: {BetaDeps.Foundation.PatchShield.SwallowedMissingMethod}");
            sb.AppendLine($"    MissingFieldException:  {BetaDeps.Foundation.PatchShield.SwallowedMissingField}");
            sb.AppendLine($"    TypeLoadException:      {BetaDeps.Foundation.PatchShield.SwallowedTypeLoad}");
            sb.AppendLine($"    Total swallowed:        {BetaDeps.Foundation.PatchShield.SwallowedTotal}");
            if (BetaDeps.Foundation.PatchShield.SwallowedTotal > 0)
            {
                sb.AppendLine($"  NOTE: every swallow = a consumer-mod patch that would have crashed your game.");
                sb.AppendLine($"        See runtime.log [BetaDeps.PatchShield] lines for the specific methods.");
            }

            // v4 #6: owners (Harmony IDs) whose patches got unpatched. Mod
            // authors typically use their mod's namespace as the Harmony ID,
            // so this is usually mod-identifiable on its own.
            var ownerMap = BetaDeps.Foundation.PatchShield.UnpatchedOwnerCounts;
            if (ownerMap.Count > 0)
            {
                sb.AppendLine($"  Unpatched owners ({ownerMap.Count}):");
                foreach (var kv in ownerMap.OrderByDescending(k => k.Value))
                {
                    sb.AppendLine($"    {kv.Value,4}  {kv.Key}");
                }
                sb.AppendLine($"        (each line = one Harmony owner ID and how many of its patches PatchShield unpatched this session.");
                sb.AppendLine($"         The owner ID is typically the mod's namespace, so each row points at a mod needing an update.)");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (PatchShield probe threw: {ex.GetType().Name}: {ex.Message})");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Embed SaveShield activity in selftest.log so mod authors can read the
    /// full diagnostic blocks (SAVE-LOAD / MISSION-INIT FAILURE) without
    /// asking the user to also share runtime.log. Header carries counters;
    /// body extracts every SaveShield === block written this session, with
    /// the most-recent N blocks rendered inline (older blocks are
    /// summarised by CULPRIT line only to keep the report compact).
    /// </summary>
    private static void AppendSaveShieldStatus(StringBuilder sb)
    {
        sb.AppendLine("--- SaveShield status ---");
        try
        {
            sb.AppendLine($"  Methods shielded:        {BetaDeps.Foundation.SaveShield.ShieldedCount}");
            sb.AppendLine($"  Duplicate-key hits:      {BetaDeps.Foundation.SaveShield.DuplicateKeyHits}");
            sb.AppendLine($"  Other load failures:     {BetaDeps.Foundation.SaveShield.OtherFailureHits}");
            sb.AppendLine($"  Total caught:            {BetaDeps.Foundation.SaveShield.DuplicateKeyHits + BetaDeps.Foundation.SaveShield.OtherFailureHits}");
            sb.AppendLine($"  Swallow-mode:            {(BetaDeps.Foundation.SaveShield.IsSwallowEnabled() ? "ENABLED (default in v0.7.3+)" : "disabled (saveshield-swallow-disabled.flag present)")}");
            sb.AppendLine($"  Exceptions swallowed:    {BetaDeps.Foundation.SaveShield.SwallowedCount}");
            if (BetaDeps.Foundation.SaveShield.SwallowedCount > 0)
            {
                sb.AppendLine($"  NOTE: each swallow = a mod's save-load or mission-init handler that would have crashed");
                sb.AppendLine($"        the game but was instead logged + dropped. The mod's specific feature did not");
                sb.AppendLine($"        fire at that call site. See FAILURE records below for the affected mods.");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (SaveShield probe threw: {ex.GetType().Name}: {ex.Message})");
        }
        sb.AppendLine();

        // v4: prefer the structured ring buffer; fall back to scanning
        // runtime.log if it's empty (e.g. failure fired before MCM loaded).
        try
        {
            var records = BetaDeps.Foundation.SaveShield.RecentFailures;
            if (records.Count > 0)
            {
                sb.AppendLine($"--- SaveShield FAILURE records ({records.Count} this session, newest first) ---");
                sb.AppendLine();
                sb.AppendLine("Mod authors: each record below names the CULPRIT assembly + current API surface +");
                sb.AppendLine("the culprit mod's manifest. That's almost always the mod that needs an update.");
                sb.AppendLine();

                const int RenderInFullN = 5;
                int renderUpTo = System.Math.Min(RenderInFullN, records.Count);
                for (int i = 0; i < renderUpTo; i++)
                {
                    sb.AppendLine($"--- record {i + 1} of {records.Count} (full) ---");
                    sb.AppendLine(records[i].ToLogBlock());
                    sb.AppendLine();
                }
                if (records.Count > RenderInFullN)
                {
                    sb.AppendLine($"  Older records (CULPRIT summary only, {records.Count - RenderInFullN} more):");
                    for (int i = RenderInFullN; i < records.Count; i++)
                    {
                        var r = records[i];
                        sb.AppendLine($"    [{i + 1}] {r.Category}: {r.CulpritAssembly} -- {r.ExceptionType} in {r.OwnerType}.{r.OwnerMethod}");
                    }
                    sb.AppendLine();
                }

                // Also drop a GitHub-ready markdown snippet of the most recent
                // failure inside selftest.log so the user can copy/paste it
                // straight into a mod's issue tracker without using the
                // Send-to-GitHub button.
                sb.AppendLine("--- GitHub issue snippet (most recent failure) ---");
                sb.AppendLine("(copy-paste the block below into the mod's bug tracker)");
                sb.AppendLine();
                sb.AppendLine("```markdown");
                sb.AppendLine(records[0].ToMarkdownSnippet());
                sb.AppendLine("```");
                sb.AppendLine();

                sb.AppendLine();
                return;
            }

            // Fallback: pull blocks from runtime.log (legacy path).
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            if (string.IsNullOrEmpty(rtPath) || !System.IO.File.Exists(rtPath))
            {
                sb.AppendLine("  (no SaveShield records this session -- nothing crashed save-load or mission-init)");
                sb.AppendLine();
                return;
            }
            var blocks = ExtractSaveShieldBlocks(rtPath);
            if (blocks.Count == 0)
            {
                sb.AppendLine("  (no SaveShield records this session -- nothing crashed save-load or mission-init)");
                sb.AppendLine();
                return;
            }
            sb.AppendLine($"--- SaveShield FAILURE blocks ({blocks.Count} from runtime.log) ---");
            for (int i = 0; i < blocks.Count; i++)
            {
                sb.AppendLine($"--- block {i + 1} ---");
                foreach (var line in blocks[i]) sb.AppendLine(line);
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (SaveShield record extraction threw: {ex.GetType().Name}: {ex.Message})");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Stream runtime.log and pull out each [BetaDeps.SaveShield] FAILURE
    /// block (delimited by the "===" lines SaveShield emits). Returns each
    /// block as a list of cleaned-up lines (timestamp/tag prefix stripped so
    /// the embedded output reads naturally inside selftest.log).
    /// </summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<string>> ExtractSaveShieldBlocks(string runtimeLogPath)
    {
        var blocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
        const string Tag = "[BetaDeps.SaveShield]";
        const string Delim = "========================================================";
        try
        {
            using var fs = new System.IO.FileStream(runtimeLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            using var sr = new System.IO.StreamReader(fs);

            System.Collections.Generic.List<string>? current = null;
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                int tagPos = line.IndexOf(Tag, System.StringComparison.Ordinal);
                if (tagPos < 0) continue;

                // Strip "[timestamp] [Tnn] [BetaDeps.SaveShield] " prefix.
                var content = line.Substring(tagPos + Tag.Length).TrimStart();

                if (content.StartsWith(Delim, System.StringComparison.Ordinal))
                {
                    if (current == null)
                    {
                        // Open marker.
                        current = new System.Collections.Generic.List<string>();
                    }
                    else
                    {
                        // Close marker -- block complete.
                        blocks.Add(current);
                        current = null;
                    }
                    continue;
                }

                if (current != null)
                {
                    current.Add("  " + content);
                }
            }

            // If runtime.log ended mid-block (crash before close marker),
            // capture it anyway.
            if (current != null && current.Count > 0)
            {
                blocks.Add(current);
            }
        }
        catch
        {
            // Swallow -- partial extraction is fine; we just won't show all blocks.
        }
        return blocks;
    }

    /// <summary>
    /// v4 #5: build a single JSON document with the SaveShield ring buffer +
    /// PatchShield counters + environment + brief test summary. Hand-built
    /// (no Newtonsoft dependency from McmSelfTest) so the document stays
    /// reproducible without extra package refs.
    /// </summary>
    private static string BuildSelftestJson(Report report)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append($"\"schema\":\"betadeps.selftest\",\"version\":1,");
        sb.Append($"\"generated_at\":\"{System.DateTime.UtcNow:o}\",");

        // Environment
        try
        {
            sb.Append("\"environment\":{");
            // Pull from Foundation's assembly so this matches the BetaDeps
            // version line in selftest.log (which uses Foundation, not MCM).
            sb.Append($"\"betadeps_version\":\"{JsonEsc(typeof(BetaDeps.Foundation.DiagLog).Assembly.GetName().Version?.ToString())}\",");
            sb.Append($"\"bannerlord_branch\":\"{JsonEsc(BetaDeps.Foundation.VersionProbe.Branch.ToString())}\",");
            sb.Append($"\"bannerlord_version\":\"{BetaDeps.Foundation.VersionProbe.Major}.{BetaDeps.Foundation.VersionProbe.Minor}\"");
            sb.Append("},");
        }
        catch { sb.Append("\"environment\":null,"); }

        // PatchShield counters
        try
        {
            sb.Append("\"patchshield\":{");
            sb.Append($"\"enabled\":{(BetaDeps.Foundation.PatchShield.IsDisabled() ? "false" : "true")},");
            sb.Append($"\"shielded_count\":{BetaDeps.Foundation.PatchShield.ShieldedCount},");
            sb.Append($"\"unpatched_count\":{BetaDeps.Foundation.PatchShield.UnpatchedCount},");
            sb.Append($"\"swallowed_missing_method\":{BetaDeps.Foundation.PatchShield.SwallowedMissingMethod},");
            sb.Append($"\"swallowed_missing_field\":{BetaDeps.Foundation.PatchShield.SwallowedMissingField},");
            sb.Append($"\"swallowed_type_load\":{BetaDeps.Foundation.PatchShield.SwallowedTypeLoad},");
            sb.Append($"\"swallowed_total\":{BetaDeps.Foundation.PatchShield.SwallowedTotal}");
            sb.Append("},");
        }
        catch { sb.Append("\"patchshield\":null,"); }

        // SaveShield: counters + every recent FailureRecord
        try
        {
            sb.Append("\"saveshield\":{");
            sb.Append($"\"shielded_count\":{BetaDeps.Foundation.SaveShield.ShieldedCount},");
            sb.Append($"\"duplicate_key_hits\":{BetaDeps.Foundation.SaveShield.DuplicateKeyHits},");
            sb.Append($"\"other_failure_hits\":{BetaDeps.Foundation.SaveShield.OtherFailureHits},");
            sb.Append($"\"swallow_enabled\":{(BetaDeps.Foundation.SaveShield.IsSwallowEnabled() ? "true" : "false")},");
            sb.Append($"\"swallowed_count\":{BetaDeps.Foundation.SaveShield.SwallowedCount},");
            sb.Append("\"recent_failures\":[");
            var records = BetaDeps.Foundation.SaveShield.RecentFailures;
            for (int i = 0; i < records.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(records[i].ToJsonObject());
            }
            sb.Append("]");
            sb.Append("},");
        }
        catch { sb.Append("\"saveshield\":null,"); }

        // Test headline
        try
        {
            sb.Append("\"test_summary\":{");
            sb.Append($"\"mods_count\":{report.Mods.Count},");
            sb.Append($"\"tested_mods\":{report.TestedMods},");
            sb.Append($"\"properties_passed\":{report.PassedProperties},");
            sb.Append($"\"properties_total\":{report.TotalProperties},");
            sb.Append($"\"done_passed\":{report.PassedDone},");
            sb.Append($"\"cancel_passed\":{report.PassedCancel},");
            sb.Append($"\"duplicate_dll_groups\":{report.DuplicateGroups}");
            sb.Append('}');
        }
        catch { sb.Append("\"test_summary\":null"); }

        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s!.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string FindCulpritInBlock(System.Collections.Generic.List<string> block)
    {
        foreach (var l in block)
        {
            var t = l.TrimStart();
            if (t.StartsWith("CULPRIT:", System.StringComparison.Ordinal))
            {
                return t.Substring("CULPRIT:".Length).Trim();
            }
        }
        return "(no CULPRIT line in block)";
    }

    private static string FindCategoryInBlock(System.Collections.Generic.List<string> block)
    {
        foreach (var l in block)
        {
            var t = l.TrimStart();
            // First line of every block is "<CATEGORY> FAILURE in <method>".
            int idx = t.IndexOf(" FAILURE in ", System.StringComparison.Ordinal);
            if (idx > 0) return t.Substring(0, idx);
        }
        return "FAILURE";
    }

    /// <summary>
    /// Scan runtime.log and emit a "Runtime log triage" section listing the
    /// counts and unique signatures of FirstChanceException entries plus
    /// any WARNING lines BetaDeps logged. This means the user only has to
    /// upload selftest.log -- everything that's actionable from runtime.log
    /// is already summarised here.
    /// </summary>
    private static void AppendRuntimeLogTriage(StringBuilder sb)
    {
        sb.AppendLine("--- Runtime log triage (auto-extracted) ---");
        try
        {
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            if (string.IsNullOrEmpty(rtPath) || !System.IO.File.Exists(rtPath))
            {
                sb.AppendLine("  (runtime.log not found at expected path)");
                return;
            }
            var fi = new System.IO.FileInfo(rtPath);
            sb.AppendLine($"  runtime.log: {rtPath}");
            sb.AppendLine($"  size: {fi.Length:N0} bytes");

            // Scan the file line-by-line. On heavy modlists we've seen 18k+
            // lines, but each is short. Use ReadLines (streaming) not
            // ReadAllLines so peak memory stays bounded.
            int missingMethodCount = 0;
            int missingFieldCount  = 0;
            int typeLoadCount      = 0;
            int warningCount       = 0;
            int caughtCount        = 0;
            var missingMethodSigs = new Dictionary<string, int>(StringComparer.Ordinal);
            var missingFieldSigs  = new Dictionary<string, int>(StringComparer.Ordinal);
            var typeLoadSigs      = new Dictionary<string, int>(StringComparer.Ordinal);
            var warningLines      = new List<string>();
            const int MaxWarnings = 25;

            using (var s = new System.IO.FileStream(rtPath, System.IO.FileMode.Open,
                                                    System.IO.FileAccess.Read,
                                                    System.IO.FileShare.ReadWrite))
            using (var r = new System.IO.StreamReader(s))
            {
                string? line;
                while ((line = r.ReadLine()) != null)
                {
                    // FirstChanceException dispatcher logs these in a
                    // consistent format. Lift the "Method not found: '..'" /
                    // "Field not found: '..'" / "Could not load type '..'"
                    // sub-string for the signature.
                    if (line.IndexOf("MissingMethodException", StringComparison.Ordinal) >= 0)
                    {
                        missingMethodCount++;
                        var sig = ExtractBetween(line, "Method not found: '", "'");
                        if (sig != null) missingMethodSigs[sig] = missingMethodSigs.TryGetValue(sig, out var c) ? c + 1 : 1;
                    }
                    else if (line.IndexOf("MissingFieldException", StringComparison.Ordinal) >= 0)
                    {
                        missingFieldCount++;
                        var sig = ExtractBetween(line, "Field not found: '", "'");
                        if (sig != null) missingFieldSigs[sig] = missingFieldSigs.TryGetValue(sig, out var c) ? c + 1 : 1;
                    }
                    else if (line.IndexOf("TypeLoadException", StringComparison.Ordinal) >= 0)
                    {
                        typeLoadCount++;
                        var sig = ExtractBetween(line, "Could not load type '", "'");
                        if (sig != null) typeLoadSigs[sig] = typeLoadSigs.TryGetValue(sig, out var c) ? c + 1 : 1;
                    }

                    if (line.IndexOf("WARNING:", StringComparison.Ordinal) >= 0)
                    {
                        warningCount++;
                        if (warningLines.Count < MaxWarnings) warningLines.Add(line.Trim());
                    }
                    if (line.IndexOf("LogCaught", StringComparison.Ordinal) >= 0
                        || line.IndexOf("caught: ", StringComparison.Ordinal) >= 0)
                    {
                        caughtCount++;
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine($"  Exception event totals:");
            sb.AppendLine($"    MissingMethodException: {missingMethodCount}");
            sb.AppendLine($"    MissingFieldException:  {missingFieldCount}");
            sb.AppendLine($"    TypeLoadException:      {typeLoadCount}");
            sb.AppendLine($"    WARNING lines:          {warningCount}");
            sb.AppendLine($"    LogCaught entries:      {caughtCount}");

            DumpSigDict(sb, "Missing methods (top by hit count)", missingMethodSigs, 20);
            DumpSigDict(sb, "Missing fields (top by hit count)",  missingFieldSigs,  10);
            DumpSigDict(sb, "Unloadable types (top by hit count)", typeLoadSigs,    20);

            if (warningLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  WARNING samples (first {warningLines.Count} of {warningCount}):");
                foreach (var w in warningLines) sb.AppendLine($"    {w}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (runtime-log triage threw: {ex.GetType().Name}: {ex.Message})");
        }
        sb.AppendLine();
    }

    private static string? ExtractBetween(string s, string start, string end)
    {
        int i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return null;
        i += start.Length;
        int j = s.IndexOf(end, i, StringComparison.Ordinal);
        if (j < 0) return null;
        return s.Substring(i, j - i);
    }

    private static void DumpSigDict(StringBuilder sb, string heading, Dictionary<string, int> dict, int maxRows)
    {
        if (dict.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"  {heading} ({dict.Count} unique):");
        int shown = 0;
        foreach (var kv in dict.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (shown >= maxRows) { sb.AppendLine($"    ... +{dict.Count - shown} more"); break; }
            sb.AppendLine($"    [{kv.Value,5}x] {kv.Key}");
            shown++;
        }
    }

    private static void AppendInstalledVsEnabled(StringBuilder sb)
    {
        sb.AppendLine("--- Installed vs enabled mods ---");
        try
        {
            // Modules root = parent of the BetaDeps folder where runtime.log lives.
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            var betaDepsDir = System.IO.Path.GetDirectoryName(rtPath);
            var modulesRoot = System.IO.Path.GetDirectoryName(betaDepsDir);
            if (string.IsNullOrEmpty(modulesRoot) || !System.IO.Directory.Exists(modulesRoot))
            {
                sb.AppendLine($"  (modules root not found)");
                return;
            }

            var installed = System.IO.Directory.EnumerateDirectories(modulesRoot)
                .Select(System.IO.Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Activated mods from LauncherData.xml -- IncompatibleModDetector
            // already knows how to read this.
            HashSet<string>? enabled = null;
            try
            {
                enabled = BetaDeps.Foundation.IncompatibleModDetector.GetEnabledModsFromLauncherData();
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  (LauncherData.xml read failed: {ex.GetType().Name}: {ex.Message})");
            }

            sb.AppendLine($"  Installed on disk: {installed.Count}");
            if (enabled != null) sb.AppendLine($"  Enabled in launcher: {enabled.Count}");
            sb.AppendLine();
            sb.AppendLine($"  Per-mod state: [E]=enabled in launcher, [D]=installed but disabled, [?]=launcher state unknown");
            foreach (var name in installed)
            {
                var marker = enabled == null ? "?"
                           : (enabled.Contains(name!) ? "E" : "D");
                sb.AppendLine($"    [{marker}] {name}");
            }

            // Highlight any mods enabled in the launcher but missing on disk
            // (a frequent confusion source -- "I disabled this but Vortex
            // still has it in the LauncherData").
            if (enabled != null)
            {
                var phantoms = enabled.Where(e => !installed.Contains(e!)).ToList();
                if (phantoms.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  Phantom entries (enabled in launcher but folder missing on disk):");
                    foreach (var p in phantoms.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                        sb.AppendLine($"    {p}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (installed-vs-enabled probe threw: {ex.GetType().Name}: {ex.Message})");
        }
    }
}
