// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Public API mirror of upstream BUTR ButterLib v2.7.2's HotKeyCategory enum.
// Consumer mods (e.g. FluidCombatLite) reference these values when declaring
// a HotKey's Category property. Must match upstream's enum members exactly.

namespace Bannerlord.ButterLib.HotKeys;

public enum HotKeyCategory
{
    Action,
    CampaignMap,
    MenuShortcut,
    OrderMenu,
    Chat
}
