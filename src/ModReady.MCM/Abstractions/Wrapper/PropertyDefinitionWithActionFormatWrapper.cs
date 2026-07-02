// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithActionFormatWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithActionFormatWrapper(object @object) { Object = @object; }
}
