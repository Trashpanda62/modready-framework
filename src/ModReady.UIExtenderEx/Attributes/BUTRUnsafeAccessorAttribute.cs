// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
using System;

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>
/// Marks a mixin method as an accessor for a non-public member of the target
/// ViewModel (UIExtenderEx 3.x surface). <see cref="Name"/> defaults to the
/// accessor method's own name; <see cref="Type"/> overrides the declaring type
/// to search (defaults to the mixin's ViewModel type).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BUTRUnsafeAccessorAttribute : Attribute
{
    public BUTRAccessorKind Kind { get; }
    public Type? Type { get; }
    public string? Name { get; set; }

    public BUTRUnsafeAccessorAttribute(BUTRAccessorKind kind) => Kind = kind;
    public BUTRUnsafeAccessorAttribute(BUTRAccessorKind kind, Type type)
    {
        Kind = kind;
        Type = type;
    }
}
