// BetaDeps clean-room. MIT, copyright 2026 Maxfield Management Group.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using BetaDeps.Foundation;

namespace MCM.Common;

public class ProxyRef<T> : IRef
{
    private const string Tag = "MCM.ProxyRef";
    private readonly Func<T> _getter;
    private readonly Action<T>? _setter;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ProxyRef(Func<T> getter, Action<T>? setter)
    {
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter;
    }
    public Type Type => typeof(T);
    public string Name => string.Empty;
    public object? Value
    {
        get => _getter();
        set
        {
            // No setter: read-only ref, write is a silent no-op (intentional --
            // some [DataSourceProperty] members on consumer-mod settings are
            // get-only and the binder still tries to flush a write-back).
            if (_setter == null) return;
            // v0.6 fix: previously this swallowed every exception (including
            // invalid casts) AND still fired PropertyChanged as if the set
            // succeeded -- which left the UI showing a stale "new" value
            // while the underlying model still had the old one. Now: log the
            // failure and skip OnPropertyChanged so the binder can refresh
            // from the real getter on next read.
            try
            {
                _setter((T)value!);
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"ProxyRef<{typeof(T).Name}>.Value set", ex);
                return;
            }
            OnPropertyChanged(nameof(Value));
        }
    }
    public void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    public override bool Equals(object? other)
        => other is ProxyRef<T> r && EqualityComparer<T>.Default.Equals(_getter(), r._getter());
    public override int GetHashCode() => _getter()?.GetHashCode() ?? 0;
}
