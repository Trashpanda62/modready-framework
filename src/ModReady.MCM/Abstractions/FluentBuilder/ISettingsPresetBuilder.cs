// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.FluentBuilder.ISettingsPresetBuilder
//
// Minimal API surface to satisfy consumer mods that declare the type via
// reflection or as a generic parameter. Implementations of this interface
// describe a single saved-preset definition that gets registered into a
// settings instance via the fluent builder pipeline.
//
// The full BUTR MCM API has methods like SetName, SetId, AddSettingValue,
// etc. — we provide the same surface so consumer mods bind by name.

using System.Collections.Generic;

namespace MCM.Abstractions.FluentBuilder;

public interface ISettingsPresetBuilder
{
    string Id { get; }
    string Name { get; }
    IDictionary<string, object?> Values { get; }

    ISettingsPresetBuilder SetId(string id);
    ISettingsPresetBuilder SetName(string name);
    ISettingsPresetBuilder SetPropertyValue(string propertyId, object? value);
}
