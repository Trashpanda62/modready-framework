// ModReady clean-room.
namespace MCM.Abstractions;
public class SettingsProviderWrapper : BaseSettingsProvider
{
    public object Provider { get; }
    public SettingsProviderWrapper(object provider) { Provider = provider; }
}
