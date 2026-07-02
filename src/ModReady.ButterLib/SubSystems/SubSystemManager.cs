// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SubSystemManager is the central registry of ISubSystem instances. ButterLib
// builds it during OnSubModuleLoad, enables each subsystem in registration
// order, and exposes lookups for runtime queries / the MCM settings tab.

using System;
using System.Collections.Generic;
using System.Linq;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.SubSystems;

public static class SubSystemManager
{
    private const string Tag = "SubSystemManager";

    private static readonly object _gate = new();
    private static readonly List<ISubSystem> _subSystems = new();
    // S3: ids that SubSystemPersistence.Load() found saved as disabled.
    // EnableAll() skips subsystems in this set so they never start up.
    private static readonly HashSet<string> _deferredDisabled = new(StringComparer.Ordinal);

    /// <summary>All registered subsystems in registration order.</summary>
    public static IReadOnlyList<ISubSystem> All
    {
        get { lock (_gate) { return _subSystems.ToArray(); } }
    }

    /// <summary>Register a subsystem. Idempotent on Id.</summary>
    public static void Register(ISubSystem subSystem)
    {
        if (subSystem == null) throw new ArgumentNullException(nameof(subSystem));
        lock (_gate)
        {
            if (_subSystems.Any(s => string.Equals(s.Id, subSystem.Id, StringComparison.Ordinal)))
            {
                DiagLog.Log(Tag, $"subsystem '{subSystem.Id}' already registered; ignoring duplicate");
                return;
            }
            _subSystems.Add(subSystem);
            DiagLog.Log(Tag, $"registered subsystem '{subSystem.Id}' ({subSystem.Name})");
        }
    }

    /// <summary>Look up a subsystem by Id.</summary>
    public static ISubSystem? Get(string id)
    {
        lock (_gate)
        {
            return _subSystems.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Mark a subsystem as deferred-disabled (or clear that mark). Called by
    /// SubSystemPersistence.Load() before EnableAll() runs so the user's saved
    /// toggle state is applied without racing against OnEnable().
    /// </summary>
    public static void SetDeferredEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            if (!enabled) _deferredDisabled.Add(id);
            else _deferredDisabled.Remove(id);
        }
    }

    /// <summary>Enable every registered subsystem that isn't deferred-disabled. Called by ButterLibSubModule.</summary>
    public static void EnableAll()
    {
        foreach (var s in All)
        {
            lock (_gate)
            {
                if (_deferredDisabled.Contains(s.Id))
                {
                    DiagLog.Log(Tag, $"{s.Id} skipped (deferred-disabled by user preference)");
                    continue;
                }
            }
            try { s.Enable(); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"EnableAll/{s.Id}", ex); }
        }
    }
}
