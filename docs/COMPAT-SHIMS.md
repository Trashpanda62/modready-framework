# Compatibility shims for downstream consumer mods

## What downstream mods actually depend on

When a mod like CREST, HeraldMarshal, or any community mod is compiled against the BUTR foundation stack, it declares two kinds of dependency:

1. **Module-level** dependency, in its own `SubModule.xml`:
   ```xml
   <DependedModule Id="Bannerlord.Harmony" />
   <DependedModule Id="Bannerlord.ButterLib" />
   <DependedModule Id="Bannerlord.UIExtenderEx" />
   <DependedModule Id="Bannerlord.MBOptionScreen" />
   ```
   Bannerlord's launcher refuses to enable the mod unless a module with each of those IDs is also enabled.

2. **Assembly-level** dependency, baked into the compiled DLL. These resolve to:
   - `HarmonyLib.*` types -> live in `0Harmony.dll` (Pardeike, MIT, unchanged from NuGet)
   - `Bannerlord.ButterLib.*` types -> live in `Bannerlord.ButterLib.dll`
   - `Bannerlord.UIExtenderEx.*` types -> live in `Bannerlord.UIExtenderEx.dll`
   - `MCMv5.*` types -> live in `MCMv5.dll`

## How ModReady satisfies each layer

### Module-level (handled by SubModule.xml only -- no shim DLL needed)

We ship "alias" SubModule.xml files in tiny stub module folders:
- `Modules\Bannerlord.Harmony\SubModule.xml` (empty SubModuleAssemblies list, declares dependency on ModReady)
- `Modules\Bannerlord.ButterLib\SubModule.xml` (same pattern)
- `Modules\Bannerlord.UIExtenderEx\SubModule.xml`
- `Modules\Bannerlord.MBOptionScreen\SubModule.xml`

Each alias module exists purely so Bannerlord's launcher sees a module with the expected Id. It loads no code of its own.

This is identical to what CREST already does on the user's machine via `CrestEnsureStubs.cs`, but baked into the ship layout instead of materialized at runtime.

### Assembly-level

- **`0Harmony.dll`** - we ship the unmodified Pardeike NuGet artifact under `Modules\ModReady\bin\Win64_Shipping_Client\`. Any consumer mod's `HarmonyLib.*` references resolve transparently.
- **`Bannerlord.ButterLib.dll`** / **`Bannerlord.UIExtenderEx.dll`** / **`MCMv5.dll`** - in Phase 1 we don't ship these yet. Phase 2 ships type-forwarding shims that redirect the upstream namespaces to ModReady's rebuilt implementations.

## Why we don't ship a `Bannerlord.Harmony.dll` type-forwarder

The CREST source kit ships a tiny `Bannerlord.Harmony.dll` shim that type-forwards a few BUTR-extension types. Those types are BUTR-authored (`HarmonyLib.BUTR.Extensions.AccessTools2`, etc.) and we deliberately do NOT use them in ModReady. So there's nothing to forward at the assembly level for Harmony.

Consumer mods that were built against BUTR's `HarmonyLib.BUTR.Extensions.AccessTools2` will fail to resolve that type under ModReady. The mitigation for those mods is the same as on any other Harmony loader: they need their author's BUTR-extensions reference upgraded or replaced. We can't paper over an Aragas-authored type with a clean-room shim that re-implements its behavior, because that would put Aragas-derived API behavior back into our distribution.

## Where the alias SubModule.xml files live

```
dist/Modules/
  ModReady/
    SubModule.xml                  # the real module
    bin/Win64_Shipping_Client/
      ModReady.Foundation.dll
      ModReady.Harmony.dll
      0Harmony.dll
  Bannerlord.Harmony/
    SubModule.xml                  # alias only, no bin/
  Bannerlord.ButterLib/
    SubModule.xml                  # alias only
  Bannerlord.UIExtenderEx/
    SubModule.xml                  # alias only
  Bannerlord.MBOptionScreen/
    SubModule.xml                  # alias only
```

The ship zip extracts all five module folders. The user sees one "ModReady" entry plus four tiny aliases in the launcher; they enable all of them once and forget about it.
