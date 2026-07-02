// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModConflictDetector -- ModReady v2.0 framework primitive #2.
//
// Surfaces the single most common silent-incompatibility in the Bannerlord
// modding ecosystem: two mods Harmony-patching the SAME engine method. The
// engine runs both, but the interaction is order- and kind-dependent and the
// player just sees "mod A and mod B don't work together" with no diagnostic.
//
// This walks Harmony's global patch registry (the same source PatchShield
// uses) and groups every patched method by the distinct THIRD-PARTY owners
// touching it. A method touched by >= 2 non-ModReady owners is a reported
// conflict, ranked by how dangerous the overlap is:
//
//   High   -- two+ owners with TRANSPILERS on one method. Transpilers rewrite
//             IL; stacking two independent rewrites of the same method body is
//             the classic hard-CTD / wrong-behavior collision.
//   Medium -- two+ owners with PREFIXES. A prefix can return false and skip the
//             original (and every later prefix), so prefix stacks silently
//             change which code runs.
//   Low    -- only postfix/finalizer overlap. Usually composes fine, but worth
//             listing so a player diagnosing a bug can see the full picture.
//
// ModReady's own owners are excluded by default: PatchShield attaches a
// finalizer to EVERY patched method, so counting it would mark the entire game
// as "in conflict". The detector is about conflicts BETWEEN consumer mods.
//
// Engine-free: depends only on System + HarmonyLib, so it runs (and is tested)
// off-engine.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using ModReady.Foundation;   // DiagLog

using HarmonyLib;

namespace ModReady.Framework
{
    /// <summary>Which Harmony patch kinds an owner applied to a method.</summary>
    [Flags]
    public enum PatchKinds
    {
        None = 0,
        Prefix = 1,
        Postfix = 2,
        Transpiler = 4,
        Finalizer = 8,
    }

    /// <summary>How dangerous a given method-level conflict is.</summary>
    public enum ConflictSeverity { Low = 0, Medium = 1, High = 2 }

    /// <summary>One mod's contribution to a contested method.</summary>
    public sealed class ConflictContributor
    {
        /// <summary>Harmony owner id (mod authors set this; usually a namespace).</summary>
        public string Owner { get; }
        /// <summary>Assembly the patch methods live in (the real mod DLL).</summary>
        public string Assembly { get; }
        /// <summary>Patch kinds this owner applied to the method.</summary>
        public PatchKinds Kinds { get; }
        /// <summary>Distinct Harmony priorities this owner used (highest first).</summary>
        public IReadOnlyList<int> Priorities { get; }

        public ConflictContributor(string owner, string assembly, PatchKinds kinds, IReadOnlyList<int> priorities)
        {
            Owner = owner;
            Assembly = assembly;
            Kinds = kinds;
            Priorities = priorities;
        }

        public override string ToString()
        {
            var kinds = Kinds == PatchKinds.None ? "none" : Kinds.ToString().Replace(", ", "+");
            var prio = Priorities.Count > 0 ? $" prio[{string.Join(",", Priorities)}]" : "";
            var asm = string.IsNullOrEmpty(Assembly) || Assembly == Owner ? "" : $" <{Assembly}>";
            return $"{Owner}{asm} ({kinds}){prio}";
        }
    }

    /// <summary>A single engine method patched by two or more third-party mods.</summary>
    public sealed class MethodConflict
    {
        public string TargetType { get; }
        public string TargetMethod { get; }
        /// <summary>Fully-qualified "Type::Method(args)" signature.</summary>
        public string TargetSignature { get; }
        public ConflictSeverity Severity { get; }
        public IReadOnlyList<ConflictContributor> Contributors { get; }

        public MethodConflict(string targetType, string targetMethod, string signature,
            ConflictSeverity severity, IReadOnlyList<ConflictContributor> contributors)
        {
            TargetType = targetType;
            TargetMethod = targetMethod;
            TargetSignature = signature;
            Severity = severity;
            Contributors = contributors;
        }

        public override string ToString()
        {
            var who = string.Join("  vs  ", Contributors.Select(c => c.ToString()));
            return $"[{Severity}] {TargetSignature}\n        {who}";
        }
    }

    public static class ModConflictDetector
    {
        private const string Tag = "ModReady.ConflictDetector";

        /// <summary>
        /// Scan Harmony's global registry for methods patched by two or more
        /// distinct owners. Excludes ModReady's own owners by default (its
        /// shield finalizers touch everything). Results are ordered most-severe
        /// first, then by signature for stable output.
        /// </summary>
        /// <param name="includeModReady">
        /// When true, ModReady owners are counted too (diagnostic use only).
        /// </param>
        public static IReadOnlyList<MethodConflict> Scan(bool includeModReady = false)
        {
            var conflicts = new List<MethodConflict>();
            IEnumerable<MethodBase> methods;
            try
            {
                methods = Harmony.GetAllPatchedMethods().ToList();
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, "GetAllPatchedMethods", ex);
                return conflicts;
            }

