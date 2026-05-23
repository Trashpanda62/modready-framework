// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// BaseGlobalSettings -- non-generic base for the AttributeGlobalSettings<T>
// family. Consumer mods occasionally cast through this when they want to
// hold a heterogeneous list of settings without committing to the concrete
// generic argument.

using MCM.Abstractions;

namespace MCM.Abstractions.Base.Global;

public abstract class BaseGlobalSettings : BaseSettings { }
