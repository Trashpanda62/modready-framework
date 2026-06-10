# BetaDeps roadmap

Hour estimates are rough — each item assumes a focused-attention session, not idle research time. Numbers in parentheses are wall-clock hours.

---

## Where we are now (2026-06-08, v0.9.1 shipped)

The compatibility layer is **done and shipped**. BetaDeps is a working dependency framework on Nexus (mod 11274): additive Harmony/UIExtenderEx/ButterLib/MCM stack, Mod Config screen with int+float sliders, PatchShield/SaveShield safety, IncompatibleModDetector, MO2 support. The v0.5 → v0.9 arc closed out every original Phase-1 compatibility item — search/filter, tooltips, wide-fleet testing, docs, GitHub repo, the lot. **Realm of Thrones is dropped** (no longer our concern), which retires the last open consumer-mod incompatibility.

With the framework solid, **v1.0 pivots from "polish the compatibility layer" to a headline player-facing feature**: the non-destructive music picker. The old Phase-1 compatibility punch-list is retired/maintenance; the remaining unshipped items move to "ongoing maintenance" below.

---

## Phase 1 — v1.0 Non-Destructive Music Picker (headline) (~46–71 h)

The flagship feature that makes BetaDeps worth installing for its own sake: choose your own music per game context (campaign, battle, siege, settlement, naval) **without overwriting any vanilla audio file**. Both core primitives are spike-proven (PSAI redirect + Engine.Music loose-file, 2026-06-08). **Full plan: [`docs/V1.0-MUSIC-PICKER-PLAN.md`](docs/V1.0-MUSIC-PICKER-PLAN.md).**

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
| **JSON/YAML settings declaration** | 8–12 | Parser, runtime settings-class generator. Drop `mod.json` → get Mod Config menu + persistence with zero C#. Massive for tweak mods. |
| **Mod templates** — `betadeps new-mod` CLI | 8–12 | Scaffolds working starter projects (combat-tweak, ai-tweak, world-tweak, etc.) |
| **Hot reload** of settings + content | 4–8 | File-watcher on `mod.json`, refresh Mod Config without restart. Real engine integration is the hard part. |
| **Debug overlay** — in-game F12 panel | 8–12 | Shows Harmony patches grouped by target, mixin attachments, live settings state, recent exceptions. Invaluable for mod authors. |
| **Localization fallback** | 4–6 | Automatic language-string lookup; mod authors stop needing to ship 24 XMLs for one menu label |
| **TOTAL** | **40–80 h** | |

---

## Phase 3 — v2.0 modding framework (100–200 h, aspirational)

Turn BetaDeps from a dependency mod into the recommended foundation for new mods. Each line item below is its own multi-week project.

| Item | Hours | Notes |
|---|---|---|
| **Event bus / mod-to-mod IPC** | 12–20 | Type-safe event registration, subscription, throttle controls |
| **Mod conflict detector** | 8–12 | Surfaces Harmony patches from different mods targeting the same method, suggests resolution |
| **Per-campaign settings** | 12–16 | Different MCM values per save-game, savegame integration |
| **Mod presets** | 4–6 | Save and switch between settings profiles |
| **Performance profiler** | 8–12 | Per-mod cost surface (Harmony patch overhead, allocations) |
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

Honest scope: BetaDeps cannot make Bannerlord modding fully accessible to people who don't write code, and v2.0 doesn't try. TaleWorlds' engine isn't designed for it.

What BetaDeps *can* offer non-coders:
- **Tweak** what coders ship via the Mod Config menu (today)
- **Mix and match** prebuilt behaviors via JSON config files for simple "change a number" mods (v1.5)
- **See** which two mods are fighting via the conflict detector (v2.0)

Authoring brand-new gameplay still needs C# and an IDE.

---

## Phase 1 next-up

**Spike #3** — prove the `MBMusicManager.StartTheme` redirect *holds* at the menu with 2 tracks cycling from a runtime-generated `soundtrack.xml`. 3–5 hours; single biggest remaining risk on the v1.0 music-picker critical path. See [`docs/V1.0-MUSIC-PICKER-PLAN.md`](docs/V1.0-MUSIC-PICKER-PLAN.md) §3 + §8.
