// ModReady clean-room.
// Lives in Models nested namespace alongside the other property builder
// interfaces (see Settings/Builder/ISettingsBuilder.cs). Upstream BUTR MCM
// puts them all here and consumer mod IL records the nested namespace name.
// Inherits from the GENERIC ISettingsPropertyBuilder<TSelf> in the parent
// FluentBuilder namespace — BEW (and other MCMv5 consumers) resolve
// SetHintText calls through `ISettingsPropertyBuilder\`1`, so anchoring to
// the non-generic Models.ISettingsPropertyBuilder here would throw
// EntryPointNotFoundException at consumer-mod JIT time.
namespace MCM.Abstractions.FluentBuilder.Models;

public interface ISettingsPropertyButtonBuilder
    : MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<ISettingsPropertyButtonBuilder> { }
