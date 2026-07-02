// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithIdWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithIdWrapper(object @object) { Object = @object; }
}
