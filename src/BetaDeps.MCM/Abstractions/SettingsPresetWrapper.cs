// BetaDeps clean-room.
namespace MCM.Abstractions;
public class SettingsPresetWrapper<TPreset> : ISettingsPreset where TPreset : class
{
    public TPreset Object { get; }
    public string SettingsId { get; private set; } = string.Empty;
    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    private SettingsPresetWrapper(TPreset preset) { Object = preset; }
    public static SettingsPresetWrapper<TPreset> Create(TPreset preset)
    {
        var w = new SettingsPresetWrapper<TPreset>(preset);
        var t = typeof(TPreset);
        w.SettingsId = (string?)t.GetProperty("SettingsId")?.GetValue(preset) ?? string.Empty;
        w.Id = (string?)t.GetProperty("Id")?.GetValue(preset) ?? string.Empty;
        w.Name = (string?)t.GetProperty("Name")?.GetValue(preset) ?? string.Empty;
        return w;
    }
    public virtual BaseSettings? LoadPreset() => null;
    public virtual bool SavePreset(BaseSettings settings) => false;
}
