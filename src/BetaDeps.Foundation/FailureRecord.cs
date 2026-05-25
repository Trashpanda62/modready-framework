// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// FailureRecord: the structured form of a SaveShield-caught exception.
// Each call to SaveShield's finalizer creates one, populates it with
// everything we know about the throw (category, culprit mod, message,
// stack, current signatures, mod manifest, etc.), appends it to the
// recent-failures ring buffer, and also emits the human-readable text
// block to runtime.log.
//
// Consumers:
//   - DiagLog text block: the canonical historical format (preserved
//     for backwards compatibility with existing parsers).
//   - selftest.log: McmSelfTest reads the ring buffer to embed blocks
//     inline so mod authors don't need to also read runtime.log.
//   - GitHub issue body: OptionsVMMixin pulls the most-recent record's
//     ToMarkdownSnippet() to prefill bug reports.
//   - JSON sidecar: McmSelfTest serializes the ring buffer alongside
//     selftest.log for machine-readable tooling.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BetaDeps.Foundation;

public sealed class FailureRecord
{
    public DateTime When { get; set; } = DateTime.UtcNow;

    /// <summary>SAVE-LOAD / MISSION-INIT / FAILURE.</summary>
    public string Category { get; set; } = "FAILURE";

    public string OwnerType { get; set; } = "?";
    public string OwnerMethod { get; set; } = "?";

    public string ExceptionType { get; set; } = "?";
    public string Message { get; set; } = string.Empty;

    public string ThrowSiteType { get; set; } = string.Empty;
    public string ThrowSiteMethod { get; set; } = string.Empty;
    public string ExSource { get; set; } = string.Empty;

    public string CulpritAssembly { get; set; } = string.Empty;
    public string CulpritFrame { get; set; } = string.Empty;
    public string CulpritAssemblyPath { get; set; } = string.Empty;

    /// <summary>True if message matches the "same key" Dictionary.Add pattern.</summary>
    public bool IsDuplicateKey { get; set; }

    /// <summary>Raw stack-trace string from the caught exception.</summary>
    public string StackTraceRaw { get; set; } = string.Empty;

    /// <summary>Parsed System.Diagnostics.StackTrace frames.</summary>
    public List<string> ParsedFrames { get; } = new();

    /// <summary>v4 #7: stack frames at the point the finalizer fired (call chain that led TO the patched method).</summary>
    public List<string> FinalizerCallChain { get; } = new();

    /// <summary>v4 #1: current method overloads matching the missing-method message.</summary>
    public List<string> CurrentSignatures { get; } = new();

    /// <summary>v4 #2: CULPRIT mod's manifest info (SubModule.xml + assembly versions).</summary>
    public ModManifest? CulpritManifest { get; set; }

    /// <summary>v4 #8: TaleWorlds members the CULPRIT assembly imports (Cecil scan).</summary>
    public List<string> ImportMatches { get; } = new();

    /// <summary>v4 — first argument string (e.g. save name) for quick identification.</summary>
    public string FirstArgSummary { get; set; } = string.Empty;

    /// <summary>
    /// Render as the historical DiagLog text block. Preserves the original
    /// format consumed by selftest.log's runtime-scan path.
    /// </summary>
    public string ToLogBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================================");
        sb.AppendLine($"{Category} FAILURE in {OwnerType}.{OwnerMethod}");
        if (!string.IsNullOrEmpty(CulpritAssembly))
        {
            sb.AppendLine($"  CULPRIT:      {CulpritAssembly}");
            if (!string.IsNullOrEmpty(CulpritFrame))
                sb.AppendLine($"                ({CulpritFrame})");
        }
        sb.AppendLine($"  exception:    {ExceptionType}");
        sb.AppendLine($"  message:      {Message}");
        if (!string.IsNullOrEmpty(FirstArgSummary))
            sb.AppendLine($"  first arg:    {FirstArgSummary}");
        if (IsDuplicateKey)
            sb.AppendLine("  diagnosis:    Dictionary<TKey,TValue>.Add key-collision during deserialization.");

        if (!string.IsNullOrEmpty(ThrowSiteType) || !string.IsNullOrEmpty(ThrowSiteMethod))
            sb.AppendLine($"  throw site:   {ThrowSiteType}.{ThrowSiteMethod}");
        if (!string.IsNullOrEmpty(ExSource))
            sb.AppendLine($"  ex.Source:    {ExSource}");

        if (CurrentSignatures.Count > 0)
        {
            sb.AppendLine($"  current API:  ({CurrentSignatures.Count} overload(s) of the named method actually exist on the current build)");
            foreach (var s in CurrentSignatures)
                sb.AppendLine($"    {s}");
        }

