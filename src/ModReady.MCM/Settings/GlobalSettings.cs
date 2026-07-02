// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.Global.GlobalSettings<TSelf>
//
// Generic singleton base for MCM-driven settings classes. Used by some
// consumer mods directly; AttributeGlobalSettings<TSelf> inherits from
// this and adds attribute-driven panel generation.
//
// Critical design points (learned the hard way -- see runtime.log dated
// 2026-05-17 for the IDontCare TextObject.ToString boot-loop):
//
//   1. AttributeGlobalSettings<TSelf> INHERITS from this class. That
//      gives consumer mods like IDontCare a transitive inheritance from
//      GlobalSettings<TSelf>, satisfying the F-bounded `TSelf : GlobalSettings<TSelf>`
//      constraint when other code references GlobalSettings<TheirType>.
//      Without that transitive chain, every call into a Harmony prefix
//      that touches IDontCareMenu trips a TypeLoadException, and since
//      IDontCare hooks TextObject.ToString() (which the engine calls on
//      every UI label), the game can't render its main menu.
//
//   2. NO `new()` constraint. Some consumer-mod settings classes have
//      non-public default ctors. We use Activator.CreateInstance<TSelf>()
//      so the runtime check fails cleanly with a real exception instead
//      of the CLR rejecting the entire type-load at JIT time.

using System;

using ModReady.Foundation;

namespace MCM.Abstractions.Base.Global;

public abstract class GlobalSettings<TSelf> : BaseGlobalSettings
    where TSelf : GlobalSettings<TSelf>
{
    private const string Tag = "MCM.GlobalSettings";

    private static readonly object _instanceLock = new();
    private static TSelf? _instance;

    /// <summary>Singleton instance. First access loads from JSON (or writes defaults).</summary>
    public static TSelf Instance
    {
        get
        {
            if (_instance != null) return _instance;
            lock (_instanceLock)
            {
                if (_instance != null) return _instance;
                try
                {
                    _instance = Activator.CreateInstance<TSelf>();
                    MCM.Internal.SettingsStorage.Load(_instance, _instance.Id);
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"Instance get for {typeof(TSelf).FullName}", ex);
                    // Best-effort defaults so the singleton is never null
                    try { _instance = Activator.CreateInstance<TSelf>(); } catch { }
                }
                return _instance!;
            }
        }
    }

    /// <summary>Persist current values to the JSON file.</summary>
    public void Save() => MCM.Internal.SettingsStorage.Save(this, Id);

    /// <summary>Force a re-read from the JSON file on next Instance access.</summary>
    public static void Reset()
    {
        lock (_instanceLock) { _instance = null; }
    }
}
