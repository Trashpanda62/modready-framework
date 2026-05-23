// BetaDeps clean-room.
using System.Collections.Generic;
using System.Linq;
namespace MCM.Abstractions;
public abstract class BaseSettingsProvider : IExternalSettingsProvider
{
    public virtual IEnumerable<SettingsDefinition> SettingsDefinitions =>
        MCM.Internal.SettingsRegistry.All.Select(r => new SettingsDefinition(r.Instance));
    public virtual BaseSettings? GetSettings(string settingsId)
        => MCM.Internal.SettingsRegistry.TryGet(settingsId)?.Instance;
    public virtual IEnumerable<ISettingsPreset> GetPresets(string settingsId) => System.Array.Empty<ISettingsPreset>();
    public virtual void SaveSettings(BaseSettings settings)
    {
        if (settings == null) return;
        var save = settings.GetType().GetMethod("Save", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        save?.Invoke(settings, null);
    }
    public virtual bool OverrideSettings(BaseSettings settings) => false;
    public virtual bool ResetSettings(BaseSettings settings) => false;
}