        if (CulpritManifest != null)
        {
            sb.AppendLine($"  CULPRIT manifest:");
            foreach (var l in CulpritManifest.ToLines())
                sb.AppendLine($"    {l}");
        }

        if (ImportMatches.Count > 0)
        {
            sb.AppendLine($"  matching imports in CULPRIT DLL ({ImportMatches.Count}):");
            foreach (var s in ImportMatches.Take(15))
                sb.AppendLine($"    {s}");
            if (ImportMatches.Count > 15)
                sb.AppendLine($"    ({ImportMatches.Count - 15} more not shown)");
        }

        if (!string.IsNullOrEmpty(StackTraceRaw))
        {
            sb.AppendLine("  stack trace:");
            foreach (var raw in StackTraceRaw.Split('\n'))
            {
                var t = raw.TrimEnd();
                if (t.Length == 0) continue;
                sb.AppendLine($"    {t}");
            }
        }

        if (ParsedFrames.Count > 0)
        {
            sb.AppendLine("  parsed frames:");
            foreach (var f in ParsedFrames)
                sb.AppendLine($"    {f}");
        }

        if (FinalizerCallChain.Count > 0)
        {
            sb.AppendLine("  finalizer call chain (what called the patched method):");
            foreach (var f in FinalizerCallChain.Take(15))
                sb.AppendLine($"    {f}");
        }

        sb.AppendLine("========================================================");
        return sb.ToString();
    }

    /// <summary>
    /// Render as a GitHub-issue-friendly markdown block. Used by the
    /// "Send to GitHub" pre-fill button to embed the failure inline so
    /// the mod author sees CULPRIT + cause + current API on first scroll.
    /// </summary>
    public string ToMarkdownSnippet()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### {Category} failure — `{CulpritAssembly}`");
        sb.AppendLine();
        sb.AppendLine("| field | value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Category | `{Category}` |");
        sb.AppendLine($"| Caught in | `{OwnerType}.{OwnerMethod}` |");
        sb.AppendLine($"| Culprit mod | **{CulpritAssembly}** |");
        if (!string.IsNullOrEmpty(CulpritFrame)) sb.AppendLine($"| Culprit frame | `{Escape(CulpritFrame)}` |");
        sb.AppendLine($"| Exception | `{ExceptionType}` |");
        sb.AppendLine($"| Message | `{Escape(Message)}` |");
        if (!string.IsNullOrEmpty(ThrowSiteType))
            sb.AppendLine($"| Throw site | `{ThrowSiteType}.{ThrowSiteMethod}` |");
        if (!string.IsNullOrEmpty(FirstArgSummary))
            sb.AppendLine($"| First arg | `{Escape(FirstArgSummary)}` |");
        sb.AppendLine();

        if (CulpritManifest != null)
        {
            sb.AppendLine("**Culprit mod manifest:**");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var l in CulpritManifest.ToLines()) sb.AppendLine(l);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (CurrentSignatures.Count > 0)
        {
            sb.AppendLine($"**Current API surface for the named method ({CurrentSignatures.Count} overload(s) on this Bannerlord build):**");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            foreach (var s in CurrentSignatures) sb.AppendLine(s);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (ImportMatches.Count > 0)
        {
            sb.AppendLine($"**Matching imports in `{CulpritAssembly}` ({ImportMatches.Count}):**");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var s in ImportMatches.Take(10)) sb.AppendLine(s);
            if (ImportMatches.Count > 10) sb.AppendLine($"... ({ImportMatches.Count - 10} more)");
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("**Stack trace:**");
        sb.AppendLine();
        sb.AppendLine("```");
        if (!string.IsNullOrEmpty(StackTraceRaw))
        {
            foreach (var raw in StackTraceRaw.Split('\n'))
            {
                var t = raw.TrimEnd();
                if (t.Length > 0) sb.AppendLine(t);
            }
        }
        else
        {
            foreach (var f in ParsedFrames) sb.AppendLine(f);
        }
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Render as a single JSON object string. Hand-built (no Newtonsoft
    /// dependency on Foundation) so we can drop it into selftest.json
    /// without taking a JSON package reference. Compact format on one
    /// line for grep-ability.
    /// </summary>
    public string ToJsonObject()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"when\":\"{When:o}\",");
        sb.Append($"\"category\":\"{JsonEsc(Category)}\",");
        sb.Append($"\"owner\":\"{JsonEsc(OwnerType + "." + OwnerMethod)}\",");
        sb.Append($"\"exception\":\"{JsonEsc(ExceptionType)}\",");
        sb.Append($"\"message\":\"{JsonEsc(Message)}\",");
        sb.Append($"\"culprit\":\"{JsonEsc(CulpritAssembly)}\",");
        sb.Append($"\"culprit_frame\":\"{JsonEsc(CulpritFrame)}\",");
        sb.Append($"\"culprit_path\":\"{JsonEsc(CulpritAssemblyPath)}\",");
        sb.Append($"\"throw_site\":\"{JsonEsc(ThrowSiteType + "." + ThrowSiteMethod)}\",");
        sb.Append($"\"is_duplicate_key\":{(IsDuplicateKey ? "true" : "false")},");
        sb.Append($"\"first_arg\":\"{JsonEsc(FirstArgSummary)}\",");
        sb.Append($"\"current_signatures\":[{string.Join(",", CurrentSignatures.Select(s => "\"" + JsonEsc(s) + "\""))}],");
        sb.Append($"\"import_matches\":[{string.Join(",", ImportMatches.Select(s => "\"" + JsonEsc(s) + "\""))}],");
        sb.Append($"\"parsed_frames\":[{string.Join(",", ParsedFrames.Select(s => "\"" + JsonEsc(s) + "\""))}],");
        sb.Append($"\"finalizer_call_chain\":[{string.Join(",", FinalizerCallChain.Select(s => "\"" + JsonEsc(s) + "\""))}],");
        sb.Append($"\"manifest\":{(CulpritManifest != null ? CulpritManifest.ToJsonObject() : "null")}");
        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s!.Length);
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

    private static string Escape(string? s) => s == null ? string.Empty : s.Replace("|", "\\|");
}

