// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Marks a mixin method that replaces the target ViewModel's method
/// <see cref="MethodName"/> when a Gauntlet binding invokes it
/// (UIExtenderEx 3.x surface).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BUTRViewModelOverrideAttribute : Attribute
{
    public string MethodName { get; }

    public BUTRViewModelOverrideAttribute(string methodName) => MethodName = methodName;
}
