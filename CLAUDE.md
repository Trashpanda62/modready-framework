# BetaDeps project — persistent instructions

## 🗣️ SHORT TRIGGER WORDS

The user prefers single-word replies for common check-ins:
- **"go"** = test cycle finished, read the logs and continue
- **"pause"** / **"call it for tonight"** = stop here, run the nightly protocol
- (Used to be "done"; user switched to "go" on 2026-05-24.)

## 🤖 OPERATING RULE — pick the easiest unblocked task and just work

Every project day on the calendar has a numbered task list in its
description, ordered easiest-unblocked first with hour estimates.
**Never ask "what's next?"** — pick the lowest-numbered TODO item
whose dependencies are met and start working on it. After completing
each item, update the calendar event description to strike it through
(prefix with `✓` and wrap in `~~...~~`), then immediately move to the
next one. Only stop and ask the user when:

- A task requires a decision only the user can make (taste, naming,
  scope cut)
- A task is blocked on something only the user can do (install
  software, post on social media, accept a TOS)
- The user explicitly says "pause" / "wait" / "call it for tonight"

If a task takes longer than its hour estimate, push the overrun into
the next day's plan rather than rushing — don't ship broken code to
stay on schedule.

## 🌅 MORNING CHECK-IN — do this at the start of every session

When the user opens a new session in the morning (greeting like "good
morning", "let's start", "what's up today", etc.), immediately:

1. Call `mcp__2fb8a39b-...__list_events` with today's date range
   (00:00 → 23:59 America/New_York, primary calendar) to see what's
   on the schedule.
2. Read the latest `C:\dev\bannerlord\MASTER-PLAN.md` to see where
   we left off last night.
3. Open with a short briefing in chat (5–8 lines): today's calendar
   items, what last night's session shipped, and a "first task" the
   user can start on. End with a single concrete action.

This is a standing morning ritual — never skip it.

## 📋 NIGHTLY MASTER PLAN UPDATE — do this at the end of every session

At the end of each working session (when the user says "call it for tonight",
"goodnight", "pause", or otherwise signals they're stopping), update
`C:\dev\bannerlord\MASTER-PLAN.md`. The update should include:

1. **Phase progress** — move items from 🔄 to ✅ where work landed; add new
   🔄 items for work the user asked to be queued
2. **Current status** section — refresh shipped/active/limitations
3. **Work hours** table — add a row for today's date with estimated hours
   (use `runtime.log` timestamps, git commit times, or session activity as
   the basis). Bump the running total
4. **Active tasks** — sync against the Cowork task list
5. **Rolling daily-breakdown calendar events** — every Sunday night (or
   the last working session of a calendar week), look ahead to the
   following Monday and create 7 Google Calendar events (one per day)
   covering the next week's project work, split into per-day tasks
   (9am–5pm America/New_York). Use the `mcp__2fb8a39b-...__create_event`
   tool. Color code by project: BetaDeps=tangerine(7),
   auto-fix=blueberry(9), v1.0=basil(10), HERALD=banana(5),
   archive=graphite(8), Total Bannerwar=peacock(7),
   Battle Tactics=grape(3), WeatherSystem=sage(2),
   RV app=lavender(1), AppCreate=flamingo(4), Junkball=tomato(11).
   Daily breakdown is currently scheduled through 2026-06-14.
6. **Master-plan .ics auto-sync** — `C:\dev\bannerlord\master-plan-calendar.ics`
   is the source of truth for project milestones. It is hosted as a
   public GitHub Gist (`https://gist.github.com/Trashpanda62/069f5e4fe73e19e19e69b1700231b50a`)
   that the user's Google Calendar subscribes to. The gist is cloned
   locally at `C:\dev\bannerlord\gist-master-plan\`. **Every nightly
   update, after you finish editing master-plan-calendar.ics, tell
   the user to run:**

   ```
   cd C:\dev\beta-deps
   .\scripts\Update-Gist.ps1
   ```

   That script copies the .ics into the gist clone, commits, and
   pushes. Google polls the gist roughly every 12–24 hours and the
   subscribed calendar updates. The script is idempotent — if the
   .ics didn't change, it exits cleanly without a push.

Then deliver a short summary in chat (5–10 lines) showing the diff from
last night: what was completed, what's queued, total hours, what's next.

The user explicitly requested nightly master-plan updates including current
progress and work hours. This is a standing instruction, not a one-off.

## ⚠️ CANONICAL TEST COMMAND — memorize this exactly

The ONLY way to ask the user to test a code change is:

```
cd C:\dev\beta-deps
.\scripts\Quick-Test.ps1
```

Do NOT invent variants like `powershell -ExecutionPolicy Bypass -File ...`, `pwsh ./scripts/...`, or anything else. The user has called this out as a recurring annoyance after every context compression. If you find yourself typing anything other than the two lines above, stop and use them. After it finishes, read `C:\dev\bannerlord\runtime.log` and `C:\dev\bannerlord\selftest.log` (Quick-Test copies them there automatically).

## ⚠️ ALWAYS include the `cd` step in PowerShell instructions

The user is non-technical and runs whatever PowerShell prompt they happen to have open — which is often `C:\WINDOWS\system32`. **Every PowerShell command block you give them must start with the `cd` line to the correct folder.** No exceptions. This applies to git commands, dotnet commands, scripts, anything.

Wrong:
```
git push
```

Right:
```
cd C:\dev\beta-deps
git push
```

If a command set spans multiple repos or folders, give a separate `cd` for each. If the user already ran `cd` earlier in the same response, repeat it in the next block anyway — they may copy/paste blocks individually or come back later from a fresh window. The user lost time on a "fatal: not a git repository" error from running `git remote set-url` in `C:\WINDOWS\system32` and called this out explicitly.

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
