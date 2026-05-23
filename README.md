# Bannerlord Beta Dependencies (BetaDeps)

Single drop-in dependency module for **Mount & Blade II: Bannerlord** that provides the foundation libraries other mods rely on — Harmony, ButterLib, UIExtenderEx, and MCM (Mod Configuration Menu) — built and tested to work on the **beta branch (e1.4.x)** where the upstream BUTR versions currently crash.

Public branch (e1.3.x) is also supported by the same build.

## Why this exists

The community-standard BUTR foundation mods have not been updated for the beta branch. Players who switch to beta lose access to most mods because Harmony, ButterLib, UIExtenderEx, and MCM either fail to load or CTD shortly after launch.

BetaDeps is a single `Modules\BetaDeps\` install that replaces all four with versions verified on beta:

- **BetaDeps.Harmony** — Harmony patch framework + Bannerlord-side loader
- **BetaDeps.ButterLib** — foundation utility library
- **BetaDeps.UIExtenderEx** — UI hook framework for Gauntlet prefab patches
- **BetaDeps.MCM** — in-game mod settings menu

Plus type-forwarding shims (`Bannerlord.Harmony.dll`, `Bannerlord.ButterLib.dll`, `Bannerlord.UIExtenderEx.dll`, `MCMv5.dll`) so existing mods that were compiled against the upstream BUTR names resolve transparently against BetaDeps. You don't need to update or recompile any mod that depends on the BUTR foundation — they just work.

## Status

This project is being rebuilt in phases. The current working baseline is sourced from CREST v0.9.3, which carries beta-safe fixes (sigsafe Harmony binds, defensive BetterExceptionWindow finalizers, UIExtenderEx visibility fix) but inherits Aragas-authored code we cannot redistribute on Nexus.

| Component | Origin | Rebuild status | Why |
|---|---|---|---|
| **BetaDeps.Harmony** | CREST fork of Bannerlord.Harmony | Phase 1 — clean-room wrapper rewrite | Aragas-authored, no permission obtainable |
| **BetaDeps.UIExtenderEx** | CREST fork of Bannerlord.UIExtenderEx | Phase 2 — clean-room rewrite | Aragas-authored, no permission obtainable |
| **BetaDeps.ButterLib** | CREST fork of Bannerlord.ButterLib | Phase 3 — pursue BUTR org permission or rewrite | Multi-author |
| **BetaDeps.MCM** | CREST fork of Bannerlord.MBOptionScreen | Phase 3 — pursue BUTR org permission or rewrite | Multi-author |
| **BetaDeps.Foundation** | New | Original — module shell, version probe, runtime log | n/a |
| **BetaDeps.Module** | New | Original — single SubModule.xml registering everything | n/a |

The underlying `Lib.Harmony` (Andreas Pardeike) is MIT-licensed and is not Aragas's work — only the Bannerlord SubModule wrapper around it is. The wrapper is the part being rewritten.

## Build target

A single Nexus zip: `BetaDeps.zip` -> extracts to `Modules\BetaDeps\` and that's the entire install. No separate Harmony / MCM / etc. modules required.

## Layout

```
beta-deps/
├── src/
│   ├── BetaDeps.Foundation/       Module shell, version probe, runtime log
│   ├── BetaDeps.Harmony/          Rebuilt Harmony wrapper (Phase 1)
│   ├── BetaDeps.UIExtenderEx/     Rebuilt UI hook framework (Phase 2)
│   └── BetaDeps.Module/           Top-level SubModule + manifest
├── bundled-deps/
│   ├── BetaDeps.ButterLib/        CREST-derived, awaiting Phase 3
│   └── BetaDeps.MCM/              CREST-derived, awaiting Phase 3
├── dist/                          Built Modules/BetaDeps/ trees and zips
├── scripts/                       Build, sign, package, verify
└── docs/                          Architecture notes, license audit, rebuild log
```
