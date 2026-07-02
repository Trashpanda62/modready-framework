// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Concrete ISettingsPresetBuilder. Captures the Id, Name, and per-property
// values a consumer mod declares fluently via
//   builder.CreatePreset("default", "Default", p => p
//       .SetId("default")
//       .SetName("Default")
//       .SetPropertyValue("Foo", 5)
//       .SetPropertyValue("Bar", true));
//
// The values dictionary maps property-Id → desired value. A future UI pass
// can wire these into the fluent settings panel as a "Presets" dropdown that
// applies the captured values when chosen. For v0.7 the point is just to
// satisfy the API surface so consumer mods (Retinues, etc.) don't hit
// MissingMethodException on the CreatePreset call.

using System.Collections.Generic;

namespace MCM.Abstractions.FluentBuilder;

internal sealed class SettingsPresetBuilderImpl : ISettingsPresetBuilder
{
    public string Id { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public IDictionary<string, object?> Values { get; } = new Dictionary<string, object?>();

    public ISettingsPresetBuilder SetId(string id)
    {
        Id = id ?? string.Empty;
        return this;
    }

    public ISettingsPresetBuilder SetName(string name)
    {
        Name = name ?? string.Empty;
        return this;
    }

    public ISettingsPresetBuilder SetPropertyValue(string propertyId, object? value)
    {
        if (!string.IsNullOrEmpty(propertyId))
            Values[propertyId] = value;
        return this;
    }
}