/// <summary>
/// Snapshot of a culprit-mod's manifest: SubModule.xml header info plus
/// every TaleWorlds.*-version referenced by its main DLL.
/// </summary>
public sealed class ModManifest
{
    public string ModFolder { get; set; } = string.Empty;
    public string ModId { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string ModAuthor { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public string AssemblyLocation { get; set; } = string.Empty;
    public List<string> DependedModules { get; } = new();
    public List<string> TaleWorldsReferences { get; } = new();

    public IEnumerable<string> ToLines()
    {
        if (!string.IsNullOrEmpty(ModName))         yield return $"Name:               {ModName}";
        if (!string.IsNullOrEmpty(ModId))           yield return $"Id:                 {ModId}";
        if (!string.IsNullOrEmpty(ModVersion))      yield return $"Version (SubModule):{ModVersion}";
        if (!string.IsNullOrEmpty(ModAuthor))       yield return $"Author:             {ModAuthor}";
        if (!string.IsNullOrEmpty(AssemblyName))    yield return $"Assembly:           {AssemblyName}";
        if (!string.IsNullOrEmpty(AssemblyVersion)) yield return $"AssemblyVersion:    {AssemblyVersion}";
        if (!string.IsNullOrEmpty(AssemblyLocation)) yield return $"DLL on disk:        {AssemblyLocation}";
        if (DependedModules.Count > 0)
            yield return $"DependedModules:    {string.Join(", ", DependedModules)}";
        if (TaleWorldsReferences.Count > 0)
        {
            yield return $"TaleWorlds refs ({TaleWorldsReferences.Count}):";
            foreach (var r in TaleWorldsReferences)
                yield return $"  - {r}";
        }
    }

    public string ToJsonObject()
    {
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"folder\":\"{JsonEsc(ModFolder)}\",");
        sb.Append($"\"id\":\"{JsonEsc(ModId)}\",");
        sb.Append($"\"name\":\"{JsonEsc(ModName)}\",");
        sb.Append($"\"version\":\"{JsonEsc(ModVersion)}\",");
        sb.Append($"\"author\":\"{JsonEsc(ModAuthor)}\",");
        sb.Append($"\"assembly_name\":\"{JsonEsc(AssemblyName)}\",");
        sb.Append($"\"assembly_version\":\"{JsonEsc(AssemblyVersion)}\",");
        sb.Append($"\"assembly_location\":\"{JsonEsc(AssemblyLocation)}\",");
        sb.Append($"\"depended_modules\":[{string.Join(",", DependedModules.Select(s => "\"" + JsonEsc(s) + "\""))}],");
        sb.Append($"\"taleworlds_refs\":[{string.Join(",", TaleWorldsReferences.Select(s => "\"" + JsonEsc(s) + "\""))}]");
        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEsc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s!.Length);
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
}
