// ModReady clean-room. MIT, copyright 2026 Maxfield Management Group.
using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MCM.Common;

public class PropertyRef : IRef
{
    private readonly PropertyInfo _property;
    private readonly object _instance;
    public event PropertyChangedEventHandler? PropertyChanged;
    public PropertyRef(PropertyInfo property, object instance)
    {
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }
    public Type Type => _property.PropertyType;
    public string Name => _property.Name;
    public object? Value
    {
        get => _property.GetValue(_instance);
        set
        {
            if (!_property.CanWrite) return;
            _property.SetValue(_instance, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }
}
