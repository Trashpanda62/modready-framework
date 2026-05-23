# BetaDeps project — persistent instructions

## ⚠️ CANONICAL TEST COMMAND — memorize this exactly

The ONLY way to ask the user to test a code change is:

```
cd C:\dev\beta-deps
.\scripts\Quick-Test.ps1
```

Do NOT invent variants like `powershell -ExecutionPolicy Bypass -File ...`, `pwsh ./scripts/...`, or anything else. The user has called this out as a recurring annoyance after every context compression. If you find yourself typing anything other than the two lines above, stop and use them. After it finishes, read `C:\dev\bannerlord\runtime.log` and `C:\dev\bannerlord\selftest.log` (Quick-Test copies them there automatically).

## Nexus file-description format (per-version upload blurb)

The per-file description field on Nexus has a **255-character limit** and shows in the Files tab. It is NOT a changelog — it's a short user-facing summary.

**Format the user approved (v0.5.8):**

> Updated Mod Config visuals (right-side hover hints, sliders on every numeric setting, auto-pagination) and faster mod switching. Fixes Retinues exit crash, BetterExceptionWindow settings UI, and Self-Test backup recovery.

**Pattern:** `Updated X (parenthetical list of specific visible changes). Fixes Y, Z.`

- "Updated …" leads with the visible improvements users actually see
- Parenthetical lists each concrete UX change in plain user language
- "Fixes …" trails with the bug fixes
- Concrete and short — no detailed how/why, no per-mod testimonials, no version numbers besides the implicit one
- Stay under 230 chars to leave a buffer for the 255-char hard limit
- One paragraph, no bullet points, no markdown — Nexus renders this as plain text

Write this to `C:\dev\bannerlord\nexus-file-description-v<X.Y.Z>.md` so the user can paste it into the Nexus upload form. Update the main Nexus description (`C:\dev\bannerlord\nexus-description.md`) separately — that one is longer-form prose with sections.

## Shipping authority

**The user — not Claude — decides what ships and when.** Both directions of this matter:

- Do NOT proactively suggest shipping a release that lacks features the user has previously called out as required (e.g., working sliders). Earlier today: "no we do not ship 1.0 yet, you don't get to decide that" and "get the sliders working again, you don't get to decide that".
- When the user says "ship it", do not push back even if a previous directive said don't ship. The user is overriding their own earlier criteria; respect that and execute.
- If a slider/feature debug session is failing badly, ask the user how they want to proceed — don't decide unilaterally to ship-as-is OR to keep debugging forever.

## v0.5.3 SLIDERS RESTORED (shipped 2026-05-22)

v0.5.3 ships with **draggable sliders working** for integer settings in the first 6 slots per Mod Config page. The slider regression was scale, not bindings or widget structure. Full bisect: see "Slider bisect findings" below.

v0.5.0 / v0.5.1 had numeric properties displaying as read-only text (no draggable sliders).

## Slider bisect findings (2026-05-22, RC10-RC19)

**Conclusion: 6 sliders per page is the hard ceiling.** 7+ SliderWidget instances on a single Mod Config page crashes Gauntlet UI construction silently (no log entry). Threshold is firm regardless of nav widgets, bindings, or surrounding XML.

What was ruled OUT:
- ❌ Slider widget construction itself (RC10 with literal `MaxValueFloat="100" MinValueFloat="0" ValueFloat="50"` renders fine)
- ❌ `ValueFloat` binding (RC11, single slider with bound value, renders fine)
- ❌ `MaxValueFloat` + `MinValueFloat` bindings (RC12, all three bindings, renders fine)
- ❌ `IsVisible="@Slot0_IsInteger"` binding on the wrapping ListPanel (RC13, renders fine)
- ❌ NavigationTargetSwitcher + NavigationAutoScrollWidget pattern (RC18 stripped them; 10 sliders still crashed)
- ❌ Sprite/brush paths (Video options sliders render fine on v1.4.5 — vanilla widget itself works)

What WAS found:
- ✓ 1 slider: works
- ✓ 5 sliders (RC15): works
- ✓ 6 sliders (RC17): works
- ✗ 7 sliders (RC16): crash
- ✗ 10 sliders (RC14): crash
- ✗ 10 sliders, nav-stripped (RC18): crash
- ✗ 20 sliders (RC8): crash

The shipping fix in v0.5.3 (`OptionsPrefabExtensions.cs`): `BuildSlotRow` gates `BuildSliderBlock` to `n < 6`. SlotCount stays 10 to match mixin pagination so no entries are hidden — slots 6-9 keep the text-only RichTextWidget fallback for numerics.

## Possible angles for v0.7 (float sliders + slot 6-9 sliders)

- **Float sliders**: add unified `Slot{n}_NumericValue` (float dispatcher: `IsInteger ? IntValue : FloatValue`) + `Slot{n}_IsNumeric` to OptionsVMMixin. One slider per slot can serve both int and float — total slider count stays ≤ 10 but with 6 visible. Combined with the `n < 6` gate, that's 6 unified sliders per page covering BOTH int and float.
- **Slot 6-9 sliders**: probably requires figuring out the actual ROOT CAUSE of the 6-slider ceiling. Candidates to investigate: per-page Gauntlet draw-call limit, per-page SliderWidget native handle count, sprite-sheet binding count, AppDomain assembly load ordering. Backward-bisect what changed between v0.4.12 (worked at 10) and v0.4.19 (broke at 10) — likely a Bannerlord game update tightened a limit. Diff vanilla OptionItem.xml in the v1.4.5 install against an older Bannerlord backup if available.
- **Diff angle**: check `OptionsVMMixin.cs` and `SettingsPropertyVM.cs` against a v0.4.12 backup if one exists.

