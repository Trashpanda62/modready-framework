# BetaDeps Direct-API Guide

**Audience:** Bannerlord mod authors who want to consume BetaDeps directly
instead of through the bundled BUTR-impersonation aliases
(`Bannerlord.Harmony`, `Bannerlord.UIExtenderEx`, `Bannerlord.ButterLib`,
`Bannerlord.MBOptionScreen`).

**Why bother:**
- The four alias modules exist for backward compatibility — they let mods
  built against the original BUTR stack load against BetaDeps without
  recompilation. Eventually they go away.
- Code written against the `BetaDeps.*` namespaces gets stable surfaces
  that don't have to fingerprint upstream BUTR for ABI compatibility.
- You get access to BetaDeps-native conveniences (PatchShield install
  hooks, `SafeBind`, `DiagLog`) that the alias surface intentionally
  doesn't expose.

**Status as of v1.0.0:** the BUTR-impersonation surface and the
BetaDeps-native surface co-exist. v1.0.0 adds the **framework-core**
primitives (EventBus, ModConflictDetector, ProfileManager, PerfProfiler) —
see **[Module 7: BetaDeps.Framework](#module-7-betadepsframework-v10)** below.
The aliases stay supported for existing mods forever. The pre-v2.0 public
API surfaces documented here are unchanged since v0.7.3; two structural
changes since then are worth knowing:

- **v0.8 — the four aliases are now real standalone modules.**
  `Bannerlord.Harmony`, `Bannerlord.UIExtenderEx`, `Bannerlord.ButterLib`,
  and `Bannerlord.MBOptionScreen` each ship in their own module folder and
  load (in dependency order) before `BetaDeps`. They're also available as
  individual optional downloads. This doesn't change how you reference
  BetaDeps — a consumer mod still lists `BetaDeps` (or any of the four) as
  a `DependedModule` and binds by name — but it's why those folders exist
  separately in a fresh install.
- **v0.9.0 — the Mod Configuration screen was rebuilt** as a single
  scrollable list with a searchable mod sidebar, collapsible groups,
  per-row sliders/checkboxes, and presets. This is purely the in-game UI;
  the settings-builder API (Global / PerCampaign / PerSave + fluent
  builder) you target is unchanged.

v0.7.3 added SaveShield as the second defensive layer alongside
PatchShield, with public APIs for mod authors who want to query
diagnostic state from their own code (see
[SaveShield](#saveshield-engine-entry-defensive-layer) below).

---

## Module layout

A BetaDeps installation under `Modules\BetaDeps\` ships these assemblies:

| Assembly                | Purpose                                              |
|-------------------------|------------------------------------------------------|
| `BetaDeps.Foundation`   | Logging, version probing, PatchShield, reflection    |
| `BetaDeps.Harmony`      | Harmony 2.x re-export + SafeBind sigsafe patcher     |
| `BetaDeps.UIExtenderEx` | UIExtender, prefab patches, view-model mixins        |
| `BetaDeps.ButterLib`    | HotKeys, sub-systems, exception handler, MBEvents    |
| `BetaDeps.MCM`          | Settings (Global / PerCampaign / PerSave), fluent builder |
| `BetaDeps.Module`       | SubModule entry, in-game Mod Config screen           |

Every consumer mod that needs any of the above lists `BetaDeps` as a
`DependedModule` and that's it — no per-library dependency.

```xml
<DependedModules>
  <DependedModule Id="BetaDeps" />
</DependedModules>
```

---

## Module 1: BetaDeps.Foundation

Diagnostic + bootstrap layer. Every BetaDeps-native consumer touches this.

### Logging

```csharp
using BetaDeps.Foundation;

DiagLog.Log("MyMod", "starting OnSubModuleLoad");
DiagLog.LogCaught("MyMod", "MyPatch.Prefix", ex);
```

`DiagLog.Log(tag, message)` writes to `Modules\BetaDeps\runtime.log`.
`DiagLog.LogCaught(tag, where, ex)` formats an exception consistently so
your entries match the rest of the log. There is no log4net / Serilog
configuration to set up — the path resolves itself even when your assembly
is loaded from an alias bin folder.

The same log surface is also reachable through `RuntimeLog.Write(...)` /
`RuntimeLog.WriteException(...)` if you want to bypass `DiagLog`'s tag
formatting.

`DiagLog.VerboseBinding = true;` turns on extra binding diagnostics (used
by `SafeBind` and `PatchShield`). Off by default.

### Version probing

```csharp
using BetaDeps.Foundation;

if (VersionProbe.Branch == GameBranch.Beta) {
    // e1.4.x and above — apply sigsafe binds
} else {
    // public branch (e1.3.x) — fast path
}

int major = VersionProbe.Major;   // e.g. 1
int minor = VersionProbe.Minor;   // e.g. 4
bool isBeta = VersionProbe.IsBeta;
```

Cached after first call. Reads `ApplicationVersion` from
`TaleWorlds.Library` directly — does not require BUTR.

### PatchShield (auto, but you can opt out)

`PatchShield` installs itself at every lifecycle hook. Consumer mods don't
need to call into it. The behavior:

- A generic Harmony finalizer is attached to every currently-patched
  method.
- If a downstream prefix throws `MissingMethodException`,
  `MissingFieldException`, or `TypeLoadException`, the finalizer swallows
  the exception, synthesizes a default `__result`, and unpatches the
  offending prefix so it doesn't replay.

If you're debugging your own mod and you *want* those exceptions to
surface unmodified, the user can drop a
`Modules\BetaDeps\patchshield-disabled.flag` file (or click `Toggle
PatchShield` in Mod Config) and PatchShield will skip the install on the
next launch.

```csharp
// In your debug build:
if (PatchShield.IsDisabled()) {
    DiagLog.Log("MyMod", "PatchShield is off — exceptions will surface raw");
}
```

`PatchShield.Install()` is callable but already runs at multiple lifecycle
points. You only need to call it if you're installing Harmony patches at
an unusual time (e.g. from a coroutine after the game is mid-mission).

#### PatchShield: per-mod owner counts (v0.7.3+)

When PatchShield auto-unpatches a prefix, it records the Harmony owner ID
of the patch (which by convention is the mod's namespace). You can query
the aggregate counts:

```csharp
foreach (var kv in PatchShield.UnpatchedOwnerCounts) {
    DiagLog.Log("MyMod", $"{kv.Key}: {kv.Value} patches unpatched this session");
}
```

Useful if your mod wants to surface a UI panel like "these mods are
currently bleeding patches" or "X mod has been unstable for the last N
sessions."

### SaveShield (engine-entry defensive layer)

SaveShield is PatchShield's sibling: it wraps engine-level entry points
where consumer-mod handlers run during save deserialization and battle
init, rather than inside Harmony prefixes. Currently shielded:

- `TaleWorlds.Core.MBSaveLoad.LoadSaveGameData`
- `TaleWorlds.SaveSystem.SaveManager.Load` (all overloads)
- `SandBox.SandBoxSaveHelper.LoadGameAction`
- `TaleWorlds.MountAndBlade.MissionState.FinishMissionLoading`
- `TaleWorlds.MountAndBlade.Mission.SetMissionMode`
- `TaleWorlds.MountAndBlade.Mission.OnInitialize`
- `TaleWorlds.MountAndBlade.Mission.SpawnTroop`

When a non-engine frame throws `MissingMethodException`,
`MissingFieldException`, `TypeLoadException`, or a duplicate-key
`ArgumentException` from one of those, SaveShield writes a full
diagnostic to `runtime.log` and (by default) drops the throw so the load
continues.

#### Counters

```csharp
using BetaDeps.Foundation;

DiagLog.Log("MyMod", $"Methods shielded:       {SaveShield.ShieldedCount}");
DiagLog.Log("MyMod", $"Duplicate-key hits:     {SaveShield.DuplicateKeyHits}");
DiagLog.Log("MyMod", $"Other load failures:    {SaveShield.OtherFailureHits}");
DiagLog.Log("MyMod", $"Exceptions swallowed:   {SaveShield.SwallowedCount}");
DiagLog.Log("MyMod", $"Swallow-mode enabled:   {SaveShield.IsSwallowEnabled()}");
```

#### Reading the ring buffer

The most recent failures are in `SaveShield.RecentFailures`
(`IReadOnlyList<FailureRecord>`, newest first, capped at 20):

```csharp
var last = SaveShield.LastFailure;
if (last != null && last.CulpritAssembly != "MyMod") {
    // Surface a UI dialog naming the other mod
    InformationManager.DisplayMessage(new InformationMessage(
        $"Another mod is failing: {last.CulpritAssembly} threw {last.ExceptionType}"));
}
```

#### FailureRecord structure

Each `FailureRecord` carries:

| Field                 | Type            | What it is                                        |
|-----------------------|-----------------|---------------------------------------------------|
| `When`                | `DateTime`      | UTC timestamp                                     |
| `Category`            | `string`        | `SAVE-LOAD` / `MISSION-INIT` / `FAILURE`          |
| `OwnerType`           | `string`        | TaleWorlds type that was shielded                 |
| `OwnerMethod`         | `string`        | TaleWorlds method that caught the throw           |
| `ExceptionType`       | `string`        | e.g. `System.MissingMethodException`              |
| `Message`             | `string`        | exception message                                 |
| `ThrowSiteType`       | `string`        | `ex.TargetSite.DeclaringType.FullName`            |
| `ThrowSiteMethod`     | `string`        | `ex.TargetSite.Name`                              |
| `CulpritAssembly`     | `string`        | deepest non-engine assembly in stack              |
| `CulpritFrame`        | `string`        | full frame description for that assembly          |
| `CulpritAssemblyPath` | `string`        | DLL path on disk                                  |
| `IsDuplicateKey`      | `bool`          | true if the exception is the "same key" pattern   |
| `StackTraceRaw`       | `string`        | `ex.StackTrace`                                   |
| `ParsedFrames`        | `List<string>`  | walked via `System.Diagnostics.StackTrace`        |
| `FinalizerCallChain`  | `List<string>`  | call chain that led TO the patched method         |
| `CurrentSignatures`   | `List<string>`  | current API overloads (if any matched)            |
| `ImportMatches`       | `List<string>`  | TaleWorlds.\* member refs in CULPRIT DLL          |
| `CulpritManifest`     | `ModManifest?`  | culprit mod's SubModule.xml + DLL refs            |
| `FirstArgSummary`     | `string`        | first arg (e.g. save name or MissionMode value)   |

And three render methods:

```csharp
last.ToLogBlock();         // the human-readable runtime.log block format
last.ToMarkdownSnippet();  // GitHub-issue-ready markdown table + stack
last.ToJsonObject();       // compact JSON (no Newtonsoft dependency)
```

#### Swallow-mode toggle

By default SaveShield drops the throw so the game keeps running. Users
can opt out via `Toggle SaveShield Swallow` in Mod Config (creates
`saveshield-swallow-disabled.flag` in `Modules\BetaDeps\`). Programmatic
read/write:

```csharp
bool nowEnabled = SaveShield.ToggleSwallow();
if (!SaveShield.IsSwallowEnabled()) {
    DiagLog.Log("MyMod", "swallow-mode is off -- exceptions will crash unmodified");
}
```

If your mod author build wants exceptions to surface raw for debugging,
ship a script that writes the disable flag on first launch and your
testers don't have to click anything.

#### ModManifest

When SaveShield identifies a CULPRIT mod, it probes the mod's manifest:

```csharp
public sealed class ModManifest {
    public string ModFolder { get; set; }
    public string ModId { get; set; }
    public string ModName { get; set; }
    public string ModVersion { get; set; }       // from SubModule.xml <Version>
    public string ModAuthor { get; set; }
    public string AssemblyName { get; set; }
    public string AssemblyVersion { get; set; }  // from DLL metadata
    public string AssemblyLocation { get; set; }
    public List<string> DependedModules { get; }
    public List<string> TaleWorldsReferences { get; } // "TaleWorlds.Core v1.0.0.0" etc.

    public IEnumerable<string> ToLines();
    public string ToJsonObject();
}
```

#### Failed-mods catalog

Append-only ledger at `Modules\BetaDeps\failed-mods-catalog.txt`. Each
session, the first time a `(CulpritAssembly, ExceptionType, OwnerMethod)`
triple is seen, a one-line entry is appended:

```csharp
FailedModsCatalog.Append(failureRecord);   // idempotent within session
string catalogPath = FailedModsCatalog.ResolvePath();
```

You generally don't call `Append` directly — SaveShield's finalizer
already does. But if you have your own catch-and-classify code path and
want to feed the same catalog, the API is public.

### Type lookup

```csharp
using BetaDeps.Foundation;

Type? engineType = ReflectionUtils.ResolveTypeByFullName(
    "TaleWorlds.MountAndBlade.Mission");
```

Walks all loaded assemblies looking for the type. Returns `null` instead
of throwing if not found. Use this in beta-branch compatibility code where
the engine moves a type between assemblies.

---

## Module 2: BetaDeps.Harmony

Harmony 2.x re-export. The public `HarmonyLib.*` namespace is unchanged
from upstream — you can use it exactly as you would with the standalone
`0Harmony.dll`. The BetaDeps-native addition is `SafeBind`.

### SafeBind — signature-verified patching

The problem: on the beta branch, TaleWorlds changes a method signature in
a way that `AccessTools.Method()` still resolves (because it finds a
method with the right name) but your prefix's argument layout no longer
matches. Result: native-side stack-corruption CTD with no managed stack.

The fix:

```csharp
using BetaDeps.Harmony;
using HarmonyLib;

var target = SafeBind.Method(
    typeof(Mission),                    // type
    "GetSomeAgent",                     // method name
    returnType: typeof(Agent),          // expected return type
    parameterCount: 2);                 // expected param count

if (target != null) {
    SafeBind.TryPatch(harmony, target,
        prefix: new HarmonyMethod(typeof(MyPatches), nameof(MyPrefix)));
}
```

If the engine method's signature changes underneath you, `SafeBind`
returns `null`, logs the mismatch, and your patch silently no-ops instead
of crashing the game. The patch never installs, your mod stays loaded.

`SafeBind.TryPatch(...)` returns `true` on success, `false` on any
binding failure (logged).

---

## Module 3: BetaDeps.UIExtenderEx

Public API surface lives in `Bannerlord.UIExtenderEx.*` for backward
compatibility with consumer mods that already reference it. The BetaDeps
implementation is in `BetaDeps.UIExtenderEx.dll` — both the alias and the
direct dependency resolve to the same code.

```csharp
using Bannerlord.UIExtenderEx;
using Bannerlord.UIExtenderEx.Attributes;

public class MyModule : MBSubModuleBase {
    protected override void OnSubModuleLoad() {
        var ext = UIExtender.Create("MyModule");
        ext.Register(typeof(MyModule).Assembly);
        ext.Verify();
        ext.Enable();
    }
}

[PrefabExtension("MyScreenPrefab", "//Widget[@Id='Foo']")]
public class FooInjector : PrefabExtensionInsertPatch { ... }

[ViewModelMixin(typeof(SomeOriginalVM))]
public class SomeOriginalMixin : BaseViewModelMixin<SomeOriginalVM> { ... }
```

There is no BetaDeps-native rename of these — the upstream surface
already lives in our codebase. The `BetaDeps.*` namespace doesn't add
anything you'd want here.

---

## Module 4: BetaDeps.ButterLib

Public API surface lives in `Bannerlord.ButterLib.*`. Same story as
UIExtenderEx — the upstream namespace is the API.

### HotKeys

```csharp
using Bannerlord.ButterLib.HotKeys;
using TaleWorlds.InputSystem;

public class ToggleEditorKey : HotKeyBase {
    public ToggleEditorKey() : base("ToggleEditorKey") { }

    protected override string DisplayName => "Toggle Tactics Editor";
    protected override string Description => "Show/hide the tactics overlay.";
    protected override InputKey DefaultKey => InputKey.F10;
    protected override string Category => "MyMod";

    protected override void OnPressed() {
        // do work
    }
}

// register at OnSubModuleLoad:
HotKeyManager.Create("MyMod").Add(new ToggleEditorKey());
```

`HotKeyManager.Create(string category)` returns a fluent builder. The
builder is registered with the engine automatically when your sub-module
finishes loading.

### Sub-systems

Lifecycle-aware feature toggles consumer mods register so the user can
turn pieces of the mod on/off at runtime without restarting:

```csharp
using Bannerlord.ButterLib.SubSystems;

public class MyFeatureSubSystem : BaseSubSystem {
    public override string Id => "MyMod.MyFeature";
    public override string Name => "My Feature";
    public override string Description => "A toggleable feature.";

    protected override void OnEnable() { /* install patches */ }
    protected override void OnDisable() { /* uninstall patches */ }
}
```

Registration happens via `SubSystemManager.Register(...)`.

---

## Module 5: BetaDeps.MCM

Mod Config Menu settings. Three storage scopes:

| Scope            | Class                            | Where it persists                 |
|------------------|----------------------------------|-----------------------------------|
| Global           | `AttributeGlobalSettings<TSelf>` | `Configs\ModSettings\<id>.json`   |
| Per-campaign     | `BasePerCampaignSettings`        | Save-file-specific settings tree  |
| Per-save         | `PerSaveSettings<TSelf>`         | Inside the save file              |

### Attribute-driven (most common)

```csharp
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

public class MySettings : AttributeGlobalSettings<MySettings> {
    public override string Id => "MyMod_v1";
    public override string DisplayName => "My Mod";

    [SettingPropertyBool("Enable feature X", HintText = "Turn X on/off.")]
    [SettingPropertyGroup("General")]
    public bool EnableX { get; set; } = true;

    [SettingPropertyFloatingInteger("X intensity", 0f, 10f,
        HintText = "0 = none, 10 = max")]
    [SettingPropertyGroup("General")]
    public float Intensity { get; set; } = 5f;
}

// at runtime:
bool on = MySettings.Instance.EnableX;
```

`AttributeGlobalSettings<TSelf>` inherits from `GlobalSettings<TSelf>`,
which provides the `Instance` singleton and JSON persistence.

### Fluent builder (build settings imperatively)

When your settings depend on data only known at runtime (faction names,
mod-list contents), build them with `BaseSettingsBuilder`:

```csharp
using MCM.Abstractions.FluentBuilder;

var settings = BaseSettingsBuilder.Create("MyMod_runtime", "My Mod (runtime)")
    .CreateGroup("Tactics", g => g
        .SetGroupOrder(10)
        .AddBool("auto_anchor", b => b
            .SetDisplayName("Auto-anchor formations")
            .SetHintText("Place anchors near terrain features automatically.")
            .SetDefaultValue(true)))
    .CreatePreset("conservative", "Conservative", p => p
        .SetPropertyValue("auto_anchor", true))
    .BuildAsGlobal();
```

Returns `FluentGlobalSettings`, a runtime `BaseSettings` instance you can
hold a reference to and read property values from at any time.

### Per-save settings

For values that should travel with the save file rather than the user
profile:

```csharp
using MCM.Abstractions.Base.PerSave;

public class MyPerSaveSettings : PerSaveSettings<MyPerSaveSettings> {
    public override string Id => "MyMod_save";
    public override string DisplayName => "My Mod (per save)";

    [SettingPropertyInteger("Notoriety threshold", 0, 100)]
    public int Threshold { get; set; } = 50;
}

// from anywhere mid-campaign:
int t = MyPerSaveSettings.Instance.Threshold;
```

`Instance` is reset when a save loads or a new campaign starts, so the
data follows the save.

---

## Module 6: BetaDeps.Module — the in-game Mod Config screen

The Mod Config screen is wired through `MCM.UI.PrefabExtensions`. You
don't normally interact with it directly; registering settings via
`AttributeGlobalSettings`, `PerSaveSettings`, or `BaseSettingsBuilder`
automatically makes them appear.

The `Toggle PatchShield` and `Toggle Auto-Disable` buttons in the Mod
Config button row are surfaced by `OptionsVMMixin` and write/delete flag
files under `Modules\BetaDeps\`. If you ship a similar opt-in feature for
your own mod, mimic that pattern (flag file + UI toggle that creates or
deletes it).

---

## Module 7: BetaDeps.Framework (v1.0)

The v1.0 framework-core primitives. All live under the `BetaDeps.Framework`
namespace; `EventBus`, `ModConflictDetector`, `PerfProfiler`, and
`SettingsProfileStore` ship in `BetaDeps.Foundation.dll`, and `ProfileManager`
ships in `MCMv5.dll` — but you reach the whole surface with one
`using BetaDeps.Framework;` (referencing `BetaDeps` covers it). These are pull
APIs: nothing happens until you call them (the one exception is the auto
conflict scan, which BetaDeps runs for you).

### EventBus — mod-to-mod messaging

Two flavors. Use the **typed** API when both mods reference a shared contract
type; use the **named-channel** API for true IPC where the two mods share no
type at all.

```csharp
using BetaDeps.Framework;

// Typed (shared contract assembly):
IDisposable sub = EventBus.Subscribe<WarDeclared>(e => Log(e.Attacker));
EventBus.Publish(new WarDeclared { Attacker = "Vlandia" });
sub.Dispose();                       // unsubscribe (idempotent)

// Named channel (no shared type — the real IPC case):
EventBus.Subscribe("Diplomacy.WarDeclared", payload => { /* inspect payload */ });
EventBus.Publish("Diplomacy.WarDeclared", someDto);

// Optional per-subscription throttle (handler sees at most one event / 250ms):
EventBus.Subscribe<TickEvent>(OnTick, minIntervalMs: 250);
```

Guarantees: a throwing handler is caught + logged (never breaks siblings or the
publisher); you may subscribe/unsubscribe from inside a handler; `Publish`
returns the number of handlers that actually fired. Counters:
`EventBus.PublishedCount`, `DeliveredCount`, `ThrottledCount`, `HandlerFaultCount`.

### ModConflictDetector — who's fighting whom

Surfaces engine methods Harmony-patched by two or more *different* third-party
mods (BetaDeps's own shields are excluded). BetaDeps auto-runs a scan at the
late lifecycle hooks and writes it to `runtime.log`; you can also pull it:

```csharp
foreach (MethodConflict c in ModConflictDetector.Scan())   // High → Low order
    Log($"{c.Severity}: {c.TargetSignature} -- {string.Join(", ", c.Contributors.Select(x => x.Owner))}");

string report   = ModConflictDetector.ToText();      // runtime.log-style block
string markdown = ModConflictDetector.ToMarkdown();  // GitHub-issue table
```

Severity: **High** = ≥2 owners with *transpilers* on one method (IL collision);
**Medium** = ≥2 *prefixes* (a prefix can skip the original); **Low** = postfix/
finalizer overlap only.

### ProfileManager — whole-loadout settings profiles

The per-mod preset layer snapshots one mod's settings; a *profile* snapshots
**every** mod's Global settings into one named bundle so a player can keep
"Hardcore" / "Casual" loadouts and switch the whole stack at once.

```csharp
using BetaDeps.Framework;

ProfileManager.CaptureAll("Hardcore");       // snapshot all Global settings now
IReadOnlyList<string> profiles = ProfileManager.List();
ProfileManager.Apply("Hardcore");            // copy back + live-reload each mod
ProfileManager.Delete("Hardcore");
```

Profiles live at `Configs\ModSettings\_Profiles\<name>\`. Per-save and
per-campaign settings are intentionally excluded (they're already save-scoped).
The MCM-free file engine (`SettingsProfileStore`) is public too if you want to
profile a different directory of `<id>.json` files.

### PerfProfiler — which mod costs frames

```csharp
using (PerfProfiler.Measure("MyMod", "RecalcInfluence"))
{
    // ... work ...
}
foreach (var e in PerfProfiler.Snapshot())   // highest total time first
    Log($"{e.Key}: {e.TotalMs:F2}ms / {e.Calls} calls  [{string.Join(",", e.Owners)}]");
```

`PerfProfiler.Enabled = false` makes `Measure` a zero-cost no-op for shipped
builds. **Opt-in auto-instrument**: drop an empty `perf-profiler.flag` into
`Modules\BetaDeps\` and BetaDeps wraps every Harmony-patched method with timing,
attributing cost to the owning mod(s) — the per-mod frame-cost surface, no code
required. (Or call `PerfProfiler.InstrumentAllPatchedMethods()` yourself.)

---

## Declarative settings (`mod.json`) — no C# required

A tweak mod that only changes a few numbers doesn't need a compiled assembly at
all. Drop a **`mod.json`** in your module folder (or any `*.betadeps.json`) and
BetaDeps builds an MCM settings page from it at load — persistence, the Mod
Config UI, and presets all work the same as a coded settings class.

```json
{
  "id": "MyTweaks_v1",
  "name": "My Tweaks",
  "scope": "global",
  "groups": [
    { "name": "Combat", "order": 0, "properties": [
      { "id": "enable", "name": "Enable",     "type": "bool",  "default": true, "hint": "Turn the tweak on/off" },
      { "id": "dmg",    "name": "Damage x",   "type": "int",   "min": 0, "max": 100, "default": 50 },
      { "id": "speed",  "name": "Move speed", "type": "float", "min": 0.0, "max": 5.0, "default": 1.0 },
      { "id": "label",  "name": "HUD label",  "type": "text",  "default": "Fast" }
    ]}
  ]
}
```

- `scope`: `global` (default), `percampaign`, or `persave`.
- A flat top-level `"properties": [...]` (no `groups`) is also accepted and lands
  in a single "General" group.
- Property types: `bool`, `int` (needs `min`/`max`), `float` (needs `min`/`max`),
  `text`. Optional per-property `hint` and `requireRestart`.
- The parser validates and reports: missing `id`, unknown type, `min > max`,
  duplicate ids, and out-of-range defaults (clamped + warned).

A coded mod can read the same values back through MCM:
`BaseSettingsBuilder`/`FluentGlobalSettings.Get<T>(id)`, or just let the JSON file
under `Configs\ModSettings\Global\<id>.json` be edited by hand. For programmatic
use, `BetaDeps.Framework.ModJsonParser.Parse(json)` (pure → schema) and
`ModJsonLoader.Load(json)` / `LoadFile(path)` are public.

---

## Scaffolding a new mod

`BetaDeps.Framework.ModScaffolder.Generate(options, targetRoot)` writes a
ready-to-build starter mod that consumes BetaDeps, so you don't hand-author the
SubModule.xml the launcher needs.

```csharp
ModScaffolder.Generate(new ScaffoldOptions {
    ModId = "MyFirstTweak",          // valid identifier; also the folder name
    ModName = "My First Tweak",
    Author = "you",
    Template = ModTemplate.SettingsOnly   // SettingsOnly | HarmonyTweak | Full
}, @"C:\dev\mymods");
```

- **SettingsOnly** — `SubModule.xml` + a sample `mod.json` (no code; pairs with the
  declarative-settings loader above).
- **HarmonyTweak** — adds `src\<id>.csproj` + a starter `MBSubModuleBase` wired for
  a `SafeBind` Harmony patch and `DiagLog` logging.
- **Full** — HarmonyTweak plus an `AttributeGlobalSettings` class.

Every template declares `<DependedModule Id="BetaDeps" />` so the launcher load
order is correct, and `Generate` returns the list of files it wrote.

---

## BetaDeps-native vs BUTR-compat migration table

| Want to do                          | BUTR-compat (works today)                         | BetaDeps-native (forward-looking)        |
|-------------------------------------|---------------------------------------------------|------------------------------------------|
| Write a log line                    | n/a (BUTR didn't expose this publicly)            | `DiagLog.Log("Tag", "msg")`              |
| Get the game branch                 | `Bannerlord.BUTR.Shared.Helpers.ApplicationVersionHelper` | `VersionProbe.Branch`                    |
| Look up a type by full name         | `AccessTools.TypeByName(...)`                     | `ReflectionUtils.ResolveTypeByFullName(...)` |
| Patch a method safely on beta       | `AccessTools.Method(...)` then verify by hand     | `SafeBind.Method(...)` + `SafeBind.TryPatch(...)` |
| Register UI prefab extensions       | `UIExtender.Create / Register / Enable`           | (same — no rename needed)                |
| Register hotkeys                    | `HotKeyManager.Create(...)`                       | (same — no rename needed)                |
| Define global settings              | `AttributeGlobalSettings<TSelf>`                  | (same)                                   |
| Define per-save settings            | `PerSaveSettings<TSelf>`                          | (same — BetaDeps added this in v0.7)     |
| Build settings imperatively         | `BaseSettingsBuilder.Create(...)`                 | (same — BetaDeps added `CreatePreset` + `SetGroupOrder` in v0.7) |
| Catch sloppy Harmony patches        | n/a (manual try/catch wrapping)                   | (automatic via PatchShield — no opt-in)  |
| Disable a known-broken mod          | n/a (write LauncherData.xml yourself)             | Drop module into `betadeps-disabled-mods.log` |

The two columns disagree only on `DiagLog`, `VersionProbe`,
`ReflectionUtils`, and `SafeBind` — the rest is the same code reached
through different namespace aliases. The BUTR aliases keep working
forever; "BetaDeps-native" just means importing from the BetaDeps
namespace where one exists, so when v2.0 deprecates the aliases for new
mods your code already follows the supported import path.

---

## Common gotchas

**SubModule.xml load order.** BetaDeps must load before your mod. Either
list `<DependedModule Id="BetaDeps" />` in your `<DependedModules>` (the
launcher then guarantees order), or set `BetaDeps` in your
`<ModulesToLoadAfterThis>` from the BetaDeps side. The launcher silently
ignores unknown IDs, so listing optional dependencies is safe.

**The `AssemblyResolve` hook.** BetaDeps installs an `AssemblyResolve`
hook at `[ModuleInitializer]` time so the first
`MCMv5.dll` / `0Harmony.dll` / `UIExtenderEx.dll` / `ButterLib.dll` the
runtime sees is BetaDeps's. If your mod bundles its own copy of any of
those, the bundled copy will *lose* the resolve race — that's intentional
and what makes the impersonation work. Don't bundle copies of those four
assemblies in your `bin\Win64_Shipping_Client\`.

**SafeBind binding diagnostics.** If a `SafeBind.TryPatch(...)` is
returning `false` and you can't see why, set
`DiagLog.VerboseBinding = true` early in `OnSubModuleLoad` — `SafeBind`
will then log the actual parameter types it found alongside the ones you
asked for.

**PatchShield unpatching.** If PatchShield is in production mode (the
default) and your prefix throws one of the three swallowable exceptions,
PatchShield will *unpatch* it after the first throw. That's by design —
otherwise the broken prefix would replay hundreds of times per second.
If you want raw exceptions during dev, ship with the
`patchshield-disabled.flag` file in your `dev\` folder and drop it in
during testing.

---

## Examples in the wild

Reference implementations using these APIs live in:

- `C:\dev\modready\framework\src\BetaDeps.Module\` — the BetaDeps own Mod Config
  screen uses `UIExtender`, `OptionsVMMixin`, and the MCM fluent builder
  end-to-end.
- `C:\dev\bannerlord\crest\` — CREST consumed an older API surface and
  is being migrated; useful as a "before/after" reference.
- **Bannerwar** (`C:\dev\bannerwar`) — standalone RTS-style battle command
  mod that consumes BetaDeps end-to-end: `DiagLog`, `SafeBind`, raw
  Harmony patches via `BetaDeps.Harmony`, and (soon) MCM settings + a
  ButterLib hotkey category for free-cam / time-control rebinds. The
  canonical "framework consumer" example.

---

## Debugging your mod against BetaDeps

### Running against the raw BUTR-equivalent stack (no PatchShield / SaveShield / MCM extras)

If you want a clean BUTR-equivalent target to develop your patches
against — same Harmony, UIExtenderEx, ButterLib, and MCM DLLs the
ecosystem has shipped for years, but without BetaDeps's defensive
finalizers or Mod Configuration surface — disable the `BetaDeps`
module in your launcher and leave the four dependency modules
(`Bannerlord.Harmony`, `Bannerlord.UIExtenderEx`, `Bannerlord.ButterLib`,
`Bannerlord.MBOptionScreen`) enabled. As of v0.8 those four are real
modules carrying the canonical DLLs and run standalone. Re-enable
BetaDeps once you want the safety net back.

### Disabling specific defensive layers while leaving BetaDeps enabled

When you're iterating on your own Harmony patches or save-load code,
BetaDeps's defensive layers will catch exceptions and silently unpatch
the offending hook — which is exactly what you want in production, but
exactly what you DON'T want during development. You want the raw stack
trace, not a silent recovery.

BetaDeps supports flag files for this. Drop an empty (or any-content)
file into `Modules\BetaDeps\` and the corresponding subsystem disables
itself on the next game launch. Delete the file to re-enable.

| Flag file (in `Modules\BetaDeps\`)    | Effect when present                                                                                                                                                                |
|----------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `patchshield-disabled.flag`            | PatchShield does NOT install its finalizer wrapper around consumer-mod Harmony prefixes. Exceptions in your prefix propagate to the engine and crash the way they normally would. |
| `saveshield-swallow-disabled.flag`     | SaveShield does NOT swallow exceptions in engine save/load entry points (MBSaveLoad.LoadSaveGameData, Mission.Tick, etc.). Save/load failures abort the way they normally would.   |
| `auto-disable-enabled.flag`            | OPT-IN. Enables BetaDeps's predictive incompatible-mod scan and runtime crash-recovery auto-disable. Default behavior (no flag) leaves your mod list alone.                       |

**Workflow tip.** Add a build-step to your mod's `build.ps1` (or whatever
script you use) that drops `patchshield-disabled.flag` before launching
Bannerlord, so every dev iteration runs with the safety net off and you
see the unvarnished stack trace. Strip the flag before shipping (or just
never commit it — your users will never see it).

**Why these aren't UI buttons.** Pre-v0.8 BetaDeps surfaced these as
buttons on the Mod Config tab. End users had no idea what PatchShield or
SaveShield were and the labels prompted "is this dangerous if I press
it?" — so the buttons were removed. Modders work at the filesystem layer
anyway and the flag-file mechanism fits that workflow better.

If you have a use case where a per-launch toggle button would genuinely
help (e.g. testing both "with-shield" and "no-shield" passes inside a
single game session), open an issue — I'll consider a dev-mode flag that
re-surfaces the buttons.

---

## Where this guide goes next

v1.0 ships this guide alongside:

- **Tactics editor** — a no-code way to author tactic mods. Substrate
  TBD (either a minimal in-BetaDeps view, or built atop Bannerwar as a
  sister mod).
- **Build-your-first-tactic walkthrough** — a step-by-step that produces
  a Nexus-shippable tactic package using BetaDeps-native APIs.

Bannerwar ships separately (`C:\dev\bannerwar`) as a standalone Nexus
mod that demonstrates consuming BetaDeps as a framework.

v2.0 will mark the four BUTR alias assemblies (Bannerlord.Harmony,
.UIExtenderEx, .ButterLib, .MBOptionScreen) as `[Obsolete]` for new mods.
Existing mods compiled against them keep working forever.
