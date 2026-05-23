// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// IPropertyDefinition + IPropertyGroupDefinition -- the abstract shape MCM
// uses to describe properties + groups regardless of whether they originated
// from [SettingProperty*] reflection or the fluent builder. The MCM UI tab
// binds against these so it can render either source uniformly.

using System.Collections.Generic;

namespace MCM.Abstractions.Settings.Properties;

public interface IPropertyDefinition
{
    string Id { get; }
    string DisplayName { get; }
    string GroupName { get; }
    int Order { get; }
    bool RequireRestart { get; }
    string HintText { get; }

    /// <summary>"bool" | "int" | "float" | "text" | "dropdown" | "button"</summary>
    string TypeKind { get; }

    object? GetValue();
    void SetValue(object? value);
}

public interface IPropertyGroupDefinition
{
    string Name { get; }
    int Order { get; }
    IReadOnlyList<IPropertyDefinition> Properties { get; }
}
