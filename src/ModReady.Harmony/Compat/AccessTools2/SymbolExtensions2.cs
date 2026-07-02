// ModReady clean-room re-implementation of HarmonyLib.BUTR.Extensions.SymbolExtensions2.
// MIT, copyright 2026 Maxfield Management Group.
//
// SymbolExtensions2.GetMethodInfo lets consumer mods write
//     SymbolExtensions2.GetMethodInfo((SomeType x) => x.SomeMethod(default))
// instead of doing the AccessTools.Method dance with explicit parameter types.
// The expression tree is parsed once at call time to extract the MethodInfo.
//
// This is a clean re-implementation of the public API surface only; the
// implementation is from-scratch and does not derive from BUTR source.

using System;
using System.Linq.Expressions;
using System.Reflection;

namespace HarmonyLib.BUTR.Extensions;

public static class SymbolExtensions2
{
    /// <summary>
    /// Extract the MethodInfo a method-call expression resolves to. Returns
    /// null on any failure to parse.
    /// </summary>
    /// <example>
    /// var mi = SymbolExtensions2.GetMethodInfo&lt;Mission&gt;(m =&gt; m.Tick(default));
    /// </example>
    public static MethodInfo? GetMethodInfo<T>(Expression<Action<T>> expression)
        => ParseMethodCall(expression?.Body);

    public static MethodInfo? GetMethodInfo(Expression<Action> expression)
        => ParseMethodCall(expression?.Body);

    public static MethodInfo? GetMethodInfo<T, TResult>(Expression<Func<T, TResult>> expression)
        => ParseMethodCall(expression?.Body);

    public static MethodInfo? GetMethodInfo<TResult>(Expression<Func<TResult>> expression)
        => ParseMethodCall(expression?.Body);

    private static MethodInfo? ParseMethodCall(Expression? body)
    {
        try
        {
            // Direct call expression
            if (body is MethodCallExpression mce) return mce.Method;
            // Property access compiles to a Property getter call; pull the getter
            if (body is MemberExpression me && me.Member is PropertyInfo pi)
                return pi.GetGetMethod(nonPublic: true);
            // Unary (cast/convert) wrapping a call
            if (body is UnaryExpression ue) return ParseMethodCall(ue.Operand);
            return null;
        }
        catch { return null; }
    }
}
