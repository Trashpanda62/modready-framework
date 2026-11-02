// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.

namespace Bannerlord.UIExtenderEx.Attributes;

/// <summary>What a <see cref="BUTRUnsafeAccessorAttribute"/> method binds to on the target ViewModel.</summary>
public enum BUTRAccessorKind
{
    Method = 0,
    Field = 1,
    StaticMethod = 2,
    StaticField = 3,
}
