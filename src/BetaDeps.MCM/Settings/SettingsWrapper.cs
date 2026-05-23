// BetaDeps clean-room.
using MCM.Common;
namespace MCM.Abstractions.Base.Global;
public class SettingsWrapper : BaseGlobalSettings, IWrapper
{
    public object Object { get; }
    public override string Id { get; }
    public override string DisplayName { get; }
    public override string FolderName { get; }
    public override string FormatType => "json";
    public SettingsWrapper(object @object)
    {
        Object = @object ?? throw new System.ArgumentNullException(nameof(@object));
        var t = Object.GetType();
        Id = (string?)t.GetProperty("Id")?.GetValue(Object) ?? t.Name;
        DisplayName = (string?)t.GetProperty("DisplayName")?.GetValue(Object) ?? t.Name;
        FolderName = (string?)t.GetProperty("FolderName")?.GetValue(Object) ?? string.Empty;
    }
}
