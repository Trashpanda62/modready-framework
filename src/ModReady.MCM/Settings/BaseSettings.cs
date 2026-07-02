// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
// Namespace deliberately matches upstream BUTR-MCM for drop-in compatibility.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MCM.Abstractions;

/// <summary>
/// Common base for any MCM settings VM. Implements INotifyPropertyChanged so
/// the UI tab can rebind when properties change at runtime.
/// </summary>
public abstract class BaseSettings : INotifyPropertyChanged
{
    /// <summary>Settings identifier; the JSON file is named after this.</summary>
    public abstract string Id { get; }

    /// <summary>Display name shown in the MCM tab list (may be a {=Key} localization tag).</summary>
    public abstract string DisplayName { get; }

    /// <summary>Optional folder name for organizing many settings panels.</summary>
    public virtual string FolderName { get; } = string.Empty;

    /// <summary>Optional format string (currently always JSON in this impl).</summary>
    public virtual string FormatType { get; } = "json2";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
