// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithFormatWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithFormatWrapper(object @object) { Object = @object; }
}
