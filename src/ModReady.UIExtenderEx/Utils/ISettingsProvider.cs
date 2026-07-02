// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ISettingsProvider is the abstraction UIExtenderEx uses to ask "is this
// optional feature enabled" without taking a compile-time dep on MCM.
// Consumer mods inject an implementation; the default looks at
// UIExtenderExSettings (a self-contained settings holder).

namespace Bannerlord.UIExtenderEx.Utils;

public interface ISettingsProvider
{
    bool IsEnabled(string featureKey, bool defaultValue);
}

internal sealed class DefaultSettingsProvider : ISettingsProvider
{
    public bool IsEnabled(string featureKey, bool defaultValue) => defaultValue;
}
