// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// MessageUtils -- diagnostic message helpers used by UIExtenderEx for
// startup warnings / dev-mode chat output. Wraps InformationManager
// (TaleWorlds.Library) via reflection so this assembly doesn't take a
// hard compile-time dep on game-side display code.

using System;

using BetaDeps.Foundation;

using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.UIExtenderEx.Utils;

public static class MessageUtils
{
    private const string Tag = "MessageUtils";

    /// <summary>
    /// Display an informational message in the in-game chat overlay. Falls
    /// back to runtime.log if the chat surface isn't reachable yet (e.g.
    /// before the main menu loads).
    /// </summary>
    public static void DisplayMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            var infoManagerType = AccessTools2.TypeByName("TaleWorlds.Library.InformationManager");
            var infoMessageType = AccessTools2.TypeByName("TaleWorlds.Library.InformationMessage");
            if (infoManagerType != null && infoMessageType != null)
            {
                var infoMessage = Activator.CreateInstance(infoMessageType, new object[] { message });
                var displayMethod = AccessTools2.Method(infoManagerType, "DisplayMessage", new[] { infoMessageType });
                if (displayMethod != null && infoMessage != null)
                {
                    displayMethod.Invoke(null, new[] { infoMessage });
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "DisplayMessage", ex);
        }
        DiagLog.Log(Tag, "InformationManager not reachable; falling back to log -> " + message);
    }
}
