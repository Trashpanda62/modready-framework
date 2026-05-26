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

    // v0.7.5 ship-blocker: AdjustableLeveling's AttentionWindow popup callback
    // calls `BaseSettingsProvider.Instance` after the user dismisses the
    // popup. Upstream BUTR MCM exposes a static singleton accessor that
    // returns the active provider (typically SettingsProviderWrapper).
    // Without this, MissingMethodException at the callback site crashes
    // the game before main menu.
    //
    // Initialized lazily on first access to whichever concrete provider
    // is registered (we ship SettingsProviderWrapper as the default).
    private static BaseSettingsProvider? _instance;
    private static readonly object _instanceLock = new();
    public static BaseSettingsProvider Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (_instanceLock)
            {
                // SettingsProviderWrapper requires a `provider` constructor
                // arg (it wraps an external provider object). For the
                // singleton we don't actually delegate to one, so pass a
                // marker object that satisfies the non-null contract.
                _instance ??= new SettingsProviderWrapper(new object());
            }
            return _instance;
        }
    }
}
