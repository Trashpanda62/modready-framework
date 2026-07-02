// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SubSystemSettingsPage is the BaseSettings sentinel registered by SettingsRegistry
// when SubSystemBridge.IsAvailable. SettingsVM.BuildGroups() detects this type
// and routes to BuildGroupsForSubSystems() instead of the normal reflection path.

namespace MCM.Internal;

internal sealed class SubSystemSettingsPage : MCM.Abstractions.BaseSettings
{
    public override string Id          => "ModReady.ButterLib.SubSystems";
    public override string DisplayName => "ButterLib Sub Systems";
}
