// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// BUTRSettingsContainer -- the typed wrapper consumer mods use to bind ButterLib
// services to MCM settings. Upstream BUTR-MCM exposes an interface that the
// MCM UI consumes to enumerate settings sources. Our implementation enumerates
// every registered AttributeGlobalSettings + FluentGlobalSettings.

using System.Collections.Generic;
using System.Linq;

using MCM.Abstractions;
using MCM.Internal;

namespace MCM.Abstractions.Settings.Containers;

public interface IBUTRSettingsContainer
{
    /// <summary>Every registered settings instance discoverable in the AppDomain.</summary>
    IEnumerable<BaseSettings> AllSettings { get; }
}

public sealed class BUTRSettingsContainer : IBUTRSettingsContainer
{
    public IEnumerable<BaseSettings> AllSettings
    {
        get
        {
            foreach (var r in SettingsRegistry.All)
                yield return r.Instance;
            foreach (var f in FluentSettingsRegistry.All)
                yield return f;
        }
    }
}
