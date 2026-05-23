// BetaDeps clean-room.
using System.Collections.Generic;
namespace MCM.Abstractions.Properties;
public interface ISettingsPropertyDiscoverer
{
    IEnumerable<SettingsPropertyDefinition> Discover(BaseSettings settings);
}
public interface IAttributeSettingsPropertyDiscoverer : ISettingsPropertyDiscoverer { }
