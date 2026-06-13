# BetaDeps v1.0.0 — Framework launch

**Built + tested in-game 2026-06-13.** BetaDeps graduates from a
compatibility/dependency mod to a **modding framework**. v1.0.0 lands the
framework-*core* primitives — the developer-facing building blocks that compile
and are verified off-engine. (This is the framework's 1.0 launch; the larger
gameplay frameworks on the long-term roadmap — weather, settlement, RTS battle
control, etc. — remain separate multi-week projects and are NOT in this build.)

All five primitives are exercised by `tools/framework-selftest` (a net472
console, **55 assertions**, runs without launching the game): `dotnet run -c
Release --project tools/framework-selftest`. An independent verifier pass
returned **SHIP** (0 blocker/high).

## New: `BetaDeps.Framework` namespace

| Primitive | Assembly | What it does |
|---|---|---|
| `EventBus` | Foundation | Typed + named-channel pub/sub for mod-to-mod IPC. Exception-isolated handlers, CAS-correct per-subscription throttle, reentrancy-safe dispatch. |
| `ModConflictDetector` | Foundation | Scans Harmony's registry for methods patched by ≥2 third-party mods; ranks High/Medium/Low; text + markdown reports. Auto-runs in-game and logs to `runtime.log`. |
| `PerfProfiler` | Foundation | Manual `Measure(owner,label)` scopes + opt-in Harmony auto-instrument that attributes per-method cost to the owning mod(s). `Enabled=false` → zero-cost. |
| `SettingsProfileStore` | Foundation | Engine-free file engine for whole-loadout settings profiles (named snapshots of a dir of `<id>.json`). |
| `ProfileManager` | MCM | Captures every Global settings file into one named profile and applies + live-reloads them — switch a whole loadout in one call. |

