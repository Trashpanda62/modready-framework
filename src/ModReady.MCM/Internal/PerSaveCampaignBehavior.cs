// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Save-file bridge for upstream MCM v5 per-save settings (v1.0.1 fix for the
// iOrNoTi report: saves created with the upstream BUTR dependencies froze on
// load / lost per-save settings under ModReady).
//
// Upstream MCM v5 persists per-save settings inside the campaign save via a
// behavior whose data entry is:
//     "_settings" : Dictionary<string, string>
//         key   = Path.Combine(FolderName, SubFolder, Id) of the settings
//         value = flat JSON {"<propertyDefinitionId>": <value>, ...}
// Every save created with upstream MCM therefore contains the container id
// C(Dictionary)-(30021,30021) [Dictionary<string,string>]. Nothing in the
// ModReady loadout defined that container, so deserializing such saves
// failed outright (the engine loads orphaned behavior data too, and any
// unresolved save id aborts the whole load). The nested definer below fixes
// that; the behavior itself keeps the payload ALIVE (the engine wipes
// behavior data that has no registered owner on every save) and bridges the
// values into ModReady's per-save JSON store so users keep their per-save
// settings when migrating a campaign in either direction.
// See docs/SAVE-COMPAT-BUTR-INTEROP.md for the observed-surface spec.

using System.Collections.Generic;
using System.Linq;

using ModReady.Foundation;

using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace MCM.Internal;

internal sealed class PerSaveCampaignBehavior : CampaignBehaviorBase
{
    private const string Tag = "MCM.PerSaveBehavior";

    // Upstream keys behavior data by Type.FullName for mod behaviors (via
    // ButterLib's CampaignBehaviorBase ctor patch); passing the exact string
    // reads/writes the same slot in existing saves. TW's LoadBehaviorData
    // fuzzy fallback (matches keys containing Type.Name) covers the rest,
    // which is also why this class keeps the upstream short name.
    private const string UpstreamStringId = "MCM.Internal.GameFeatures.PerSaveCampaignBehavior";

    private Dictionary<string, string>? _settings = new();

    public PerSaveCampaignBehavior() : base(UpstreamStringId) { }

    public override void RegisterEvents() { }

    public override void SyncData(IDataStore dataStore)
    {
        if (dataStore.IsSaving)
        {
            // Pull current values from the JSON store into the payload so
            // the save carries what the user actually configured under
            // ModReady. Keys we can't match keep their loaded value.
            RefreshPayloadFromJsonStore();
        }

        dataStore.SyncData("_settings", ref _settings);
        _settings ??= new Dictionary<string, string>();

        if (dataStore.IsLoading)
        {
            // Save file is the source of truth at load time: write every
            // payload through to Configs\ModSettings\PerSave\<campaignId>\.
            // The lazy PerSaveSettings<T>.Instance path then loads from
            // those files as usual -- no separate load path.
            var imported = 0;
            foreach (var kv in _settings)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (SettingsStorage.ImportPerSavePayload(SettingsIdOf(kv.Key), kv.Value))
                    imported++;
            }
            if (imported > 0)
                DiagLog.Log(Tag, $"imported {imported}/{_settings.Count} per-save payload(s) from the save file");
        }
    }

    private void RefreshPayloadFromJsonStore()
    {
        if (_settings is null || _settings.Count == 0) return;
        foreach (var key in _settings.Keys.ToList())
        {
            var content = SettingsStorage.ReadPerSaveFileContent(SettingsIdOf(key));
            if (content != null)
                _settings[key] = content;
        }
        // Settings first created under ModReady have no upstream payload key
        // to update (their FolderName/SubFolder half is unknowable from the
        // JSON store). They intentionally stay JSON-only; upstream treats a
        // missing entry as defaults. Documented limitation.
    }

    /// <summary>
    /// Upstream payload keys are "FolderName\SubFolder\Id" paths; the
    /// settings id ModReady's JSON store is keyed by is the last segment.
    /// </summary>
    private static string SettingsIdOf(string payloadKey)
    {
        var parts = payloadKey.Split('\\', '/');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
                return parts[i];
        }
        return payloadKey;
    }

    /// <summary>
    /// Guarantees the Dictionary&lt;string,string&gt; container definition
    /// exists in the ModReady loadout (ConstructContainerDefinition is a
    /// no-op when the engine or another mod already defines it). The base id
    /// is irrelevant here -- container save-ids derive purely from the
    /// element type ids and this definer registers no class/struct/enum ids.
    /// </summary>
    private sealed class SaveCompatTypeDefiner : SaveableTypeDefiner
    {
        public SaveCompatTypeDefiner() : base(0) { }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(Dictionary<string, string>));
        }
    }
}
