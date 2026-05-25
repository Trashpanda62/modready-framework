// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Implementation.BaseSettingsContainer<T>. The upstream BUTR MCM
// internal namespace uses this generic base to hold settings instances
// keyed by type. Consumer mods (and the upstream MCM runtime) reference
// it by name at type-load. v0.7.2 adds a generic stub so the CLR's
// type-load step succeeds; method bodies are minimal pass-throughs.

using System;
using System.Collections.Generic;

namespace MCM.Implementation
{
    /// <summary>
    /// Generic container for settings instances of type T. Stub.
    /// </summary>
    public abstract class BaseSettingsContainer<T> where T : class
    {
        protected readonly Dictionary<string, T> _settings = new(StringComparer.Ordinal);

        public virtual T? GetSettings(string id) =>
            _settings.TryGetValue(id, out var s) ? s : null;

        public virtual IEnumerable<T> AllSettings => _settings.Values;

        public virtual bool Register(string id, T settings)
        {
            if (string.IsNullOrEmpty(id) || settings == null) return false;
            _settings[id] = settings;
            return true;
        }
    }
}
