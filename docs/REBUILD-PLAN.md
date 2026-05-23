# BetaDeps rebuild plan

## Why we're doing this

1. **BUTR upstream is broken on beta (e1.4.x).** That's why CREST was built. BetaDeps inherits CREST's beta fixes as its starting baseline.
2. **CREST was rejected by Nexus.** Aragas (deceased) authored Bannerlord.Harmony and Bannerlord.UIExtenderEx and we cannot obtain permission. The fork-and-rebrand path is closed.
3. **Goal: a single Nexus-approvable module that works on beta.** All bundled code must either be original to us, or carry redistribution rights we can document.

## Phases

### Phase 0 — Baseline (done with the creation of this folder)

- New project skeleton under `beta-deps/`.
- Working CREST v0.9.3 sources stay in `C:\dev\bannerlord\Bannerlord.*\` as the **reference implementation**. We read them, but the output goes here under new names.

### Phase 1 — BetaDeps.Harmony (clean-room wrapper rewrite)

The piece we need to replace is the *Bannerlord SubModule wrapper* that loads Lib.Harmony (MIT, pardeike) at the right point in the module load order and exposes it to other mods. Roughly 300–500 lines.

Scope:
- `BetaDeps.Harmony.csproj` — references `Lib.Harmony` NuGet directly, plus TaleWorlds refs.
- `BetaDepsHarmonySubModule.cs` — derives from `MBSubModuleBase`; on `OnSubModuleLoad` ensures Lib.Harmony is loaded before any patch-using sibling submodule.
- `BetaDepsHarmonyTypeForwards.cs` — type-forwards `HarmonyLib.*` from `0Harmony.dll` so consumer mods compiled against standard Harmony resolve transparently.
- `BetaSafeBind.cs` — sigsafe binding logic re-authored from scratch (the *concept* originated in CREST, which we own; the implementation here is independent of any Aragas-authored code).
- Module folder name: `BetaDeps.Harmony` during dev; the final ship layout is a single `Modules\BetaDeps\`.

Acceptance:
- Launches on e1.4.x beta and e1.3.x public without CTD.
- Other BetaDeps / consumer-mod patches bind via the wrapper.
- DLL inspector shows no Aragas copyright lines and no copied source blocks.

### Phase 2 — BetaDeps.UIExtenderEx (clean-room rewrite)

The harder piece. UIExtenderEx hooks `GauntletMovie.Load`, runs XML prefab patches, and exposes a registration API. ~1500–3000 lines.

Scope:
- New API surface inspired by public Bannerlord modding patterns, written from scratch.
- Hook into `GauntletMovie.Load` via BetaDeps.Harmony.
- XML prefab patch model: locate prefab → apply child insertions / attribute edits → re-emit.
- ViewModel mixin support compatible with downstream consumer mods.

Acceptance: same beta-stable launch test; consumer XMLs render correctly.

### Phase 3 — BetaDeps.ButterLib and BetaDeps.MCM

Two paths, decided after Phases 1-2 ship:

- **Path A — BUTR org permission letter.** ButterLib and MCM have living, contactable authors. A formal permission letter to the BUTR org may unlock redistribution. Lowest effort.
- **Path B — rewrite.** Larger surface area but full ownership.

Until Phase 3 lands, BetaDeps ships with `bundled-deps/BetaDeps.ButterLib/` and `bundled-deps/BetaDeps.MCM/` rebranded from CREST as a transitional measure. The third-party license file documents that these are derivative works of BUTR's MIT-licensed sources.

### Phase 4 — Single-module packaging

- One `SubModule.xml` at `Modules\BetaDeps\` referencing every assembly above.
- One Nexus zip: `BetaDeps.zip`.
- Type-forwarder DLLs so legacy consumer mods (`Bannerlord.Harmony.dll`, `Bannerlord.ButterLib.dll`, etc.) keep resolving.

## Module folder naming

We use `BetaDeps.<Component>` for nested project folders during development but the final ship layout is a single `Modules\BetaDeps\` on disk. This avoids the "10 standalone modules in the launcher" UX problem.

## License posture

- BetaDeps original code: MIT, copyright Steve / Maxfield Management Group.
- Lib.Harmony 2.x: MIT, copyright Andreas Pardeike — bundled per MIT.
- Microsoft.Extensions.DependencyInjection + transitive deps: MIT, copyright Microsoft.
- Newtonsoft.Json: MIT, copyright James Newton-King.
- Mono.Cecil + MonoMod: MIT, copyright JB Evain / 0x0ade et al.
- **No Aragas-authored code is shipped.** The build verifier checks every BetaDeps-authored DLL for the string "Aragas" and fails the build if found.
- **Namespace strings like `Bannerlord.BUTR`, `HarmonyLib.BUTR.Extensions` ARE used inside BetaDeps**, by design. These are public-API namespace paths the BUTR community standardized on; consumer mods compiled against those namespaces resolve drop-in to BetaDeps's clean-room implementations. Using a namespace name is not the same as importing copyrighted code.
