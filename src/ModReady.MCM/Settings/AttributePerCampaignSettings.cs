// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCM.Abstractions.Base.PerCampaign.AttributePerCampaignSettings<TSelf>
//
// Attribute-driven per-campaign settings base. Mirrors
// AttributePerSaveSettings<TSelf> exactly -- see PerCampaignSettings.cs for
// why this stub exists (Phase 1.1 / finding B3: the registry named this type
// but the assembly didn't define it, so consumers declaring it crashed with
// TypeLoadException at type-load time).

namespace MCM.Abstractions.Base.PerCampaign;

// Inherits from PerCampaignSettings<TSelf> rather than BasePerCampaignSettings
// directly, mirroring the upstream BUTR chain (AttributePerCampaignSettings ->
// PerCampaignSettings -> BasePerCampaignSettings -> BaseSettings) so consumer
// mods that reference either intermediate base find it via the same
// inheritance chain the CLR's type-load resolver walks.
public abstract class AttributePerCampaignSettings<TSelf> : PerCampaignSettings<TSelf>
    where TSelf : AttributePerCampaignSettings<TSelf>
{
    // Intentionally empty. Persistence + Instance singleton inherited from
    // PerCampaignSettings<TSelf>. F-bounded TSelf constraint matches upstream
    // MCM ABI so consumer mods' override resolution works at type-load time.
}
