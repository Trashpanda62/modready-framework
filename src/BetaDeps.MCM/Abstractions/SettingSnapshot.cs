// BetaDeps clean-room.
namespace MCM.Abstractions;
public class SettingSnapshot
{
    public string SettingsId { get; }
    public string Name { get; }
    public SettingSnapshot(string settingsId, string name)
    {
        SettingsId = settingsId ?? string.Empty;
        Name = name ?? string.Empty;
    }
}
