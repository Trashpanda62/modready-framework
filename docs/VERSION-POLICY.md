# ModReady version policy (M19 / Phase 4.1, 2026-06-11)

ModReady ships two kinds of assemblies, and they version differently on
purpose. This file is the policy of record — check it before "fixing" any
version number.

## The three surfaces

Every shipped DLL has three version surfaces a mod or user can observe:

1. **AssemblyVersion** — what `Assembly.GetName().Version` returns and what
   the CLR records in references. Mods gate features on this.
2. **FileVersion** — what Explorer's file properties and crash dumps show.
3. **Advertised module version** — the `<Version value="..."/>` in the
   module's `SubModule.xml`, shown in the launcher and read via
   `ModuleHelper`.

## Impersonation assemblies (the BUTR API shims)

`Bannerlord.UIExtenderEx.dll`, `Bannerlord.ButterLib.dll`, `MCMv5.dll` —
these claim an upstream identity so consumer mods load against them
unmodified.

**Rule: all three surfaces carry the SAME advertised upstream line, ending
in `.99` to sort above any real upstream patch of that line.**

| Assembly | All three surfaces |
|---|---|
| Bannerlord.UIExtenderEx.dll | 2.14.99.0 / module v2.14.99.0 |
| Bannerlord.ButterLib.dll | 2.10.99.0 / module v2.10.99.0 |
| MCMv5.dll | 5.11.99.0 / module v5.11.99.0 |

Why `.99`: a mod checking "is UIExtenderEx >= 2.12" passes; the version
still honestly identifies the claimed upstream compatibility line. The
actual ModReady build that produced the DLL goes in
**InformationalVersion** (`ModReady <release>`), which is where diagnostics
should look.

When the claimed upstream line moves (e.g. we implement surface added in a
newer upstream release), bump the alias `SubModule.xml` AND the csproj
AssemblyVersion/FileVersion together — never one without the others. The
ApiCompat gate (scripts/generate-butr-audit.py + audit-apicompat.py) is the
evidence required for such a bump.

`Bannerlord.Harmony.dll` (ModReady.HarmonyHost) is a half-exception:
nothing binds to the loader shim by version, so its **AssemblyVersion stays
pinned at 0.9.0.0**, while FileVersion matches the advertised module
version (v2.4.99.0).

## Internal assemblies (ModReady.*)

`ModReady.Foundation.dll`, `ModReady.Harmony.dll` — our own infrastructure,
referenced by the other ModReady projects.

**Rule: AssemblyVersion is pinned at 0.9.0.0 and is NEVER bumped per
release** — sibling DLLs bind against it, and bumping it would force every
assembly in the fan to ship in lockstep (the H3/alias-refresh trap).
FileVersion tracks the actual ModReady release (e.g. 0.9.2.0) and is the
number to read when checking what's deployed. The ModReady module's own
`SubModule.xml` version (`v0.9.2`) tracks the release too.

## Release checklist touchpoints

- Bump: ModReady module `SubModule.xml` version + internal assemblies'
  `FileVersion` + `InformationalVersion` strings.
- Do NOT bump: any `AssemblyVersion`; impersonation lines (unless the
  claimed upstream line itself moves — see above).
- `scripts/Build-Phase1.ps1 -Version <x.y.z>` stamps the zip name; the
  csproj values above are authoritative for the DLLs.
