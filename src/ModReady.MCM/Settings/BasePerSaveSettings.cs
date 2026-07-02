// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Per-save settings: values are persisted with the save file itself, not in
// a separate JSON. Used when a setting needs to travel with the save (e.g.
// difficulty modifiers, mod-specific flags that should match exactly when
// the save is loaded later).

using MCM.Abstractions;

namespace MCM.Abstractions.Base.PerSave;

public abstract class BasePerSaveSettings : BaseSettings { }
