// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MCMUISubModule -- the MBSubModuleBase that wires up the in-game Options
// tab. Registers our UIExtenderEx prefab patches + ViewModel mixin so the
// "Mod Configuration" tab appears in the Options screen on the next open.

using System;
using System.Reflection;

using Bannerlord.UIExtenderEx;

using ModReady.Foundation;

using TaleWorlds.MountAndBlade;

namespace MCM.UI;

public class MCMUISubModule : MBSubModuleBase
{
    private const string Tag = "MCMUISubModule";
    private UIExtender? _extender;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        try
        {
            var asmName = typeof(MCMUISubModule).Assembly.GetName();
            DiagLog.Log(Tag, $"OnSubModuleLoad: {asmName.Name} v{asmName.Version}");

            // Register our prefab patches + mixin via UIExtenderEx so the
            // Options screen gets the Mod Configuration tab.
            _extender = UIExtender.Create("ModReady.MCM.UI");
            _extender.Register(typeof(MCMUISubModule).Assembly);
            _extender.Verify();
            _extender.Enable();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnSubModuleLoad", ex);
        }
    }
}
