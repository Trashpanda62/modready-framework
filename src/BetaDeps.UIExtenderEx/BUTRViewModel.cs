// BetaDeps clean-room re-implementation of Bannerlord.UIExtenderEx.BUTRViewModel.
// MIT, copyright 2026 Maxfield Management Group.
//
// BUTRViewModel is a thin convenience base over TaleWorlds.Library.ViewModel.
// Adds a SetField helper that combines a backing-field assignment with the
// OnPropertyChangedWithValue notification in a single call.

using System.Collections.Generic;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx;

public abstract class BUTRViewModel : ViewModel
{
    /// <summary>
    /// Backing-field setter that raises OnPropertyChanged if the value
    /// actually changed. Returns true on change, false on no-op.
    /// </summary>
    protected new bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
