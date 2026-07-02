// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Bannerlord.ButterLib.ExceptionHandler.ExceptionHandlerSubSystem
//
// Upstream BUTR ButterLib's ExceptionHandlerSubSystem catches game crashes
// and shows the BUTR Crash Reporter dialog. Consumer mods (notably
// AdmiralNelson's) call .Instance?.Disable() to opt out of the BUTR
// reporter (e.g. because they ship their own crash handler). If Instance
// is null they pop a Warning dialog: "unable to disable butterlib
// exception, please tell admiralnelson about this!".
//
// Our stub instantiates exactly one instance during ButterLibSubModule
// .OnSubModuleLoad (see _BindEarly) so .Instance is non-null and .Disable
// is a no-op (we don't ship the BUTR crash handler anyway).

using ModReady.Foundation;

namespace Bannerlord.ButterLib.ExceptionHandler;

public sealed class ExceptionHandlerSubSystem : Bannerlord.ButterLib.SubSystems.ISubSystem
{
    private const string Tag = "ExceptionHandlerSubSystem";

    public static ExceptionHandlerSubSystem? Instance { get; private set; }

    public string Id => "ExceptionHandler";
    public string Name => "Exception Handler";
    public string Description => "Stub: ModReady doesn't host the BUTR crash reporter, so Enable/Disable are no-ops.";
    public bool IsEnabled { get; private set; }
    public bool CanBeDisabled => true;
    public bool CanBeSwitchedAtRuntime => true;

    public ExceptionHandlerSubSystem()
    {
        Instance = this;
    }

    /// <summary>
    /// Called by ButterLibSubModule during OnSubModuleLoad to ensure
    /// .Instance is set before consumer mods touch it.
    /// </summary>
    internal static void _BindEarly()
    {
        if (Instance == null)
        {
            new ExceptionHandlerSubSystem();
            try { DiagLog.Log(Tag, "Instance bound (stub)"); } catch { }
        }
    }

    public void Enable()
    {
        IsEnabled = true;
        try { DiagLog.Log(Tag, "Enable() (no-op stub)"); } catch { }
    }

    public void Disable()
    {
        IsEnabled = false;
        try { DiagLog.Log(Tag, "Disable() (no-op stub)"); } catch { }
    }
}
