// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.BaseSettings -- alias for MCM.Abstractions.BaseSettings.
// A handful of consumer mods (DismembermentPlus, etc.) reference this exact
// namespaced name in typerefs or generic constraints. Without this type the
// CLR throws TypeLoadException when JIT touches those refs, even though our
// MCM.Abstractions.BaseSettings would otherwise satisfy the semantic intent.
// We expose this as a thin abstract subclass so the typeref resolves; any
// further behavior is inherited from the real BaseSettings.

namespace MCM.Abstractions.Base;

public abstract class BaseSettings : MCM.Abstractions.BaseSettings { }
