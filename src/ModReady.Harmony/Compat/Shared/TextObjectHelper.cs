// ModReady clean-room re-implementation of Bannerlord.BUTR.Shared.Helpers.TextObjectHelper.
// MIT, copyright 2026 Maxfield Management Group.
//
// Wraps TaleWorlds.Localization.TextObject creation/translation so consumer
// mods can render {=Key}Default Text strings without taking a hard compile
// dep on TaleWorlds.Localization (which is reflection-loaded here).

using System;

using ModReady.Foundation;

using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.BUTR.Shared.Helpers;

public static class TextObjectHelper
{
    private const string Tag = "TextObjectHelper";

    /// <summary>
    /// Strip the leading {=Key} localization marker from a string and return
    /// the fallback text. If no marker is present, returns the original
    /// string unchanged.
    /// </summary>
    public static string StripLocalizationKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        if (s.Length < 4 || s[0] != '{' || s[1] != '=') return s;
        var close = s.IndexOf('}', 2);
        return close < 0 ? s : s.Substring(close + 1);
    }

    /// <summary>
    /// Construct a TaleWorlds.Localization.TextObject (boxed as object so
    /// callers don't take a compile-time dep). Returns null if the type
    /// isn't reachable.
    /// </summary>
    public static object? Create(string text)
    {
        try
        {
            var t = AccessTools2.TypeByName("TaleWorlds.Localization.TextObject");
            if (t == null) return null;
            return Activator.CreateInstance(t, new object?[] { text, null });
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Create", ex);
            return null;
        }
    }

    /// <summary>
    /// Translate a TextObject to its current-locale display string. If the
    /// object isn't a TextObject, calls ToString() as a fallback.
    /// </summary>
    public static string ToStringSafe(object? textObject)
    {
        if (textObject == null) return string.Empty;
        try
        {
            var m = AccessTools2.Method(textObject.GetType(), "ToString", Type.EmptyTypes);
            if (m != null) return m.Invoke(textObject, null) as string ?? string.Empty;
        }
        catch { }
        return textObject.ToString() ?? string.Empty;
    }
}
