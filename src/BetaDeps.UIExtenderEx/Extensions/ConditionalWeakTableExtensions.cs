// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Convenience extensions on ConditionalWeakTable used by ButterLib/UIExtenderEx
// for VM-instance-keyed side tables. .NET Framework 4.7.2 lacks the newer
// AddOrUpdate / Remove return-value semantics, so we shim them here.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Bannerlord.UIExtenderEx.Extensions;

public static class ConditionalWeakTableExtensions
{
    /// <summary>
    /// Add or update an entry. If the key already exists, the old value is
    /// replaced. Returns the value that's now associated with the key.
    /// </summary>
    public static TValue AddOrUpdate<TKey, TValue>(this ConditionalWeakTable<TKey, TValue> table, TKey key, TValue value)
        where TKey : class
        where TValue : class
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        // ConditionalWeakTable on net472 only supports Add (throws if key exists).
        try { table.Remove(key); } catch { /* not present */ }
        table.Add(key, value);
        return value;
    }
}
