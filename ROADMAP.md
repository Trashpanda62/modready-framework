# ModReady roadmap

Hour estimates are rough — each item assumes a focused-attention session, not idle research time. Numbers in parentheses are wall-clock hours.

---

## Where we are now (2026-06-08, v0.9.1 shipped)

The compatibility layer is **done and shipped**. ModReady is a working dependency framework on Nexus (mod 11274): additive Harmony/UIExtenderEx/ButterLib/MCM stack, Mod Config screen with int+float sliders, PatchShield/SaveShield safety, IncompatibleModDetector, MO2 support. The v0.5 → v0.9 arc closed out every original Phase-1 compatibility item — search/filter, tooltips, wide-fleet testing, docs, GitHub repo, the lot. **Realm of Thrones is dropped** (no longer our concern), which retires the last open consumer-mod incompatibility.

With the framework solid, **v1.0 pivots from "polish the compatibility layer" to a headline player-facing feature**: the non-destructive music picker. The old Phase-1 compatibility punch-list is retired/maintenance; the remaining unshipped items move to "ongoing maintenance" below.

---

## Phase 1 — v1.0 Non-Destructive Music Picker (headline) (~46–71 h)

The flagship feature that makes ModReady worth installing for its own sake: choose your own music per game context (campaign, battle, siege, settlement, naval) **without overwriting any vanilla audio file**. Both core primitives are spike-proven (PSAI redirect + Engine.Music loose-file, 2026-06-08). **Full plan: [`docs/V1.0-MUSIC-PICKER-PLAN.md`](docs/V1.0-MUSIC-PICKER-PLAN.md).**

| Item | Hours | Notes |
|---|---|---|
| **Spike #3** — runtime soundtrack gen + `StartTheme` redirect holds w/ cycling tracks | 3–5 | Biggest remaining risk; de-risks the whole PSAI path. **Next session's starter.** |
| **Spike #4** — settlement playback loop + bard suppression + slider + clean release | 4–6 | De-risks the Engine.Music settlement path |
| `MusicConfig` + folder-convention loader + context/pool model | 3–4 | BYO folder tree IS the data model |
| `PsaiRedirectManager` — soundtrack gen + `StartTheme` patch + theme map | 6–9 | Covers menu/campaign/battle/siege/victory/defeat/naval |
| `SettlementMusicManager` — channel mgmt + advance loop + bard suppression | 6–9 | Covers town/village/tavern (FMOD path) |
| `NavalGate` — War Sails DLC detection + conditional Naval context | 1–2 | Gate naval row on NavalDLC presence |
| **UI-A** — MCM groups (enable/mode/volume/track-count per context) | 4–6 | Fast path; makes the engine usable on infra we already have |
| **UI-B** — dedicated two-pane Music screen + Preview | 10–16 | The headline-worthy picker; marketing centerpiece |
| Persistence + per-save vs global decision + defaults | 2–3 | Runtime-only state; no save-format impact |
| Optional **royalty-free sample pack** (separate download) | 3–5 | One-click "hear it working" for newcomers |
| Spike teardown + docs + Nexus description + changelog + release | 4–6 | See plan §14 teardown checklist |
| **TOTAL** | **~46–71 h** | Engine-usable subtotal (through UI-A): ~29–44 h |

---

## v1.0 ongoing maintenance (unshipped compat items, low priority)

Carried over from the original Phase-1 list; none block the v1.0 music release.

| Item | Hours | Notes |
|---|---|---|
| **BetterExceptionWindow MCMv5 adapter** compat shim | 2–3 | The MCM.UI.Adapter.MCMv5 assembly has 3 types missing methods on our MCMv5 |
| **Immersive Battlefields MCM_IB_Addon** error suppression | 1 | "Sequence contains no matching element" on discovery; move to QUIRK status |
| **6-slider ceiling** root cause investigation | 3–5 | Diff v0.4.12 (worked at 10) vs v0.4.19 (broke at 10); could unlock slots 6-9 |
| **API reference** — XML doc comments + generated docs site | 2–3 | |

---

## Phase 2 — v1.5 modder experience layer (40–80 h)

Lower the bar to author a Bannerlord mod.

| Item | Hours | Notes |
|---|---|---|
| ✅ **JSON/YAML settings declaration** | 8–12 | DONE (2026-06-13) — `mod.json` → Mod Config menu + persistence with zero C#. `ModReady.Framework.ModJsonParser`/`ModJsonLoader`; auto-discovered at MCM load. 26 self-test assertions. |
| ✅ **Mod templates** — `modready new-mod` | 8–12 | DONE (2026-06-13) — `ModReady.Framework.ModScaffolder.Generate` (SettingsOnly / HarmonyTweak / Full templates → SubModule.xml + mod.json/csproj/SubModule.cs). Standalone CLI wrapper still TODO. 14 self-test assertions. |
| **Hot reload** of settings + content | 4–8 | File-watcher on `mod.json`, refresh Mod Config without restart. Real engine integration is the hard part. |
| **Debug overlay** — in-game F12 panel | 8–12 | Shows Harmony patches grouped by target, mixin attachments, live settings state, recent exceptions. Invaluable for mod authors. |
| **Localization fallback** | 4–6 | Automatic language-string lookup; mod authors stop needing to ship 24 XMLs for one menu label |
| **TOTAL** | **40–80 h** | |

