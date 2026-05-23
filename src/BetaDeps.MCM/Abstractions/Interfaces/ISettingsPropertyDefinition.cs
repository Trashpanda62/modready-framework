// BetaDeps clean-room.
using System;
namespace MCM.Abstractions;
public interface ISettingsPropertyDefinition : IPropertyDefinitionBase
{
    string Id { get; }
    string GroupName { get; }
    int GroupOrder { get; }
    Type PropertyType { get; }
}
