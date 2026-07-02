// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Bannerlord.ButterLib.ButterLibSaveableTypeDefiner
//
// Real save-system binding (v1.0.1 save-compat fix; was a no-op stub that
// did not derive from SaveableTypeDefiner, so consumer-mod definers built
// on it were silently skipped by DefinitionContext.CollectTypes and their
// saveable types never registered -> loading such saves failed).
//
// Interop contract (see docs/SAVE-COMPAT-BUTR-INTEROP.md): the base save id
// must be 2_000_000_000 + 2018 (upstream's NexusMods project id) * 1000 so
// that type ids embedded in existing upstream-created save files resolve to
// the definitions registered here. The engine resolves save-file types by
// numeric id only; the offset passed by subclasses is added to this base.
//
// Known offset reservations under this base (from observed save data):
//   00-04  CampaignIdentifier (deleted upstream in the e1.6.0 era; ids do
//          not appear in any modern save -- intentionally not re-registered)
//   5      ObjectSystem (OSSaveableTypeDefiner, ObjectSystemSaveCompat.cs)

using TaleWorlds.SaveSystem;

namespace Bannerlord.ButterLib
{
    /// <summary>
    /// Base class of ButterLib's saving system. Consumer mods derive their
    /// own <see cref="SaveableTypeDefiner"/>s from this to get a save-id
    /// namespace under ButterLib's reserved base id.
    /// </summary>
    public abstract class ButterLibSaveableTypeDefiner : SaveableTypeDefiner
    {
        private const int ButterLibBaseId = 2002018000;

        protected ButterLibSaveableTypeDefiner(int saveBaseId) : base(ButterLibBaseId + saveBaseId) { }
    }
}
