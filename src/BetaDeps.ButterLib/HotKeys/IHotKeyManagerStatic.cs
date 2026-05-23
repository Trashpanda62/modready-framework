// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Internal seam that HotKeyManager.Create/CreateWithOwnCategory dispatches
// through. Upstream pulls this from the ButterLib DI container; we publish
// a default no-op implementation from ButterLibSubModule.OnSubModuleLoad
// so consumer mods that call the static factories don't get a null
// HotKeyManager back.

using System.Collections.Generic;

namespace Bannerlord.ButterLib.HotKeys;

internal interface IHotKeyManagerStatic
{
    IList<HotKeyBase> HotKeys { get; }
    HotKeyManager Create(string modName);
    HotKeyManager CreateWithOwnCategory(string modName, string categoryName);
}
