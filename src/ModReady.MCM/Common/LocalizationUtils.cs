// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Common.LocalizationUtils — type stub.
//
// XorberaxLegacy v1.x and other consumer mods reference this type by
// full name during new-campaign init. Without the type existing in our
// MCMv5 shim assembly, the CLR throws TypeLoadException at JIT-bind
// time, which our SettingsStorage reset-on-failure flow caught and
// fed into the save/load feedback loop (caught now by the v0.7.5 guard,
// but still produced a degraded session — better to expose the type).
//
// Upstream BUTR LocalizationUtils is a static helper class with text-
// formatting + token-replacement methods (e.g. for {=foo}fallback{=}
// localized string syntax). We stub the type with the most commonly-
// called surface; consumer mods that call methods not stubbed here
// will surface as MissingMethodException, which we can add as needed.

using System;
using System.Collections.Generic;

namespace MCM.Common;

public static class LocalizationUtils
{
    /// <summary>
    /// Upstream BUTR helper that pulls the human-readable text out of a
    /// `{=token}Fallback Text{=}` -style string. Most consumer mods just
    /// pass already-resolved strings here, so the no-op passthrough is
    /// behaviourally equivalent in the common case.
    /// </summary>
    public static string GetLocalizedText(string? input)
    {
        return input ?? string.Empty;
    }

    /// <summary>
    /// Strip a `{=...}` prefix from a localization string if present.
    /// Pass-through if no token prefix is found.
    /// </summary>
    public static string StripLocalizationToken(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var s = input!;
        if (s.StartsWith("{=", StringComparison.Ordinal))
        {
            var close = s.IndexOf('}', 2);
            if (close >= 0 && close < s.Length - 1)
                return s.Substring(close + 1);
        }
        return s;
    }

    /// <summary>
    /// Token-replacement helper. Given a template like "{name} has {count}
    /// items", substitute every {key} with the dictionary value for that
    /// key. XorberaxLegacy and other mods use this to format MCM hint text
    /// + button labels at runtime.
    ///
    /// Implementation is a simple `{key} -> ToString()` walk; no recursion,
    /// no nested formats, no localization lookups. Sufficient for the
    /// common case (consumer mods that already pre-localized their template).
    /// </summary>
    public static string Localize(string? template, Dictionary<string, object>? values)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (values == null || values.Count == 0) return template!;
        var s = template!;
        foreach (var kv in values)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            var token = "{" + kv.Key + "}";
            var replacement = kv.Value?.ToString() ?? string.Empty;
            s = s.Replace(token, replacement);
        }
        return s;
    }

    /// <summary>
    /// Single-key convenience overload. Equivalent to building a one-entry
    /// dictionary and calling Localize(template, dict) — saves the caller
    /// the dictionary allocation.
    /// </summary>
    public static string Localize(string? template, string? key, object? value)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (string.IsNullOrEmpty(key)) return template!;
        return template!.Replace("{" + key + "}", value?.ToString() ?? string.Empty);
    }
}
