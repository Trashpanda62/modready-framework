// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyGroupDefinitionWrapper : IWrapper
{
    public object Object { get; }
    public PropertyGroupDefinitionWrapper(object @object) { Object = @object; }
}
