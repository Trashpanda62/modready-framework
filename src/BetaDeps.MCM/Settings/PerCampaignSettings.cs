// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.PerCampaign.PerCampaignSettings<TSelf>
//
// Generic singleton base for per-campaign settings classes (the per-campaign
// counterpart to GlobalSettings<TSelf> / PerSaveSettings<TSelf>).
//
// Phase 1.1 / finding B3 of the 2026-06-10 code review: SettingsRegistry's
// recognized-base list already named this type, but the type itself didn't
// exist in the assembly. Any consumer compiled against real MCMv5 declaring
//
//     public class X : AttributePerCampaignSettings<X> { ... }
//
// got a TypeLoadException the moment the CLR touched X -- the exact failure
// mode documented for the PerSave triad ("the AIInfluence saga",
// PerSaveSettings.cs:18-23). The PerSave stubs fixed that class of crash for
// PerSave consumers; this file completes the PerCampaign side.
//
// Per-campaign semantics upstream: one value set per campaign id. As with
// the v0.7 PerSave stub, BetaDeps does not yet wire a separate per-campaign
// storage scope -- values load/save through the global JSON. Correct scoped
// storage for BOTH PerSave and PerCampaign lands together in Phase 2 (item
// 2.4 / finding H5). What matters here is that the type exists with the
// exact name MCM.Abstractions.Base.PerCampaign.PerCampaignSettings`1 so
// consumer assemblies load without TypeLoadException.

using System;

using BetaDeps.Foundation;

namespace MCM.Abstractions.Base.PerCampaign;

public abstract class PerCampaignSettings<TSelf> : BasePerCampaignSettings
    where TSelf : PerCampaignSettings<TSelf>
{
    private const string Tag = "MCM.PerCampaignSettings";

    private static readonly object _instanceLock = new();
    private static TSelf? _instance;

    /// <summary>
    /// Singleton instance for the current campaign. Constructed on first
    /// access; reset by Reset() when a campaign ends or a save is loaded.
    /// </summary>
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
                    // Phase 2.4 / H5: SettingsStorage routes per-campaign
                    // instances to Configs\ModSettings\PerCampaign\<campaignId>\;
                    // the tracker resets this singleton on campaign start/end.
                    MCM.Internal.ScopedSettingsTracker.Register(
                        $"percampaign:{typeof(TSelf).FullName}", Reset);
                    try { MCM.Internal.SettingsStorage.Load(_instance, _instance.Id); }
                    catch (Exception ex)
                    {
                        DiagLog.LogCaught(Tag, $"Load({typeof(TSelf).FullName})", ex);
                    }
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"Instance get for {typeof(TSelf).FullName}", ex);
                    try { _instance = Activator.CreateInstance<TSelf>(); } catch { }
                }
                return _instance!;
            }
        }
    }

    /// <summary>Persist current values.</summary>
    public void Save() => MCM.Internal.SettingsStorage.Save(this, Id);

    /// <summary>Force a re-read on next Instance access. Call when a campaign
    /// loads so the singleton picks up that campaign's data.</summary>
    public static void Reset()
    {
        lock (_instanceLock) { _instance = null; }
    }
}
