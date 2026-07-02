// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Stub interfaces for Bannerlord.ButterLib.ObjectSystem.*. Consumer mods
// reference these at type-load time; we ship them as empty contracts so
// the CLR doesn't throw TypeLoadException when those mods JIT. Real
// extension-data persistence isn't wired up yet -- no implementation is
// registered in the DI container, so resolving any of these returns null
// and (Phase 2C) emits a CompatWarn from GenericServiceProvider, making
// the gap visible in modready-compat-warnings.log.
//
// v0.7.2: added IMBObjectExtensionDataStore, IMBObjectFinder, IMBObjectKeeper.

using System;
using System.Collections.Generic;

namespace Bannerlord.ButterLib.ObjectSystem
{
    /// <summary>
    /// Per-object extension data storage. Consumer mods attach arbitrary
    /// key/value state to MBObjects (Hero, Settlement, etc.) and read it
    /// back later. Stub -- we hold the data in a per-process dictionary.
    /// </summary>
    public interface IMBObjectExtensionDataStore
    {
        T? GetVariable<T>(object holder, string key);
        void SetVariable<T>(object holder, string key, T value);
        void RemoveVariable(object holder, string key);
    }

    /// <summary>Lookup MBObject instances by id. Stub.</summary>
    public interface IMBObjectFinder
    {
        object? Find(string stringId);
    }

    /// <summary>
    /// Keeper preserves transient MBObjects across save/load. Stub -- no
    /// persistence semantics yet.
    /// </summary>
    public interface IMBObjectKeeper
    {
        void Keep(object obj);
        void Forget(object obj);
        IEnumerable<object> All();
    }
}
