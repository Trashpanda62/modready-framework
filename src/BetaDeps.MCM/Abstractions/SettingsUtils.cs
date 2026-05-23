// BetaDeps clean-room.
using System.Collections.Generic;
namespace MCM.Abstractions;
public static class SettingsUtils
{
    public static bool CheckIsValid(BaseSettings? settings) => settings != null && !ReferenceEquals(settings, UnavailableSetting.Instance);
    public static IEnumerable<SettingsPropertyDefinition> GetAllSettingPropertyDefinitions(BaseSettings? settings)
    {
        if (settings == null) yield break;
        yield break;
    }
}
