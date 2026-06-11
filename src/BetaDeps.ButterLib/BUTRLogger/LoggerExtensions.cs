// BetaDeps clean-room.
using System;
namespace BUTR.DependencyInjection.Logger;
public static class LoggerExtensions
{
    // BUTR consumers pass structured templates like "Loading {ModuleName}" with
    // positional args. string.Format only understands {0}-style indices, so a
    // named placeholder throws FormatException. Never let a diagnostic log call
    // crash the consumer: format defensively and fall back to the raw template
    // (better a literal "{ModuleName}" in the log than a thrown exception).
    private static string SafeFormat(string message, object?[] args)
    {
        if (args == null || args.Length == 0) return message;
        try { return string.Format(message, args); } catch { return message; }
    }
    public static void LogWarning(this IBUTRLogger? logger, string message, params object?[] args)
    {
        if (logger == null) return;
        try { logger.LogWarning(SafeFormat(message, args)); } catch { }
    }
    public static void LogInformation(this IBUTRLogger? logger, string message, params object?[] args)
    {
        if (logger == null) return;
        try { logger.LogInformation(SafeFormat(message, args)); } catch { }
    }
    public static void LogError(this IBUTRLogger? logger, Exception ex, string message)
        => logger?.LogError(ex, message);
}
