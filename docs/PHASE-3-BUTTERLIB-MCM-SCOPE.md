# Phase 3 — BetaDeps.ButterLib + BetaDeps.MCM rebuild scope

Goal: clean-room replacements for the two remaining Aragas-adjacent / BUTR foundation libraries so the BetaDeps bundle is fully self-contained, then a single Nexus zip.

## Scope finding (from task #18 catalog)

**CREST.Harmony's ButterLib usage:** none. CREST.Harmony references the `Bannerlord.ButterLib` namespace only in string literals for the patch self-test (it checks that BEWPatch is the patch owner on 5 finalizer targets). No `using` imports, no compile-time API consumption.

**CREST's actual ButterLib dependency surface** is therefore:
- A `ButterLibSubModule` class that, on load, calls `BEWPatch.Enable()`.
- `BEWPatch.Enable()` installs Harmony finalizers on 5 well-known tick methods (Managed.ApplicationTick, Module.OnApplicationTick, ScreenManager.Tick, ManagedScriptHolder.TickComponents, Mission.Tick) and writes their bindings to a Harmony owner ID starting with `Bannerlord.ButterLib.ExceptionHandler.BEWPatch`.
- That's it.

**Crest.MCM.UI's ButterLib usage** is broader (HotKeys, Common.Extensions, Common.Helpers) but Crest.MCM.UI is the heavy MCM UI piece, not a typical consumer mod. For Phase 3 first iteration we ship core MCM, not the UI tab; the UI piece becomes task #22.

**CREST settings attribute usage:**
- `AttributeGlobalSettings<TSelf>` (generic singleton base)
- `[SettingPropertyBool(displayName, ...)]`
- `[SettingPropertyInteger(displayName, min, max, valueFormat, ...)]`
- `[SettingPropertyFloatingInteger(displayName, min, max, valueFormat, ...)]`
- `[SettingPropertyDropdown(displayName, ...)]`
- `[SettingPropertyGroup(displayName, GroupOrder = n)]`
- Common parameters: `Order`, `IsToggle`, `RequireRestart`, `HintText`
- Localization keys in `{=Id}Display Name` format

## Implementation target

### Phase 3a — BetaDeps.ButterLib (small, mostly mechanical)

```
src/BetaDeps.ButterLib/
  BetaDeps.ButterLib.csproj
  BetaDepsButterLibSubModule.cs       # MBSubModuleBase entry; calls BEWPatch.Enable()
  ExceptionHandler/
    BEWPatch.cs                       # finalizer install on the 5 tick methods
    Finalizer.cs                      # shared finalizer body that logs and swallows
```

The finalizer logic is straightforward — accept the exception, write it to runtime.log, return null so Harmony swallows it. Already-validated approach from CREST's runtime.log.

### Phase 3b — BetaDeps.MCM core (the declaration surface)

```
src/BetaDeps.MCM/
  BetaDeps.MCM.csproj                 # AssemblyName=MCMv5.dll
  Attributes/
    SettingPropertyAttribute.cs       # base
    SettingPropertyBoolAttribute.cs
    SettingPropertyIntegerAttribute.cs
    SettingPropertyFloatingIntegerAttribute.cs
    SettingPropertyDropdownAttribute.cs
    SettingPropertyGroupAttribute.cs
  Settings/
    BaseSettings.cs                   # base class for any settings VM
    AttributeGlobalSettings.cs        # generic singleton base mods inherit from
    SettingsStorage.cs                # JSON read/write to Documents folder
  BetaDepsMCMSubModule.cs             # MBSubModuleBase entry
```

The runtime: when `AttributeGlobalSettings<T>` is instantiated, reflect over T's properties, find the `[SettingProperty*]` attributes, build a property table, load the JSON file (or write defaults), keep the in-memory state. Save on property changes.

### Phase 3c — MCM UI tab (heaviest, deferred)

Task #22. The in-game Options panel tab that renders the loaded settings as widgets. Built on top of BetaDeps.UIExtenderEx (prefab patches into OptionsScreen.xml + ViewModelMixin on OptionsVM). This is non-trivial and gets its own iteration; without it, settings still persist to JSON correctly but the in-game viewer won't render.

## Build target

After Phase 3 first-iteration ships:

```
Modules/BetaDeps/bin/Win64_Shipping_Client/
  Bannerlord.ButterLib.dll    (NEW)
  MCMv5.dll                   (NEW -- assembly name matches upstream)
  Bannerlord.UIExtenderEx.dll (Phase 2)
  BetaDeps.Harmony.dll        (Phase 1)
  BetaDeps.Foundation.dll     (Phase 1)
  0Harmony.dll, Mono.Cecil.*, MonoMod.*

Modules/Bannerlord.ButterLib/      (alias, structural payload only)
Modules/Bannerlord.MBOptionScreen/ (alias, structural payload only)
```

## License posture (Phase 3 update)

- All BetaDeps.* DLLs original work, MIT, copyright Maxfield Management Group.
- No Aragas / BUTR-derived source. The BEW finalizer install pattern is generic Harmony usage; finalizer behavior (eat exception + log) is industry-standard.
- The MCM attribute API surface is dictated by consumer mods, but the implementation behind it is fresh code.
