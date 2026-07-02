// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Save-file compatibility for upstream ButterLib's ObjectSystem storage
// (v1.0.1 fix for the iOrNoTi report: saves created with the upstream BUTR
// dependencies froze on load under ModReady).
//
// Every campaign save created with upstream ButterLib contains two container
// entries written by its per-MBObject extension-data behavior:
//     C(Dictionary)-(222444701,30000)   "Vars"  = Dictionary<DataKey, object?>
//     C(Dictionary)-(222444701,30020)   "Flags" = Dictionary<DataKey, bool>
// (222444701 = DataKey class id, registered at base 222_444_700 + 1; 30000 /
// 30020 = the engine's basic ids for object / bool.) The engine deserializes
// the ENTIRE save graph -- orphaned behavior data included -- so if those ids
// have no registered definition the whole load fails: the loading screen
// hangs and the launcher's Continue bounces to the main menu.
//
// This file registers definitions with the exact ids and member layout the
// engine finds in those saves (ids and layout taken from observed save data;
// see docs/SAVE-COMPAT-BUTR-INTEROP.md) and adds a campaign behavior that
// adopts the payload and re-emits it on save. The behavior matters because
// CampaignBehaviorManager.OnBeforeSave clears ALL stored behavior data and
// re-saves only currently-registered behaviors -- without a live owner the
// upstream payload would be destroyed on the first ModReady save, silently
// wiping mod state for users who later switch back to upstream.
//
// Notes on shape fidelity:
//  - Upstream declares the dictionaries as ConcurrentDictionary and patches
//    the engine's TypeExtensions.IsContainer to map that to
//    ContainerType.Dictionary. Plain Dictionary<,> produces byte-identical
//    container save-ids without any engine patch, so that is what we use;
//    saves round-trip in both directions.
//  - Member ids in save files are (classLevel, localSaveId) pairs -- field
//    NAMES are never stored -- so DataKey only has to be a direct subclass
//    of object with [SaveableField(0)] MBGUID and [SaveableField(1)] string.
//  - "keepReferences" (List<MBObjectBase>, written by upstream's
//    MBObjectKeeper) is write-only upstream: it is never read back on load
//    and is rebuilt from live Keep() calls each session. We register the
//    container so upstream saves resolve, but do not write the entry.

using System.Collections.Generic;

using TaleWorlds.CampaignSystem;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace Bannerlord.ButterLib.ObjectSystem
{
    /// <summary>
    /// Clean-room stand-in for upstream ButterLib's per-MBObject extension
    /// data behavior. Preserves the "Vars"/"Flags" payload of upstream saves
    /// across ModReady sessions and keeps ModReady saves loadable under
    /// upstream. Registered for every campaign by ButterLibSubModule.
    /// </summary>
    internal sealed class MBObjectExtensionDataStore : CampaignBehaviorBase
    {
        // Upstream keys behavior data by Type.FullName for mod behaviors (it
        // patches the CampaignBehaviorBase ctor); passing that exact string
        // makes our reads/writes hit the same dictionary slot. TW's own
        // LoadBehaviorData fuzzy fallback (matches keys containing Type.Name)
        // covers any residual mismatch in either direction.
        private const string UpstreamStringId =
            "Bannerlord.ButterLib.Implementation.ObjectSystem.MBObjectExtensionDataStore";

        private Dictionary<DataKey, object?>? _vars = new();
        private Dictionary<DataKey, bool>? _flags = new();

        public MBObjectExtensionDataStore() : base(UpstreamStringId) { }

        public override void RegisterEvents() { }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Vars", ref _vars);
            _vars ??= new Dictionary<DataKey, object?>();

            dataStore.SyncData("Flags", ref _flags);
            _flags ??= new Dictionary<DataKey, bool>();
        }

        /// <summary>
        /// Key of one extension-data entry. Save-format surface: class id
        /// 222444701, field 0 = MBGUID, field 1 = string. Value equality so
        /// logically-equal keys collapse when the engine refills the
        /// dictionaries on load.
        /// </summary>
        private sealed class DataKey
        {
            // Both fields are populated by the save system via reflection
            // (FormatterServices.GetUninitializedObject + field writes); the
            // initializers only exist to silence CS0649.
            [SaveableField(0)]
            internal MBGUID ObjectId = default;

            [SaveableField(1)]
            internal string? Key = null;

            public override bool Equals(object? obj) =>
                obj is DataKey other && ObjectId.Equals(other.ObjectId) && string.Equals(Key, other.Key, System.StringComparison.Ordinal);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (ObjectId.GetHashCode() * 397) ^ (Key?.GetHashCode() ?? 0);
                }
            }

            public override string ToString() => $"{ObjectId}::{Key}";
        }

        /// <summary>
        /// Registers DataKey and the two payload containers under the exact
        /// ids found in upstream-created saves. Discovered and instantiated
        /// automatically by the engine's DefinitionContext.CollectTypes.
        /// </summary>
        private sealed class SavedTypeDefiner : SaveableTypeDefiner
        {
            // Upstream's reserved base for this subsystem (observed in save
            // data as DataKey id 222444701 = base + local id 1).
            public SavedTypeDefiner() : base(222_444_700) { }

            protected override void DefineClassTypes()
            {
                AddClassDefinition(typeof(DataKey), 1);
            }

            protected override void DefineContainerDefinitions()
            {
                // Container save-ids derive from the element ids, so these
                // resolve to C(Dictionary)-(222444701,30000) and
                // C(Dictionary)-(222444701,30020) -- the two ids every
                // upstream-created save contains.
                ConstructContainerDefinition(typeof(Dictionary<DataKey, object?>));
                ConstructContainerDefinition(typeof(Dictionary<DataKey, bool>));
            }
        }
    }

    /// <summary>
    /// Clean-room equivalent of upstream's ObjectSystem definer (offset 5
    /// under the ButterLib base id): registers the List&lt;MBObjectBase&gt;
    /// container upstream's MBObjectKeeper writes as "keepReferences" on
    /// every save. ConstructContainerDefinition is a no-op when the engine
    /// (or another mod) already defines the container.
    /// </summary>
    internal sealed class OSSaveableTypeDefiner : ButterLibSaveableTypeDefiner
    {
        public OSSaveableTypeDefiner() : base(5) { }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(List<MBObjectBase>));
        }
    }
}
