# BetaDeps — "Ship It" Release Process

**Trigger:** Steve says **"ship it"** (optionally with a version, e.g. "ship it 0.9.3").
This is the canonical release runbook. Follow it top to bottom.

Phase 1 (the actual feature/bug work) is already done and tested in-game by the
time "ship it" is said. This runbook is **phases 2–6**: audit → version/commit/tag
→ package → push → Steam Workshop.

**Pick the version:** the next number after the last git tag (`git tag --list`).
There is no `v0.9.1` tag historically — tags start sparse; just go forward.

---

## Phase 2 — Audit

1. `git diff <last-tag>` — read **every** change since the last release.
2. Watch for: unintended side effects / scope creep (e.g. editing a *shared* brush
   that also affects another feature — this bit us once with `BetaDeps.RowHover`),
   dead code, stale comments, debug leftovers.
3. **Version metadata in every `.csproj`:**
   - Bump **`FileVersion`** + **`InformationalVersion`** → `X.Y.Z` / `BetaDeps X.Y.Z`.
   - **DO NOT bump `AssemblyVersion`.** This is load-bearing:
     - Impersonation libs — MCM `5.11.99.0`, UIExtenderEx `2.13.2.0`,
       ButterLib `2.10.4.0` — **must mirror upstream BUTR** or consumer-mod
       assembly binding resolves to the wrong copy and BetaDeps fails silently.
     - Own-assembly pins — Foundation / Harmony / HarmonyHost `0.9.0.0` — are
       **deliberately stable**; their own csproj comments warn that bumping them
       causes *CLR binding failures at game launch* on incremental builds.

---

## Phase 3 — Version, commit, tag

1. `src/BetaDeps.Module/SubModule.xml` → `<Version value="vX.Y.Z" />`.
   Leave `<Url value="" />` **empty** — it's a homepage field, **not** the Workshop
   item selector. A bare item-id there is malformed and ships to subscribers.
2. Update `.gitignore` for any new dev scratch (reflection dumps, SDK artifacts).
3. Commit to `main` (trunk-based; this repo tags releases directly on `main`).
   Message: `vX.Y.Z: <one-line summary>`, ending with the Co-Authored-By trailer.
4. `git tag -a vX.Y.Z -m "vX.Y.Z: <summary>"`.

---

## Phase 4 — Package (Nexus artifacts)

1. `powershell scripts\Build-Phase1.ps1 -Version X.Y.Z -SkipDeploy`
   - Clean-builds all projects and stages `dist\Modules\`. Gates that WILL fail
     the build (fix the source, commit, re-run — **never** bypass):
     - no Aragas/BUTR copyright strings in our authored DLLs,
     - **XML lint** (catches `--` inside XML comments — illegal, Vortex-crash risk),
     - release validation (well-formed `SubModule.xml` + non-empty dependency
       versions — empty `DependentVersion` crashes Vortex's BUTR manager).
   - Produces `dist\BetaDeps-vX.Y.Z.zip` (the 5-folder bundle).
   - `-SkipDeploy` because the live install is already current from Phase 1 dev.
2. `powershell scripts\Package-Optional-Deps.ps1 -Version X.Y.Z`
   - Slices the 4 standalone dependency zips (Harmony, UIExtenderEx, ButterLib,
     MCM) from the already-staged `dist\Modules\` tree (no rebuild → identical
     binaries to the bundle).
3. Result in `dist\`: 1 bundle + 4 dependency zips, all release-validated.

---

## Phase 5 — Push

1. `git push origin main`
2. `git push origin vX.Y.Z`
   - If a post-tag fix forced you to move the tag: `git push origin vX.Y.Z --force`
     (acceptable here — solo repo, tag freshly cut).

---

## Phase 6 — Steam Workshop prep

The TaleWorlds uploader takes a **`WorkshopUpdate-*.xml`** task file
(`<ItemId>` + `<ModuleFolder>`→`dist\` + `<ChangeNotes>`), **NOT** a `SubModule.xml`.
`<Url>` is irrelevant to it. `UpdateItem` carries **no** name/description → existing
Workshop page names & descriptions are preserved.

1. In `C:\dev\bannerlord\workshop\`, set `<ChangeNotes>` for **only the modules
   that actually changed** (usually `WorkshopUpdate-BetaDeps.xml` +
   `WorkshopUpdate-MCM.xml`; leave the 3 pure-dependency notes empty if unchanged).
2. `<ModuleFolder>` already points at `C:\dev\modready\framework\dist\Modules\...` (current build).
3. Item IDs: BetaDeps `3741426797` · Harmony `3741428196` · UIExtenderEx
   `3741428357` · ButterLib `3741428541` · MCM `3741428715`.

---

## Manual steps — only Steve can do these (Claude cannot)

- **Nexus:** upload the 5 zips from `dist\` via the website (1 main + 4 optional).
  Keep existing dependency-file names/descriptions as-is.
- **Steam:** with Steam running + logged in, **double-click**
  `C:\dev\modready\framework\UploadToWorkshop.bat` from File Explorer. Steam API injection
  requires a real double-click — it fails silently from any script/terminal.

---

## Hard-won gotchas (don't relearn these)

| Trap | Rule |
|------|------|
| Stale GUI resources | Deploy = DLL **and** `GUI\` to **both** live + dist (`deploy-mcm.ps1`). DLL-only leaves brushes stale. |
| Text won't recolor | `Brush.ColorFactor` multiplies sprite color, not the font channel. Use `Brush.FontColor`. |
| Build lint fails on `--` | `--` is illegal inside XML comments. The build gate enforces it. |
| Workshop "didn't update" | Uploader takes `WorkshopUpdate-*.xml`, not `SubModule.xml`. |
| Consumer mods break | Never bump the impersonation `AssemblyVersion`s. |
| Vortex crash | Every `DependedModule` needs a non-empty `DependentVersion`. (Build gate checks.) |
