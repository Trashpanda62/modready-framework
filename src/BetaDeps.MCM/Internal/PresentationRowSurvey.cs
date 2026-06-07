// BetaDeps.MCM -- PresentationRowSurvey
//
// Slice 1 of the "every mod on one page" plan. Diagnostic-only: walks every
// registered settings class the way OptionsVMMixin.SelectMod would (group
// header divider when the group name changes + one row per property), counts
// rows, and writes a per-mod report to
//   Modules\BetaDeps\presentation-row-survey.log
//
// Gated on a flag file Modules\BetaDeps\presentation-survey.flag — the same
// flag-file pattern used elsewhere (patchshield-disabled.flag, etc.). Fires
// once after SettingsRegistry.DiscoverAll runs in MCMSubModule. Output is the
// single number we need to size SlotCount against in slice 3 (current
// SlotCount = 20, ROT-class mods currently paginate).
//
// Reads no live state and writes no settings. Pure read-only walk over the
// registry; safe to ship gated even if a future build forgets to delete the
// flag-file (the worst case is one .log file written per launch).
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using BetaDeps.Foundation;

namespace MCM.Internal;

internal static class PresentationRowSurvey
{
    private const string Tag = "MCM.PresentationSurvey";
    private const string FlagFileName = "presentation-survey.flag";
    private const string OutputFileName = "presentation-row-survey.log";

    private static bool _ran;

    /// <summary>
    /// Runs the survey once if the flag file is present. Subsequent calls
    /// are no-ops within the same session.
    /// </summary>
    public static void RunIfRequested()
    {
        if (_ran) return;
        try
        {
            var dir = ResolveBetaDepsModuleDir();
            if (string.IsNullOrEmpty(dir)) return;
            var flagPath = Path.Combine(dir!, FlagFileName);
            if (!File.Exists(flagPath))
            {
                // Default off; the survey is a one-shot diagnostic tool.
                return;
            }
            _ran = true;
            var report = BuildReport();
            var outputPath = Path.Combine(dir!, OutputFileName);
            File.WriteAllText(outputPath, report);
            DiagLog.Log(Tag, $"presentation-row survey written to {outputPath}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "RunIfRequested", ex);
        }
    }

    private static string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# BetaDeps presentation-row survey");
        sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("# One row per registered settings class.");
        sb.AppendLine("# rows = (named-group header dividers) + (one per property)");
        sb.AppendLine("# This is the number of slots OptionsVMMixin._presentation would");
        sb.AppendLine("# need to render the mod on a single page (no pagination).");
        sb.AppendLine();
        sb.AppendLine("rows\tgroups\tprops\tid\tdisplay_name");

        var rowCounts = new List<int>();
        IReadOnlyCollection<RegisteredSettings>? all = null;
        try { all = SettingsRegistry.All; }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "SettingsRegistry.All", ex); }

        if (all == null)
        {
            sb.AppendLine("# (registry returned null)");
            return sb.ToString();
        }

        foreach (var entry in all)
        {
            int rows = 0, groups = 0, props = 0;
            string id = "(unknown)";
            string display = "(unknown)";
            try
            {
                id = entry.Id ?? "(null-id)";
                display = entry.DisplayName ?? "";
                // Construct a transient SettingsVM the same way SelectMod does
                // so we walk the exact group/property surface the panel renders.
                var vm = new MCM.UI.GUI.ViewModels.SettingsVM(entry.Instance);
                string lastGroup = string.Empty;
                bool lastGroupSeen = false;
                foreach (var g in vm.SettingPropertyGroups)
                {
                    var groupName = TextHelper.StripLocalizationKeys(g.GroupName ?? string.Empty);
                    if (!string.IsNullOrEmpty(groupName) &&
                        (!lastGroupSeen || !string.Equals(groupName, lastGroup, StringComparison.Ordinal)))
                    {
                        rows++;
                        groups++;
                        lastGroup = groupName;
                        lastGroupSeen = true;
                    }
                    foreach (var p in g.SettingProperties)
                    {
                        rows++;
                        props++;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"BuildReport/{id}", ex);
                sb.AppendLine($"# ERROR surveying '{id}': {ex.GetType().Name}: {ex.Message}");
                continue;
            }
            rowCounts.Add(rows);
            sb.AppendLine($"{rows}\t{groups}\t{props}\t{id}\t{display}");
        }

        sb.AppendLine();
        sb.AppendLine("# ----- Summary -----");
        if (rowCounts.Count == 0)
        {
            sb.AppendLine("# (no mods surveyed)");
        }
        else
        {
            rowCounts.Sort();
            int max = rowCounts[rowCounts.Count - 1];
            int min = rowCounts[0];
            int median = rowCounts[rowCounts.Count / 2];
            int p95 = rowCounts[(int)Math.Min(rowCounts.Count - 1, Math.Floor(rowCounts.Count * 0.95))];
            sb.AppendLine($"# mods surveyed: {rowCounts.Count}");
            sb.AppendLine($"# row count -- min: {min}  median: {median}  p95: {p95}  MAX: {max}");
            sb.AppendLine($"# SlotCount currently: 20");
            sb.AppendLine($"# To fit every mod on one page, SlotCount would need to be at least: {max}");
        }

        return sb.ToString();
    }

    private static string? ResolveBetaDepsModuleDir()
    {
        try
        {
            // We're loaded as MCMv5.dll from Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\.
            // Walk up to Modules\ then into Modules\BetaDeps\.
            var ownPath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(ownPath)) return null;
            var binDir = Path.GetDirectoryName(ownPath);          // Win64_Shipping_Client
            var modBin = Path.GetDirectoryName(binDir);           // bin
            var thisModule = Path.GetDirectoryName(modBin);       // Bannerlord.MBOptionScreen
            var modulesRoot = Path.GetDirectoryName(thisModule);  // Modules
            if (string.IsNullOrEmpty(modulesRoot)) return null;
            return Path.Combine(modulesRoot!, "BetaDeps");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ResolveBetaDepsModuleDir", ex);
            return null;
        }
    }
}
