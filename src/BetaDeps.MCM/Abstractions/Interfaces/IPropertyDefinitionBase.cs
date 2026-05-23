// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions;
public interface IPropertyDefinitionBase
{
    string DisplayName { get; }
    string HintText { get; }
    int Order { get; }
    bool RequireRestart { get; }
    IRef PropertyReference { get; }
}
