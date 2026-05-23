// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// MCM.Common.DropdownDefault<T> -- the standard dropdown provider class
// consumer mods use to expose a [SettingPropertyDropdown] property. Wraps
// a list of T choices plus a SelectedIndex; SelectedValue points at the
// currently selected element. Implements INotifyPropertyChanged so UI
// bindings refresh when the user picks a different option.
//
// Namespace deliberately matches upstream BUTR-MCM for drop-in compatibility.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MCM.Common;

/// <summary>
/// Generic dropdown provider used as the value type of a property tagged
/// with <c>[SettingPropertyDropdown]</c>. Exposes a list of values plus the
/// currently-selected index/value, and raises <see cref="INotifyPropertyChanged"/>
/// when the selection moves.
/// </summary>
[Serializable]
public class DropdownDefault<T> : IEnumerable<T>, INotifyPropertyChanged
{
    private readonly List<T> _items;
    private int _selectedIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DropdownDefault()
    {
        _items = new List<T>();
        _selectedIndex = -1;
    }

    public DropdownDefault(IEnumerable<T> items, int selectedIndex = 0)
    {
        _items = items?.ToList() ?? new List<T>();
        _selectedIndex = _items.Count == 0 ? -1 : Math.Max(0, Math.Min(selectedIndex, _items.Count - 1));
    }

    /// <summary>Number of choices.</summary>
    public int Count => _items.Count;

    /// <summary>Currently selected index. -1 if no items.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_items.Count == 0)
            {
                _selectedIndex = -1;
                return;
            }
            var clamped = Math.Max(0, Math.Min(value, _items.Count - 1));
            if (clamped == _selectedIndex) return;
            _selectedIndex = clamped;
            OnPropertyChanged(nameof(SelectedIndex));
            OnPropertyChanged(nameof(SelectedValue));
        }
    }

    /// <summary>The currently selected element. Default(T) if empty.</summary>
    public T? SelectedValue
    {
        get
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return default;
            return _items[_selectedIndex];
        }
        set
        {
            var newIndex = _items.IndexOf(value!);
            if (newIndex >= 0) SelectedIndex = newIndex;
        }
    }

    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public override string ToString() => SelectedValue?.ToString() ?? string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is DropdownDefault<T> other)
        {
            // Equality is by selected value; matches upstream behavior where
            // serialization round-trips by SelectedValue.
            return EqualityComparer<T?>.Default.Equals(SelectedValue, other.SelectedValue);
        }
        if (obj is T t) return EqualityComparer<T?>.Default.Equals(SelectedValue, t);
        return false;
    }

    public override int GetHashCode() => SelectedValue?.GetHashCode() ?? 0;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
