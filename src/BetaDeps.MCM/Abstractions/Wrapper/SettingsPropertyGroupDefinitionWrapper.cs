// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class SettingsPropertyGroupDefinitionWrapper : IWrapper
{
    public object Object { get; }
    public SettingsPropertyGroupDefinitionWrapper(object @object) { Object = @object; }
}
