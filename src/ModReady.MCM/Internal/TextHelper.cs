// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Small text-cleaning helper used everywhere a Bannerlord-style
// localization-prefixed display string (e.g. "{=mcmbbhtitle00}Radius")
// reaches our UI layer. TaleWorlds's TextObject parser strips the
// "{=key}" prefix when rendering, but we're displaying raw attribute
// strings without going through TextObject. This helper does the same
// strip so the labels look clean.
//
// ── Convention for future code ────────────────────────────────────────
// ANY string that originates from a consumer mod's settings (attribute
// declarations, fluent-builder ctor arguments, BaseSettings overrides,
// dropdown ToString() returns, dynamic TextValue reads) MUST pass
// through StripLocalizationKeys before being rendered to the UI. This
// is the only sanitization step between consumer-mod input and the
// Mod Config tab. Audited sites that already comply:
//   - RegisteredSettings.DisplayName (getter)
//   - SettingsVM.DisplayName (ctor) + group names (ctor)
//   - SettingsPropertyVM.DisplayName / HintText (both attribute + fluent ctors)
//   - SettingsPropertyVM.ButtonContentText (getter — both fluent + attribute)
//   - SettingsPropertyVM.TextValue (getter — dynamic read)
//   - SettingsPropertyVM.GetDropdownDisplayText (private helper)
//   - OptionsVMMixin.RebuildModList group names + _selectedModName
// If you add a new string-rendering path, run it through here. Idempotent
// and cheap (compiled regex), so double-stripping is safe.

using System;
using System.Text.RegularExpressions;

namespace MCM.Internal;

internal static class TextHelper
{
    // Matches a single Bannerlord-style {=key} localization tag at the
    // start of a string or anywhere a key appears unaccompanied. Using a
    // compiled regex because every property label runs through this on
    // construction and we want to keep the cost negligible.
    private static readonly Regex _localizationKey =
        new(@"\{=[^}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Strip Bannerlord-style "{=key}" localization tags from a string. If
    /// the result is empty (the string was just a key with no fallback
    /// text), returns the input unchanged so the user at least sees the
    /// key rather than a blank label.
    /// </summary>
    public static string StripLocalizationKeys(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Localization fix (Nexus bug "localization doesn't work (MCM)",
        // XorberaxLegacy repro): when the string carries a "{=key}" tag, resolve
        // it through the engine's TextObject FIRST so language packs apply -- the
        // key resolves to the translated text when any module's
        // ModuleData/Languages/*.xml defines it, else to the fallback. Upstream
        // MCM hands display strings to TextObject; we previously only stripped the
        // key and showed the English fallback, so non-English packs never applied.
        //
        // Guarded on purpose: TextObject needs the engine text manager (only up
        // once a menu/campaign is loaded) and some mods (e.g. IDontCare) Harmony-
        // hook TextObject.ToString(). ANY failure -- or a result that still looks
        // like a raw key -- falls through to the static strip below, so a bad hook
        // or an early call can never blank or crash the Mod Config panel. Plain
        // (keyless) strings skip TextObject entirely and just pass through.
        if (input!.IndexOf("{=", StringComparison.Ordinal) >= 0)
        {
            try
            {
                var resolved = new TaleWorlds.Localization.TextObject(input).ToString();
                if (!string.IsNullOrEmpty(resolved) &&
                    resolved.IndexOf("{=", StringComparison.Ordinal) < 0)
                {
                    return resolved;
                }
            }
            catch
            {
                // fall through to the static strip
            }
        }

        var stripped = _localizationKey.Replace(input, string.Empty).Trim();
        return stripped.Length == 0 ? input : stripped;
    }
}
