// BetaDeps clean-room.
namespace MCM.Abstractions;
public sealed class UnavailableSetting : BaseSettings
{
    public static readonly UnavailableSetting Instance = new();
    private UnavailableSetting() { }
    public override string Id => "(unavailable)";
    public override string DisplayName => "(unavailable)";
    public override string FolderName => string.Empty;
    public override string FormatType => "json";
}
