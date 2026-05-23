// BetaDeps clean-room.
using System;
namespace BUTR.DependencyInjection.Logger;
public interface IBUTRLogger
{
    void LogTrace(string message);
    void LogDebug(string message);
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogError(Exception ex, string message);
    void LogCritical(string message);
}
public interface IBUTRLogger<T> : IBUTRLogger { }
