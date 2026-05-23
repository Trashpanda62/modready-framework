// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Settings whose values live per campaign (one JSON per campaign id under
// Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Campaign\<campaignId>\).
// Different campaign saves can have different values for the same setting.

using MCM.Abstractions;

namespace MCM.Abstractions.Base.PerCampaign;

public abstract class BasePerCampaignSettings : BaseSettings { }
