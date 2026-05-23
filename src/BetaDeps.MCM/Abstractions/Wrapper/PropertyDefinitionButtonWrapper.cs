// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionButtonWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionButtonWrapper(object @object) { Object = @object; }
}
