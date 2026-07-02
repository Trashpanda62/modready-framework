// ModReady clean-room.
namespace MCM.Abstractions;
public interface IPropertyGroupDefinition
{
    string GroupName { get; }
    int GroupOrder { get; }
    bool IsMainToggle { get; }
}
