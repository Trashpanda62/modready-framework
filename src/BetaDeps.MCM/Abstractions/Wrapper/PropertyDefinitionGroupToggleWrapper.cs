// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionGroupToggleWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionGroupToggleWrapper(object @object) { Object = @object; }
}
