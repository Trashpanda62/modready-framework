// BetaDeps clean-room.
using System;
namespace BUTR.DependencyInjection.Logger;
public static class LoggerExtensions
{
    public static void LogWarning(this IBUTRLogger? logger, string message, params object?[] args)
    {
        if (logger == null) return;
        try { logger.LogWarning(args == null || args.Length == 0 ? message : string.Format(message, args)); } catch { }
    }
    public static void LogInformation(this IBUTRLogger? logger, string message, params object?[] args)
        => logger?.LogInformation(args == null || args.Length == 0 ? message : string.Format(message, args));
    public static void LogError(this IBUTRLogger? logger, Exception ex, string message)
        => logger?.LogError(ex, message);
}
