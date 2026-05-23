// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Wrapper;
public class PropertyDefinitionDropdownWrapper : IWrapper
{
    public object Object { get; }
    public PropertyDefinitionDropdownWrapper(object @object) { Object = @object; }
}
