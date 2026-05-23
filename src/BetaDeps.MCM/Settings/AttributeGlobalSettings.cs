// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.Global.AttributeGlobalSettings<TSelf>
//
// Attribute-driven settings base. Inherits from GlobalSettings<TSelf>
// (NOT directly from BaseGlobalSettings) so consumer mod classes that
// inherit from AttributeGlobalSettings<MyType> transitively satisfy the
// F-bounded `TSelf : GlobalSettings<TSelf>` constraint elsewhere in the
// codebase. See GlobalSettings.cs for the failure mode this prevents.
//
// All actual Instance/Save/Reset logic lives on GlobalSettings<TSelf>;
// this class is just the marker the [SettingProperty*] reflection passes
// look for to decide whether to auto-generate a settings panel.

namespace MCM.Abstractions.Base.Global;

public abstract class AttributeGlobalSettings<TSelf> : GlobalSettings<TSelf>
    where TSelf : AttributeGlobalSettings<TSelf>
{
    // Intentionally empty. All persistence and singleton logic is
    // inherited from GlobalSettings<TSelf>.
}
