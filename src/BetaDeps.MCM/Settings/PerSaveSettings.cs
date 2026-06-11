// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.PerSave.PerSaveSettings<TSelf>
//
// Generic singleton base for per-save settings classes (the per-save
// counterpart to GlobalSettings<TSelf>). Used by consumer mods like:
//   - Detailed Character Creation's DccPerSaveSettings (the SaveInstance
//     getter referenced in runtime.log at 22:21:12)
//   - Bannerlord.Diplomacy and DiplomacyNavalDLCPatch (via MCM PerSave
//     prefixes on OnGameInitializationFinished)
//   - zzCharacterCreation (CharacterDevelopmentEditor) for per-save state
//
// Per-save semantics: values are persisted with the save file itself, not
// in a separate Configs\ModSettings JSON. The Instance singleton is reset
// when a save loads / a new campaign starts so the data follows the save.
// Pattern mirrors GlobalSettings<TSelf>; differs only in storage scope.
//
// IMPORTANT (learned from the AIInfluence saga): consumer-mod assemblies
// reference this type at JIT/load time. If the type doesn't exist with
// the exact name MCM.Abstractions.Base.PerSave.PerSaveSettings`1, the CLR
// throws TypeLoadException when the mod's static initializer runs, and
// the game crashes during campaign init. The class itself can be a thin
// stub — what matters is that it exists with the right name + arity.

using System;

using BetaDeps.Foundation;

namespace MCM.Abstractions.Base.PerSave;

public abstract class PerSaveSettings<TSelf> : BasePerSaveSettings
    where TSelf : PerSaveSettings<TSelf>
{
    private const string Tag = "MCM.PerSaveSettings";

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
                    // Phase 2.4 / H5: SettingsStorage now routes per-save
                    // instances to Configs\ModSettings\PerSave\<campaignId>\,
                    // and the tracker resets this singleton on campaign
                    // start/end so the next access reloads under the new
                    // campaign's path instead of bleeding values across.
                    MCM.Internal.ScopedSettingsTracker.Register(
                        $"persave:{typeof(TSelf).FullName}", Reset);
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

    /// <summary>Force a re-read on next Instance access. Call when a save
    /// loads so the singleton picks up the new campaign's per-save data.</summary>
    public static void Reset()
    {
        lock (_instanceLock) { _instance = null; }
    }
}
