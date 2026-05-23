// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionTextWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionTextWrapper(object @object) { Object = @object; }
}
