// BetaDeps.Foundation -- Bootstrap (NEUTERED)
//
// This file previously contained a [ModuleInitializer]-marked method
// that called AssemblyVersionShim.Install() at assembly load time.
// That approach caused BLSE to silently abandon the entire BetaDeps
// module (no log writes from any path). We now install
// AssemblyVersionShim from BetaDepsHarmonySubModule.OnSubModuleLoad
// and AliasStubSubModule.OnSubModuleLoad instead.
//
// File kept (not deleted) because the workspace mount denies unlink()
// during edit -- overwriting with empty content has the same effect of
// removing the [ModuleInitializer] from the compiled assembly.

namespace BetaDeps.Foundation;

// Empty namespace -- no types declared.
