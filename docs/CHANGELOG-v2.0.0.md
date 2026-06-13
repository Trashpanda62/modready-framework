# BetaDeps v2.0.0 — Framework-core

**Built 2026-06-13.** BetaDeps graduates from a compatibility/dependency mod to
a **modding framework**. v2.0.0 lands the framework-*core* primitives — the
developer-facing building blocks that compile and are verified off-engine. The
gameplay megafeatures on the Phase-3 roadmap (weather, settlement, RTS battle
control, etc.) remain separate multi-week projects and are NOT in this build.

All five primitives are exercised by `tools/framework-selftest` (a net472
console, 54 assertions, runs without launching the game): `dotnet run -c Release
--project tools/framework-selftest`.

## New: `BetaDeps.Framework` namespace

| Primitive | Assembly | What it does |
|---|---|---|
| `EventBus` | Foundation | Typed + named-channel pub/sub for mod-to-mod IPC. Exception-isolated handlers, per-subscription throttle, reentrancy-safe dispatch. |
| `ModConflictDetector` | Foundation | Scans Harmony's registry for methods patched by ≥2 third-party mods; ranks High/Medium/Low; text + markdown reports. Auto-runs in-game and logs to `runtime.log`. |
| `PerfProfiler` | Foundation | Manual `Measure(owner,label)` scopes + opt-in Harmony auto-instrument that attributes per-method cost to the owning mod(s). `Enabled=false` → zero-cost. |
| `SettingsProfileStore` | Foundation | Engine-free file engine for whole-loadout settings profiles (named snapshots of a dir of `<id>.json`). |
| `ProfileManager` | MCM | Captures every Global settings file into one named profile and applies + live-reloads them — switch a whole loadout in one call. |

Per-campaign settings (the remaining v2.0 roadmap settings item) were already
completed in v0.9.x (`PerCampaignSettings<T>` + `Configs\ModSettings\PerCampaign\<campaignId>\`).

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

- BetaDeps module `SubModule.xml`: `v0.9.2` → **`v2.0.0`**.
- Internal assemblies (`BetaDeps.Foundation`, `BetaDeps.Harmony`): `FileVersion`
  → `2.0.0.0`; `AssemblyVersion` stays pinned `0.9.0.0` (dependents bind to it).
- Impersonation assemblies (UIExtenderEx/ButterLib/MCMv5) and `Bannerlord.Harmony`:
  Assembly/FileVersion unchanged; only `InformationalVersion` → `BetaDeps 2.0.0`.
- `BetaDeps-v2.0.0.zip` produced; Aragas-string, XML-lint, and release-validation
  gates all pass.

## API docs

Consumer guide: `docs/BETADEPS-NATIVE-API.md` → **Module 7: BetaDeps.Framework**.

## Not in this build (still roadmap)

Event-driven gameplay frameworks that need the live engine: weather, settlement
("lively cities"), custom tactics, top-down RTS battle control, battlesizer,
achievement unblocker bundle, blood/gore slider, in-game mod composer. Each is
its own project; none are required for the framework-core to ship.
