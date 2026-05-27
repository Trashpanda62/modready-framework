# BetaDeps

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Latest release](https://img.shields.io/badge/release-v0.7.0-green.svg)](https://github.com/Trashpanda62/Betadeps/releases)
[![Game version](https://img.shields.io/badge/Bannerlord-e1.4.5-orange.svg)](https://store.steampowered.com/app/261550/Mount__Blade_II_Bannerlord/)

A single drop-in dependency module for **Mount & Blade II: Bannerlord** that replaces the entire BUTR dependency stack (Harmony + UIExtenderEx + ButterLib + MCM) with one folder, one load entry, and one settings UI — and now catches the entire class of "consumer mod built against a stale TaleWorlds API" crashes so your modlist keeps booting through TaleWorlds updates.

## What it does

If you've ever fought your launcher about Harmony version conflicts, missing ButterLib, broken UIExtenderEx, or MCM crashing on the beta branch — BetaDeps exists to make that go away. Install it, enable it at the top of your load order, and every mod that depends on the BUTR dependency stack will resolve against BetaDeps transparently. No recompilation, no updates to other mods, no patching.

## Install

The easiest way is from Nexus Mods:

1. Download the latest release from [Nexus Mods](https://www.nexusmods.com/mountandblade2bannerlord/mods/11274).
2. Extract the zip into your Bannerlord `Modules\` folder.
3. Launch the game through BLSE LauncherEx (or your preferred launcher) and enable **BetaDeps** at the top of your load order, above any mod that needs Harmony, ButterLib, UIExtenderEx, or MCM.
4. Make sure the upstream BUTR mods (Bannerlord.Harmony, Bannerlord.ButterLib, Bannerlord.UIExtenderEx, MCMv5) are **disabled or uninstalled** — BetaDeps replaces them.

### Do I need to enable the four alias folders manually?

**It depends on which launcher you're using.**

- **BLSE LauncherEx** (most modders' choice) auto-activates the four alias folders (`Bannerlord.Harmony`, `Bannerlord.UIExtenderEx`, `Bannerlord.ButterLib`, `Bannerlord.MBOptionScreen`) for you. Enable BetaDeps and BLSE silently turns on the aliases that consumer mods need.
- **Vanilla TaleWorlds launcher** does NOT auto-resolve dependencies. You must manually check all five boxes — BetaDeps plus the four aliases.

If a fresh install doesn't show all four aliases in the launcher yet, BetaDeps materializes them on first run. Close the launcher, start the game once to let BetaDeps create the folders, exit, then reopen the launcher — all four will appear and you can enable them.

## Features

- **PatchShield (v0.7).** Generic Harmony finalizer installed on every patched method during every lifecycle hook. When a consumer mod's prefix throws `MissingMethodException`, `MissingFieldException`, or `TypeLoadException` because TaleWorlds renamed/removed something it was patching, BetaDeps logs it, synthesizes a sensible default return value, and auto-unpatches the offending prefix so it stops firing. The class of crash that used to take out your campaign load every time TaleWorlds shipped a patch.
- **One module, four libraries.** Harmony, ButterLib, UIExtenderEx, and MCM all live inside `Modules\BetaDeps\` so there's exactly one thing to enable and one thing to update.
- **Mod Config UI.** In-game settings menu with draggable sliders on every numeric setting, dropdowns, toggles, and right-side hover hints that explain what each setting does as you mouse over it.
- **In-game Self-Test.** A one-click test that round-trips every setting across every installed mod and reports any mismatches before they corrupt your save.
- **Compatibility shims.** Type-forwarding DLLs (`Bannerlord.Harmony.dll`, `Bannerlord.ButterLib.dll`, `Bannerlord.UIExtenderEx.dll`, `MCMv5.dll`) so existing mods that were compiled against the upstream BUTR names resolve against BetaDeps without any changes.
- **Opt-in single-launch recovery.** Click `Toggle Auto-Disable` in Mod Config to enable a Harmony pre-construction guard that blocks known-broken mods from instantiating. All recovery features that modify `LauncherData.xml` ship OFF by default — your modlist is not touched unless you ask.
- **Beta branch support.** Verified on Bannerlord e1.4.5. The public branch (e1.3.x) is also supported by the same build.

## Where this is going

BetaDeps right now is a drop-in replacement for the BUTR dependency stack. That's the starting point, not the destination. The end goal is a single thing: an **always-compatible modding kit** that keeps your modded Bannerlord running through every TaleWorlds update, every beta branch, every patch that normally breaks half your mod list overnight.

When TaleWorlds pushes a game update, BetaDeps gets updated to match — before the modlist starts dying. Mods that would crash the game on the new version get **auto-disabled** with a clear note telling you why, so the launcher boots even when individual mods are temporarily broken upstream. You hit Play, the game starts. Every time. The worst case becomes "I'm playing without DismembermentPlus this week" instead of "I can't launch my save."

That's the goal: **you keep playing, no matter what.**

## Build from source

Requirements:
- .NET SDK 6.0 or later
- A working Bannerlord install (used as the reference for game DLLs)
- PowerShell 5+ on Windows

```powershell
cd C:\dev
git clone https://github.com/Trashpanda62/Betadeps.git
cd Betadeps
.\scripts\Build-Phase1.ps1
```

The build script produces `dist\Modules\BetaDeps\` (the deployable module tree) and `dist\BetaDeps-v<version>.zip` (the Nexus upload artifact).

## Quick test loop

For testing changes during development:

```powershell
cd C:\dev\beta-deps
.\scripts\Quick-Test.ps1
```

This rebuilds, launches BLSE LauncherEx, and copies the runtime logs back to a known location for inspection.

## License

BetaDeps is released under the [MIT License](LICENSE) and will stay that way. Use it, fork it, ship it in your own modlist or mod pack — no royalty, no attribution requirement beyond the standard MIT license terms.

The underlying [Lib.Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike is MIT-licensed and used unchanged.

## Support the work / commission a mod

BetaDeps is free, and that won't change regardless of how this page does. If you'd like to support development, or commission custom mod work, that lives on Patreon: [patreon.com/Trashpanda62](https://patreon.com/Trashpanda62).

## Contributing

Issues, bug reports, and pull requests are all welcome. If you're filing a bug, please include:

- Your Bannerlord version (e1.4.x or e1.3.x)
- Your BetaDeps version (visible in the Mod Config settings menu)
- Your `runtime.log` (under `Modules\BetaDeps\runtime.log`)
- A short description of what you did and what happened

For larger feature ideas or rewrites, open an issue first so we can discuss the approach before you spend time on a PR.

## Credits

- [Andreas Pardeike](https://github.com/pardeike) — original [Lib.Harmony](https://github.com/pardeike/Harmony) used inside BetaDeps.Harmony.
- The Bannerlord modding community — for keeping mods alive across the years of game updates.
