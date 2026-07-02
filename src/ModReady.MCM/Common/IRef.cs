// ModReady clean-room. MIT, copyright 2026 Maxfield Management Group.
using System;
using System.ComponentModel;

namespace MCM.Common;

public interface IRef : INotifyPropertyChanged
{
    object? Value { get; set; }
    Type Type { get; }
    string Name { get; }
}
