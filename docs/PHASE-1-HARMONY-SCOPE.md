# Phase 1 — ModReady.Harmony rewrite scope

Goal: a clean-room replacement for the BUTR Bannerlord.Harmony wrapper, owned by us, working on e1.4.x beta.

## What the wrapper actually does

(Distilled from observing CREST's Bannerlord.Harmony fork behavior at runtime, not from copying source.)

1. Loads `0Harmony.dll` (Lib.Harmony, MIT, pardeike) into the process before any sibling submodule's patches run.
2. Exposes the `HarmonyLib` namespace to other mods compiled against the standard `0Harmony.dll`.
3. Provides a couple of safety helpers (`AccessTools2`-style signature-verified binding) so a target API drift on beta logs instead of CTDs.
4. Is itself an `MBSubModuleBase` so Bannerlord's module loader sees it.

That's it. The rest of what BUTR's wrapper ships (`BannerlordHarmonyLoader.dll`, etc.) is plumbing to satisfy *its* update mechanism and is not required for ModReady.

## What we ship

```
Modules/ModReady/
  SubModule.xml                     (top-level, registers ModReady submodules)
  bin/Win64_Shipping_Client/
    0Harmony.dll                    (MIT, pardeike — unchanged NuGet artifact)
    ModReady.Harmony.dll            (NEW — our wrapper)
    ModReady.Foundation.dll         (NEW — log/version probe)
    Bannerlord.Harmony.dll          (NEW — type-forwarder shim for legacy consumers)
```

## ModReady.Harmony.dll — file inventory

```
src/ModReady.Harmony/
  ModReady.Harmony.csproj
  ModReadyHarmonySubModule.cs       # MBSubModuleBase entry point
  SafeBind.cs                       # signature-verified binding helper
  HarmonyRuntimeGate.cs             # ensures 0Harmony is loaded once, early
  BetaSigSafePatches.cs             # CTD-prevention patches validated in CREST
```

No file in this list contains copied BUTR source. All are authored from scratch against the public Harmony 2.x API and TaleWorlds' published `MBSubModuleBase`.

## Acceptance tests for Phase 1

1. Build `ModReady.Harmony.dll` against .NET 4.7.2 + Bannerlord game refs.
2. Drop the four DLLs above into a fresh `Modules\ModReady\` and enable in launcher.
3. Launch e1.4.x beta — reaches main menu, opens Options without CTD.
4. Launch e1.3.x public — same.
5. Run a campaign load, enter a battle, open Options mid-mission — no CTD.
6. `Modules\ModReady\runtime.log` contains "ModReady.Harmony loaded" and the sigsafe pre-bind verification lines.
7. Verify with `strings` or `ilspy` that no Aragas / BUTR copyright strings are embedded.

## What we copy vs what we write

- **Copy:** `0Harmony.dll` (MIT NuGet artifact, no modification).
- **Reference but rewrite:** the sigsafe-binding *concept* from CREST's `CrestBattleSizeBetaPatches.cs` — this is our own original work from the CREST sprint, so re-authoring it under ModReady names is paperwork, not infringement.
- **Do not touch:** any source file under `C:\dev\bannerlord\Bannerlord.Harmony\src\` that has an Aragas-authored upstream lineage.
