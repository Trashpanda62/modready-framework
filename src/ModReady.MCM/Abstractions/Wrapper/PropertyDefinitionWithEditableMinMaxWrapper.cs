// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithEditableMinMaxWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithEditableMinMaxWrapper(object @object) { Object = @object; }
}
