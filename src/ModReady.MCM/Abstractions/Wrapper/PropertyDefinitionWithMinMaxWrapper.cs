// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithMinMaxWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithMinMaxWrapper(object @object) { Object = @object; }
}
