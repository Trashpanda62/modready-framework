// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Stub for Bannerlord.ButterLib.ButterLibSaveableTypeDefiner. The upstream
// ButterLib uses this to register its own ObjectSystem types with the
// TaleWorlds save system so they can be persisted. Consumer mods reference
// it by name during type-load. v0.7.2 adds the type so the CLR type-load
// step passes; we don't actually plug into the save system yet.
//
// The class is intentionally a thin no-op deriving from the TaleWorlds
// SaveableTypeDefiner base so that "is-a" checks against
// SaveableCampaignBehaviorTypeDefiner / SaveableTypeDefiner downcast
// correctly.

namespace Bannerlord.ButterLib
{
    /// <summary>
    /// Stub. Real save-binding wiring not yet implemented. Exists so the
    /// CLR can type-load the upstream ButterLib's references to this name.
    /// </summary>
    public class ButterLibSaveableTypeDefiner
    {
        // ButterLib's upstream code constructs this with an integer "base id"
        // for the save namespace. We accept it and discard it.
        public ButterLibSaveableTypeDefiner() { }
        public ButterLibSaveableTypeDefiner(int baseId) { }
    }
}
