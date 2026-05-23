// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SubSystemManager is the central registry of ISubSystem instances. ButterLib
// builds it during OnSubModuleLoad, enables each subsystem in registration
// order, and exposes lookups for runtime queries / the MCM settings tab.

using System;
using System.Collections.Generic;
using System.Linq;

using BetaDeps.Foundation;

namespace Bannerlord.ButterLib.SubSystems;

public static class SubSystemManager
{
    private const string Tag = "SubSystemManager";

    private static readonly object _gate = new();
    private static readonly List<ISubSystem> _subSystems = new();

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

    /// <summary>Enable every registered subsystem. Called by ButterLibSubModule.</summary>
    public static void EnableAll()
    {
        foreach (var s in All)
        {
            try { s.Enable(); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"EnableAll/{s.Id}", ex); }
        }
    }
}
