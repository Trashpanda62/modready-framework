# Task #22 — MCM UI tab architecture plan

Task #22 is split into five sub-tasks (#24-#28) each individually testable.

## Sub-task layout

```
22a (#24) DONE   SettingsRegistry + auto-discovery on OnBeforeInitialModuleScreenSetAsRoot
22b (#25) NEXT   Options.xml prefab patch + OptionsVM mixin -> adds the "Mod Configuration" tab
22c (#26)        Tab content prefab (two-pane layout: mod list / property grid) + screen VM
22d (#27)        Per-property widget set (Bool / Integer / FloatingInteger / Dropdown / Group)
22e (#28)        Apply / Revert / Default buttons + persistence wiring
```

## Architecture sketch

```
SettingsRegistry            <-  AttributeGlobalSettings<T>.Instance singletons
       |
       v
MCMScreenVM                 <-  list of RegisteredSettings, currently selected
       |                       (mixed into OptionsVM via UIExtenderEx mixin)
       v
PropertyGridVM              <-  per-mod reflective traversal of [SettingProperty*]
       |
       v
SettingPropertyVM           <-  per-property: name, group, value, type-aware widget
   (Bool / Int / Float / Dropdown variants)
```

## Prefab patches (task #22b/c)

Two prefab patches against `Options.xml`:

1. **Add tab toggle.** XPath: `descendant::ListPanel[@Id='TabToggleList']/Children/OptionsTabToggle[5]`. Insert a sibling toggle button with our tab's Id.
2. **Add tab content panel.** XPath: `descendant::TabControl[@Id='TabControl']/Children/*[5]`. Insert a new tab content widget pointing at our content prefab (loaded from `Modules\BetaDeps\GUI\Prefabs2\MCMModsTab.xml`).

Both XPaths match the CREST.MCM.UI patterns we observed in `OptionsPrefabExtensions.cs`.

## ViewModel mixin (task #22b)

`[ViewModelMixin(nameof(OptionsVM.RefreshValues), handleDerived: true)]` on a class that exposes:
- `MCMScreenVM ModConfigScreen { get; }` -- the screen our tab binds to
- `[DataSourceMethod] ExecuteOpenMods()` -- the tab toggle handler

This requires the deferred binding integration from task #13 (making mixin DataSourceProperty/DataSourceMethod members reachable from Gauntlet bindings). May need to be done as part of #22b.

## Localization

CREST settings strings use `{=Key}Display Name` format. Phase 3 first iteration: strip the `{=Key}` prefix at render time and display the literal text, ignoring localization. Full localization is a polish item; English fallback is acceptable for v0.3.

## Out of scope for first MCM UI iteration

- IDropdownProvider deep support (dropdowns render as labels until a consumer mod actually uses one)
- Per-save / per-campaign settings (only global settings supported)
- Settings presets (Save Default / Restore feature)
- Imports/exports
- Tooltips beyond the HintText property

These are all CREST-extra features absent on first ship.

## Files we'll add

```
src/BetaDeps.MCM/
  UI/
    MCMScreenVM.cs                       # screen-level VM
    OptionsVMMixin.cs                    # [ViewModelMixin] on OptionsVM
    OptionsPrefabExtensions.cs           # [PrefabExtension] patches against Options.xml
    PropertyGridVM.cs
    SettingPropertyVM.cs                 # base
    SettingPropertyBoolVM.cs
    SettingPropertyIntegerVM.cs
    SettingPropertyFloatingIntegerVM.cs
    SettingPropertyDropdownVM.cs
    SettingsGroupVM.cs

  GUI/
    Prefabs2/
      MCMTabToggle.xml                   # the tab toggle XML fragment (file content for #22b)
      MCMTabContent.xml                  # the tab content XML fragment
      MCMModsTab.xml                     # full screen layout
```

Plus matching `MCMv5.csproj` `<Content>` items so the GUI XMLs ship into the module folder at build.
