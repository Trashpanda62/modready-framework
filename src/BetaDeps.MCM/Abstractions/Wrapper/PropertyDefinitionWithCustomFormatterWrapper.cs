// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionWithCustomFormatterWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionWithCustomFormatterWrapper(object @object) { Object = @object; }
}
