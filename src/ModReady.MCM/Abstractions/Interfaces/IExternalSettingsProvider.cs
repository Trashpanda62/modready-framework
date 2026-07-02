// ModReady clean-room.
using System.Collections.Generic;
namespace MCM.Abstractions;
public interface IExternalSettingsProvider
{
    IEnumerable<SettingsDefinition> SettingsDefinitions { get; }
    BaseSettings? GetSettings(string settingsId);
    void SaveSettings(BaseSettings settings);
}
