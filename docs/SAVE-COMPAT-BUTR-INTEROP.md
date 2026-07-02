# Save-compat interop spec: upstream BUTR ButterLib / MCM v5 → ModReady

Status: implemented (feature/music-picker-v1.1 era, 2026-07-01).
Trigger: Nexus report by iOrNoTi (30 Jun 2026) — saves created with the
upstream BUTR dependencies freeze on the loading screen (or Continue bounces
back to the main menu) when ModReady's clean-room modules are swapped in;
new campaigns work.

This document is the clean-room interop spec: every number and layout below
was extracted from **observed save data** (binary type-id tables of real .sav
files created under upstream deps across three different eras/modlists) and
from the decompiled TaleWorlds save-system engine code, then cross-checked
against BUTR's public repos only to name things. No upstream code was copied.

## Why upstream saves failed to load

TaleWorlds' save format references types by **numeric SaveId only** (no type
names): `TypeSaveId(int)` for classes, `ContainerSaveId(containerType,
keyId[,valueId])` for containers. `LoadContext.Load` deserializes the ENTIRE
object graph — including behavior-data blobs of behaviors that no longer
exist — inside one try/catch; any unresolvable SaveId throws, is caught,
`Debug.Print`ed, and `Load` returns false. The campaign loading screen then
sits forever (no crash UI) and the launcher's Continue falls back to the
main menu. Exactly the reported symptom.

Dumping the type-id tables of real upstream-created saves (Jan 2026 pure
upstream, May 2026 upstream/CREST, across unrelated modlists) shows **three
ButterLib/MCM-originated ids present in every one of them**, none of which
the ModReady modules registered:

| SaveId (string form)              | What it is                                          |
|-----------------------------------|-----------------------------------------------------|
| `C(Dictionary)-(222444701,30000)` | ButterLib ObjectSystem `Vars`  — Dict<DataKey, object> |
| `C(Dictionary)-(222444701,30020)` | ButterLib ObjectSystem `Flags` — Dict<DataKey, bool>   |
| `C(Dictionary)-(30021,30021)`     | MCM v5 per-save settings `_settings` — Dict<string, string> |

