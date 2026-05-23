// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Defensive helpers over TaleWorlds.Library.MbEvent<T> -- the game's
// observable event type. Adds a TryAddNonSerializedListener variant that
// catches the AddListener throw on weird stack states, plus a safe
// invocation wrapper. Used by ButterLib subsystems that want to subscribe
// without risk of CTD during teardown.

using System;
using System.Reflection;

using BetaDeps.Foundation;

using HarmonyLib.BUTR.Extensions;

namespace Bannerlord.ButterLib.Common.Extensions;

public static class MbEventExtensions
{
    private const string Tag = "MbEventExtensions";

    /// <summary>
    /// Calls AddNonSerializedListener on an MbEvent via reflection. Returns
    /// true if the call succeeded; logs and returns false on any exception.
    /// </summary>
    public static bool TryAddNonSerializedListener(object? mbEvent, object? owner, Delegate? handler)
    {
        if (mbEvent == null || handler == null) return false;
        try
        {
            var t = mbEvent.GetType();
            var m = AccessTools2.Method(t, "AddNonSerializedListener", new[] { typeof(object), handler.GetType() });
            if (m == null) return false;
            m.Invoke(mbEvent, new object?[] { owner, handler });
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "TryAddNonSerializedListener", ex);
            return false;
        }
    }
}
