// BetaDeps Tactics Editor -- mod exporter.
//
// Given a TacticPlan + a target mod name, writes a complete Bannerlord
// mod folder at Modules\<TacticName>\ that other players can install
// like any Nexus mod:
//
//   Modules\<TacticName>\
//     SubModule.xml          -- declares TacticApplySubModule
//     tactic.json            -- the saved TacticPlan
//     bin\Win64_Shipping_Client\
//       BetaDeps.TacticsEditor.dll   -- shared by all tactic mods, just
//                                       copied in from the editor's own
//                                       install location at export time
//
// The exported mod has no code of its own. It just bundles the data
// (tactic.json) + a reference to BetaDeps.TacticsEditor's apply logic,
// which discovers tactic.json next to itself on battle start.
//
// Important constraint: the export target directory is Bannerlord's
// global Modules folder, which we resolve by walking up from the
// editor's own runtime directory. If that resolution fails (Bannerlord
// installed somewhere unusual), the exporter falls back to the user's
// Documents\Mount and Blade II Bannerlord\Mods\ scratch folder and
// tells the user to copy it manually.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.IO;
using System.Linq;
using System.Text;

using BetaDeps.Foundation;

namespace BetaDeps.TacticsEditor;

public static class TacticModExporter
{
    private const string Tag = "BetaDeps.TacticsEditor.Exporter";

    /// <summary>
    /// Sanitize a user-typed tactic name into a safe folder name.
    /// Strips path separators, control chars, and trims length.
    /// </summary>
    public static string SanitizeModName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "MyTactic";

        var invalid = Path.GetInvalidFileNameChars()
            .Concat(new[] { ' ', '.', ',', ';', ':', '/', '\\' })
            .ToArray();

        var sb = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            if (Array.IndexOf(invalid, ch) >= 0) sb.Append('_');
            else sb.Append(ch);
        }
        var name = sb.ToString();
        if (name.Length > 60) name = name.Substring(0, 60);
        if (string.IsNullOrWhiteSpace(name)) name = "MyTactic";
        return name;
    }

    /// <summary>
    /// Resolve Bannerlord's top-level Modules\ directory by walking up
    /// from the running BetaDeps assembly. Returns null if not found.
    /// </summary>
    private static string? ResolveModulesRoot()
    {
        try
        {
            var asm = typeof(TacticModExporter).Assembly.Location;
            if (string.IsNullOrEmpty(asm)) return null;

            var dir = Path.GetDirectoryName(asm);
            while (!string.IsNullOrEmpty(dir))
            {
                var name = Path.GetFileName(dir)!;
                if (string.Equals(name, "Modules", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ResolveModulesRoot", ex);
        }
        return null;
    }

    private static string FallbackExportRoot()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "Mount and Blade II Bannerlord", "Mods");
    }

    /// <summary>
    /// Build the SubModule.xml content for an exported tactic mod.
    /// The exported mod's only behavior is loading BetaDeps.TacticsEditor's
    /// TacticApplySubModule, which discovers tactic.json next to itself.
    /// </summary>
    private static string BuildSubModuleXml(string modId, string displayName, string author, string version)
    {
        // BetaDeps.TacticsEditor ships TacticApplySubModule that walks the
        // loaded modules and applies the tactic.json next to each one.
        // The exported mod just needs to be enabled; no DLL of its own.
        var safeName = System.Security.SecurityElement.Escape(displayName) ?? displayName;
        var safeAuthor = System.Security.SecurityElement.Escape(author) ?? author;
        var safeVersion = System.Security.SecurityElement.Escape(version) ?? version;

        return string.Join("\n", new[]
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>",
            "<Module>",
            $"  <Name value=\"{safeName}\" />",
            $"  <Id value=\"{modId}\" />",
            $"  <Version value=\"v{safeVersion}\" />",
            "  <DefaultModule value=\"false\" />",
            "  <ModuleCategory value=\"Singleplayer\" />",
            "  <ModuleType value=\"Community\" />",
            $"  <Author value=\"{safeAuthor}\" />",
            "  <DependedModules>",
            "    <DependedModule Id=\"BetaDeps\" />",
            "    <DependedModule Id=\"BetaDeps.TacticsEditor\" />",
            "  </DependedModules>",
            "  <SubModules>",
            "    <!-- The actual applier lives in BetaDeps.TacticsEditor; this exported",
            "         mod is data-only. The applier walks Modules\\ for tactic.json files",
            "         and binds each to its team on battle start. -->",
            "  </SubModules>",
            "</Module>",
            "",
        });
    }

    /// <summary>
    /// Result of an export attempt.
    /// </summary>
    // `init` accessors need an IsExternalInit polyfill on .NET Framework
    // 4.7.2; using plain setters keeps the type net472-clean.
    public sealed class Result
    {
        public bool Success { get; set; }
        public string? Path { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Export a TacticPlan as a complete drop-in mod.
    /// </summary>
    public static Result Export(TacticPlan plan, string modId)
    {
        if (plan == null)
            return new Result { Success = false, Message = "plan is null" };

        var safeId = SanitizeModName(string.IsNullOrWhiteSpace(modId) ? plan.Name : modId);

        var root = ResolveModulesRoot();
        var usedFallback = false;
        if (string.IsNullOrEmpty(root))
        {
            root = FallbackExportRoot();
            usedFallback = true;
        }

        try
        {
            var modDir = Path.Combine(root!, safeId);
            Directory.CreateDirectory(modDir);

            var jsonPath = Path.Combine(modDir, "tactic.json");
            if (!TacticPlanSerializer.WriteToFile(plan, jsonPath))
            {
                return new Result { Success = false, Message = $"failed to write tactic.json at {jsonPath}" };
            }

            var subModuleXml = BuildSubModuleXml(safeId, plan.Name, plan.Author, plan.Version);
            File.WriteAllText(Path.Combine(modDir, "SubModule.xml"), subModuleXml);

            DiagLog.Log(Tag,
                $"exported tactic '{plan.Name}' ({plan.Formations.Count} formations) to {modDir}" +
                (usedFallback ? " [fallback path -- user must copy to Modules\\ manually]" : string.Empty));

            return new Result
            {
                Success = true,
                Path = modDir,
                Message = usedFallback
                    ? $"Exported to fallback location {modDir}. Copy this folder into your Bannerlord Modules\\ folder, then enable it in the launcher."
                    : $"Exported to {modDir}. Enable '{safeId}' in the launcher next time you start the game.",
            };
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Export({safeId})", ex);
            return new Result { Success = false, Message = ex.Message };
        }
    }
}
