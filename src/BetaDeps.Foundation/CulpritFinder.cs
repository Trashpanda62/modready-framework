// BetaDeps.Foundation -- CulpritFinder
//
// Phase 3.1 of the 2026-06-10 remediation plan: the shared "whose fault is
// this exception" core, extracted from SaveShield so PatchShield (and any
// future shield) attributes failures the same way instead of growing its
// own divergent copy.
//
// Given an exception, walk its stack frames -- the parsed
// System.Diagnostics.StackTrace form first, the raw string form as a
// fallback for inlined/trimmed frames -- and return the deepest frame whose
// declaring type is NOT engine / framework / BetaDeps infrastructure.
// That frame's assembly is the most likely consumer-mod culprit.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace BetaDeps.Foundation;

public static class CulpritFinder
{
    /// <summary>Identity of the most likely culprit frame of an exception.
    /// All fields empty when no non-engine frame was found (the failure is
    /// engine- or framework-internal).</summary>
    public readonly struct CulpritInfo
    {
        public CulpritInfo(string assemblyName, string frameDescription, string assemblyLocation)
        {
            AssemblyName = assemblyName;
            FrameDescription = frameDescription;
            AssemblyLocation = assemblyLocation;
        }
        public string AssemblyName { get; }
        public string FrameDescription { get; }
        /// <summary>Absolute path to the culprit assembly's DLL on disk (or "" if not resolvable).</summary>
        public string AssemblyLocation { get; }
        /// <summary>True when a non-engine culprit frame was identified.</summary>
        public bool HasCulprit => !string.IsNullOrEmpty(AssemblyName);
    }

    private static readonly string[] _enginePrefixes =
    {
        "TaleWorlds.", "SandBox", "StoryMode", "CustomBattle",
        "BetaDeps", "Bannerlord.Harmony", "Bannerlord.UIExtenderEx",
        "Bannerlord.ButterLib", "MCMv5", "0Harmony", "HarmonyLib",
        "Mono.Cecil", "MonoMod", "System.", "Microsoft.", "mscorlib",
        "Newtonsoft.Json", "Serilog",
    };

    /// <summary>
    /// Walk the exception's stack frames (both the parsed System.Diagnostics
    /// .StackTrace form and the raw string form as fallback) and return the
    /// deepest frame whose declaring type is NOT in one of the engine /
    /// infrastructure prefixes. That's the most likely consumer-mod culprit.
    /// </summary>
    public static CulpritInfo FindCulpritFrame(Exception? ex)
    {
        if (ex == null) return new CulpritInfo(string.Empty, string.Empty, string.Empty);

        // First try the parsed StackTrace -- gives us assembly info even when
        // some frames had their string form trimmed.
        try
        {
            var st = new System.Diagnostics.StackTrace(ex, fNeedFileInfo: false);
            var frames = st.GetFrames();
            if (frames != null)
            {
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    if (m == null) continue;
                    var declType = m.DeclaringType;
                    if (declType == null) continue;
                    var asm = declType.Assembly;
                    var asmName = asm?.GetName()?.Name ?? string.Empty;
                    if (IsEngineFrame(asmName, declType.FullName)) continue;
                    var loc = asm?.Location ?? string.Empty;
                    return new CulpritInfo(
                        asmName,
                        $"{declType.FullName}.{m.Name} -- frame from {(string.IsNullOrEmpty(loc) ? "<unknown>" : loc)}",
                        loc);
                }
            }
        }
        catch { /* fall through to string parse */ }

        // Fallback: text-grep the raw StackTrace lines looking for an
        // "at <Type>.<Method>" pattern whose Type FullName doesn't start
        // with an engine prefix.
        try
        {
            var raw = ex.StackTrace ?? string.Empty;
            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.TrimStart().TrimEnd();
                if (!line.StartsWith("at ")) continue;
                var rest = line.Substring(3); // strip "at "
                // Find the last '.' before the first '(' -- splits type from method.
                int paren = rest.IndexOf('(');
                if (paren < 0) continue;
                var sig = rest.Substring(0, paren);
                int lastDot = sig.LastIndexOf('.');
                if (lastDot <= 0) continue;
                var typeName = sig.Substring(0, lastDot);
                if (IsEngineFrame(asmName: null, typeFullName: typeName)) continue;
                // Best-effort assembly inference: take the first dotted segment.
                int firstDot = typeName.IndexOf('.');
                var likelyAsm = firstDot > 0 ? typeName.Substring(0, firstDot) : typeName;
                return new CulpritInfo(likelyAsm, $"{rest} -- frame parsed from stack-trace text", string.Empty);
            }
        }
        catch { /* swallow */ }

        return new CulpritInfo(string.Empty, string.Empty, string.Empty);
    }

    /// <summary>True when the frame belongs to the engine, the .NET
    /// framework, or BetaDeps' own infrastructure -- i.e. NOT a consumer
    /// mod, so it can never be a culprit.</summary>
    public static bool IsEngineFrame(string? asmName, string? typeFullName)
    {
        foreach (var prefix in _enginePrefixes)
        {
            if (!string.IsNullOrEmpty(asmName) &&
                asmName!.StartsWith(prefix, StringComparison.Ordinal))
                return true;
            if (!string.IsNullOrEmpty(typeFullName) &&
                typeFullName!.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