(Basic-type ids are engine-defined at base 30000: object=30000, bool=30020,
string=30021. 222444701 = ButterLib's DataKey class id, see below.)

ModReady-era saves contain none of these — and, critically, nothing in the
ModReady loadout **defines** them, so loading any upstream save fails.

## Upstream surface that must be matched

### 1. ButterLib ObjectSystem storage behavior

Upstream ButterLib ships a campaign behavior (`MBObjectExtensionDataStore`)
that persists per-MBObject extension data. Its save surface:

- Behavior data key (`CampaignBehaviorBase.StringId`):
  `Bannerlord.ButterLib.Implementation.ObjectSystem.MBObjectExtensionDataStore`
  (upstream Harmony-patches the CampaignBehaviorBase ctor to use
  `Type.FullName` for non-official modules; TW's own
  `CampaignBehaviorDataStore.LoadBehaviorData` has a fuzzy fallback that
  matches any stored key *containing* `Type.Name`, so Name↔FullName
  mismatches self-heal — we still pass the exact FullName explicitly).
- SyncData entries: `"Vars"` (Dictionary<DataKey, object?>) and `"Flags"`
  (Dictionary<DataKey, bool>). Upstream declares the fields as
  ConcurrentDictionary and patches `TypeExtensions.IsContainer` so they map
  to `ContainerType.Dictionary`; the resulting on-disk ids are identical to
  plain `Dictionary<,>`, which is what we use (no engine patch needed).
- `DataKey`: a private class registered via a nested
  `SaveableTypeDefiner` with base id **222_444_700**, class local id **1**
  (→ 222444701). Field layout (member ids are `(classLevel, localSaveId)`
  pairs; names are NOT stored in saves):
  - `[SaveableField(0)] MBGUID ObjectId`
  - `[SaveableField(1)] string? Key`
  - class level 2 (direct subclass of object).
- Also synced when saving: `"keepReferences"` (List<MBObjectBase>) from the
  MBObjectKeeper service. Write-only upstream (never read back on load);
  container registered by upstream's `OSSaveableTypeDefiner`. We register
  the container for load-compat but do not write the entry.

### 2. ButterLib public definer base class

`Bannerlord.ButterLib.ButterLibSaveableTypeDefiner` is a public **abstract**
subclass of `TaleWorlds.SaveSystem.SaveableTypeDefiner` with
`protected ctor(int saveBaseId) : base(2002018000 + saveBaseId)`
(2_000_000_000 + 2018 [upstream's NexusMods id] × 1000). Consumer mods
derive their own definers from it. The previous ModReady stub did NOT derive
from SaveableTypeDefiner, so any consumer definer built on it was silently
dropped by the engine's definer discovery (`DefinitionContext.CollectTypes`
instantiates every non-abstract SaveableTypeDefiner subclass in every
assembly that references TaleWorlds.SaveSystem) — all of that mod's saveable
types would be unresolvable. Fixed by making the base class real.

Known upstream id reservations under that base: CampaignIdentifier used
offsets 00–04 (subsystem deleted upstream in the e1.6.0 era — old ids do not
appear in any modern save we inspected, so they are NOT re-registered);
ObjectSystem uses offset **5** (`OSSaveableTypeDefiner`, registers the
`List<MBObjectBase>` container only).

### 3. MCM v5 per-save settings behavior

Upstream MCM v5 persists per-save settings inside the save file via
`MCM.Internal.GameFeatures.PerSaveCampaignBehavior`:

- Behavior data key: `MCM.Internal.GameFeatures.PerSaveCampaignBehavior`
  (FullName, via ButterLib's ctor patch; same fuzzy-fallback note applies).
- SyncData entry: `"_settings"` — `Dictionary<string, string>` where
  key = `Path.Combine(FolderName, SubFolder, Id)` of the settings object and
  value = flat JSON `{"<propertyDefinitionId>": <value>, ...}` (definition id
  defaults to the property name for attribute-based settings — the same key
  our SettingsStorage writes).
- MCM registers NO saveable types of its own; the Dictionary<string,string>
  container definition came from elsewhere in the upstream loadout. Nothing
  in the ModReady loadout defines it, so we construct it ourselves
  (`ConstructContainerDefinition` is a no-op when a definition already
  exists, so this can never conflict with the engine or other mods).

## Behavior-data lifecycle (why bridge behaviors, not just definers)

`CampaignBehaviorManager.OnBeforeSave` calls
`_behaviorDict.ClearBehaviorData()` and then re-saves data ONLY for
currently-registered behaviors. Orphaned payloads (data whose behavior does
not exist in the current loadout) survive a load but are **destroyed on the
first save**. Registering the type ids alone would make upstream saves load,
but the first ModReady save would wipe upstream's ObjectSystem vars and MCM
per-save settings — silently breaking the "switch back to upstream" path and
any consumer mod relying on that state. Hence ModReady registers live bridge
behaviors that adopt and re-emit the data.

## What ModReady now implements

### ModReady.ButterLib (`Bannerlord.ButterLib.dll`)

- `ButterLibSaveableTypeDefiner` — real public abstract
  `SaveableTypeDefiner`, base id 2002018000 + offset (was a non-derived
  no-op stub).
- `OSSaveableTypeDefiner` (internal, offset 5) — registers
  `List<MBObjectBase>` (skip-if-already-defined).
- `MBObjectExtensionDataStore : CampaignBehaviorBase` (internal), StringId
  `Bannerlord.ButterLib.Implementation.ObjectSystem.MBObjectExtensionDataStore`,
  added to every campaign in `ButterLibSubModule.OnGameStart`. Syncs
  `"Vars"`/`"Flags"` as plain `Dictionary<DataKey, object?>` /
  `Dictionary<DataKey, bool>`; preserves loaded entries verbatim and writes
  them back on save. Nested `DataKey` (fields `[SaveableField(0)] MBGUID`,
  `[SaveableField(1)] string?`) and nested `SavedTypeDefiner`
  (base 222_444_700, class id 1, the two Dictionary containers).
- NOT implemented (deliberate): the public MBObjectBaseExtensions read/write
  API (no ModReady-tested consumer calls it — vars are preserved, not
  serviced), `keepReferences` re-emission (write-only upstream, rebuilt from
  live Keep() calls each session), CampaignIdentifier types (removed
  upstream in 2021, absent from every inspected save), the
  CampaignBehaviorBase ctor StringId patch (TW's fuzzy fallback already
  bridges key styles both ways), and the IsContainer/DefinitionContext
  engine patches (not needed when using plain Dictionary and the guarded
  ConstructContainerDefinition).

### ModReady.MCM (`MCMv5.dll`)

- `PerSaveCampaignBehavior : CampaignBehaviorBase` (internal,
  `MCM.Internal`), StringId
  `MCM.Internal.GameFeatures.PerSaveCampaignBehavior`, added in
  `ModReadyMCMSubModule.OnGameStart`. Bridges the save-file payload to
  ModReady's per-save JSON store (`Configs\ModSettings\PerSave\<campaignId>\`):
  - on load: every parseable payload entry is written through to
    `PerSave\<campaignId>\<Id>.json` (Id = last path segment of the payload
    key; last-writer-wins, atomic write with .bak rotation). The existing
    lazy `PerSaveSettings<T>.Instance` load path then picks the values up
    from disk — no new load path.
  - on save: refreshes each existing payload entry from the current
    campaign's per-save JSON file of the same Id (unknown/unmatched keys are
    preserved verbatim), then syncs. Settings first created under ModReady
    (no upstream key to update) intentionally stay JSON-only: their upstream
    `FolderName/SubFolder` key half is unknowable, and upstream treats a
    missing entry as defaults — same as today.
  - nested `SaveCompatTypeDefiner` constructs the
    `Dictionary<string, string>` container definition (guarded, see above).
- Value-shape note: both sides serialize settings as flat
  `{"<id>": value}` JSON keyed by property name for attribute-based
  settings, so payloads round-trip. Fluent/custom-id properties whose
  definition id differs from the CLR property name fall back to defaults
  across the boundary (accepted, documented limitation).

## Verification recipe

1. `python tools\dump_save_ids.py <save.sav>` — dumps the type-id
   table (the script parses the .sav container: `[int32 metaSize][JSON
   metadata][raw-deflate GameData]`, then the header archive). An
   upstream-created save must show the three ids above; a ModReady save made
   after this change shows the same three (behaviors now write them).
2. Steve's live test (Quick-Test): load a save created with upstream
   Harmony/UIExtenderEx/ButterLib/MCM v5 under ModReady → campaign loads;
   save under ModReady; reload → still loads; (optional) swap upstream deps
   back → save still loads there with per-save MCM values intact.

## Save files inspected (ground truth)

- `save_quick_mjalnoring_danhildr_0.sav` (2026-01-18, pure upstream era)
- `Starter Save.sav` (2026-05-06, upstream + CREST era)
- `saveauto1.sav` (2026-05-03), `saveauto2.sav` (2026-06-14, ModReady era)

All in `C:\Users\Steve\Documents\Mount and Blade II Bannerlord\Game Saves`.