---

## Phase 3 — v2.0 modding framework (100–200 h, aspirational)

Turn ModReady from a dependency mod into the recommended foundation for new mods. Each line item below is its own multi-week project.

### Framework-core — SHIPPED as the v1.0.0 release (2026-06-13)

The developer-framework primitives — the parts that compile + are unit-verified
off-engine — landed together and the module ships as **v1.0.0** (Steve's call to
brand the framework launch v1.0; tested in-game 2026-06-13). New public surface
under the `ModReady.Framework` namespace (see `docs/MODREADY-NATIVE-API.md`
Module 7). All five below verified by the `tools/framework-selftest` harness
(55 assertions, runs without the game):

- **EventBus** — typed + named-channel pub/sub for mod-to-mod IPC; exception-isolated, throttle, reentrancy-safe (`Foundation/Framework/EventBus.cs`).
- **ModConflictDetector** — scans Harmony's registry for methods patched by ≥2 third-party owners, ranks High/Medium/Low; auto-logs in-game via `FrameworkBootstrap` (`Foundation/Framework/ModConflictDetector.cs`).
- **Mod presets (whole-loadout profiles)** — `SettingsProfileStore` (engine-free file engine) + `ProfileManager` (MCM glue: capture every Global settings file → named profile → switch + live reload).
- **Performance profiler** — manual `Measure` scopes + opt-in Harmony auto-instrument (flag-gated `perf-profiler.flag`), per-owner cost attribution (`Foundation/Framework/PerfProfiler.cs`).
- **Per-campaign settings** — already completed in v0.9.x (`PerCampaignSettings<T>` + scoped `Configs\ModSettings\PerCampaign\<campaignId>\`); the v2.0 line is satisfied.

Remaining Phase 3 items below (weather/settlement/RTS/etc.) are the gameplay
megafeatures — each still its own multi-week project needing live-game work.

| Item | Hours | Notes |
|---|---|---|
| ✅ **Event bus / mod-to-mod IPC** | 12–20 | DONE v1.0.0 — type-safe + named-channel, throttle, exception isolation |
| ✅ **Mod conflict detector** | 8–12 | DONE v1.0.0 — surfaces ≥2-owner Harmony overlaps, severity-ranked, auto-logged |
| ✅ **Per-campaign settings** | 12–16 | DONE (v0.9.x) — per-campaign-id scoped storage + singleton reset |
| ✅ **Mod presets** | 4–6 | DONE v1.0.0 — whole-loadout profile capture/apply with live reload |
| ✅ **Performance profiler** | 8–12 | DONE v1.0.0 — manual scopes + opt-in per-mod Harmony auto-instrument |
| **Mod-list search/filter** (already in v1.0 above; extended here with tags, categories) | 2–4 | |
| **Weather framework** | 20–40 | Reusable weather engine — wind, rain, snow effects mod authors can call |
| **Settlement framework** ("more lively cities") | 30–60 | Background events, NPC patterns, day-night routines |
| **Battlesizer** | 8–12 | Granular battle-size scaling beyond what BattleSizeResized does |
| **Custom tactics framework** | 30–50 | Author scripts AI tactics declaratively |
| **Top-down Total War-style battle control** | 50–100 | Huge feature — RTS-style command from an overview camera |
| **Achievement unblocker** | 4–8 | Bundle the unblocker into the framework |
| **Blood/gore slider** | 4–8 | Bundle settings into Mod Config |
| **In-game mod composer** (low-code mod creation) | 80–160 | Visual builder for non-coders — out of v2.0 scope unless someone with engine experience joins |
| **TOTAL** | **100–200+ h** | (some items above can spin into their own projects) |

---

## Non-coder accessibility

Honest scope: ModReady cannot make Bannerlord modding fully accessible to people who don't write code, and v2.0 doesn't try. TaleWorlds' engine isn't designed for it.

What ModReady *can* offer non-coders:
- **Tweak** what coders ship via the Mod Config menu (today)
- **Mix and match** prebuilt behaviors via JSON config files for simple "change a number" mods (v1.5)
- **See** which two mods are fighting via the conflict detector (v2.0)

Authoring brand-new gameplay still needs C# and an IDE.

---

## Phase 1 next-up

**Spike #3** — prove the `MBMusicManager.StartTheme` redirect *holds* at the menu with 2 tracks cycling from a runtime-generated `soundtrack.xml`. 3–5 hours; single biggest remaining risk on the v1.0 music-picker critical path. See [`docs/V1.0-MUSIC-PICKER-PLAN.md`](docs/V1.0-MUSIC-PICKER-PLAN.md) §3 + §8.
