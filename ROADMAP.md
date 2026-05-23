# BetaDeps roadmap

Hour estimates are rough — each item assumes a focused-attention session, not idle research time. Numbers in parentheses are wall-clock hours.

---

## Where we are now (2026-05-22, v0.5.6 shipped)

Tonight's session arc:
- **v0.5.0** initial Nexus release (text-only numerics; sliders deferred)
- **v0.5.1** polish round — 10 audit fixes (ProxyRef LogCaught, Interlocked install gates, FNV-1a hash, allocation-free hot paths, consolidated reflection helper, dead-code removal)
- **v0.5.3** sliders RESTORED for integer settings — completed the 6-week bisect, pinned the regression to a 6-per-page Gauntlet construction ceiling
- **v0.5.4** LoaderException diagnostic + `--no-incremental` build hardening
- **v0.5.5** float sliders via unified Slot{n}_FloatValue dispatcher (ints + floats share one slider per slot)
- **v0.5.6** Mod Config polish — action buttons render, duplicate names disambiguated, source-folder annotation for cryptic DisplayNames, page-summary cleanup

All compatibility-layer functionality is in place. ROT (Realm of Thrones) is the one outstanding consumer-mod incompatibility (two model classes missing v1.4.5 abstract methods).

---

## Phase 1 — v1.0 production-ready compatibility (30–50 h)

Polish, document, and harden what BetaDeps already does.

| Item | Hours | Notes |
|---|---|---|
| Mod-list **search/filter** in Mod Config | 2–3 | Text box, filters the prev/next-cycler by substring; high QoL with 24+ mods. **Picked as next session's starter.** |
| Hover **tooltips** / hint panel display | 1–2 | Mixin already collects HintText; just need to render it below the row list |
| **ROT shim** attempt #3 (proper subclass with ref assemblies) | 4–6 | Needs TaleWorlds reference-assembly access; subclass DefaultArmyManagementCalculationModel + DefaultCombatSimulationModel, override the missing methods, register at GameStarter.AddModel time |
| **6-slider ceiling** root cause investigation | 3–5 | Diff v0.4.12 (worked at 10) vs v0.4.19 (broke at 10). Could unlock slots 6-9 too. Could also be a dead-end. |
| **BetterExceptionWindow MCMv5 adapter** compat shim | 2–3 | The MCM.UI.Adapter.MCMv5 assembly has 3 types missing methods on our MCMv5; provide shim implementations |
| **Immersive Battlefields MCM_IB_Addon** error suppression | 1 | Throws "Sequence contains no matching element" on discovery; catch it cleaner, move it to QUIRK status |
| **README.md** — full project overview, install instructions, contribute guide | 2–3 | |
| **API reference** — XML doc comments on every public type/member, generate docs site | 2–3 | |
| **GitHub repo** setup + issue templates + CI workflow | 1–2 | |
| **Build your first BetaDeps mod** walkthrough — start to MCM-visible setting | 3–4 | |
| **Wide-fleet testing** (run with 50+ mods enabled) | 3–5 | Real-world load-order stress test |
| **v1.0 release polish** — final pass on Nexus description, version-bump, changelog | 2 | |
| **TOTAL** | **30–50 h** | |

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

**Mod-list search/filter** — 2–3 hour starter task for the next session.