Per-campaign settings (a related roadmap settings item) were already completed
in v0.9.x (`PerCampaignSettings<T>` + `Configs\ModSettings\PerCampaign\<campaignId>\`).

### Also in this build: declarative settings (`mod.json`)

The v1.5 modder-layer headline, brought forward: a mod author drops a `mod.json`
(or `*.betadeps.json`) in their module folder and BetaDeps builds an MCM settings
page from it at load — **zero C#**. `BetaDeps.Framework.ModJsonParser` (pure
parse+validate → schema) and `ModJsonLoader` (build + register via the fluent
pipeline); auto-discovered at MCM `OnBeforeInitialModuleScreenSetAsRoot`. Supports
bool/int/float/text properties, groups, global/percampaign/persave scope, and
validates missing-id / unknown-type / min>max / duplicate-id / out-of-range
defaults. 26 self-test assertions cover parse + end-to-end build/read-back. See
`docs/BETADEPS-NATIVE-API.md` → "Declarative settings (mod.json)".

### Also in this build: `new-mod` scaffolding

`BetaDeps.Framework.ModScaffolder.Generate(opts, targetRoot)` writes a ready-to-go
starter mod that consumes BetaDeps, in three templates: **SettingsOnly** (just a
SubModule.xml + a sample mod.json — zero C#), **HarmonyTweak** (csproj + a starter
`MBSubModuleBase` using `SafeBind`/`DiagLog`), and **Full** (adds an
`AttributeGlobalSettings` class). Validates the mod id, declares the BetaDeps
dependency, and the generated mod.json round-trips through `ModJsonParser`. Pure
file-gen, engine-free; 14 self-test assertions. (Standalone CLI wrapper is TODO.)

### Music selector — settlement path + naval gate

The non-destructive BYO music picker's PSAI path (menu/campaign/battle/siege/naval)
was already built + wired; this build completes the **settlement** half and the
naval gating:

- **`SettlementMusicManager`** (`Harmony/Music`): the Town/Village/Tavern path.
  Settlement music is FMOD-event-based (PSAI can't reach it), so it drives a free
  `TaleWorlds.Engine.Music` channel — acquire on settlement entry, `LoadClip` +
  `PlayMusic` from the context's `PlaybackPool`, advance when `IsMusicPlaying`
  goes false, `StopMusic`/`UnloadClip` on exit. All engine access is reflection +
  try/catch (a music fault can never crash the game). Tick-driven beside
  `PsaiRedirectManager.Pump()`.
- **`NavalGate`** (`Harmony/Music`): single War-Sails detection point (Modules\NavalDLC
  folder or a loaded naval assembly) so the Naval context/UI row gates correctly.
- Pure logic (settlement classification, naval detection) is unit-tested
  off-engine (8 assertions); the live audio behavior needs an in-game check.
- Spike teardown (plan §14) confirmed already complete (no `MusicSpike*` code remains).

Remaining for the picker: **UI-A** — the in-game MCM enable/mode/volume controls
per context (the engine works today by dropping `.ogg` files into
`Music/BYO/<Context>/`; UI-A surfaces the toggles).

### All-in-One installer → v1.0.0

`installer/` (Inno Setup bundle of BLSE + the five BetaDeps modules) bumped to
**v1.0.0** (`Build-Installer.ps1` default, `.iss` `AppVersion`, README). It stages
the current `dist\Modules`, so it ships the v1.0.0 modules. Compiling the `.exe`
needs Inno Setup 6 + a BLSE download (`-BlseDir`).

## In-game wiring

`FrameworkBootstrap.RunLateInit` is called from the same late lifecycle hooks
that re-install PatchShield/SaveShield (`BetaDepsHarmonySubModule.TryInstallPatchShield`):

- **Conflict scan** runs automatically and logs to `runtime.log`, re-logging only
  when the conflict count grows (so deferred-patch mods are still captured
  without spam).
- **Perf auto-instrument** is OFF by default; drop an empty `perf-profiler.flag`
  in `Modules\BetaDeps\` to enable it (same flag-file convention as the shields).

Everything is wrapped so a framework fault can never crash module load.

## Versioning (per docs/VERSION-POLICY.md)

- BetaDeps module `SubModule.xml`: → **`v1.0.0`**.
- Internal assemblies (`BetaDeps.Foundation`, `BetaDeps.Harmony`): `FileVersion`
  → `1.0.0.0`; `AssemblyVersion` stays pinned `0.9.0.0` (dependents bind to it).
- Impersonation assemblies (UIExtenderEx/ButterLib/MCMv5) and `Bannerlord.Harmony`:
  Assembly/FileVersion unchanged; only `InformationalVersion` → `BetaDeps 1.0.0`.
- `BetaDeps-v1.0.0.zip` produced; Aragas-string, XML-lint, and release-validation
  gates all pass.

## API docs

Consumer guide: `docs/BETADEPS-NATIVE-API.md` → **Module 7: BetaDeps.Framework**.

## Bug-fix pass (2026-06-13)

A multi-agent adversarial bug hunt (10 subsystem clusters, each finding
independently verified) surfaced **7 confirmed correctness bugs**, all fixed:

- **VersionProbe** (`Foundation`): cached a failed `Unknown` detection permanently
  if probed before TaleWorlds version types load → could silently disable beta
  sigsafe patches for the session. Now memoizes only successful detections.
- **AddBool/AddToggle IRef overloads** (`MCM`): hard `(bool)` unbox threw
  `InvalidCastException` into a consumer mod's load if a non-bool ref was bound.
  Now coerced defensively like the int/text overloads.
- **PrefabPatcher v1 Insert** (`UIExtenderEx`): v1 `Prepend`/`Append` (child
  semantics) were mapped to v2 sibling ops → older mods' widgets placed in the
  wrong container. Now route through child insertion.
- **BEWPatch dedup dict** (`ButterLib`): keyed on the exception *message*, so a
  per-tick varying message leaked memory on the 60 Hz finalizer path and defeated
  the throttle. Now keyed on (method, exception-type).
- **McmSelfTest backup/restore** (`MCM`): only snapshotted the Global folder,
  leaving per-save/per-campaign settings unprotected if the test crashed. Now
  backs up the whole `ModSettings` tree.
- **PatchShield verdict cache** (`Foundation`): a second distinct culprit on the
  same method could never be unpatched. Now re-enters the unpatch path while
  retries remain, still stops walking once the cap is hit.
- **ModJsonParser.Parse** (`MCM`, new code): could throw on a type-mismatched
  `default`/`min`/`max`. Now returns a validation error (regression-tested).

## Bugs caught + fixed during the build

- **PerfProfiler timing-stack imbalance** when `Enabled` toggled mid-instrumented-call:
  prefix/finalizer now push/pop unconditionally; only accounting is gated. Covered
  by a regression assertion.
- **EventBus throttle race** under concurrent tick-thread dispatch: the window
  stamp is now CAS-swapped so "at most one delivery per window" holds under
  concurrency (flagged by the verifier pass).