## Possible angles not yet tried

- ~~Add `<NavigationTargetSwitcher>` and `<NavigationAutoScrollWidget>` siblings BEFORE the SliderWidget~~ **TRIED v0.6 RC7/RC8 (2026-05-22): RULED OUT.** Adding the nav siblings did not fix it. RC7 (slot 0 only) appeared to work — but only because slot 0 landed on group headers in both AIInfluence and Faster Time, so `Slot0_IsInteger`/`Slot0_IsFloating` were both false and the slider widget was skipped via `IsVisible="false"`. RC8 (all 10 slots) reproduced the exact v0.4.19+ crash signature: clicking Options showed Video page partially rendered with no tab strip, no Cancel/Done buttons, no dropdowns populated. Conclusion: the SliderWidget construction itself crashes, navigation siblings are NOT the missing piece. `BuildSliderBlock` helper kept in `OptionsPrefabExtensions.cs` for future RC attempts; `BuildSlotRow` reverted to text-only.
- Diff OptionsVMMixin.cs and SettingsPropertyVM.cs against any known v0.4.12 era backup
- Check if a Bannerlord game update broke vanilla slider construction expectations (compare current Sprite paths `SPGeneral\SPOptions\standart_slider_*` to current game install)
- Try slider with Min/Max as constant bindings (not slot-specific) to eliminate divide-by-zero on edge cases
- Try `WidthSizePolicy="CoverChildren"` on the slider's parent ListPanel (current is `CoverChildren` already)
- Test slider XML extraction by injecting it directly into vanilla OptionsGroupedPage.xml with vanilla bindings (no Slot{n}_ prefix) to isolate "is this our bindings that crash, or the slider widget itself on current game version"

## Workflow rules

- When the Edit tool truncates a file mid-string, use bash heredoc (`cat >> file <<'EOF' ... EOF`) instead.
- Touch source files before suggesting Quick-Test so dotnet's incremental build doesn't skip recompilation.
- Always look at the deployed DLL mtime vs source mtime before interpreting a crash log — sometimes the test ran against stale code.
- The Nexus description (`dist/nexus-description.md`) must accurately reflect what shipped. Update it before declaring a build ship-ready.

## ALWAYS use Quick-Test or Ralph-Loop — never ask the user to copy logs manually

This project ships two PowerShell drivers in `scripts/`. **Always recommend one of these instead of asking the user for log copies, screenshots, or manual rebuilds.** I keep forgetting they exist — re-read this section every session.

### `scripts/Quick-Test.ps1` — the default "did my change work?" loop

What it does: kills leftover BL processes → rebuilds via Build-Phase1.ps1 → launches BLSE LauncherEx → waits for `runtime.log` to stabilize → **copies `runtime.log` AND `selftest.log` to `C:\dev\bannerlord\` so Claude can read them directly.**

Use it whenever:
- I asked the user to test a code change
- I need to see the latest runtime.log / selftest.log
- The user reports a bug and I need a fresh log

**Tell the user this exact step:**
```
cd C:\dev\beta-deps
.\scripts\Quick-Test.ps1
```
Then read BOTH `C:\dev\bannerlord\{runtime,selftest}.log` AND `Modules\BetaDeps\{runtime,selftest}.log`. The `bannerlord` copies can lag (Quick-Test snapshots once and doesn't always catch the final state if the game session continues past the stable-detect window). The `Modules\BetaDeps\` versions are the live ground truth once Bannerlord has exited. **Never tell the user the lag is their fault** — it's how the copy step works, not user error.

### `scripts/Ralph-Loop.ps1` — the hypothesis-driven bisection loop

What it does: snapshots mutable source → applies a named hypothesis (`baseline` / `no-shim` / `minimal-xml` / `no-mcm`) → builds → launches → captures a full diag report (runtime.log + rgl_log + crash dumps + BLSE logs + DLL probe) into `ralph-report-<hyp>-<ts>.txt` → restores the snapshot.

Use it whenever:
- BetaDeps fails to load and we don't know why (boot-time / SubModule failures)
- I want to A/B test "does this loadout fix or break loading?" without leaving permanent changes
- The user reports "won't start" type bugs

**Tell the user the exact hypothesis to run, e.g.:**
```
cd C:\dev\beta-deps
.\scripts\Ralph-Loop.ps1 -Hypothesis baseline
```

### When NOT to fall back to manual steps

If I'm about to ask the user to:
- "Open mod options, then send me the log" → say `Quick-Test.ps1` instead
- "Copy these files for me" → they're already copied by Quick-Test, just read `C:\dev\bannerlord\runtime.log` / `selftest.log`
- "Rebuild then relaunch" → that's literally what Quick-Test does
- "Try this loadout to see if it crashes" → that's Ralph-Loop with a hypothesis

STOP and recommend the script. The user has these tooled up for a reason.
