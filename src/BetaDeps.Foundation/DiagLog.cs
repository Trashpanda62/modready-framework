// BetaDeps.Foundation -- DiagLog
//
// Thin static facade over RuntimeLog. Other BetaDeps assemblies prefer
// to call this via reflection so they don't have to take a hard build-
// time reference on BetaDeps.Foundation.dll. The reflection pattern is:
//
//   var foundation = AppDomain.CurrentDomain.GetAssemblies()
//       .FirstOrDefault(a => a.GetName().Name == "BetaDeps.Foundation");
//   var diag = foundation?.GetType("BetaDeps.Foundation.DiagLog");
//   diag?.GetMethod("Log")?.Invoke(null, new object[] { "tag", "msg" });
//
// Failing reflection drops the log line silently, which is the desired
// behavior -- diagnostics should never CTD the game.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

namespace BetaDeps.Foundation;

public static class DiagLog
{
    /// <summary>Append a diagnostic line.</summary>
    public static void Log(string tag, string message)
        => RuntimeLog.Write(tag, message);

    /// <summary>Append a caught-exception block.</summary>
    public static void LogCaught(string tag, string where, Exception ex)
        => RuntimeLog.WriteException(tag, where, ex);

    /// <summary>Absolute path to runtime.log, exposed for the in-game About panel.</summary>
    public static string GetLogPath() => RuntimeLog.Path;

    /// <summary>
    /// v0.6: master "verbose binding" toggle. When true, ViewModelBindingPatch
    /// logs every Slot/Next/Prev/Selected/BetaDeps SetPropertyValue (~60 lines
    /// per Options refresh). Default false so the ship-build is quiet; flip
    /// to true when bisecting a binding regression. Same idea as the older
    /// ExecuteBeginHint/ExecuteEndHint filter -- chatter that's useful only
    /// during active debugging.
    /// </summary>
    public static bool VerboseBinding = false;
}
