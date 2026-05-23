# BetaDeps

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Latest release](https://img.shields.io/badge/release-v0.5.8-green.svg)](https://github.com/stevenh1156-hash/Betadeps/releases)
[![Game version](https://img.shields.io/badge/Bannerlord-e1.4.5-orange.svg)](https://store.steampowered.com/app/261550/Mount__Blade_II_Bannerlord/)

A single drop-in dependency module for **Mount & Blade II: Bannerlord** that replaces the entire BUTR foundation stack (Harmony + UIExtenderEx + ButterLib + MCM) with one folder, one load entry, and one settings UI.

## What it does

If you've ever fought your launcher about Harmony version conflicts, missing ButterLib, broken UIExtenderEx, or MCM crashing on the beta branch — BetaDeps exists to make that go away. Install it, enable it at the top of your load order, and every mod that depends on the BUTR foundation will resolve against BetaDeps transparently. No recompilation, no updates to other mods, no patching.

## Install

The easiest way is from Nexus Mods:

1. Download the latest release from [Nexus Mods](https://www.nexusmods.com/mountandblade2bannerlord) (search "BetaDeps").
2. Extract the zip into your Bannerlord `Modules\` folder.
3. Launch the game through BLSE LauncherEx (or your preferred launcher) and enable **BetaDeps** at the top of your load order, above any mod that needs Harmony, ButterLib, UIExtenderEx, or MCM.
4. Make sure the upstream BUTR mods (Bannerlord.Harmony, Bannerlord.ButterLib, Bannerlord.UIExtenderEx, MCMv5) are **disabled or uninstalled** — BetaDeps replaces them.

## Features

- **One module, four libraries.** Harmony, ButterLib, UIExtenderEx, and MCM all live inside `Modules\BetaDeps\` so there's exactly one thing to enable and one thing to update.
- **Mod Config UI.** In-game settings menu with draggable sliders on every numeric setting, dropdowns, toggles, and right-side hover hints that explain what each setting does as you mouse over it.
- **In-game Self-Test.** A one-click test that round-trips every setting across every installed mod and reports any mismatches before they corrupt your save.
- **Compatibility shims.** Type-forwarding DLLs (`Bannerlord.Harmony.dll`, `Bannerlord.ButterLib.dll`, `Bannerlord.UIExtenderEx.dll`, `MCMv5.dll`) so existing mods that were compiled against the upstream BUTR names resolve against BetaDeps without any changes.
- **Beta branch support.** Verified on Bannerlord e1.4.5. The public branch (e1.3.x) is also supported by the same build.

## Build from source

Requirements:
- .NET SDK 6.0 or later
- A working Bannerlord install (used as the reference for game DLLs)
- PowerShell 5+ on Windows

```powershell
git clone https://github.com/stevenh1156-hash/Betadeps.git
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

This rebuilds, launches BLSE LauncherEx, and copies the runtime logs back to a known location for inspection. See [CLAUDE.md](CLAUDE.md) for the full development workflow.

## License

BetaDeps is released under the [MIT License](LICENSE) and will stay that way. Use it, fork it, ship it in your own modlist or mod pack — no royalty, no attribution requirement beyond the standard MIT license terms.

The underlying [Lib.Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike is MIT-licensed and used unchanged.

## Support the work / commission a mod

BetaDeps is free, and that won't change regardless of how this page does. If you'd like to support development, or commission custom mod work, that lives on Patreon: [patreon.com/trashpanda62](https://patreon.com/trashpanda62).

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
