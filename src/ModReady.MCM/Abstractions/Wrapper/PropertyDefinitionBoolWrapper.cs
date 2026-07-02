// ModReady clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionBoolWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionBoolWrapper(object @object) { Object = @object; }
}
