// BetaDeps.Harmony -- SafeBind
//
// Signature-verified binding helpers. The point: on the beta branch
// (e1.4.x), TaleWorlds frequently changes a method's signature in ways
// that AccessTools.Method() can still resolve (because it finds *a*
// method with the right name), but the Harmony patch we wanted to
// attach would then bind to a method whose argument layout doesn't
// match our prefix/postfix. The result is a native-side stack
// corruption CTD with no managed stack to diagnose.
//
// SafeBind looks up the target method by name AND verifies its
// return type and parameter count (and optionally parameter types)
// match what the caller expected. If anything is off, the bind is
// skipped, a diagnostic line is written, and the patch never gets
// installed. The game stays up; the feature silently no-ops.
//
// This is the "sigsafe pre-bind verification" pattern we developed
// during the CREST sprint -- original work, re-authored cleanly here
// against the public Harmony 2.x API and System.Reflection.
//
// MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib;

namespace BetaDeps.Harmony;

public static class SafeBind
{
    private const string Tag = "SafeBind";

    /// <summary>
    /// Look up an instance method by name on the given type and verify
    /// its return type and parameter count match. Returns null on any
    /// mismatch or lookup failure; the caller can pattern-match on
    /// null and skip patching cleanly.
    /// </summary>
    public static MethodInfo? Method(
        Type declaringType,
        string methodName,
        Type expectedReturnType,
        int expectedParamCount,
        Type[]? expectedParamTypes = null,
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
    {
        if (declaringType == null || string.IsNullOrEmpty(methodName))
        {
            DiagLog.Log(Tag, $"reject: null/empty inputs ({declaringType?.FullName ?? "?"}::{methodName ?? "?"})");
            return null;
        }
        try
        {
            // Enumerate by name -- a single GetMethod() throws if there are overloads,
            // and we want to be tolerant of overload sets.
            var candidates = declaringType.GetMethods(flags)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();
            if (candidates.Length == 0)
            {
                DiagLog.Log(Tag, $"miss: {declaringType.FullName}::{methodName} -- no candidates with given binding flags");
                return null;
            }

            foreach (var m in candidates)
            {
                var prms = m.GetParameters();
                if (prms.Length != expectedParamCount) continue;
                if (!TypeMatches(m.ReturnType, expectedReturnType)) continue;
                if (expectedParamTypes != null && expectedParamTypes.Length == prms.Length)
                {
                    bool allOk = true;
                    for (int i = 0; i < prms.Length; i++)
                    {
                        if (!TypeMatches(prms[i].ParameterType, expectedParamTypes[i])) { allOk = false; break; }
                    }
                    if (!allOk) continue;
                }
                // Found a match.
                DiagLog.Log(Tag, $"bind: {declaringType.FullName}::{methodName} OK (return={m.ReturnType.Name}, {prms.Length} params)");
                return m;
            }

            // Nothing matched -- log what we did find so the user can read it.
            var summary = string.Join(" | ",
                candidates.Select(m =>
                    $"{m.ReturnType.Name} {m.Name}({m.GetParameters().Length})"));
            DiagLog.Log(Tag, $"reject: {declaringType.FullName}::{methodName} -- signature drift. expected return={expectedReturnType.Name}, params={expectedParamCount}. saw: [{summary}]. Patch will be skipped (game stays up).");
            return null;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Method({declaringType?.FullName}::{methodName})", ex);
            return null;
        }
    }

    /// <summary>
    /// Apply a Harmony patch only if SafeBind successfully resolves the
    /// target. Returns true if patched, false if skipped. Either way the
    /// caller does not throw.
    /// </summary>
    public static bool TryPatch(
        HarmonyLib.Harmony harmony,
        MethodInfo? target,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? finalizer = null,
        HarmonyMethod? transpiler = null)
    {
        if (harmony == null || target == null) return false;
        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix, finalizer: finalizer, transpiler: transpiler);
            DiagLog.Log(Tag, $"patched: {target.DeclaringType?.FullName}::{target.Name}");
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"TryPatch({target.DeclaringType?.FullName}::{target.Name})", ex);
            return false;
        }
    }

    private static bool TypeMatches(Type actual, Type expected)
    {
        if (actual == expected) return true;
        if (actual == null || expected == null) return false;
        // Allow assignment-compatible matches so callers can specify
        // base types when they don't care about exact derived types.
        return expected.IsAssignableFrom(actual);
    }
}