            foreach (var method in methods)
            {
                if (method == null) continue;
                MethodConflict? c;
                try { c = Evaluate(method, includeModReady); }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"evaluate {Describe(method)}", ex);
                    continue;
                }
                if (c != null) conflicts.Add(c);
            }

            conflicts.Sort((a, b) =>
            {
                int s = b.Severity.CompareTo(a.Severity);
                return s != 0 ? s : string.CompareOrdinal(a.TargetSignature, b.TargetSignature);
            });
            return conflicts;
        }

        private static MethodConflict? Evaluate(MethodBase method, bool includeModReady)
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null) return null;

            // owner -> (kinds, priorities, assembly)
            var byOwner = new Dictionary<string, (PatchKinds kinds, HashSet<int> prio, string asm)>(StringComparer.Ordinal);

            void Fold(IEnumerable<Patch>? patches, PatchKinds kind)
            {
                if (patches == null) return;
                foreach (var p in patches)
                {
                    var owner = p.owner ?? string.Empty;
                    if (string.IsNullOrEmpty(owner)) continue;
                    if (!includeModReady && IsModReadyOwner(owner, p)) continue;

                    if (!byOwner.TryGetValue(owner, out var agg))
                        agg = (PatchKinds.None, new HashSet<int>(), AssemblyOf(p));
                    agg.kinds |= kind;
                    agg.prio.Add(p.priority);
                    if (string.IsNullOrEmpty(agg.asm)) agg.asm = AssemblyOf(p);
                    byOwner[owner] = agg;
                }
            }

            Fold(info.Prefixes, PatchKinds.Prefix);
            Fold(info.Postfixes, PatchKinds.Postfix);
            Fold(info.Transpilers, PatchKinds.Transpiler);
            Fold(info.Finalizers, PatchKinds.Finalizer);

            // A conflict requires two or more DISTINCT owners on the same method.
            if (byOwner.Count < 2) return null;

            var contributors = byOwner
                .Select(kv => new ConflictContributor(
                    kv.Key, kv.Value.asm, kv.Value.kinds,
                    kv.Value.prio.OrderByDescending(x => x).ToArray()))
                .OrderBy(c => c.Owner, StringComparer.Ordinal)
                .ToList();

            var severity = Rank(contributors);
            return new MethodConflict(
                method.DeclaringType?.FullName ?? "?",
                method.Name,
                FullSignature(method),
                severity,
                contributors);
        }

        private static ConflictSeverity Rank(List<ConflictContributor> contributors)
        {
            int transpilerOwners = contributors.Count(c => (c.Kinds & PatchKinds.Transpiler) != 0);
            if (transpilerOwners >= 2) return ConflictSeverity.High;
            int prefixOwners = contributors.Count(c => (c.Kinds & PatchKinds.Prefix) != 0);
            if (prefixOwners >= 2) return ConflictSeverity.Medium;
            return ConflictSeverity.Low;
        }

        private static bool IsModReadyOwner(string owner, Patch p)
        {
            if (owner.StartsWith("ModReady", StringComparison.OrdinalIgnoreCase)) return true;
            // Also catch patches whose method lives in a ModReady assembly even
            // if the owner id was set to something else.
            var asm = AssemblyOf(p);
            return asm.StartsWith("ModReady", StringComparison.OrdinalIgnoreCase);
        }

        private static string AssemblyOf(Patch p)
        {
            try { return p.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string FullSignature(MethodBase m)
        {
            try
            {
                var ps = string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
                return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}({ps})";
            }
            catch { return $"{m.DeclaringType?.FullName ?? "?"}::{m.Name}"; }
        }

        private static string Describe(MethodBase m)
            => $"{m.DeclaringType?.FullName ?? "?"}.{m.Name}";

        // ----------------------------------------------------------------
        // Reporting
        // ----------------------------------------------------------------

        /// <summary>Plain-text report; empty string when there are no conflicts.</summary>
        public static string ToText(IReadOnlyList<MethodConflict>? conflicts = null)
        {
            conflicts ??= Scan();
            if (conflicts.Count == 0) return "No inter-mod Harmony conflicts detected.";

            var sb = new StringBuilder();
            sb.AppendLine($"ModReady mod-conflict report -- {conflicts.Count} contested method(s)");
            sb.AppendLine($"  High: {conflicts.Count(c => c.Severity == ConflictSeverity.High)}" +
                          $"  Medium: {conflicts.Count(c => c.Severity == ConflictSeverity.Medium)}" +
                          $"  Low: {conflicts.Count(c => c.Severity == ConflictSeverity.Low)}");
            sb.AppendLine();
            foreach (var c in conflicts)
                sb.AppendLine(c.ToString());
            return sb.ToString();
        }

        /// <summary>GitHub-issue-ready markdown table.</summary>
        public static string ToMarkdown(IReadOnlyList<MethodConflict>? conflicts = null)
        {
            conflicts ??= Scan();
            var sb = new StringBuilder();
            sb.AppendLine("### ModReady mod-conflict report");
            sb.AppendLine();
            if (conflicts.Count == 0) { sb.AppendLine("_No inter-mod Harmony conflicts detected._"); return sb.ToString(); }
            sb.AppendLine("| Severity | Method | Mods involved |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in conflicts)
            {
                var mods = string.Join("<br>", c.Contributors.Select(x => x.ToString()));
                sb.AppendLine($"| {c.Severity} | `{c.TargetSignature}` | {mods} |");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Run a scan and write the text report to runtime.log (and return it).
        /// Safe to call from a late lifecycle hook once all mods have patched.
        /// </summary>
        public static IReadOnlyList<MethodConflict> ScanAndLog()
        {
            var conflicts = Scan();
            try
            {
                if (conflicts.Count == 0)
                {
                    DiagLog.Log(Tag, "scan: no inter-mod Harmony conflicts detected");
                }
                else
                {
                    DiagLog.Log(Tag, $"scan: {conflicts.Count} contested method(s) " +
                        $"(High {conflicts.Count(c => c.Severity == ConflictSeverity.High)}, " +
                        $"Medium {conflicts.Count(c => c.Severity == ConflictSeverity.Medium)}, " +
                        $"Low {conflicts.Count(c => c.Severity == ConflictSeverity.Low)})");
                    foreach (var c in conflicts.Where(c => c.Severity >= ConflictSeverity.Medium))
                        DiagLog.Log(Tag, "  " + c.ToString().Replace("\n", " "));
                }
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "ScanAndLog", ex); }
            return conflicts;
        }
    }
}
