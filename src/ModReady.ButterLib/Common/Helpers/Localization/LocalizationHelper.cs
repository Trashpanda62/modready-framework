// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.

using Bannerlord.BUTR.Shared.Helpers;

namespace Bannerlord.ButterLib.Common.Helpers;

public static class LocalizationHelper
{
    /// <summary>
    /// Strip a {=Key} prefix from a localizable string and return the literal
    /// display text. If no key prefix is present, returns the input unchanged.
    /// </summary>
    public static string StripKey(string s) => TextObjectHelper.StripLocalizationKey(s);

    /// <summary>
    /// Construct a TaleWorlds.Localization.TextObject for the given input,
    /// boxed as object so callers don't take a compile-time dep on
    /// TaleWorlds.Localization. Returns the input string back if TextObject
    /// isn't reachable.
    /// </summary>
    public static object CreateTextObject(string s) => TextObjectHelper.Create(s) ?? (object)s;
}
