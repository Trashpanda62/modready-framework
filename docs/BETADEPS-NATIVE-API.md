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

**Status as of v0.7.1:** the BUTR-impersonation surface and the
BetaDeps-native surface co-exist. v1.0 keeps both. v2.0 deprecates the
aliases for new mods (existing mods keep working forever).

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

- `C:\dev\beta-deps\src\BetaDeps.Module\` — the BetaDeps own Mod Config
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
