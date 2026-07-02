// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.PerSave.AttributePerSaveSettings<TSelf>
//
// Attribute-driven per-save settings base. Mirrors AttributeGlobalSettings<TSelf>
// but for settings that persist with the save file rather than globally.
// Consumer mods (e.g., Detailed Character Creation) declare:
//
//     public class MySaveSettings : AttributePerSaveSettings<MySaveSettings> { ... }
//
// The class itself is just a marker the [SettingProperty*] reflection passes
// look for to decide whether to auto-generate a settings panel and to scope
// persistence to the current save campaign.

namespace MCM.Abstractions.Base.PerSave;

// v0.7: inherits from PerSaveSettings<TSelf> rather than BasePerSaveSettings
// directly. Mirrors the upstream BUTR pattern (AttributePerSaveSettings ->
// PerSaveSettings -> BasePerSaveSettings -> BaseSettings) so consumer mods
// that reference either intermediate base class find it via the same
// inheritance chain the CLR's type-load resolver walks.
public abstract class AttributePerSaveSettings<TSelf> : PerSaveSettings<TSelf>
    where TSelf : AttributePerSaveSettings<TSelf>
{
    // Intentionally empty. Persistence + Instance singleton inherited from
    // PerSaveSettings<TSelf>. F-bounded TSelf constraint matches upstream
    // MCM ABI so consumer mods' override resolution works at type-load time.
}
