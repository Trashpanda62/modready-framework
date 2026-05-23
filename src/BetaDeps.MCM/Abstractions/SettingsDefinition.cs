// BetaDeps clean-room.
namespace MCM.Abstractions;
public class SettingsDefinition
{
    public string SettingsId { get; }
    public string DisplayName { get; }
    public SettingsDefinition(string settingsId, string displayName = "")
    {
        SettingsId = settingsId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
    }
    public SettingsDefinition(BaseSettings settings)
    {
        SettingsId = settings?.Id ?? string.Empty;
        DisplayName = settings?.DisplayName ?? string.Empty;
    }
}
