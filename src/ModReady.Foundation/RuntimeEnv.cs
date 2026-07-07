// ModReady.Foundation -- RuntimeEnv
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// Tiny runtime probe. On Steam/GOG the game is a .NET Framework (net472)
// process -> Environment.Version is 4.x. On Game Pass / Microsoft Store the
// game is a .NET 6 (CoreCLR) process -> Environment.Version is 6.x, REGARDLESS
// of the assembly's own target framework (a net472-targeted DLL loaded on
// CoreCLR still reports the runtime's 6.x here). That's what lets the Win64-
// only ModReady umbrella module detect "I've been loaded on Game Pass" and
// no-op its Steam-only hooks instead of running (and crashing) on .NET 6.

using System;

namespace ModReady.Foundation;

public static class RuntimeEnv
{
    /// <summary>True when running on .NET 5+ / CoreCLR (Game Pass / MS Store),
    /// false on .NET Framework (Steam / GOG).</summary>
    public static bool IsNetCore => Environment.Version.Major >= 5;
}
